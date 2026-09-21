using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Pdf;

namespace EggPdf.Svg;

/// <summary>
/// Geometry helpers for the filter rasterizer (SvgRenderer.Filter.cs): 2D affine matrices,
/// transform-attribute parsing, and flattening of shapes into polylines.
/// </summary>
public static partial class SvgRenderer
{
    /// <summary>2D affine matrix, row-vector convention: p' = p * M (matches PDF's cm semantics).</summary>
    private readonly struct Matrix2D
    {
        public readonly float A, B, C, D, E, F;
        public Matrix2D(float a, float b, float c, float d, float e, float f) { A = a; B = b; C = c; D = d; E = e; F = f; }
        public (float x, float y) Apply(float x, float y) => (A * x + C * y + E, B * x + D * y + F);
        public float UniformScale => (float)Math.Sqrt(Math.Abs((double)A * D - (double)B * C));
    }

    /// <summary>m1 * m2 (m1 the left factor): a point transforms as p * m1 * m2, i.e. m1 applied first.</summary>
    private static Matrix2D MatrixMultiply(Matrix2D m1, Matrix2D m2) => new Matrix2D(
        m1.A * m2.A + m1.B * m2.C, m1.A * m2.B + m1.B * m2.D,
        m1.C * m2.A + m1.D * m2.C, m1.C * m2.B + m1.D * m2.D,
        m1.E * m2.A + m1.F * m2.C + m2.E, m1.E * m2.B + m1.F * m2.D + m2.F);

    /// <summary>
    /// Parse a transform attribute into an equivalent Matrix2D, mirroring ApplyTransform's
    /// parsing exactly so a blurred shape ends up at the same position a normally-painted
    /// sibling would. Each successively parsed function becomes the new left factor,
    /// matching how each subsequent 'cm' operator becomes the new left-multiplying CTM
    /// factor in PDF (the same reason ApplyTransform's sequential cm emissions compose
    /// correctly for a vector-drawn point).
    /// </summary>
    private static Matrix2D ParseTransformToMatrix(string transform)
    {
        var result = new Matrix2D(1, 0, 0, 1, 0, 0);
        int i = 0;
        while (i < transform.Length)
        {
            SkipWhitespace(transform, ref i);
            if (i >= transform.Length) break;

            if (transform.Substring(i).StartsWith("translate(", StringComparison.OrdinalIgnoreCase))
            {
                i += 10;
                float tx = ReadNumber(transform, ref i);
                float ty = ReadNumberOpt(transform, ref i, 0);
                SkipParen(transform, ref i);
                result = MatrixMultiply(new Matrix2D(1, 0, 0, 1, tx, ty), result);
            }
            else if (transform.Substring(i).StartsWith("scale(", StringComparison.OrdinalIgnoreCase))
            {
                i += 6;
                float sx = ReadNumber(transform, ref i);
                float sy = ReadNumberOpt(transform, ref i, sx);
                SkipParen(transform, ref i);
                result = MatrixMultiply(new Matrix2D(sx, 0, 0, sy, 0, 0), result);
            }
            else if (transform.Substring(i).StartsWith("rotate(", StringComparison.OrdinalIgnoreCase))
            {
                i += 7;
                float angle = ReadNumber(transform, ref i);
                SkipParen(transform, ref i);
                float rad = angle * (float)Math.PI / 180f;
                float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
                result = MatrixMultiply(new Matrix2D(cos, sin, -sin, cos, 0, 0), result);
            }
            else if (transform.Substring(i).StartsWith("matrix(", StringComparison.OrdinalIgnoreCase))
            {
                i += 7;
                float a = ReadNumber(transform, ref i);
                float b = ReadNumber(transform, ref i);
                float c = ReadNumber(transform, ref i);
                float d = ReadNumber(transform, ref i);
                float e = ReadNumber(transform, ref i);
                float f = ReadNumber(transform, ref i);
                SkipParen(transform, ref i);
                result = MatrixMultiply(new Matrix2D(a, b, c, d, e, f), result);
            }
            else
            {
                i++;
            }
        }
        return result;
    }

