using System;
using System.Collections.Generic;
using EggPdf.Pdf;

namespace EggPdf.Svg;

public static partial class SvgRenderer
{
    private readonly struct Matrix2D
    {
        public readonly float A, B, C, D, E, F;
        public Matrix2D(float a, float b, float c, float d, float e, float f) { A = a; B = b; C = c; D = d; E = e; F = f; }
        public (float x, float y) Apply(float x, float y) => (A * x + C * y + E, B * x + D * y + F);
        public float UniformScale => (float)Math.Sqrt(Math.Abs((double)A * D - (double)B * C));
    }

    private static readonly Matrix2D IdentityMatrix = new(1, 0, 0, 1, 0, 0);

    private static Matrix2D MatrixMultiply(Matrix2D appliedFirst, Matrix2D appliedSecond) => new Matrix2D(
        appliedFirst.A * appliedSecond.A + appliedFirst.B * appliedSecond.C, appliedFirst.A * appliedSecond.B + appliedFirst.B * appliedSecond.D,
        appliedFirst.C * appliedSecond.A + appliedFirst.D * appliedSecond.C, appliedFirst.C * appliedSecond.B + appliedFirst.D * appliedSecond.D,
        appliedFirst.E * appliedSecond.A + appliedFirst.F * appliedSecond.C + appliedSecond.E,
        appliedFirst.E * appliedSecond.B + appliedFirst.F * appliedSecond.D + appliedSecond.F);

    private static Matrix2D ParseTransformToMatrix(string transform)
    {
        var result = IdentityMatrix;
        var arguments = new List<float>(6);
        int i = 0;
        while (i < transform.Length)
        {
            SvgPathData.SkipSeparators(transform, ref i);
            int nameStart = i;
            while (i < transform.Length && char.IsLetter(transform[i])) i++;
            if (i == nameStart)
            {
                if (i < transform.Length) i++;
                continue;
            }
            var name = transform.Substring(nameStart, i - nameStart).ToLowerInvariant();
            while (i < transform.Length && char.IsWhiteSpace(transform[i])) i++;
            if (i >= transform.Length || transform[i] != '(') break;
            i++;

            arguments.Clear();
            while (SvgPathData.TryReadNumber(transform, ref i, out float argument))
                arguments.Add(argument);
            while (i < transform.Length && transform[i] != ')') i++;
            if (i < transform.Length) i++;

            var function = TransformFunctionMatrix(name, arguments);
            if (function.HasValue) result = MatrixMultiply(function.Value, result);
        }
        return result;
    }

    private static Matrix2D? TransformFunctionMatrix(string name, List<float> arguments)
    {
        if (arguments.Count == 0) return null;
        float first = arguments[0];
        switch (name)
        {
            case "translate":
                return new Matrix2D(1, 0, 0, 1, first, arguments.Count > 1 ? arguments[1] : 0f);
            case "scale":
                return new Matrix2D(first, 0, 0, arguments.Count > 1 ? arguments[1] : first, 0, 0);
            case "rotate":
            {
                double radians = first * Math.PI / 180.0;
                float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
                var rotation = new Matrix2D(cos, sin, -sin, cos, 0, 0);
                if (arguments.Count < 3) return rotation;
                float cx = arguments[1], cy = arguments[2];
                return MatrixMultiply(MatrixMultiply(new Matrix2D(1, 0, 0, 1, -cx, -cy), rotation), new Matrix2D(1, 0, 0, 1, cx, cy));
            }
            case "skewx":
                return new Matrix2D(1, 0, (float)Math.Tan(first * Math.PI / 180.0), 1, 0, 0);
            case "skewy":
                return new Matrix2D(1, (float)Math.Tan(first * Math.PI / 180.0), 0, 1, 0, 0);
            case "matrix":
                return arguments.Count < 6
                    ? null
                    : new Matrix2D(arguments[0], arguments[1], arguments[2], arguments[3], arguments[4], arguments[5]);
            default:
                return null;
        }
    }

