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

    private static float ResolveLengthValue(string value, float containingSize, float fontSize)
    {
        // Check for calc(), min(), max(), clamp() expressions
        if (CalcResolver.IsMathFunction(value))
            return CalcResolver.Resolve(value, containingSize, fontSize);

        // Intrinsic sizing keywords
        var lower = value.Trim().ToLowerInvariant();
        if (lower == "max-content")
            return containingSize; // approximation: use all available space
        if (lower == "min-content")
            return fontSize * 10f; // approximation: ~10 chars wide (single word)
        if (lower.StartsWith("fit-content(", StringComparison.Ordinal) && lower[lower.Length - 1] == ')')
        {
            var inner = value.Substring(12, value.Length - 13).Trim();
            float maxArg = ResolveLengthValue(inner, containingSize, fontSize);
            return System.Math.Min(containingSize, System.Math.Max(0f, maxArg));
        }

        if (value.EndsWith("px"))
            return ParseFloatN(value, value.Length - 2);

        if (value.EndsWith("em"))
            return ParseFloatN(value, value.Length - 2) * fontSize;

        if (value.EndsWith("rem"))
            return ParseFloatN(value, value.Length - 3) * DefaultFontSize;

        if (value.EndsWith("%"))
            return ParseFloatN(value, value.Length - 1) / 100f * containingSize;

        if (value.EndsWith("pt"))
            return ParseFloatN(value, value.Length - 2) * 96f / 72f;

        if (value.EndsWith("pc"))
            return ParseFloatN(value, value.Length - 2) * 96f / 6f; // 1pc = 12pt = 16px

        if (value.EndsWith("cm"))
            return ParseFloatN(value, value.Length - 2) * 96f / 2.54f;

        if (value.EndsWith("mm"))
            return ParseFloatN(value, value.Length - 2) * 96f / 25.4f;

        if (value.EndsWith("in"))
            return ParseFloatN(value, value.Length - 2) * 96f;

        // Viewport-relative units
        if (value.EndsWith("vw"))
        {
            float vw = _viewportWidth > 0 ? _viewportWidth : containingSize;
            return ParseFloatN(value, value.Length - 2) / 100f * vw;
        }
        if (value.EndsWith("vh"))
        {
            float vh = _viewportHeight > 0 ? _viewportHeight : containingSize;
            return ParseFloatN(value, value.Length - 2) / 100f * vh;
        }
        if (value.EndsWith("vmin"))
        {
            float vmin = _viewportWidth > 0 && _viewportHeight > 0
                ? System.Math.Min(_viewportWidth, _viewportHeight) : containingSize;
            return ParseFloatN(value, value.Length - 4) / 100f * vmin;
        }
        if (value.EndsWith("vmax"))
        {
            float vmax = _viewportWidth > 0 && _viewportHeight > 0
                ? System.Math.Max(_viewportWidth, _viewportHeight) : containingSize;
            return ParseFloatN(value, value.Length - 4) / 100f * vmax;
        }

        // ch: width of the '0' glyph (approximated as 0.5em)
        if (value.EndsWith("ch"))
            return ParseFloatN(value, value.Length - 2) * fontSize * 0.5f;

        // lh: line height relative unit (1lh = line-height of the element, approximated as 1.2em)
        if (value.EndsWith("lh"))
            return ParseFloatN(value, value.Length - 2) * fontSize * 1.2f;

        // Try bare number (treat as px)
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

    // Parse the first `length` characters of `s` as a float without allocating a Substring.
    private static float ParseFloatN(string s, int length)
    {
#if NETSTANDARD2_0
        if (float.TryParse(s.Substring(0, length), NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
            return r;
        return 0;
#else
        if (float.TryParse(s.AsSpan(0, length), NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
            return r;
        return 0;
#endif
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