    /// <summary>
    /// A shape's outline as flattened polylines in its own local coordinate system, one per
    /// subpath, each flagged closed or open; null for elements this rasterizer doesn't draw
    /// (text, images).
    /// </summary>
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

        var pts = new List<(float x, float y)>();
        if (rx <= 0 && ry <= 0)
        {
            pts.Add((x, y)); pts.Add((x + w, y)); pts.Add((x + w, y + h)); pts.Add((x, y + h));
            return pts;
        }

        const float k = 0.5523f;
        float kx = rx * k, ky = ry * k;
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

    /// <summary>Flatten an SVG path's "d" data into one polyline per subpath (same command grammar as ConvertSvgPathToPdf), each flagged closed by a 'Z'.</summary>
    private static List<(List<(float x, float y)> points, bool closed)> FlattenSvgPath(string d)
    {
        var subpaths = new List<(List<(float x, float y)>, bool)>();
        var pts = new List<(float x, float y)>();
        bool closed = false;
        float curX = 0, curY = 0, startX = 0, startY = 0;
        char lastCmd = ' ';
        int i = 0;
        while (i < d.Length)
        {
            SkipWhitespace(d, ref i);
            if (i >= d.Length) break;

            char cmd = d[i];
            if (char.IsLetter(cmd)) { i++; lastCmd = cmd; }
            else cmd = lastCmd;

            bool relative = char.IsLower(cmd);
            char cmdUpper = char.ToUpper(cmd);

            switch (cmdUpper)
            {
                case 'M':
                {
                    float x = ReadNumber(d, ref i), y = ReadNumber(d, ref i);
                    if (relative) { x += curX; y += curY; }
                    if (pts.Count > 0)
                    {
                        subpaths.Add((pts, closed));
                        pts = new List<(float x, float y)>();
                        closed = false;
                    }
                    curX = x; curY = y; startX = x; startY = y;
                    pts.Add((x, y));
                    lastCmd = relative ? 'l' : 'L';
                    break;
                }
                case 'L':
                {
                    float x = ReadNumber(d, ref i), y = ReadNumber(d, ref i);
                    if (relative) { x += curX; y += curY; }
                    curX = x; curY = y;
                    pts.Add((x, y));
                    break;
                }
                case 'H':
                {
                    float x = ReadNumber(d, ref i);
                    if (relative) x += curX;
                    curX = x;
                    pts.Add((curX, curY));
                    break;
                }
                case 'V':
                {
                    float y = ReadNumber(d, ref i);
                    if (relative) y += curY;
                    curY = y;
                    pts.Add((curX, curY));
                    break;
                }
                case 'C':
                {
                    float x1 = ReadNumber(d, ref i), y1 = ReadNumber(d, ref i);
                    float x2 = ReadNumber(d, ref i), y2 = ReadNumber(d, ref i);
                    float x = ReadNumber(d, ref i), y = ReadNumber(d, ref i);
                    if (relative) { x1 += curX; y1 += curY; x2 += curX; y2 += curY; x += curX; y += curY; }
                    RasterCanvas.FlattenCubicBezier(curX, curY, x1, y1, x2, y2, x, y, pts);
                    curX = x; curY = y;
                    break;
                }
                case 'Q':
                {
                    float qx = ReadNumber(d, ref i), qy = ReadNumber(d, ref i);
                    float x = ReadNumber(d, ref i), y = ReadNumber(d, ref i);
                    if (relative) { qx += curX; qy += curY; x += curX; y += curY; }
                    float cx1 = curX + 2f / 3f * (qx - curX);
                    float cy1 = curY + 2f / 3f * (qy - curY);
                    float cx2 = x + 2f / 3f * (qx - x);
                    float cy2 = y + 2f / 3f * (qy - y);
                    RasterCanvas.FlattenCubicBezier(curX, curY, cx1, cy1, cx2, cy2, x, y, pts);
                    curX = x; curY = y;
                    break;
                }
                case 'Z':
                    curX = startX; curY = startY;
                    pts.Add((curX, curY));
                    subpaths.Add((pts, true));
                    // Drawing after a close continues from the subpath's start point
                    pts = new List<(float x, float y)> { (curX, curY) };
                    closed = false;
                    break;
                case 'A':
                {
                    float arx = ReadNumber(d, ref i), ary = ReadNumber(d, ref i), rot = ReadNumber(d, ref i);
                    float large = ReadNumber(d, ref i), sweep = ReadNumber(d, ref i);
                    float x = ReadNumber(d, ref i), y = ReadNumber(d, ref i);
                    if (relative) { x += curX; y += curY; }
                    FlattenArc(pts, curX, curY, arx, ary, rot, large != 0f, sweep != 0f, x, y);
                    curX = x; curY = y;
                    break;
                }
                default:
                    i++;
                    break;
            }
        }
        subpaths.Add((pts, closed));
        subpaths.RemoveAll(s => s.Item1.Count < 2);
        return subpaths;
    }

