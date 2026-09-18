using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Pdf;

namespace EggPdf.Svg;

/// <summary>
/// feGaussianBlur support: PDF has no vector blur primitive, so a blur-filtered shape is
/// rasterized to a bitmap, convolved with a real Gaussian kernel, and embedded as an
/// image XObject instead of the normal vector path operators the rest of SvgRenderer
/// emits. Only single-primitive (&lt;filter&gt; containing exactly one &lt;feGaussianBlur&gt;)
/// filters on filled shapes are supported -- see CollectBlurFilters and RenderBlurredShape.
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
    /// Collect &lt;filter&gt; definitions that contain exactly one &lt;feGaussianBlur&gt;
    /// child with a positive stdDeviation (id -> stdDeviation). Filters with other or
    /// multiple primitives (drop shadows, color matrices, composited filter graphs) are
    /// a much larger format and are left unresolved: a shape referencing one of those
    /// simply renders via the normal, unblurred vector path.
    /// </summary>
    private static void CollectBlurFilters(SvgElement el, Dictionary<string, float> result)
    {
        if (el.TagName == "filter")
        {
            var id = el.GetAttribute("id");
            // Tag/attribute names are lowercased uniformly by this parser (no HTML5
            // "foreign content" case-preservation for SVG), so "feGaussianBlur" and
            // "stdDeviation" arrive as "fegaussianblur" / "stddeviation".
            if (!string.IsNullOrEmpty(id) && el.Children.Count == 1 && el.Children[0].TagName == "fegaussianblur")
            {
                var stdDevStr = el.Children[0].GetAttribute("stddeviation");
                if (!string.IsNullOrEmpty(stdDevStr))
                {
                    var firstToken = stdDevStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (firstToken.Length > 0 &&
                        float.TryParse(firstToken[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float stdDev) &&
                        stdDev > 0)
                    {
                        result[id] = stdDev;
                    }
                }
            }
        }
        foreach (var child in el.Children)
            CollectBlurFilters(child, result);
    }

    private static bool TryResolveBlurFilter(SvgElement el, Dictionary<string, float> blurFilters, out float stdDeviation)
    {
        stdDeviation = 0;
        var filterAttr = el.GetAttribute("filter");
        if (string.IsNullOrEmpty(filterAttr)) return false;

        var trimmed = filterAttr.Trim();
        if (!trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return false;

        int hashIdx = trimmed.IndexOf('#');
        if (hashIdx < 0) return false;
        int endIdx = trimmed.IndexOf(')', hashIdx);
        if (endIdx < 0) endIdx = trimmed.Length;

        var id = trimmed.Substring(hashIdx + 1, endIdx - hashIdx - 1).Trim('\'', '"', ' ');
        return !string.IsNullOrEmpty(id) && blurFilters.TryGetValue(id, out stdDeviation);
    }

    /// <summary>Extract a filled shape's outline in its own local coordinate system, or null for shapes this rasterizer doesn't support (line, text -- these keep painting via the normal unblurred vector path).</summary>
    private static List<(float x, float y)>? ExtractShapeLocalPoints(SvgElement el)
    {
        switch (el.TagName)
        {
            case "rect":
                return ExtractRectPoints(el);
            case "circle":
            {
                float cx = GetFloat(el, "cx"), cy = GetFloat(el, "cy"), r = GetFloat(el, "r");
                return ExtractEllipsePoints(cx, cy, r, r);
            }
            case "ellipse":
            {
                float cx = GetFloat(el, "cx"), cy = GetFloat(el, "cy"), rx = GetFloat(el, "rx"), ry = GetFloat(el, "ry");
                return ExtractEllipsePoints(cx, cy, rx, ry);
            }
            case "polygon":
            case "polyline":
            {
                var points = el.GetAttribute("points");
                return string.IsNullOrEmpty(points) ? null : ParsePointsList(points);
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

    /// <summary>Flatten an SVG path's "d" data into line-segment points (same command grammar as ConvertSvgPathToPdf; arcs are approximated as a line-to, matching that method's own documented simplification).</summary>
    private static List<(float x, float y)> FlattenSvgPath(string d)
    {
        var pts = new List<(float x, float y)>();
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
                    break;
                case 'A':
                {
                    ReadNumber(d, ref i); ReadNumber(d, ref i); ReadNumber(d, ref i);
                    ReadNumber(d, ref i); ReadNumber(d, ref i);
                    float x = ReadNumber(d, ref i), y = ReadNumber(d, ref i);
                    if (relative) { x += curX; y += curY; }
                    curX = x; curY = y;
                    pts.Add((x, y));
                    break;
                }
                default:
                    i++;
                    break;
            }
        }
        return pts;
    }

    private static (byte r, byte g, byte b, byte a) ExtractFillColor(SvgElement el)
    {
        var fillAttr = el.GetAttribute("fill");
        var fill = string.IsNullOrEmpty(fillAttr) ? "black" : fillAttr; // SVG default fill is black
        var (r, g, b) = ParseSvgColor(fill);

        float opacity = 1f;
        var opacityAttr = el.GetAttribute("fill-opacity");
        if (!string.IsNullOrEmpty(opacityAttr) &&
            float.TryParse(opacityAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out float op))
            opacity = Math.Max(0f, Math.Min(1f, op));

        return ((byte)Math.Round(r * 255f), (byte)Math.Round(g * 255f), (byte)Math.Round(b * 255f), (byte)Math.Round(opacity * 255f));
    }

    /// <summary>
    /// Rasterize a filled shape, blur it, and embed the result as an image XObject
    /// positioned at its (transformed) bounding box. Only filled shapes are supported --
    /// stroke-only shapes under a blur filter render via the normal vector path instead
    /// (the rasterizer only fills polygons, see RasterCanvas).
    /// </summary>
    private static void RenderBlurredShape(SvgElement el, StringBuilder sb, PdfDocument pdfDoc,
        List<string> usedImages, float stdDeviation, Matrix2D matrix)
    {
        if (el.GetAttribute("fill") == "none") return;

        var localPts = ExtractShapeLocalPoints(el);
        if (localPts == null || localPts.Count < 3) return;

        var pdfPts = new List<(float x, float y)>(localPts.Count);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in localPts)
        {
            var tp = matrix.Apply(p.x, p.y);
            pdfPts.Add(tp);
            if (tp.x < minX) minX = tp.x;
            if (tp.x > maxX) maxX = tp.x;
            if (tp.y < minY) minY = tp.y;
            if (tp.y > maxY) maxY = tp.y;
        }
        if (minX > maxX || minY > maxY) return;

        // stdDeviation is in the shape's local coordinate system; approximate its scale
        // to PDF points via the matrix's area-based uniform-scale factor.
        float scale = matrix.UniformScale;
        if (scale <= 0f) return;
        float sigmaPt = stdDeviation * scale;
        float marginPt = Math.Max(1f, sigmaPt * 3f);

        const float pxPerPt = 2f; // raster resolution
        float bboxWpt = (maxX - minX) + marginPt * 2f;
        float bboxHpt = (maxY - minY) + marginPt * 2f;
        int canvasW = Math.Max(1, (int)Math.Ceiling(bboxWpt * pxPerPt));
        int canvasH = Math.Max(1, (int)Math.Ceiling(bboxHpt * pxPerPt));

        const int maxDim = 2000; // guard against pathological/degenerate shapes
        if (canvasW > maxDim || canvasH > maxDim) return;

        var canvas = new RasterCanvas(canvasW, canvasH);
        var (fr, fg, fb, fa) = ExtractFillColor(el);

        var canvasPts = new List<(float x, float y)>(pdfPts.Count);
        foreach (var p in pdfPts)
        {
            float cx = (p.x - minX + marginPt) * pxPerPt;
            float cy = (maxY - p.y + marginPt) * pxPerPt; // canvas Y grows downward, PDF Y grows upward
            canvasPts.Add((cx, cy));
        }
        canvas.FillPolygon(canvasPts, fr, fg, fb, fa);

        float sigmaPx = sigmaPt * pxPerPt;
        GaussianBlurRaster.ApplyInPlace(canvas, sigmaPx);

        string imgName = "SvgBlur" + ((uint)(el.GetHashCode() ^ (canvasW << 16) ^ canvasH)).ToString("X8");
        var pdfImage = PdfImage.FromRgba(imgName, canvasW, canvasH, canvas.Pixels);
        pdfDoc.AddImage(pdfImage);
        usedImages.Add(imgName);

        float imgXpt = minX - marginPt;
        float imgYpt = minY - marginPt;
        sb.AppendLine("q");
        sb.AppendLine($"{F(bboxWpt)} 0 0 {F(bboxHpt)} {F(imgXpt)} {F(imgYpt)} cm");
        sb.AppendLine($"/{imgName} Do");
        sb.AppendLine("Q");
    }
}
