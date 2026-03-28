using System.Globalization;
using System.Text;

namespace EggPdf.Text;

/// <summary>
/// Synthesizes bold and italic font variants when the actual font variant
/// is not available. Used as fallback when e.g., "Arial Bold" doesn't exist
/// but the CSS requests font-weight: bold.
///
/// Bold synthesis: draw text with a slight stroke (outline) to thicken glyphs.
/// Italic synthesis: apply a skew transform (shear) to slant glyphs.
/// </summary>
public static class FontSynthesis
{
    /// <summary>
    /// Generate PDF content stream operators to synthesize bold text.
    /// Uses text rendering mode 2 (fill + stroke) with a thin stroke width.
    /// </summary>
    /// <param name="fontSize">Font size in PDF points.</param>
    /// <returns>PDF operators to prepend before the text operator.</returns>
    public static string SynthesizeBoldBegin(float fontSize)
    {
        // Stroke width proportional to font size (~2% of font size)
        float strokeWidth = fontSize * 0.02f;
        var sb = new StringBuilder();
        sb.Append("q "); // save graphics state
        sb.Append($"{F(strokeWidth)} w "); // set stroke width
        sb.Append("2 Tr "); // text rendering mode: fill + stroke
        return sb.ToString();
    }

    /// <summary>PDF operators to append after bold text to restore normal rendering.</summary>
    public static string SynthesizeBoldEnd()
    {
        return "0 Tr Q "; // restore text mode + graphics state
    }

    /// <summary>
    /// Generate PDF content stream operators to synthesize italic text.
    /// Applies a horizontal skew transform (~12 degrees).
    /// </summary>
    /// <returns>PDF operators to prepend before the text operator.</returns>
    public static string SynthesizeItalicBegin()
    {
        // Skew by ~12 degrees: matrix [1 0 tan(12°) 1 0 0]
        // tan(12°) ≈ 0.2126
        float skew = 0.2126f;
        return $"q 1 0 {F(skew)} 1 0 0 cm ";
    }

    /// <summary>PDF operators to append after italic text.</summary>
    public static string SynthesizeItalicEnd()
    {
        return "Q ";
    }

    /// <summary>
    /// Check if font synthesis is needed for a given font request.
    /// Returns true if the requested variant doesn't exist and synthesis should be applied.
    /// </summary>
    public static (bool needsBold, bool needsItalic) CheckSynthesisNeeded(
        string requestedFamily, bool wantBold, bool wantItalic,
        string resolvedFontName)
    {
        bool needsBold = false;
        bool needsItalic = false;

        if (wantBold && !resolvedFontName.Contains("Bold"))
            needsBold = true;

        if (wantItalic && !resolvedFontName.Contains("Italic") &&
            !resolvedFontName.Contains("Oblique"))
            needsItalic = true;

        return (needsBold, needsItalic);
    }

    private static string F(float v) => v.ToString("F4", CultureInfo.InvariantCulture);
}