    /// <summary>Append the polyline approximating an SVG elliptical arc (endpoint parameterization, SVG 1.1 appendix F.6).</summary>
    private static void FlattenArc(List<(float x, float y)> pts, float x0, float y0, float rx, float ry,
        float rotationDegrees, bool largeArc, bool sweep, float x1, float y1)
    {
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        if ((x0 == x1 && y0 == y1)) return;
        if (rx == 0f || ry == 0f) { pts.Add((x1, y1)); return; }

        double phi = rotationDegrees * Math.PI / 180.0;
        double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);
        double dx2 = (x0 - x1) / 2.0, dy2 = (y0 - y1) / 2.0;
        double x1p = cosPhi * dx2 + sinPhi * dy2, y1p = -sinPhi * dx2 + cosPhi * dy2;

        double lambda = x1p * x1p / ((double)rx * rx) + y1p * y1p / ((double)ry * ry);
        double rxd = rx, ryd = ry;
        if (lambda > 1.0) { double s = Math.Sqrt(lambda); rxd *= s; ryd *= s; }

        double num = rxd * rxd * ryd * ryd - rxd * rxd * y1p * y1p - ryd * ryd * x1p * x1p;
        double den = rxd * rxd * y1p * y1p + ryd * ryd * x1p * x1p;
        double coef = den == 0.0 ? 0.0 : Math.Sqrt(Math.Max(0.0, num / den));
        if (largeArc == sweep) coef = -coef;
        double cxp = coef * rxd * y1p / ryd, cyp = -coef * ryd * x1p / rxd;
        double cx = cosPhi * cxp - sinPhi * cyp + (x0 + x1) / 2.0;
        double cy = sinPhi * cxp + cosPhi * cyp + (y0 + y1) / 2.0;

        double theta1 = Math.Atan2((y1p - cyp) / ryd, (x1p - cxp) / rxd);
        double theta2 = Math.Atan2((-y1p - cyp) / ryd, (-x1p - cxp) / rxd);
        double delta = theta2 - theta1;
        if (sweep && delta < 0) delta += 2 * Math.PI;
        else if (!sweep && delta > 0) delta -= 2 * Math.PI;

        int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 16)));
        for (int s = 1; s <= steps; s++)
        {
            double t = theta1 + delta * s / steps;
            double ex = rxd * Math.Cos(t), ey = ryd * Math.Sin(t);
            pts.Add(((float)(cosPhi * ex - sinPhi * ey + cx), (float)(sinPhi * ex + cosPhi * ey + cy)));
        }
        pts[pts.Count - 1] = (x1, y1);
    }
}
