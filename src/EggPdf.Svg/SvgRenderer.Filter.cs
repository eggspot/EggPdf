using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Core;
using EggPdf.Pdf;

namespace EggPdf.Svg;

/// <summary>
/// SVG filter effects (<c>filter="url(#id)"</c>). PDF has no vector filter primitive, so a
/// filtered element is rasterized -- its filled and stroked shapes, or every shape under a
/// filtered &lt;g&gt; -- run through the filter graph (<see cref="SvgFilter"/>) at ~144 dpi, and
/// embedded as an image XObject positioned over the filter region. Elements containing text,
/// images or gradient/pattern paints can't be rasterized here and paint unfiltered instead.
/// </summary>
public static partial class SvgRenderer
{
    /// <summary>Fill/stroke properties as they cascade down a filtered subtree.</summary>
    private struct FilterPaint
    {
        public string Fill, Stroke, Color;
        public float FillOpacity, StrokeWidth, StrokeOpacity, Opacity;

        public static FilterPaint Default => new FilterPaint
        {
            Fill = "black", Stroke = "none", Color = "black",
            FillOpacity = 1f, StrokeWidth = 1f, StrokeOpacity = 1f, Opacity = 1f,
        };
    }

    private sealed class FilterShape
    {
        public List<(List<(float x, float y)> points, bool closed)> Subpaths = null!;
        public FilterPaint Paint;
    }

    private static Dictionary<string, SvgFilter> CollectFilters(SvgElement root)
    {
        var filters = new Dictionary<string, SvgFilter>();
        CollectFilters(root, filters);
        return filters;
    }

    private static void CollectFilters(SvgElement el, Dictionary<string, SvgFilter> result)
    {
        if (el.TagName == "filter")
        {
            var id = el.GetAttribute("id");
            if (!string.IsNullOrEmpty(id))
            {
                var parsed = SvgFilter.TryParse(el);
                if (parsed != null) result[id] = parsed;
            }
        }
        foreach (var child in el.Children)
            CollectFilters(child, result);
    }

    private static SvgFilter? ResolveFilter(SvgElement el, Dictionary<string, SvgFilter> filters)
    {
        var value = SvgFilter.Prop(el, "filter").Trim();
        if (!value.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return null;

        int hash = value.IndexOf('#');
        if (hash < 0) return null;
        int end = value.IndexOf(')', hash);
        if (end < 0) end = value.Length;
        var id = value.Substring(hash + 1, end - hash - 1).Trim('\'', '"', ' ');
        return filters.TryGetValue(id, out var filter) ? filter : null;
    }

    private const int MaxFilterCanvas = 2000;

    /// <summary>
    /// Rasterize <paramref name="el"/> (in its own local coordinate system -- its transform and
    /// its ancestors' are already on the content stream's CTM), filter it, and emit the result
    /// as an image. Returns false when the element can't be rasterized, so the caller paints
    /// it normally.
    /// </summary>
    private static bool RenderFilteredElement(SvgElement el, StringBuilder sb, PdfDocument pdfDoc,
        List<string> usedImages, SvgFilter filter, Matrix2D matrix)
    {
        var shapes = new List<FilterShape>();
        bool supported = CollectFilterShapes(el, new Matrix2D(1, 0, 0, 1, 0, 0), FilterPaint.Default, true, shapes);
        if (!supported || shapes.Count == 0) return false;

        float scale = matrix.UniformScale;
        if (scale <= 0f) return true;

        // Bounding box of the geometry (fill area, no stroke) in local units
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var shape in shapes)
            foreach (var sub in shape.Subpaths)
                foreach (var p in sub.points)
                {
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }
        if (minX > maxX) return false;

        var (rx, ry, rw, rh) = filter.ResolveRegion(minX, minY, maxX - minX, maxY - minY);
        if (rw <= 0f || rh <= 0f) return true; // an empty filter region renders nothing

        // ~2 pixels per PDF point, reduced when the region would overflow the canvas cap
        float pxPerUnit = 2f * scale;
        float largest = Math.Max(rw, rh) * pxPerUnit;
        if (largest > MaxFilterCanvas) pxPerUnit *= MaxFilterCanvas / largest;
        // The small epsilon keeps float noise (1.2f * 20 = 24.0000005) from adding a whole extra pixel
        int canvasW = Math.Max(1, (int)Math.Ceiling(rw * pxPerUnit - 0.001f));
        int canvasH = Math.Max(1, (int)Math.Ceiling(rh * pxPerUnit - 0.001f));

        var source = new FilterImage(canvasW, canvasH);
        foreach (var shape in shapes)
            DrawFilterShape(source, shape, rx, ry, pxPerUnit);

        var ctx = new FilterRunContext(pxPerUnit, pxPerUnit, pxPerUnit, 0, 0, pxPerUnit, -rx * pxPerUnit, -ry * pxPerUnit);
        var result = filter.Run(source, ctx);

        var pixels = new byte[canvasW * canvasH * 4];
        for (int i = 0; i < canvasW * canvasH; i++)
        {
            float a = result.Px[i * 4 + 3];
            if (a <= 0.0001f) continue;
            for (int c = 0; c < 3; c++)
                pixels[i * 4 + c] = (byte)Math.Round(Math.Min(1f, result.Px[i * 4 + c] / a) * 255f);
            pixels[i * 4 + 3] = (byte)Math.Round(Math.Min(1f, a) * 255f);
        }

        string imgName = "SvgFx" + ((uint)(el.GetHashCode() ^ (canvasW << 16) ^ canvasH)).ToString("X8", CultureInfo.InvariantCulture);
        pdfDoc.AddImage(PdfImage.FromRgba(imgName, canvasW, canvasH, pixels));
        usedImages.Add(imgName);

        // The image's unit square has y up while local SVG space has y down: flip it back
        float drawW = canvasW / pxPerUnit, drawH = canvasH / pxPerUnit;
        sb.AppendLine("q");
        sb.AppendLine($"{F(drawW)} 0 0 {F(-drawH)} {F(rx)} {F(ry + drawH)} cm");
        sb.AppendLine($"/{imgName} Do");
        sb.AppendLine("Q");
        return true;
    }

