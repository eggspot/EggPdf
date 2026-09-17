using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    /// <summary>
    /// Apply text-transform for width measurement (uppercase glyphs are wider).
    /// The painted text is transformed by the renderer; measuring the transformed
    /// text keeps layout and paint in agreement. Box text stays untransformed.
    /// </summary>
    private static string ApplyTextTransformForMeasure(string text, ComputedStyle style)
    {
        var tt = style.Get("text-transform");
        if (string.IsNullOrEmpty(tt) || tt == "none") return text;
        if (tt == "uppercase") return text.ToUpperInvariant();
        if (tt == "lowercase") return text.ToLowerInvariant();
        return text; // capitalize changes width negligibly
    }

    /// <summary>
    /// CSS white-space:normal collapsing for an inline run: \n \r \t map to
    /// spaces, runs of ordinary spaces collapse to one, edge whitespace trims.
    /// NBSP is rendered content — never trimmed at the edges or collapsed in
    /// the interior (unlike char.IsWhiteSpace, which misclassifies it as
    /// collapsible and would silently swallow a "&amp;nbsp;"-only run, e.g. a
    /// `&lt;span&gt;–&amp;nbsp;&lt;/span&gt;` bullet-dash marker losing its trailing
    /// space entirely). Zero-allocation when the text is already normalized.
    /// </summary>
    private static string NormalizeInlineWhitespace(string text)
    {
        int start = 0, end = text.Length;
        while (start < end && IsCollapsibleWhitespace(text[start])) start++;
        while (end > start && IsCollapsibleWhitespace(text[end - 1])) end--;
        if (start >= end) return "";

        bool needsRewrite = false;
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            if (c == '\n' || c == '\r' || c == '\t' ||
                (c == ' ' && text[i - 1] == ' '))
            {
                needsRewrite = true;
                break;
            }
        }

        if (!needsRewrite)
            return start == 0 && end == text.Length ? text : text.Substring(start, end - start);

        var sb = new System.Text.StringBuilder(end - start);
        bool prevSpace = false;
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            if (c == '\n' || c == '\r' || c == '\t') c = ' ';
            if (c == ' ' && prevSpace) continue;
            sb.Append(c);
            prevSpace = c == ' ';
        }
        return sb.ToString();
    }

}
