using System;
using System.Collections.Generic;

namespace EggPdf.Layout;

/// <summary>
/// Phase 1/3 text measurement using approximate character widths.
/// Will be replaced with real TrueType font metrics in later phases.
/// </summary>
public static class TextMeasurer
{
    // Average character width as fraction of font size
    // Helvetica average: ~0.5 * fontSize for most characters
    private const float AvgCharWidthRatio = 0.5f;
    private const float MonospaceCharWidthRatio = 0.6f;
    private const float DefaultLineHeight = 1.2f;

    /// <summary>Measure the width of text in approximate pixels.</summary>
    public static float MeasureWidth(string text, float fontSize, string? fontFamily)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        float ratio = IsMonospace(fontFamily) ? MonospaceCharWidthRatio : AvgCharWidthRatio;
        return text.Length * fontSize * ratio;
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
            float width = MeasureWidth(candidate, fontSize, fontFamily);

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

    private static bool IsMonospace(string? fontFamily)
    {
        if (string.IsNullOrEmpty(fontFamily)) return false;
        return fontFamily.IndexOf("monospace", StringComparison.OrdinalIgnoreCase) >= 0 ||
               fontFamily.IndexOf("Courier", StringComparison.OrdinalIgnoreCase) >= 0 ||
               fontFamily.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
