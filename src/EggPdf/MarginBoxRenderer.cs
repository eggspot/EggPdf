using System.Collections.Generic;
using EggPdf.Css;
using EggPdf.Layout;

namespace EggPdf;

/// <summary>
/// Builds synthetic LayoutBox entries for @page margin boxes (@top-center,
/// @bottom-right, etc.), positioned in full-page pixel coordinates so they
/// can be repainted per page exactly like position:fixed content -- PdfRenderer
/// appends them to its fixedBoxes list, which already knows how to substitute
/// the counter(page)/counter(pages) sentinels CssCounterContext emits.
/// </summary>
internal static class MarginBoxRenderer
{
    private const float DefaultFontSize = 16f;

    public static List<LayoutBox> Build(PageSettings settings, float pageWidthPx, float pageHeightPx)
    {
        var result = new List<LayoutBox>();
        if (settings.MarginBoxes.Count == 0)
            return result;

        // A fresh counter context is enough here: margin-box content only ever
        // needs counter(page)/counter(pages) (sentinel emission, no document
        // state) or literal strings -- it has no element of its own to read
        // document counter state from.
        var counterCtx = new CssCounterContext();

        foreach (var kv in settings.MarginBoxes)
        {
            var geometry = ComputeGeometry(kv.Key, pageWidthPx, pageHeightPx,
                settings.MarginTop, settings.MarginRight, settings.MarginBottom, settings.MarginLeft);
            if (geometry == null)
                continue;

            var mb = kv.Value;
            float fontSize = !string.IsNullOrEmpty(mb.FontSize)
                ? BlockLayout.ResolveLength(mb.FontSize, 0, DefaultFontSize)
                : DefaultFontSize;
            if (fontSize <= 0) fontSize = DefaultFontSize;

            var style = new ComputedStyle();
            style.Set("font-size", fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px");
            style.Set("text-align", geometry.Value.TextAlign);
            if (!string.IsNullOrEmpty(mb.Color)) style.Set("color", mb.Color!);
            if (!string.IsNullOrEmpty(mb.FontFamily)) style.Set("font-family", mb.FontFamily!);
            if (!string.IsNullOrEmpty(mb.FontWeight)) style.Set("font-weight", mb.FontWeight!);

            var content = counterCtx.ResolveContent(mb.Content, element: null, style: style);
            if (string.IsNullOrEmpty(content))
                continue;

            float lineHeight = TextMeasurer.GetLineHeight(fontSize, null);
            float contentWidth = TextMeasurer.MeasureWidth(content, fontSize,
                style.FontFamily, style.FontWeight, style.Get("font-style"));

            var (bandX, bandY, bandWidth, bandHeight, _) = geometry.Value;
            if (bandWidth <= 0 || bandHeight <= 0)
                continue;

            float boxY = bandY + (bandHeight - lineHeight) / 2f;
            if (boxY < bandY) boxY = bandY;

            result.Add(new LayoutBox
            {
                Style = style,
                Text = content,
                X = bandX,
                Y = boxY,
                Width = bandWidth,
                Height = lineHeight,
                ContentWidth = contentWidth,
                ContentHeight = lineHeight,
            });
        }

        return result;
    }

    /// <summary>
    /// Computes a margin box's paint band in full-page pixel coordinates: the
    /// three boxes along each edge (e.g. top-left/top-center/top-right) share
    /// the full content-width band between the corners and are distinguished
    /// only by text-align, matching how they visually read on the page even
    /// though the spec models them as separately auto-sized boxes.
    /// </summary>
    private static (float X, float Y, float Width, float Height, string TextAlign)? ComputeGeometry(
        string position, float pageWidthPx, float pageHeightPx,
        float marginTop, float marginRight, float marginBottom, float marginLeft)
    {
        float contentWidth = pageWidthPx - marginLeft - marginRight;
        float contentHeight = pageHeightPx - marginTop - marginBottom;
        float bottomBandY = pageHeightPx - marginBottom;
        float rightBandX = pageWidthPx - marginRight;

        switch (position.ToLowerInvariant())
        {
            case "top-left-corner":
                return (0, 0, marginLeft, marginTop, "left");
            case "top-left":
                return (marginLeft, 0, contentWidth, marginTop, "left");
            case "top-center":
                return (marginLeft, 0, contentWidth, marginTop, "center");
            case "top-right":
                return (marginLeft, 0, contentWidth, marginTop, "right");
            case "top-right-corner":
                return (rightBandX, 0, marginRight, marginTop, "left");

            case "bottom-left-corner":
                return (0, bottomBandY, marginLeft, marginBottom, "left");
            case "bottom-left":
                return (marginLeft, bottomBandY, contentWidth, marginBottom, "left");
            case "bottom-center":
                return (marginLeft, bottomBandY, contentWidth, marginBottom, "center");
            case "bottom-right":
                return (marginLeft, bottomBandY, contentWidth, marginBottom, "right");
            case "bottom-right-corner":
                return (rightBandX, bottomBandY, marginRight, marginBottom, "left");

            case "left-top":
                return (0, marginTop, marginLeft, contentHeight / 3f, "left");
            case "left-middle":
                return (0, marginTop + contentHeight / 3f, marginLeft, contentHeight / 3f, "left");
            case "left-bottom":
                return (0, marginTop + contentHeight * 2f / 3f, marginLeft, contentHeight / 3f, "left");

            case "right-top":
                return (rightBandX, marginTop, marginRight, contentHeight / 3f, "left");
            case "right-middle":
                return (rightBandX, marginTop + contentHeight / 3f, marginRight, contentHeight / 3f, "left");
            case "right-bottom":
                return (rightBandX, marginTop + contentHeight * 2f / 3f, marginRight, contentHeight / 3f, "left");

            default:
                return null;
        }
    }
}
