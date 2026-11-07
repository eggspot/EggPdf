using System.Globalization;

namespace EggPdf.Css;

internal static class ComputedFontMetrics
{
    internal const float InitialFontSizePx = 16f;
    private const float FontSizeStepRatio = 1.2f;

    internal static float ComputeFontSizeAndLineHeight(ComputedStyle style, ComputedStyle? parentStyle, float rootFontSizePx)
    {
        float parentFontSizePx = parentStyle != null && TryParsePx(parentStyle.FontSize, out float parentPx)
            ? parentPx
            : InitialFontSizePx;

        float fontSizePx = parentFontSizePx;
        var specifiedFontSize = style.FontSize;
        if (TryParsePx(specifiedFontSize, out float alreadyPx))
        {
            fontSizePx = alreadyPx;
        }
        else if (!string.IsNullOrEmpty(specifiedFontSize) &&
                 TryResolveFontSize(specifiedFontSize!.Trim(), parentFontSizePx, rootFontSizePx, out float resolved))
        {
            fontSizePx = resolved;
            style.Set("font-size", FormatPx(resolved));
        }

        var lineHeight = style.Get("line-height");
        if (!string.IsNullOrEmpty(lineHeight) && !TryParsePx(lineHeight, out _) &&
            TryResolveLineHeightLength(lineHeight!.Trim(), fontSizePx, rootFontSizePx, out float lineHeightPx))
        {
            style.Set("line-height", FormatPx(lineHeightPx));
        }

        return fontSizePx;
    }

    internal static bool TryParsePx(string? value, out float px)
    {
        px = 0f;
        if (string.IsNullOrEmpty(value) || value!.Length < 3 || !value.EndsWith("px", System.StringComparison.OrdinalIgnoreCase))
            return false;
        return CssLengthUnits.TryParseNumber(value, value.Length - 2, out px);
    }

    private static bool TryResolveFontSize(string value, float parentPx, float rootPx, out float px)
    {
        if (TryAbsoluteSizeKeyword(value, out px)) return true;
        if (string.Equals(value, "smaller", System.StringComparison.OrdinalIgnoreCase))
        {
            px = parentPx / FontSizeStepRatio;
            return true;
        }
        if (string.Equals(value, "larger", System.StringComparison.OrdinalIgnoreCase))
        {
            px = parentPx * FontSizeStepRatio;
            return true;
        }
        return TryResolveLength(value, parentPx, parentPx, rootPx, out px) && px >= 0f;
    }

    private static bool TryResolveLineHeightLength(string value, float fontSizePx, float rootPx, out float px)
    {
        px = 0f;
        if (string.Equals(value, "normal", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (CssLengthUnits.TryParseNumber(value, value.Length, out _)) return false;
        return TryResolveLength(value, fontSizePx, fontSizePx, rootPx, out px);
    }

    private static bool TryResolveLength(string value, float percentBasis, float emBasis, float rootPx, out float px)
    {
        px = 0f;
        if (value.Length == 0) return false;
        if (value[value.Length - 1] == '%')
        {
            if (!CssLengthUnits.TryParseNumber(value, value.Length - 1, out float percent)) return false;
            px = percent / 100f * percentBasis;
            return true;
        }

        int unitStart = CssLengthUnits.UnitStart(value);
        if (unitStart == 0 || unitStart == value.Length || CssLengthUnits.IsViewportUnit(value, unitStart))
            return false;
        if (!CssLengthUnits.TryParseNumber(value, unitStart, out float number)) return false;
        var context = new CssLengthContext(percentBasis, emBasis, rootPx, 0f, 0f);
        return CssLengthUnits.TryToPx(number, value, unitStart, context, out px);
    }

    private static bool TryAbsoluteSizeKeyword(string value, out float px)
    {
        px = 0f;
        if (value.Length == 0 || !char.IsLetter(value[0])) return false;
        switch (value.ToLowerInvariant())
        {
            case "xx-small": px = 9f; return true;
            case "x-small": px = 10f; return true;
            case "small": px = 13f; return true;
            case "medium": px = 16f; return true;
            case "large": px = 18f; return true;
            case "x-large": px = 24f; return true;
            case "xx-large": px = 32f; return true;
            case "xxx-large": px = 48f; return true;
            default: px = 0f; return false;
        }
    }

    private static string FormatPx(float px) => px.ToString("0.####", CultureInfo.InvariantCulture) + "px";
}
