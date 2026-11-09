using System;
using System.Collections.Generic;

namespace EggPdf.Layout;

/// <summary>
/// Character width tables for the PDF Standard 14 fonts (Helvetica, Times, Courier).
/// Widths are in 1/1000 of a unit (standard PDF font metric units).
/// To get pixel width: charWidth * fontSize / 1000.
/// </summary>
public static class StandardFontMetrics
{
    private static readonly ushort[] HelveticaWidths = new ushort[]
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    };

    private static readonly ushort[] HelveticaBoldWidths = new ushort[]
    {
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    };

    private static readonly ushort[] TimesRomanWidths = new ushort[]
    {
        250, 333, 408, 500, 500, 833, 778, 180, 333, 333, 500, 564, 250, 333, 250, 278,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 278, 278, 564, 564, 564, 444,
        921, 722, 667, 667, 722, 611, 556, 722, 722, 333, 389, 722, 611, 889, 722, 722,
        556, 722, 667, 556, 611, 722, 722, 944, 722, 722, 611, 333, 278, 333, 469, 500,
        333, 444, 500, 444, 500, 444, 333, 500, 500, 278, 278, 500, 278, 778, 500, 500,
        500, 500, 333, 389, 278, 500, 500, 722, 500, 500, 444, 480, 200, 480, 541,
    };

    private static readonly ushort[] TimesBoldWidths = new ushort[]
    {
        250, 333, 555, 500, 500, 1000, 833, 278, 333, 333, 500, 570, 250, 333, 250, 278,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 333, 333, 570, 570, 570, 500,
        930, 722, 667, 722, 722, 667, 611, 778, 778, 389, 500, 778, 667, 944, 722, 778,
        611, 778, 722, 556, 667, 722, 722, 1000, 722, 722, 667, 333, 278, 333, 581, 500,
        333, 500, 556, 444, 556, 444, 333, 500, 556, 278, 333, 556, 278, 833, 556, 500,
        556, 556, 444, 389, 333, 556, 500, 722, 500, 500, 444, 394, 220, 394, 520,
    };

    private static readonly ushort[] TimesItalicWidths = new ushort[]
    {
        250, 333, 420, 500, 500, 833, 778, 214, 333, 333, 500, 675, 250, 333, 250, 278,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 333, 333, 675, 675, 675, 500,
        920, 611, 611, 667, 722, 611, 611, 722, 722, 333, 444, 667, 556, 833, 667, 722,
        611, 722, 611, 500, 556, 722, 611, 833, 611, 556, 556, 389, 278, 389, 422, 500,
        333, 500, 500, 444, 500, 444, 278, 500, 500, 278, 278, 444, 278, 722, 500, 500,
        500, 500, 389, 389, 278, 500, 444, 667, 444, 444, 389, 400, 275, 400, 541,
    };

    private static readonly ushort[] TimesBoldItalicWidths = new ushort[]
    {
        250, 389, 555, 500, 500, 833, 778, 278, 333, 333, 500, 570, 250, 333, 250, 278,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 333, 333, 570, 570, 570, 500,
        832, 667, 667, 667, 722, 667, 667, 722, 778, 389, 500, 667, 611, 889, 722, 722,
        611, 722, 667, 556, 611, 722, 667, 889, 667, 611, 611, 333, 278, 333, 570, 500,
        333, 500, 500, 444, 500, 444, 333, 500, 556, 278, 278, 500, 278, 778, 556, 500,
        500, 500, 389, 389, 278, 556, 444, 667, 500, 444, 389, 348, 220, 348, 570,
    };

    private const char FirstLatin1Letter = (char)0xC0;
    private const char LastLatin1Letter = (char)0xFF;
    private static readonly char[] Latin1BaseLetters = BuildLatin1BaseLetters();

    private static char[] BuildLatin1BaseLetters()
    {
        var bases = new char[LastLatin1Letter - FirstLatin1Letter + 1];
        for (char c = FirstLatin1Letter; c <= LastLatin1Letter; c++)
        {
            var decomposed = c.ToString().Normalize(System.Text.NormalizationForm.FormD);
            bases[c - FirstLatin1Letter] = decomposed.Length > 1 && decomposed[0] < 127 ? decomposed[0] : '\0';
        }
        return bases;
    }

    private static int WidthOf(int c, ushort[] widths)
    {
        if (c == (char)0x00A0) c = ' ';
        if (c >= FirstLatin1Letter && c <= LastLatin1Letter && Latin1BaseLetters[c - FirstLatin1Letter] != '\0')
            c = Latin1BaseLetters[c - FirstLatin1Letter];
        return c >= 32 && c <= 126 ? widths[c - 32] : UnlistedCharacterWidth;
    }

    private const ushort CourierWidth = 600;
    private const int UnlistedCharacterWidth = 500;

    private static readonly Dictionary<string, ushort[]> FontWidthTables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Helvetica"] = HelveticaWidths,
        ["Helvetica-Bold"] = HelveticaBoldWidths,
        ["Helvetica-Oblique"] = HelveticaWidths,
        ["Helvetica-BoldOblique"] = HelveticaBoldWidths,
        ["Arial"] = HelveticaWidths,
        ["Arial-Bold"] = HelveticaBoldWidths,
        ["Arial-Italic"] = HelveticaWidths,
        ["Arial-BoldItalic"] = HelveticaBoldWidths,
        ["Times-Roman"] = TimesRomanWidths,
        ["Times-Bold"] = TimesBoldWidths,
        ["Times-Italic"] = TimesItalicWidths,
        ["Times-BoldItalic"] = TimesBoldItalicWidths,
    };

    /// <summary>
    /// Measure the width of text using standard PDF font metrics.
    /// Returns width in pixels for the given font size.
    /// </summary>
    public static float MeasureWidth(string text, float fontSize, string pdfFontName)
    {
        if (string.IsNullOrEmpty(text) || fontSize <= 0)
            return 0;

        if (pdfFontName.StartsWith("Courier", StringComparison.OrdinalIgnoreCase))
            return text.Length * CourierWidth * fontSize / 1000f;

        if (!FontWidthTables.TryGetValue(pdfFontName, out var widths))
            widths = HelveticaWidths;

        int totalWidth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            totalWidth += WidthOf(text[i], widths);
        }

        return totalWidth * fontSize / 1000f;
    }

    public static float MeasureCharWidth(char ch, float fontSize, string pdfFontName)
    {
        if (fontSize <= 0) return 0;
        if (pdfFontName.StartsWith("Courier", StringComparison.OrdinalIgnoreCase))
            return CourierWidth * fontSize / 1000f;
        if (!FontWidthTables.TryGetValue(pdfFontName, out var widths))
            widths = HelveticaWidths;
        return WidthOf(ch, widths) * fontSize / 1000f;
    }

    /// <summary>
    /// Map a CSS font-family + weight + style to a PDF standard font name. When
    /// <paramref name="fontFeatureSettings"/> has active tags, a deterministic suffix is
    /// appended (see <see cref="EggPdf.Text.TrueType.FontFeatureSettings"/>) so distinct
    /// feature declarations on the same family/weight/style get distinct embedded-font
    /// entries -- font-feature-settings changes which glyph a codepoint maps to, so it
    /// cannot share a single cached glyph map the way plain kerning/positioning can.
    /// </summary>
    public static string ResolvePdfFontName(string? fontFamily, string? fontWeight, string? fontStyle,
        string? fontFeatureSettings = null)
    {
        var baseName = ResolvePdfFontNameCore(fontFamily, fontWeight, fontStyle);
        var suffix = EggPdf.Text.TrueType.FontFeatureSettings.BuildFontNameSuffix(fontFeatureSettings);
        return suffix.Length == 0 ? baseName : baseName + suffix;
    }

    private static string ResolvePdfFontNameCore(string? fontFamily, string? fontWeight, string? fontStyle)
    {
        bool bold = fontWeight == "bold" || fontWeight == "700" || fontWeight == "800" || fontWeight == "900";
        bool italic = fontStyle == "italic" || fontStyle == "oblique";

        int numericWeight = 0;
        if (!string.IsNullOrEmpty(fontWeight))
            int.TryParse(fontWeight, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out numericWeight);
        bool weightVariant = TextMeasurer.FontDataProvider != null &&
            numericWeight >= 100 && numericWeight <= 900 &&
            numericWeight != 400 && numericWeight != 700;
        if (weightVariant)
        {
            var baseName = ResolveBaseFontName(fontFamily, false, italic);
            return baseName + EggPdf.Css.FontVariationMarker.NameSuffix(fontFamily) + "-W" + numericWeight;
        }

        return ResolveBaseFontName(fontFamily, bold, italic) + EggPdf.Css.FontVariationMarker.NameSuffix(fontFamily);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string family, bool bold, bool italic), string>
        _baseNameCache = new System.Collections.Concurrent.ConcurrentDictionary<(string, bool, bool), string>();
    private const int BaseNameCacheMaxEntries = 512;

    private static string ResolveBaseFontName(string? fontFamily, bool bold, bool italic)
    {
        var installed = TextMeasurer.InstalledFontName(fontFamily, bold, italic);
        if (installed != null) return installed;

        var key = (fontFamily ?? "", bold, italic);
        if (_baseNameCache.TryGetValue(key, out var cached)) return cached;

        var result = ResolveBaseFontNameUncached(key.Item1, bold, italic);
        if (_baseNameCache.Count < BaseNameCacheMaxEntries)
            _baseNameCache.TryAdd(key, result);
        return result;
    }

    private static string ResolveBaseFontNameUncached(string fontFamily, bool bold, bool italic)
    {
        var family = fontFamily.ToLowerInvariant().Trim();

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
            family.IndexOf("georgia", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (family.IndexOf("serif", StringComparison.OrdinalIgnoreCase) >= 0 &&
             family.IndexOf("sans", StringComparison.OrdinalIgnoreCase) < 0))
        {
            if (bold && italic) return "Times-BoldItalic";
            if (bold) return "Times-Bold";
            if (italic) return "Times-Italic";
            return "Times-Roman";
        }

        if (family.IndexOf("arial", StringComparison.OrdinalIgnoreCase) >= 0 ||
            family.IndexOf("segoe", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (bold && italic) return "Arial-BoldItalic";
            if (bold) return "Arial-Bold";
            if (italic) return "Arial-Italic";
            return "Arial";
        }

        if (bold && italic) return "Helvetica-BoldOblique";
        if (bold) return "Helvetica-Bold";
        if (italic) return "Helvetica-Oblique";
        return "Helvetica";
    }
}
