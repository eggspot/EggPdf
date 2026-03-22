using System;
using System.Collections.Generic;

namespace EggPdf.Layout;

/// <summary>
/// Character width tables for the PDF Standard 14 fonts (Helvetica, Times-Roman, Courier).
/// Widths are in 1/1000 of a unit (standard PDF font metric units).
/// To get pixel width: charWidth * fontSize / 1000.
/// </summary>
internal static class StandardFontMetrics
{
    // Helvetica character widths (ISO Latin-1, chars 32-255)
    // Source: PDF Reference Appendix D / AFM files
    private static readonly ushort[] HelveticaWidths = new ushort[]
    {
        // 32-47: space ! " # $ % & ' ( ) * + , - . /
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        // 48-63: 0-9 : ; < = > ?
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        // 64-79: @ A-O
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        // 80-95: P-Z [ \ ] ^ _
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        // 96-111: ` a-o
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        // 112-126: p-z { | } ~
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    };

    // Helvetica-Bold character widths
    private static readonly ushort[] HelveticaBoldWidths = new ushort[]
    {
        // 32-47
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        // 48-63
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        // 64-79
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        // 80-95
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        // 96-111
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        // 112-126
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    };

    // Times-Roman character widths
    private static readonly ushort[] TimesRomanWidths = new ushort[]
    {
        // 32-47
        250, 333, 408, 500, 500, 833, 778, 180, 333, 333, 500, 564, 250, 333, 250, 278,
        // 48-63
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 278, 278, 564, 564, 564, 444,
        // 64-79
        921, 722, 667, 667, 722, 611, 556, 722, 722, 333, 389, 722, 611, 889, 722, 722,
        // 80-95
        556, 722, 667, 556, 611, 722, 722, 944, 722, 722, 611, 333, 278, 333, 469, 500,
        // 96-111
        333, 444, 500, 444, 500, 444, 333, 500, 500, 278, 278, 500, 278, 778, 500, 500,
        // 112-126
        500, 500, 333, 389, 278, 500, 500, 722, 500, 500, 444, 480, 200, 480, 541,
    };

    // Courier: all characters are 600 units wide (monospace)
    private const ushort CourierWidth = 600;

    private static readonly Dictionary<string, ushort[]> FontWidthTables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Helvetica"] = HelveticaWidths,
        ["Helvetica-Bold"] = HelveticaBoldWidths,
        ["Helvetica-Oblique"] = HelveticaWidths,  // same widths as regular
        ["Helvetica-BoldOblique"] = HelveticaBoldWidths,
        ["Times-Roman"] = TimesRomanWidths,
        ["Times-Bold"] = TimesRomanWidths, // approximate with regular widths
        ["Times-Italic"] = TimesRomanWidths,
        ["Times-BoldItalic"] = TimesRomanWidths,
    };

    /// <summary>
    /// Measure the width of text using standard PDF font metrics.
    /// Returns width in pixels for the given font size.
    /// </summary>
    public static float MeasureWidth(string text, float fontSize, string pdfFontName)
    {
        if (string.IsNullOrEmpty(text) || fontSize <= 0)
            return 0;

        // Courier is monospace
        if (pdfFontName.StartsWith("Courier", StringComparison.OrdinalIgnoreCase))
            return text.Length * CourierWidth * fontSize / 1000f;

        if (!FontWidthTables.TryGetValue(pdfFontName, out var widths))
            widths = HelveticaWidths; // default fallback

        int totalWidth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int c = text[i];
            if (c >= 32 && c <= 126)
            {
                totalWidth += widths[c - 32];
            }
            else
            {
                // For characters outside the basic range, use average width
                totalWidth += 500;
            }
        }

        return totalWidth * fontSize / 1000f;
    }

    /// <summary>
    /// Map a CSS font-family + weight + style to a PDF standard font name.
    /// </summary>
    public static string ResolvePdfFontName(string? fontFamily, string? fontWeight, string? fontStyle)
    {
        bool bold = fontWeight == "bold" || fontWeight == "700" || fontWeight == "800" || fontWeight == "900";
        bool italic = fontStyle == "italic" || fontStyle == "oblique";

        var family = (fontFamily ?? "").ToLowerInvariant().Trim();

        if (family.IndexOf("monospace", StringComparison.OrdinalIgnoreCase) >= 0 ||
            family.IndexOf("courier", StringComparison.OrdinalIgnoreCase) >= 0 ||
            family.IndexOf("consolas", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (bold && italic) return "Courier-BoldOblique";
            if (bold) return "Courier-Bold";
            if (italic) return "Courier-Oblique";
            return "Courier";
        }

        if (family.IndexOf("times", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (family.IndexOf("serif", StringComparison.OrdinalIgnoreCase) >= 0 &&
             family.IndexOf("sans", StringComparison.OrdinalIgnoreCase) < 0))
        {
            if (bold && italic) return "Times-BoldItalic";
            if (bold) return "Times-Bold";
            if (italic) return "Times-Italic";
            return "Times-Roman";
        }

        // Default: Helvetica (sans-serif)
        if (bold && italic) return "Helvetica-BoldOblique";
        if (bold) return "Helvetica-Bold";
        if (italic) return "Helvetica-Oblique";
        return "Helvetica";
    }
}
