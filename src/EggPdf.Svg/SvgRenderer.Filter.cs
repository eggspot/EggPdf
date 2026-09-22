using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Core;
using EggPdf.Pdf;

namespace EggPdf.Svg;

/// <summary>
/// SVG filter effects (<c>filter="url(#id)"</c>). PDF has no vector filter primitive, so a
/// filtered element is rasterized -- its filled and stroked shapes, text, or every shape under a
/// filtered &lt;g&gt; -- run through the filter graph (<see cref="SvgFilter"/>) at ~144 dpi, and
/// embedded as an image XObject positioned over the filter region. Elements containing raster
/// images or pattern paints can't be rasterized here and paint unfiltered instead.
/// Source collection (shapes, text, gradients) lives in SvgRenderer.FilterSource.cs.
/// </summary>
public static partial class SvgRenderer
{
    /// <summary>Document-wide lookups gathered once per SVG: usable filters, and every element by id.</summary>
    private sealed class SvgDefs
    {
        public readonly Dictionary<string, SvgFilter> Filters = new Dictionary<string, SvgFilter>();
        public readonly Dictionary<string, SvgElement> Ids = new Dictionary<string, SvgElement>();
    }

    private static SvgDefs CollectDefs(SvgElement root)
    {
        var defs = new SvgDefs();
        CollectDefs(root, defs);
        return defs;
    }

    private static void CollectDefs(SvgElement el, SvgDefs defs)
    {
        var id = el.GetAttribute("id");
        if (!string.IsNullOrEmpty(id))
        {
            defs.Ids[id] = el;
            if (el.TagName == "filter")
            {
                var parsed = SvgFilter.TryParse(el);
                if (parsed != null) defs.Filters[id] = parsed;
            }
        }
        foreach (var child in el.Children)
            CollectDefs(child, defs);
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
        List<string> usedImages, SvgFilter filter, SvgDefs defs, Matrix2D matrix)
    {
        var shapes = new List<FilterShape>();
        bool supported = CollectFilterShapes(el, new Matrix2D(1, 0, 0, 1, 0, 0), FilterPaint.Default, true, defs, shapes);
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
            DrawFilterShape(source, shape, rx, ry, pxPerUnit, defs);

        var images = new FilterImageSource
        {
            RenderElement = id => RenderReferencedElement(id, defs, canvasW, canvasH, rx, ry, pxPerUnit),
            LoadBitmap = LoadFilterBitmap,
        };
        var ctx = new FilterRunContext(pxPerUnit, pxPerUnit, pxPerUnit, 0, 0, pxPerUnit, -rx * pxPerUnit, -ry * pxPerUnit, images);
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

    /// <summary>feImage href="#id": rasterize that element in the filtered element's user space.</summary>
    private static FilterImage? RenderReferencedElement(string id, SvgDefs defs, int width, int height,
        float originX, float originY, float pxPerUnit)
    {
        if (!defs.Ids.TryGetValue(id, out var target)) return null;

        var shapes = new List<FilterShape>();
        if (!CollectFilterShapes(target, new Matrix2D(1, 0, 0, 1, 0, 0), FilterPaint.Default, false, defs, shapes)) return null;

        var image = new FilterImage(width, height);
        foreach (var shape in shapes) DrawFilterShape(image, shape, originX, originY, pxPerUnit, defs);
        return image;
    }

    /// <summary>Decode a data: URI holding a PNG, GIF, BMP or WebP into a premultiplied bitmap; null for anything else.</summary>
    private static FilterImage? LoadFilterBitmap(string href)
    {
        int comma = href.IndexOf(',');
        if (!href.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || comma < 0) return null;

        byte[] bytes;
        try
        {
            var payload = href.Substring(comma + 1);
            bytes = href.Substring(0, comma).IndexOf(";base64", StringComparison.OrdinalIgnoreCase) >= 0
                ? Convert.FromBase64String(payload)
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
        }
        catch (FormatException)
        {
            return null;
        }

        PdfImage? decoded = null;
        if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 'P') decoded = PdfImage.FromPng("fe", bytes);
        else if (bytes.Length > 6 && bytes[0] == 'G' && bytes[1] == 'I') decoded = PdfImage.FromGif("fe", bytes);
        else if (bytes.Length > 2 && bytes[0] == 'B' && bytes[1] == 'M') decoded = PdfImage.FromBmp("fe", bytes);
        else if (bytes.Length > 12 && bytes[0] == 'R' && bytes[1] == 'I') decoded = PdfImage.FromWebP("fe", bytes);
        if (decoded == null || decoded.Format != PdfImageFormat.Raw) return null; // JPEG stays DCT-compressed here

        var image = new FilterImage(decoded.Width, decoded.Height);
        for (int i = 0; i < decoded.Width * decoded.Height; i++)
        {
            float a = decoded.SMaskData != null ? decoded.SMaskData[i] / 255f : 1f;
            for (int c = 0; c < 3; c++) image.Px[i * 4 + c] = decoded.Data[i * 3 + c] / 255f * a;
            image.Px[i * 4 + 3] = a;
        }
        return image;
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
