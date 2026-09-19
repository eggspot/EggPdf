using System.Collections.Generic;
using EggPdf.Text.TrueType;

namespace EggPdf.Pdf;

public partial class PdfDocument
{
    /// <summary>
    /// Attach the source font and glyph remapping to an already-registered embedded font so the
    /// painter can shape complex-script text (GSUB/GPOS) against the original glyph IDs and map
    /// the result into the subset.
    /// </summary>
    public void SetShapingData(string fontName, FontData source, Dictionary<ushort, ushort> oldToNewGlyphId)
    {
        if (!_embeddedFonts.TryGetValue(fontName, out var font)) return;
        font.ShapingFont = source;
        font.OldToNewGlyphId = oldToNewGlyphId;
    }

    /// <summary>The source font for glyph-level shaping, when this embedded font was registered for it.</summary>
    public bool TryGetShapingFont(string fontName, out FontData font)
    {
        font = null!;
        if (!_embeddedFonts.TryGetValue(fontName, out var data) || data.ShapingFont == null || data.OldToNewGlyphId == null)
            return false;
        font = data.ShapingFont;
        return true;
    }

    /// <summary>
    /// Map an original-font glyph ID to its subset glyph ID and report its default advance in
    /// 1/1000 em (the width the PDF font dictionary will apply).
    /// </summary>
    public bool TryMapShapedGlyph(string fontName, ushort originalGlyphId, out ushort subsetGlyphId, out float defaultAdvanceThousandths)
    {
        subsetGlyphId = 0;
        defaultAdvanceThousandths = 0;
        if (!_embeddedFonts.TryGetValue(fontName, out var data) || data.OldToNewGlyphId == null ||
            !data.OldToNewGlyphId.TryGetValue(originalGlyphId, out subsetGlyphId) || data.UnitsPerEm <= 0)
            return false;

        ushort w = subsetGlyphId < data.Widths.Length ? data.Widths[subsetGlyphId] : (ushort)0;
        defaultAdvanceThousandths = w * 1000f / data.UnitsPerEm;
        return true;
    }

    /// <summary>Units per em of an embedded font (0 if unknown).</summary>
    public int GetUnitsPerEm(string fontName)
        => _embeddedFonts.TryGetValue(fontName, out var data) ? data.UnitsPerEm : 0;
}