    // ── Source collection ─────────────────────────────────────────────────────

    /// <summary>
    /// Gather every drawable shape under <paramref name="el"/> with its geometry in the root
    /// element's local space. Returns false when the subtree holds something that can't be
    /// rasterized (text, images, gradient/pattern paints).
    /// </summary>
    private static bool CollectFilterShapes(SvgElement el, Matrix2D rel, FilterPaint inherited, bool isRoot, List<FilterShape> shapes)
    {
        if (string.Equals(SvgFilter.Prop(el, "display"), "none", StringComparison.OrdinalIgnoreCase)) return true;

        // The filtered element's own transform is already on the CTM; descendants' are not
        if (!isRoot)
        {
            var transform = el.GetAttribute("transform");
            if (transform.Length > 0) rel = MatrixMultiply(ParseTransformToMatrix(transform), rel);
        }

        var paint = ReadFilterPaint(el, inherited);
        switch (el.TagName)
        {
            case "g": case "svg": case "a":
                foreach (var child in el.Children)
                    if (!CollectFilterShapes(child, rel, paint, false, shapes)) return false;
                return true;
            case "defs": case "filter": case "clippath": case "mask": case "symbol": case "title": case "desc": case "metadata":
                return true;
            case "text": case "tspan": case "image": case "use": case "foreignobject":
                return false;
        }

        var subpaths = ExtractShapeSubpaths(el);
        if (subpaths == null) return false;
        if (UsesUnrasterizablePaint(paint)) return false;

        var transformed = new List<(List<(float x, float y)>, bool)>(subpaths.Count);
        foreach (var (points, closed) in subpaths)
        {
            var moved = new List<(float x, float y)>(points.Count);
            foreach (var p in points) moved.Add(rel.Apply(p.x, p.y));
            transformed.Add((moved, closed));
        }
        shapes.Add(new FilterShape { Subpaths = transformed, Paint = paint });
        return true;
    }

    private static bool UsesUnrasterizablePaint(FilterPaint p)
        => p.Fill.StartsWith("url(", StringComparison.OrdinalIgnoreCase) || p.Stroke.StartsWith("url(", StringComparison.OrdinalIgnoreCase);

    private static FilterPaint ReadFilterPaint(SvgElement el, FilterPaint parent)
    {
        var paint = parent;
        var v = SvgFilter.Prop(el, "fill"); if (v.Length > 0) paint.Fill = v;
        v = SvgFilter.Prop(el, "stroke"); if (v.Length > 0) paint.Stroke = v;
        v = SvgFilter.Prop(el, "color"); if (v.Length > 0) paint.Color = v;
        paint.FillOpacity = ReadFraction(SvgFilter.Prop(el, "fill-opacity"), paint.FillOpacity);
        paint.StrokeOpacity = ReadFraction(SvgFilter.Prop(el, "stroke-opacity"), paint.StrokeOpacity);
        v = SvgFilter.Prop(el, "stroke-width");
        if (v.Length > 0 && float.TryParse(v.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float sw)) paint.StrokeWidth = sw;
        // opacity is not inherited but composes down the tree
        paint.Opacity = parent.Opacity * ReadFraction(SvgFilter.Prop(el, "opacity"), 1f);
        return paint;
    }

