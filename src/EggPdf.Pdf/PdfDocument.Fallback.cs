using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

public partial class PdfDocument
{
    private readonly Dictionary<string, List<string>> _fallbackFonts = new Dictionary<string, List<string>>(StringComparer.Ordinal);

    /// <summary>
    /// Declare that the embedded font <paramref name="fallbackFontName"/> supplies glyphs the embedded font
    /// <paramref name="primaryFontName"/> lacks. Fallbacks are consulted in registration order, so a text run
    /// can switch fonts per character (see <see cref="GetFallbackFonts"/>).
    /// </summary>
    public void AddFallbackFont(string primaryFontName, string fallbackFontName)
    {
        if (!_fallbackFonts.TryGetValue(primaryFontName, out var list))
            _fallbackFonts[primaryFontName] = list = new List<string>();
        if (!list.Contains(fallbackFontName)) list.Add(fallbackFontName);
    }

    /// <summary>The fallback fonts registered for a primary font, in lookup order (empty when it has none).</summary>
    public IReadOnlyList<string> GetFallbackFonts(string primaryFontName)
        => _fallbackFonts.TryGetValue(primaryFontName, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();
}