    private static List<(List<(float x, float y)> points, bool closed)>? ExtractShapeSubpaths(SvgElement el)
    {
        var result = new List<(List<(float x, float y)>, bool)>();
        switch (el.TagName)
        {
            case "rect":
                result.Add((ExtractRectPoints(el), true));
                return result;
            case "circle":
            {
                float cx = GetFloat(el, "cx"), cy = GetFloat(el, "cy"), r = GetFloat(el, "r");
                result.Add((ExtractEllipsePoints(cx, cy, r, r), true));
                return result;
            }
            case "ellipse":
            {
                float cx = GetFloat(el, "cx"), cy = GetFloat(el, "cy"), rx = GetFloat(el, "rx"), ry = GetFloat(el, "ry");
                result.Add((ExtractEllipsePoints(cx, cy, rx, ry), true));
                return result;
            }
            case "line":
                result.Add((new List<(float x, float y)>
                {
                    (GetFloat(el, "x1"), GetFloat(el, "y1")), (GetFloat(el, "x2"), GetFloat(el, "y2")),
                }, false));
                return result;
            case "polygon":
            case "polyline":
            {
                var points = el.GetAttribute("points");
                if (string.IsNullOrEmpty(points)) return null;
                result.Add((ParsePointsList(points), el.TagName == "polygon"));
                return result;
            }
            case "path":
            {
                var d = el.GetAttribute("d");
                return string.IsNullOrEmpty(d) ? null : FlattenSvgPath(d);
            }
            default:
                return null;
        }
    }

    private static List<(float x, float y)> ExtractRectPoints(SvgElement el)
    {
        float x = GetFloat(el, "x"), y = GetFloat(el, "y"), w = GetFloat(el, "width"), h = GetFloat(el, "height");
        float rx = GetFloat(el, "rx"), ry = GetFloat(el, "ry");
        if (ry == 0) ry = rx;
        if (rx == 0) rx = ry;
        rx = Math.Min(rx, w / 2f);
        ry = Math.Min(ry, h / 2f);

        var pts = new List<(float x, float y)>();
        if (rx <= 0 && ry <= 0)
        {
            pts.Add((x, y)); pts.Add((x + w, y)); pts.Add((x + w, y + h)); pts.Add((x, y + h));
            return pts;
        }

        float kx = rx * BezierCircleFactor, ky = ry * BezierCircleFactor;
        pts.Add((x + rx, y));
        pts.Add((x + w - rx, y));
        RasterCanvas.FlattenCubicBezier(x + w - rx, y, x + w - rx + kx, y, x + w, y + ry - ky, x + w, y + ry, pts, 8);
        pts.Add((x + w, y + h - ry));
        RasterCanvas.FlattenCubicBezier(x + w, y + h - ry, x + w, y + h - ry + ky, x + w - rx + kx, y + h, x + w - rx, y + h, pts, 8);
        pts.Add((x + rx, y + h));
        RasterCanvas.FlattenCubicBezier(x + rx, y + h, x + rx - kx, y + h, x, y + h - ry + ky, x, y + h - ry, pts, 8);
        pts.Add((x, y + ry));
        RasterCanvas.FlattenCubicBezier(x, y + ry, x, y + ry - ky, x + rx - kx, y, x + rx, y, pts, 8);
        return pts;
    }

    private static List<(float x, float y)> ExtractEllipsePoints(float cx, float cy, float rx, float ry, int segments = 48)
    {
        var pts = new List<(float x, float y)>(segments);
        for (int i = 0; i < segments; i++)
        {
            double theta = 2 * Math.PI * i / segments;
            pts.Add((cx + rx * (float)Math.Cos(theta), cy + ry * (float)Math.Sin(theta)));
        }
        return pts;
    }

    private static List<(List<(float x, float y)> points, bool closed)> FlattenSvgPath(string d)
    {
        var subpaths = new List<(List<(float x, float y)>, bool)>();
        var pts = new List<(float x, float y)>();
        float currentX = 0, currentY = 0;
        foreach (var segment in SvgPathData.Parse(d))
        {
            switch (segment.Kind)
            {
                case PathSegmentKind.MoveTo:
                    if (pts.Count > 0)
                    {
                        subpaths.Add((pts, false));
                        pts = new List<(float x, float y)>();
                    }
                    pts.Add((segment.X, segment.Y));
                    break;
                case PathSegmentKind.LineTo:
                    pts.Add((segment.X, segment.Y));
                    break;
                case PathSegmentKind.CubicTo:
                    RasterCanvas.FlattenCubicBezier(currentX, currentY, segment.X1, segment.Y1, segment.X2, segment.Y2, segment.X, segment.Y, pts);
                    break;
                case PathSegmentKind.Close:
                    pts.Add((segment.X, segment.Y));
                    subpaths.Add((pts, true));
                    pts = new List<(float x, float y)> { (segment.X, segment.Y) };
                    break;
            }
            currentX = segment.X;
            currentY = segment.Y;
        }
        subpaths.Add((pts, false));
        subpaths.RemoveAll(s => s.Item1.Count < 2);
        return subpaths;
    }
}
