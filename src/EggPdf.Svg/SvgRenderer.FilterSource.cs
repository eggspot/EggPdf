using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Core;
using EggPdf.Pdf;
using EggPdf.Text;
using EggPdf.Text.TrueType;

namespace EggPdf.Svg;

public static partial class SvgRenderer
{
    private static readonly FontResolver FilterFontResolver = new FontResolver();

    private const float HairlineHalfWidth = 0.75f;

    /// <summary>
    /// Fill/stroke properties as they cascade down a filtered subtree, whose shapes, text (via glyph outlines) and
    /// gradient paints are rasterized into the filter's premultiplied float source graphic. opacity is not inherited
    /// but composes down the tree.
    /// </summary>
    private struct FilterPaint
    {
        public string Fill, Stroke, Color;
        public float FillOpacity, StrokeWidth, StrokeOpacity, Opacity;

        /// <summary><c>fill-rule: evenodd</c>; SVG's default is nonzero.</summary>
        public bool EvenOdd;

        public static FilterPaint Default => new FilterPaint
        {
            Fill = "black", Stroke = "none", Color = "black",
            FillOpacity = 1f, StrokeWidth = 1f, StrokeOpacity = 1f, Opacity = 1f,
        };
    }

    private sealed class FilterShape
    {
        /// <summary>Outline polylines in the filtered element's local space, each flagged closed or open.</summary>
        public List<(List<(float x, float y)> points, bool closed)> Subpaths = null!;
        public FilterPaint Paint;
        /// <summary>The shape's own user space -> filtered-element local space (gradients are defined in the former).</summary>
        public Matrix2D Rel;
        /// <summary>Bounding box of the geometry in the shape's own user space (objectBoundingBox gradient units).</summary>
        public float BoxX, BoxY, BoxW, BoxH;
    }

    /// <summary>
    /// Gather every drawable shape under <paramref name="el"/> with its geometry in the root
    /// element's local space. Returns false when the subtree holds something that can't be
    /// rasterized (raster images, pattern paints, text without an installed font). The filtered element's own
    /// transform is already on the CTM; its descendants' are applied here.
    /// </summary>
    private static bool CollectFilterShapes(SvgElement el, Matrix2D rel, FilterPaint inherited, bool isRoot,
        SvgDefs defs, List<FilterShape> shapes)
    {
        if (string.Equals(SvgFilter.Prop(el, "display"), "none", StringComparison.OrdinalIgnoreCase)) return true;

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
                    if (!CollectFilterShapes(child, rel, paint, false, defs, shapes)) return false;
                return true;
            case "defs": case "filter": case "clippath": case "mask": case "symbol": case "title": case "desc": case "metadata":
                return true;
            case "image": case "foreignobject":
                return false;
            case "use":
                return CollectUse(el, rel, paint, defs, shapes);
            case "text":
            {
                float penX = 0, penY = 0;
                return AddTextShapes(el, rel, paint, defs, shapes, ref penX, ref penY);
            }
        }

        var subpaths = ExtractShapeSubpaths(el);
        if (subpaths == null) return false;
        if (!PaintsAreRasterizable(paint, defs)) return false;

