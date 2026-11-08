using System;
using System.Globalization;
using EggPdf.Layout;
using EggPdf.Pdf;
using EggPdf.Svg;

namespace EggPdf.Paint;

public static partial class BoxPainter
{
    private const int MaxBackgroundTiles = 4096;
    private const float DefaultSvgBackgroundWidthPx = 300f;
    private const float DefaultSvgBackgroundHeightPx = 150f;

    private static void PaintUrlBackground(PdfPage page, LayoutBox box, string url, byte[] data,
        float effectiveX, float pageHeightPx, float adjustedY)
    {
        string imgName = "BgImg" + url.GetHashCode().ToString("X8", CultureInfo.InvariantCulture);
        var pdfImage = PdfImage.FromEncodedImage(imgName, data);
        var svgRoot = pdfImage == null ? SvgImageDocument.ParseRoot(data) : null;
        if (pdfImage == null && svgRoot == null) return;

        float naturalWidth, naturalHeight;
        if (pdfImage != null)
        {
            CurrentPdfDoc?.AddImage(pdfImage);
            naturalWidth = pdfImage.Width;
            naturalHeight = pdfImage.Height;
        }
        else
        {
            (naturalWidth, naturalHeight) = BlockLayout.SvgIntrinsicSize(svgRoot!) ?? (DefaultSvgBackgroundWidthPx, DefaultSvgBackgroundHeightPx);
        }
        if (naturalWidth <= 0f || naturalHeight <= 0f || box.Width <= 0f || box.Height <= 0f) return;

        var (tileWidth, tileHeight) = BackgroundTileSize(FirstLayer(box.Style.Get("background-size")), box.Width, box.Height, naturalWidth, naturalHeight);
        if (tileWidth <= 0f || tileHeight <= 0f) return;
        var (offsetX, offsetY) = BackgroundOffset(FirstLayer(box.Style.Get("background-position")), box.Width, box.Height, tileWidth, tileHeight);
        var (repeatX, repeatY) = BackgroundRepeat(FirstLayer(box.Style.Get("background-repeat")));

        float startX = repeatX ? offsetX - (float)Math.Ceiling(offsetX / tileWidth) * tileWidth : offsetX;
        float startY = repeatY ? offsetY - (float)Math.Ceiling(offsetY / tileHeight) * tileHeight : offsetY;
        float endX = repeatX ? box.Width : offsetX + tileWidth;
        float endY = repeatY ? box.Height : offsetY + tileHeight;

        page.SaveState();
        page.AddClipRect(effectiveX * PdfCoordinates.PxToPt, (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt,
            box.Width * PdfCoordinates.PxToPt, box.Height * PdfCoordinates.PxToPt);
        int tiles = 0;
        for (float ty = startY; ty < endY && tiles < MaxBackgroundTiles; ty += tileHeight)
        {
            for (float tx = startX; tx < endX && tiles < MaxBackgroundTiles; tx += tileWidth)
            {
                float pdfX = (effectiveX + tx) * PdfCoordinates.PxToPt;
                float pdfY = (pageHeightPx - adjustedY - ty - tileHeight) * PdfCoordinates.PxToPt;
                float pdfW = tileWidth * PdfCoordinates.PxToPt;
                float pdfH = tileHeight * PdfCoordinates.PxToPt;
                if (pdfImage != null)
                    page.AddImage(imgName, pdfX, pdfY, pdfW, pdfH);
                else
                    PaintSvgTile(page, svgRoot!, pdfX, pdfY, pdfW, pdfH);
                tiles++;
            }
        }
        page.RestoreState();
    }

    private static void PaintSvgTile(PdfPage page, Html.Dom.HtmlElement svgRoot, float pdfX, float pdfY, float pdfW, float pdfH)
    {
        var svg = SvgParser.Parse(svgRoot);
        if (svg == null) return;
        var result = SvgRenderer.RenderWithResources(svg, pdfX, pdfY, pdfW, pdfH, CurrentPdfDoc, StandardFontMetrics.MeasureWidth);
        page.AppendRawContent(result.Commands);
        foreach (var image in result.UsedImages) page.RegisterUsedImage(image);
        foreach (var font in result.UsedFonts) page.RegisterUsedFont(font);
        foreach (var state in result.UsedExtGStates) page.RegisterUsedExtGState(state);
    }

    private static string FirstLayer(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        int depth = 0;
        for (int i = 0; i < value!.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            else if (value[i] == ',' && depth == 0) return value.Substring(0, i).Trim();
        }
        return value.Trim();
    }

    internal static (float Width, float Height) BackgroundTileSize(string size, float areaWidth, float areaHeight,
        float naturalWidth, float naturalHeight)
    {
        if (size == "cover" || size == "contain")
        {
            float scaleX = areaWidth / naturalWidth, scaleY = areaHeight / naturalHeight;
            float scale = size == "cover" ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            return (naturalWidth * scale, naturalHeight * scale);
        }

        var parts = size.Split(SpaceSep, StringSplitOptions.RemoveEmptyEntries);
        float? width = parts.Length >= 1 && parts[0] != "auto" ? ResolveOptionalLength(parts[0], areaWidth) : null;
        float? height = parts.Length >= 2 && parts[1] != "auto" ? ResolveOptionalLength(parts[1], areaHeight) : null;
        if (width.HasValue && height.HasValue) return (width.Value, height.Value);
        if (width.HasValue) return (width.Value, width.Value * naturalHeight / naturalWidth);
        if (height.HasValue) return (height.Value * naturalWidth / naturalHeight, height.Value);
        return (naturalWidth, naturalHeight);
    }

    internal static (float X, float Y) BackgroundOffset(string position, float areaWidth, float areaHeight, float tileWidth, float tileHeight)
    {
        var parts = position.Split(SpaceSep, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return (0f, 0f);
        string? horizontal = null, vertical = null;
        if (parts.Length == 1)
        {
            if (parts[0] == "top" || parts[0] == "bottom") vertical = parts[0];
            else horizontal = parts[0];
        }
        else if (parts.Length >= 2)
        {
            bool swapped = parts[0] == "top" || parts[0] == "bottom" || parts[1] == "left" || parts[1] == "right";
            horizontal = swapped ? parts[1] : parts[0];
            vertical = swapped ? parts[0] : parts[1];
        }
        return (AxisOffset(horizontal, "left", "right", areaWidth - tileWidth, areaWidth),
                AxisOffset(vertical, "top", "bottom", areaHeight - tileHeight, areaHeight));
    }

    private static float AxisOffset(string? value, string startKeyword, string endKeyword, float freeSpace, float areaSize)
    {
        if (string.IsNullOrEmpty(value) || value == "center") return freeSpace / 2f;
        if (value == startKeyword) return 0f;
        if (value == endKeyword) return freeSpace;
        if (value!.EndsWith("%", StringComparison.Ordinal) &&
            float.TryParse(value.Substring(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float percent))
            return freeSpace * percent / 100f;
        return ResolveOptionalLength(value, areaSize) ?? 0f;
    }

    internal static (bool X, bool Y) BackgroundRepeat(string repeat)
    {
        var parts = repeat.Split(SpaceSep, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return (true, true);
        if (parts.Length == 1)
        {
            switch (parts[0])
            {
                case "no-repeat": return (false, false);
                case "repeat-x": return (true, false);
                case "repeat-y": return (false, true);
                default: return (true, true);
            }
        }
        return (parts[0] != "no-repeat", parts[1] != "no-repeat");
    }
}
