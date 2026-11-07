using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    public static float ResolveLength(string? value, float containingSize, float fontSize)
    {
        if (string.IsNullOrEmpty(value) || value == "auto" || value == "0")
            return 0;

        return ResolveLengthValue(value, containingSize, fontSize);
    }

    internal static float? ResolveOptionalLength(string? value, float containingSize, float fontSize)
    {
        if (string.IsNullOrEmpty(value) || value == "auto")
            return null;

        return ResolveLengthValue(value, containingSize, fontSize);
    }

    private const float MinContentEmsApproximation = 10f;

    private static float ResolveLengthValue(string value, float containingSize, float fontSize)
    {
        if (CalcResolver.IsMathFunction(value))
            return CalcResolver.Resolve(value, containingSize, fontSize);

        value = value.Trim();
        if (value.Length == 0)
            return 0;

        if (string.Equals(value, "max-content", StringComparison.OrdinalIgnoreCase))
            return containingSize;
        if (string.Equals(value, "min-content", StringComparison.OrdinalIgnoreCase))
            return fontSize * MinContentEmsApproximation;
        if (value.StartsWith("fit-content(", StringComparison.OrdinalIgnoreCase) && value[value.Length - 1] == ')')
        {
            var inner = value.Substring(12, value.Length - 13).Trim();
            float maxArg = ResolveLengthValue(inner, containingSize, fontSize);
            return System.Math.Min(containingSize, System.Math.Max(0f, maxArg));
        }

        if (value[value.Length - 1] == '%')
            return LengthUnits.ParseNumber(value, value.Length - 1) / 100f * containingSize;

        int unitStart = LengthUnits.UnitStart(value);
        if (unitStart < value.Length && unitStart > 0 &&
            LengthUnits.TryToPx(LengthUnits.ParseNumber(value, unitStart), value, unitStart, containingSize, fontSize, out float px))
            return px;

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float bare))
            return bare;

        return 0;
    }

    internal static float ResolveFontSize(string? value, float parentFontSize)
    {
        if (string.IsNullOrEmpty(value))
            return parentFontSize;

        if (value == "smaller") return parentFontSize * 0.833f;
        if (value == "larger") return parentFontSize * 1.2f;

        return ResolveLengthValue(value, 0, parentFontSize);
    }

    private static float ParseFloat(string s)
    {
        if (float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            return result;
        return 0;
    }

    private static bool TryParseFirstToken(string s, out float value)
    {
        int end = s.IndexOf(' ');
        string token = end >= 0 ? s.Substring(0, end) : s;
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string? FirstSrcsetUrl(string? srcset)
    {
        if (string.IsNullOrEmpty(srcset)) return null;
        int commaIdx = srcset.IndexOf(',');
        string candidate = commaIdx >= 0 ? srcset.Substring(0, commaIdx) : srcset;
        candidate = candidate.Trim();
        int spaceIdx = candidate.IndexOf(' ');
        return spaceIdx >= 0 ? candidate.Substring(0, spaceIdx) : candidate;
    }

    /// <summary>
    /// Trim collapsible HTML whitespace (space, tab, CR, LF) from both ends of a string,
    /// but preserve U+00A0 NON-BREAKING SPACE which must never be collapsed or trimmed.
    /// </summary>
    private static string TrimHtmlText(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int start = 0, end = s.Length - 1;
        while (start <= end && IsCollapsibleWhitespace(s[start])) start++;
        while (end >= start && IsCollapsibleWhitespace(s[end])) end--;
        return start > end ? "" : s.Substring(start, end - start + 1);
    }

    /// <summary>
    /// Returns true only when every character in the string is collapsible whitespace.
    /// U+00A0 (non-breaking space) is NOT collapsible and causes this to return false.
    /// </summary>
    private static bool IsHtmlWhitespaceOnly(string s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        for (int i = 0; i < s.Length; i++)
            if (!IsCollapsibleWhitespace(s[i])) return false;
        return true;
    }

    /// <summary>Collapsible whitespace: regular space, tab, CR, LF only — NOT U+00A0.</summary>
    private static bool IsCollapsibleWhitespace(char c)
        => c == ' ' || c == '\t' || c == '\r' || c == '\n';

}
