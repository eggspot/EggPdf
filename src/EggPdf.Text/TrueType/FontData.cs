namespace EggPdf.Text.TrueType;

/// <summary>
/// Parsed TrueType font data with metrics needed for text layout and PDF embedding.
/// </summary>
public class FontData
{
    /// <summary>Font family name (e.g., "Arial", "DejaVu Sans").</summary>
    public string FamilyName { get; set; } = "";

    /// <summary>Units per em (typically 1000 or 2048).</summary>
    public int UnitsPerEm { get; set; }

    /// <summary>Ascent in font units (positive, above baseline).</summary>
    public int Ascent { get; set; }

    /// <summary>Descent in font units (negative, below baseline).</summary>
    public int Descent { get; set; }

    /// <summary>Line gap in font units.</summary>
    public int LineGap { get; set; }

    /// <summary>Number of glyphs in the font.</summary>
    public int NumGlyphs { get; set; }

    /// <summary>Advance widths per glyph ID (in font units).</summary>
    public ushort[] AdvanceWidths { get; set; } = System.Array.Empty<ushort>();

    /// <summary>cmap: Unicode codepoint -> glyph ID mapping.</summary>
    internal CmapData? Cmap { get; set; }

    /// <summary>Raw font file bytes (for PDF embedding).</summary>
    public byte[] RawData { get; set; } = System.Array.Empty<byte>();

    /// <summary>Get glyph ID for a Unicode codepoint.</summary>
    public ushort GetGlyphId(int codepoint)
    {
        return Cmap?.GetGlyphId(codepoint) ?? 0;
    }

    /// <summary>Get advance width for a glyph ID (in font units).</summary>
    public ushort GetAdvanceWidth(ushort glyphId)
    {
        if (glyphId < AdvanceWidths.Length)
            return AdvanceWidths[glyphId];
        // Last width applies to remaining glyphs
        return AdvanceWidths.Length > 0 ? AdvanceWidths[AdvanceWidths.Length - 1] : (ushort)0;
    }

    /// <summary>Measure text width in font units.</summary>
    public int MeasureTextWidth(string text)
    {
        int width = 0;
        foreach (char c in text)
        {
            var glyphId = GetGlyphId(c);
            width += GetAdvanceWidth(glyphId);
        }
        return width;
    }

    /// <summary>Measure text width in pixels at a given font size.</summary>
    public float MeasureTextWidthPx(string text, float fontSizePx)
    {
        if (UnitsPerEm == 0) return 0;
        return MeasureTextWidth(text) * fontSizePx / UnitsPerEm;
    }
}

/// <summary>Character-to-glyph mapping data.</summary>
internal class CmapData
{
    private readonly System.Collections.Generic.Dictionary<int, ushort> _map = new();

    public void Add(int codepoint, ushort glyphId) => _map[codepoint] = glyphId;

    public ushort GetGlyphId(int codepoint)
        => _map.TryGetValue(codepoint, out var id) ? id : (ushort)0;
}