    private static float ReadFraction(string value, float fallback)
    {
        if (value.Length == 0) return fallback;
        bool percent = value.EndsWith("%", StringComparison.Ordinal);
        if (percent) value = value.Substring(0, value.Length - 1);
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float n)) return fallback;
        return Math.Max(0f, Math.Min(1f, percent ? n / 100f : n));
    }

    private static Color? ResolvePaintColor(string paint, string currentColor)
    {
        var v = paint.Trim();
        if (v.Length == 0 || v.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (v.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) v = currentColor;
        return Color.TryParse(v) ?? Color.Black;
    }

    // ── Rasterizing the source graphic ───────────────────────────────────────

    private static void DrawFilterShape(FilterImage dst, FilterShape shape, float originX, float originY, float pxPerUnit)
    {
        var paint = shape.Paint;
        var canvasPaths = new List<(List<(float x, float y)> points, bool closed)>(shape.Subpaths.Count);
        foreach (var (points, closed) in shape.Subpaths)
        {
            var mapped = new List<(float x, float y)>(points.Count);
            foreach (var p in points) mapped.Add(((p.x - originX) * pxPerUnit, (p.y - originY) * pxPerUnit));
            canvasPaths.Add((mapped, closed));
        }

        var fill = ResolvePaintColor(paint.Fill, paint.Color);
        if (fill.HasValue)
        {
            var layer = new RasterCanvas(dst.Width, dst.Height);
            foreach (var (points, _) in canvasPaths)
                if (points.Count >= 3) layer.FillPolygon(points, fill.Value.R, fill.Value.G, fill.Value.B, 255);
            CompositeLayer(dst, layer, paint.FillOpacity * paint.Opacity * (fill.Value.A / 255f));
        }

        var stroke = ResolvePaintColor(paint.Stroke, paint.Color);
        float widthPx = paint.StrokeWidth * pxPerUnit;
        if (stroke.HasValue && widthPx > 0f)
        {
            var layer = new RasterCanvas(dst.Width, dst.Height);
            foreach (var (points, closed) in canvasPaths)
                StrokePolyline(layer, points, closed, widthPx, stroke.Value);
            CompositeLayer(dst, layer, paint.StrokeOpacity * paint.Opacity * (stroke.Value.A / 255f));
        }
    }

    /// <summary>Stroke a polyline as one quad per segment plus a round join disc at each joint (butt caps).</summary>
    private static void StrokePolyline(RasterCanvas layer, List<(float x, float y)> pts, bool closed, float width, Color color)
    {
        int n = pts.Count;
        if (closed && n > 1 && pts[0] == pts[n - 1]) n--; // a path's closing 'Z' repeats the first point
        int segments = closed ? n : n - 1;
        float half = width / 2f;

        for (int i = 0; i < segments; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % n];
            float dx = b.x - a.x, dy = b.y - a.y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len <= 0f) continue;
            float nx = -dy / len * half, ny = dx / len * half;
            layer.FillPolygon(new List<(float x, float y)>
            {
                (a.x + nx, a.y + ny), (b.x + nx, b.y + ny), (b.x - nx, b.y - ny), (a.x - nx, a.y - ny),
            }, color.R, color.G, color.B, 255);
        }

        if (half < 0.75f) return; // hairlines don't need round joins
        int firstJoint = closed ? 0 : 1, lastJoint = closed ? n - 1 : n - 2;
        for (int i = firstJoint; i <= lastJoint; i++)
            layer.FillPolygon(ExtractEllipsePoints(pts[i].x, pts[i].y, half, half, 16), color.R, color.G, color.B, 255);
    }

    /// <summary>Source-over a straight-alpha raster layer, scaled by <paramref name="opacity"/>, onto a premultiplied float image.</summary>
    private static void CompositeLayer(FilterImage dst, RasterCanvas layer, float opacity)
    {
        var src = layer.Pixels;
        for (int i = 0; i < dst.Width * dst.Height; i++)
        {
            float a = src[i * 4 + 3] / 255f * opacity;
            if (a <= 0f) continue;
            float inv = 1f - a;
            dst.Px[i * 4] = src[i * 4] / 255f * a + dst.Px[i * 4] * inv;
            dst.Px[i * 4 + 1] = src[i * 4 + 1] / 255f * a + dst.Px[i * 4 + 1] * inv;
            dst.Px[i * 4 + 2] = src[i * 4 + 2] / 255f * a + dst.Px[i * 4 + 2] * inv;
            dst.Px[i * 4 + 3] = a + dst.Px[i * 4 + 3] * inv;
        }
    }
}
