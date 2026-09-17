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
///
/// Each box is tagged (via its ComputedStyle, the same internal-property
/// convention used elsewhere, e.g. -eggpdf-pin-bottom) with its margin-box
/// position and which page type it applies to -- "all" (from the base @page
/// rule), or "first"/"left"/"right" (from a selector-scoped @page rule).
/// BoxPainter.PaintFixedBoxes resolves, per physical page, which single box
/// wins for each position: a page-type-specific box if one exists for that
/// position (even an empty one, from an explicit `content: none` override),
/// otherwise the "all" box.
/// </summary>
internal static class MarginBoxRenderer
{
    private const float DefaultFontSize = 16f;

    public static List<LayoutBox> Build(PageSettings settings, float pageWidthPx, float pageHeightPx)
    {
        var result = new List<LayoutBox>();
        if (settings.MarginBoxes.Count == 0 && settings.FirstPageMarginBoxes.Count == 0 &&
            settings.LeftPageMarginBoxes.Count == 0 && settings.RightPageMarginBoxes.Count == 0)
            return result;

        // A fresh counter context is enough here: margin-box content only ever
        // needs counter(page)/counter(pages) (sentinel emission, no document
        // state) or literal strings -- it has no element of its own to read
        // document counter state from.
        var counterCtx = new CssCounterContext();

        BuildForSet(settings.MarginBoxes, "all", counterCtx, settings, pageWidthPx, pageHeightPx, result);
        BuildForSet(settings.FirstPageMarginBoxes, "first", counterCtx, settings, pageWidthPx, pageHeightPx, result);
        BuildForSet(settings.LeftPageMarginBoxes, "left", counterCtx, settings, pageWidthPx, pageHeightPx, result);
        BuildForSet(settings.RightPageMarginBoxes, "right", counterCtx, settings, pageWidthPx, pageHeightPx, result);

        return result;
    }

    private static void BuildForSet(Dictionary<string, PageMarginBoxSettings> boxes, string applicability,
        CssCounterContext counterCtx, PageSettings settings, float pageWidthPx, float pageHeightPx,
        List<LayoutBox> result)
    {
        foreach (var kv in boxes)
        {
            var geometry = ComputeGeometry(kv.Key, pageWidthPx, pageHeightPx,
                settings.MarginTop, settings.MarginRight, settings.MarginBottom, settings.MarginLeft);
            if (geometry == null)
                continue;
            var (bandX, bandY, bandWidth, bandHeight, textAlign) = geometry.Value;
            if (bandWidth <= 0 || bandHeight <= 0)
                continue;

            var mb = kv.Value;
            float fontSize = !string.IsNullOrEmpty(mb.FontSize)
                ? BlockLayout.ResolveLength(mb.FontSize, 0, DefaultFontSize)
                : DefaultFontSize;
            if (fontSize <= 0) fontSize = DefaultFontSize;

            var style = new ComputedStyle();
            style.Set("font-size", fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px");
            style.Set("text-align", textAlign);
            style.Set("-eggpdf-margin-box-position", kv.Key);
            style.Set("-eggpdf-page-applicability", applicability);
            if (!string.IsNullOrEmpty(mb.Color)) style.Set("color", mb.Color!);
            if (!string.IsNullOrEmpty(mb.FontFamily)) style.Set("font-family", mb.FontFamily!);
            if (!string.IsNullOrEmpty(mb.FontWeight)) style.Set("font-weight", mb.FontWeight!);

            // A page-type-specific rule whose content resolves empty (explicit
            // `content: none`, or a blank literal) still needs a box: it marks
            // "this position is intentionally suppressed for this page type",
            // distinct from "no override was declared" (which falls back to
            // the base box instead of suppressing it). PaintFixedBoxes tells
            // the two apart by Text being null vs a real (possibly empty-after-
            // trim) string -- so pass through null rather than "".
            var content = counterCtx.ResolveContent(mb.Content, element: null, style: style);

            float lineHeight = TextMeasurer.GetLineHeight(fontSize, null);
            float contentWidth = string.IsNullOrEmpty(content)
                ? 0
                : TextMeasurer.MeasureWidth(content, fontSize, style.FontFamily, style.FontWeight, style.Get("font-style"));

            float boxY = bandY + (bandHeight - lineHeight) / 2f;
            if (boxY < bandY) boxY = bandY;

            result.Add(new LayoutBox
            {
                Style = style,
                Text = content, // null when suppressed/empty -- PaintFixedBoxes paints nothing but still "claims" this position
                X = bandX,
                Y = boxY,
                Width = bandWidth,
                Height = lineHeight,
                ContentWidth = contentWidth,
                ContentHeight = lineHeight,
            });
        }
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
