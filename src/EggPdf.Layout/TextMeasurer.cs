using System;
using System.Collections.Generic;

namespace EggPdf.Layout;

/// <summary>
/// Text measurement using PDF Standard 14 font metrics (Helvetica, Times-Roman, Courier).
/// Uses exact per-character widths from the PDF specification.
/// </summary>
public static class TextMeasurer
{
    private const float DefaultLineHeight = 1.2f;

    /// <summary>Measure the width of text in pixels using standard font metrics.</summary>
    public static float MeasureWidth(string text, float fontSize, string? fontFamily)
    {
        return MeasureWidth(text, fontSize, fontFamily, null, null);
    }

    /// <summary>Measure the width of text with font weight/style for accurate font selection.</summary>
    public static float MeasureWidth(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var pdfFont = StandardFontMetrics.ResolvePdfFontName(fontFamily, fontWeight, fontStyle);
        return StandardFontMetrics.MeasureWidth(text, fontSize, pdfFont);
    }

    /// <summary>Get line height for a font size.</summary>
    public static float GetLineHeight(float fontSize, string? lineHeight)
    {
        if (!string.IsNullOrEmpty(lineHeight) && lineHeight != "normal")
        {
            if (float.TryParse(lineHeight, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float multiplier))
                return fontSize * multiplier;
        }
        return fontSize * DefaultLineHeight;
    }

    /// <summary>
    /// Break text into lines that fit within maxWidth.
    /// Returns list of line strings.
    /// </summary>
    public static List<string> WrapText(string text, float fontSize, string? fontFamily, float maxWidth)
    {
        return WrapText(text, fontSize, fontFamily, null, null, maxWidth);
    }

    /// <summary>
    /// Break text into lines that fit within maxWidth, with font weight/style awareness.
    /// </summary>
    public static List<string> WrapText(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle, float maxWidth)
    {
        return WrapText(text, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, "normal");
    }

    /// <summary>
    /// Break text into lines respecting white-space CSS property.
    /// </summary>
    public static List<string> WrapText(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle, float maxWidth, string whiteSpace)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
            return lines;

        bool preserveNewlines = whiteSpace == "pre" || whiteSpace == "pre-wrap" || whiteSpace == "pre-line";
        bool preserveSpaces = whiteSpace == "pre" || whiteSpace == "pre-wrap";
        bool allowWrap = whiteSpace != "pre" && whiteSpace != "nowrap";

        if (preserveNewlines)
        {
            // Split on newlines first
            var physicalLines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (var physLine in physicalLines)
            {
                var processedLine = preserveSpaces ? physLine : CollapseWhitespace(physLine);

                if (allowWrap && maxWidth > 0)
                {
                    // pre-wrap or pre-line: wrap within each physical line
                    var wrappedLines = WrapSingleLine(processedLine, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, preserveSpaces);
                    lines.AddRange(wrappedLines);
                }
                else
                {
                    // pre or nowrap: no wrapping, output as-is
                    lines.Add(processedLine);
                }
            }
        }
        else if (!allowWrap)
        {
            // nowrap: collapse whitespace, no wrapping
            lines.Add(CollapseWhitespace(text));
        }
        else
        {
            // normal: collapse whitespace, wrap at word boundaries
            var collapsed = CollapseWhitespace(text);
            if (maxWidth <= 0)
            {
                if (!string.IsNullOrEmpty(collapsed)) lines.Add(collapsed);
                return lines;
            }
            var wrappedLines = WrapSingleLine(collapsed, fontSize, fontFamily, fontWeight, fontStyle, maxWidth, false);
            lines.AddRange(wrappedLines);
        }

        return lines;
    }

    /// <summary>Wrap a single line of text at word boundaries.</summary>
    private static List<string> WrapSingleLine(string text, float fontSize, string? fontFamily,
        string? fontWeight, string? fontStyle, float maxWidth, bool preserveSpaces)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            lines.Add("");
            return lines;
        }

        if (maxWidth <= 0)
        {
            lines.Add(text);
            return lines;
        }

        // Split into words
        string[] words;
        if (preserveSpaces)
        {
            // For pre/pre-wrap: split on spaces but keep them as separate "words"
            words = SplitPreservingSpaces(text);
        }
        else
        {
            words = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        if (words.Length == 0)
        {
            lines.Add("");
            return lines;
        }

        var currentLine = words[0];
        for (int i = 1; i < words.Length; i++)
        {
            string separator = preserveSpaces ? "" : " ";
            var candidate = currentLine + separator + words[i];
            float width = MeasureWidth(candidate, fontSize, fontFamily, fontWeight, fontStyle);

            if (width <= maxWidth)
            {
                currentLine = candidate;
            }
            else
            {
                lines.Add(currentLine);
                currentLine = words[i];
            }
        }

        if (!string.IsNullOrEmpty(currentLine) || preserveSpaces)
            lines.Add(currentLine);

        return lines;
    }

    /// <summary>Collapse sequences of whitespace into single spaces and trim.</summary>
    private static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var result = new char[text.Length];
        int len = 0;
        bool lastWasSpace = true; // trim leading

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                if (!lastWasSpace)
                {
                    result[len++] = ' ';
                    lastWasSpace = true;
                }
            }
            else
            {
                result[len++] = c;
                lastWasSpace = false;
            }
        }

        // Trim trailing space
        if (len > 0 && result[len - 1] == ' ')
            len--;

        return new string(result, 0, len);
    }

    /// <summary>Split text into chunks preserving spaces (for pre/pre-wrap).</summary>
    private static string[] SplitPreservingSpaces(string text)
    {
        var parts = new List<string>();
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
            {
                if (i > start)
                    parts.Add(text.Substring(start, i - start));
                parts.Add(" ");
                start = i + 1;
            }
        }

        if (start < text.Length)
            parts.Add(text.Substring(start));

        return parts.ToArray();
    }
}
