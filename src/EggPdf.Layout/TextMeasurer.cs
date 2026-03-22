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
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            if (!string.IsNullOrEmpty(text)) lines.Add(text);
            return lines;
        }

        // Split into words
        var words = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return lines;

        var currentLine = words[0];
        for (int i = 1; i < words.Length; i++)
        {
            var candidate = currentLine + " " + words[i];
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

        if (!string.IsNullOrEmpty(currentLine))
            lines.Add(currentLine);

        return lines;
    }
}