        var (bx, by, bw, bh) = BoundsOf(subpaths);
        shapes.Add(new FilterShape { Subpaths = MapSubpaths(subpaths, rel), Paint = paint, Rel = rel, BoxX = bx, BoxY = by, BoxW = bw, BoxH = bh });
        return true;
    }

    private static bool CollectUse(SvgElement el, Matrix2D rel, FilterPaint paint, SvgDefs defs, List<FilterShape> shapes)
    {
        var href = el.GetAttribute("href");
        if (href.Length == 0) href = el.GetAttribute("xlink:href");
        if (href.Length < 2 || href[0] != '#' || !defs.Ids.TryGetValue(href.Substring(1), out var target)) return false;

        float dx = GetFloat(el, "x"), dy = GetFloat(el, "y");
        if (dx != 0f || dy != 0f) rel = MatrixMultiply(new Matrix2D(1, 0, 0, 1, dx, dy), rel);
        return CollectFilterShapes(target, rel, paint, false, defs, shapes);
    }

    private static List<(List<(float x, float y)> points, bool closed)> MapSubpaths(
        List<(List<(float x, float y)> points, bool closed)> subpaths, Matrix2D rel)
    {
        var mapped = new List<(List<(float x, float y)>, bool)>(subpaths.Count);
        foreach (var (points, closed) in subpaths)
        {
            var moved = new List<(float x, float y)>(points.Count);
            foreach (var p in points) moved.Add(rel.Apply(p.x, p.y));
            mapped.Add((moved, closed));
        }
        return mapped;
    }

    private static (float x, float y, float w, float h) BoundsOf(List<(List<(float x, float y)> points, bool closed)> subpaths)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var (points, _) in subpaths)
            foreach (var p in points)
            {
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x);
                minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
            }
        return minX > maxX ? (0, 0, 0, 0) : (minX, minY, maxX - minX, maxY - minY);
    }

    private static bool PaintsAreRasterizable(FilterPaint p, SvgDefs defs)
        => IsResolvablePaint(p.Fill, defs) && IsResolvablePaint(p.Stroke, defs);

    private static bool IsResolvablePaint(string paint, SvgDefs defs)
    {
        if (!paint.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return true;
        var id = PaintReferenceId(paint);
        return id != null && defs.Ids.TryGetValue(id, out var target) && (target.TagName == "lineargradient" || target.TagName == "radialgradient");
    }

    private static string? PaintReferenceId(string paint)
    {
        int hash = paint.IndexOf('#');
        if (hash < 0) return null;
        int end = paint.IndexOf(')', hash);
        if (end < 0) end = paint.Length;
        return paint.Substring(hash + 1, end - hash - 1).Trim('\'', '"', ' ');
    }

    private static FilterPaint ReadFilterPaint(SvgElement el, FilterPaint parent)
    {
        var paint = parent;
        var v = SvgFilter.Prop(el, "fill"); if (v.Length > 0) paint.Fill = v;
        v = SvgFilter.Prop(el, "stroke"); if (v.Length > 0) paint.Stroke = v;
        v = SvgFilter.Prop(el, "color"); if (v.Length > 0) paint.Color = v;
        v = SvgFilter.Prop(el, "fill-rule").Trim();
        if (v.Equals("evenodd", StringComparison.OrdinalIgnoreCase)) paint.EvenOdd = true;
        else if (v.Equals("nonzero", StringComparison.OrdinalIgnoreCase)) paint.EvenOdd = false;
        paint.FillOpacity = ReadFraction(SvgFilter.Prop(el, "fill-opacity"), paint.FillOpacity);
        paint.StrokeOpacity = ReadFraction(SvgFilter.Prop(el, "stroke-opacity"), paint.StrokeOpacity);
        v = SvgFilter.Prop(el, "stroke-width");
        if (v.Length > 0 && float.TryParse(v.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float sw)) paint.StrokeWidth = sw;
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

    /// <summary>
    /// Turn a &lt;text&gt; element (and its &lt;tspan&gt; children) into glyph-outline shapes using the installed font
    /// for its family. Advances come from the font's hmtx table (no kerning or shaping); font y runs up, so it is
    /// flipped. Returns false when no suitable font is installed, so the element paints unfiltered instead of
    /// vanishing.
    /// </summary>
    private static bool AddTextShapes(SvgElement el, Matrix2D rel, FilterPaint paint, SvgDefs defs,
        List<FilterShape> shapes, ref float penX, ref float penY)
    {
        if (!PaintsAreRasterizable(paint, defs)) return false;

        float x = FirstNumber(el.GetAttribute("x"), float.NaN), y = FirstNumber(el.GetAttribute("y"), float.NaN);
        if (!float.IsNaN(x)) penX = x;
        if (!float.IsNaN(y)) penY = y;
        penX += FirstNumber(el.GetAttribute("dx"), 0f);
        penY += FirstNumber(el.GetAttribute("dy"), 0f);

        string text = CollapseWhitespace(el.TextContent);
        if (text.Length > 0)
        {
            var font = ResolveTextFont(el);
            if (font == null || font.UnitsPerEm <= 0) return false;

            float size = 16f;
            var fs = SvgFilter.Prop(el, "font-size");
            if (fs.Length > 0 && float.TryParse(fs.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedSize) && parsedSize > 0f) size = parsedSize;
            float scale = size / font.UnitsPerEm;

            var codePoints = CodePoints(text);
            float width = 0f;
            foreach (int codePoint in codePoints) width += font.GetAdvanceWidth(font.GetGlyphId(codePoint)) * scale;
            var anchor = SvgFilter.Prop(el, "text-anchor");
            float startX = anchor == "middle" ? penX - width / 2f : anchor == "end" ? penX - width : penX;

            var glyphPaint = paint;
            glyphPaint.EvenOdd = false;
            float cursor = startX;
            foreach (int codePoint in codePoints)
            {
                ushort gid = font.GetGlyphId(codePoint);
                var contours = GlyphOutlines.Get(font, gid);
                if (contours.Count > 0)
                {
                    var subpaths = new List<(List<(float x, float y)>, bool)>(contours.Count);
                    foreach (var contour in contours)
                    {
                        var pts = new List<(float x, float y)>(contour.Count);
                        foreach (var p in contour) pts.Add((cursor + p.x * scale, penY - p.y * scale));
                        subpaths.Add((pts, true));
                    }
                    var (bx, by, bw, bh) = BoundsOf(subpaths);
                    shapes.Add(new FilterShape { Subpaths = MapSubpaths(subpaths, rel), Paint = glyphPaint, Rel = rel, BoxX = bx, BoxY = by, BoxW = bw, BoxH = bh });
                }
                cursor += font.GetAdvanceWidth(gid) * scale;
            }
            penX = cursor;
        }

        foreach (var child in el.Children)
        {
            if (child.TagName != "tspan") continue;
            if (!AddTextShapes(child, rel, ReadFilterPaint(child, paint), defs, shapes, ref penX, ref penY)) return false;
        }
        return true;
    }

    private static FontData? ResolveTextFont(SvgElement el)
    {
        var weight = SvgFilter.Prop(el, "font-weight").Trim().ToLowerInvariant();
        bool bold = weight == "bold" || weight == "bolder" || (int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) && w >= 600);
        var style = SvgFilter.Prop(el, "font-style").Trim().ToLowerInvariant();
        bool italic = style == "italic" || style == "oblique";

        var families = SvgFilter.Prop(el, "font-family");
        if (families.Length == 0) families = "sans-serif";
        foreach (var raw in families.Split(','))
        {
            var family = raw.Trim().Trim('"', '\'');
            if (family.Length == 0) continue;
            var font = FilterFontResolver.Resolve(family, bold, italic);
            if (font != null) return VariableFontInstancer.InstanceForWeight(font, bold ? 700 : 400);
        }
        var fallback = FilterFontResolver.Resolve("sans-serif", bold, italic);
        return fallback == null ? null : VariableFontInstancer.InstanceForWeight(fallback, bold ? 700 : 400);
    }

    /// <summary>The text's Unicode code points, with surrogate pairs combined and a lone surrogate kept as itself.</summary>
    private static List<int> CodePoints(string text)
    {
        var codePoints = new List<int>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoints.Add(char.ConvertToUtf32(text[i], text[i + 1]));
                i++;
            }
            else codePoints.Add(text[i]);
        }
        return codePoints;
    }

    private static float FirstNumber(string list, float fallback)
    {
        if (list.Length == 0) return fallback;
        var first = list.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return first.Length > 0 && float.TryParse(first[0].Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : fallback;
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text.Trim())
        {
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static void DrawFilterShape(FilterImage dst, FilterShape shape, float originX, float originY, float pxPerUnit, SvgDefs defs)
    {
        var paint = shape.Paint;
        var canvasPaths = new List<(List<(float x, float y)> points, bool closed)>(shape.Subpaths.Count);
        foreach (var (points, closed) in shape.Subpaths)
        {
            var mapped = new List<(float x, float y)>(points.Count);
            foreach (var p in points) mapped.Add(((p.x - originX) * pxPerUnit, (p.y - originY) * pxPerUnit));
            canvasPaths.Add((mapped, closed));
        }

        DrawPaint(dst, shape, paint.Fill, paint.FillOpacity, canvasPaths, originX, originY, pxPerUnit, defs, stroke: false);
        if (paint.StrokeWidth * pxPerUnit > 0f)
            DrawPaint(dst, shape, paint.Stroke, paint.StrokeOpacity, canvasPaths, originX, originY, pxPerUnit, defs, stroke: true);
    }

    private static void DrawPaint(FilterImage dst, FilterShape shape, string paintValue, float paintOpacity,
        List<(List<(float x, float y)> points, bool closed)> canvasPaths, float originX, float originY, float pxPerUnit, SvgDefs defs, bool stroke)
    {
        var paint = shape.Paint;
        GradientPaint? gradient = null;
        Color? solid = null;
        if (paintValue.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            gradient = GradientPaint.TryCreate(PaintReferenceId(paintValue), defs, shape);
            if (gradient == null) return;
        }
        else
        {
            solid = ResolvePaintColor(paintValue, paint.Color);
            if (!solid.HasValue) return;
        }

        var layer = new RasterCanvas(dst.Width, dst.Height);
        byte r = solid?.R ?? 255, g = solid?.G ?? 255, b = solid?.B ?? 255;
        if (!stroke)
        {
            var contours = new List<IReadOnlyList<(float x, float y)>>(canvasPaths.Count);
            foreach (var (points, _) in canvasPaths) contours.Add(points);
            layer.FillContours(contours, paint.EvenOdd, r, g, b, 255);
        }
        else
        {
            float widthPx = paint.StrokeWidth * pxPerUnit;
            foreach (var (points, closed) in canvasPaths)
                StrokePolyline(layer, points, closed, widthPx, r, g, b);
        }

        float opacity = paintOpacity * paint.Opacity * (solid.HasValue ? solid.Value.A / 255f : 1f);
        if (gradient != null) ColorizeLayer(layer, gradient, shape, originX, originY, pxPerUnit);
        CompositeLayer(dst, layer, opacity);
    }

    /// <summary>
    /// Replace a white coverage layer's colour with the gradient's colour at each covered pixel, mapping each pixel
    /// centre to the filtered element's local space and then to the shape's own user space.
    /// </summary>
    private static void ColorizeLayer(RasterCanvas layer, GradientPaint gradient, FilterShape shape, float originX, float originY, float pxPerUnit)
    {
        var px = layer.Pixels;
        var inverse = InvertMatrix(shape.Rel);
        for (int y = 0; y < layer.Height; y++)
        {
            for (int x = 0; x < layer.Width; x++)
            {
                int i = (y * layer.Width + x) * 4;
                if (px[i + 3] == 0) continue;
                float lx = originX + (x + 0.5f) / pxPerUnit, ly = originY + (y + 0.5f) / pxPerUnit;
                var (ux, uy) = inverse.Apply(lx, ly);
                var (cr, cg, cb, ca) = gradient.ColorAt(ux, uy);
                px[i] = cr; px[i + 1] = cg; px[i + 2] = cb;
                px[i + 3] = (byte)Math.Round(px[i + 3] * ca);
            }
        }
    }

    private static Matrix2D InvertMatrix(Matrix2D m)
    {
        float det = m.A * m.D - m.B * m.C;
        if (Math.Abs(det) < 1e-9f) return new Matrix2D(1, 0, 0, 1, 0, 0);
        float a = m.D / det, b = -m.B / det, c = -m.C / det, d = m.A / det;
        float e = -(a * m.E + c * m.F), f = -(b * m.E + d * m.F);
        return new Matrix2D(a, b, c, d, e, f);
    }

    /// <summary>
    /// Stroke a polyline as one quad per segment plus a round join disc at each joint (butt caps); hairlines get no
    /// joins. A path's closing 'Z' repeats the first point, which is dropped.
    /// </summary>
    private static void StrokePolyline(RasterCanvas layer, List<(float x, float y)> pts, bool closed, float width, byte r, byte g, byte b)
    {
        int n = pts.Count;
        if (closed && n > 1 && pts[0] == pts[n - 1]) n--;
        int segments = closed ? n : n - 1;
        float half = width / 2f;

        for (int i = 0; i < segments; i++)
        {
            var a = pts[i];
            var c = pts[(i + 1) % n];
            float dx = c.x - a.x, dy = c.y - a.y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len <= 0f) continue;
            float nx = -dy / len * half, ny = dx / len * half;
            layer.FillPolygon(new List<(float x, float y)>
            {
                (a.x + nx, a.y + ny), (c.x + nx, c.y + ny), (c.x - nx, c.y - ny), (a.x - nx, a.y - ny),
            }, r, g, b, 255);
        }

        if (half < HairlineHalfWidth) return;
        int firstJoint = closed ? 0 : 1, lastJoint = closed ? n - 1 : n - 2;
        for (int i = firstJoint; i <= lastJoint; i++)
            layer.FillPolygon(ExtractEllipsePoints(pts[i].x, pts[i].y, half, half, 16), r, g, b, 255);
    }
}
