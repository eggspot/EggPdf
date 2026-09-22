using System.Collections.Generic;
using EggPdf.Pdf;
using EggPdf.Text.OpenType;
using EggPdf.Text.TrueType;

namespace EggPdf.Paint;

public static partial class BoxPainter
{
    /// <summary>
    /// Paint complex-script text (Thai, Indic, Arabic with marks) by shaping it against the
    /// original font's GSUB/GPOS tables and drawing the resulting glyph IDs -- remapped into the
    /// embedded subset -- with their per-glyph offsets and advances.
    /// </summary>
    private static void PaintShapedRun(PdfPage page, PdfDocument doc, string fontName, FontData font,
        string logicalText, bool rtl, float pdfX, float pdfY, float pdfFontSize,
        float colorR, float colorG, float colorB, float letterSpacing)
    {
        var shaped = ComplexTextShaper.Shape(font, logicalText, rtl);
        if (shaped.Length == 0 || font.UnitsPerEm <= 0) return;

        float toThousandths = 1000f / font.UnitsPerEm;
        var placements = new List<PdfGlyphPlacement>(shaped.Length);
        foreach (var g in shaped)
        {
            // A glyph absent from the subset (text changed after collection, e.g. ellipsis
            // truncation) degrades to .notdef rather than dropping out of the advance chain.
            if (!doc.TryMapShapedGlyph(fontName, g.GlyphId, out ushort subsetGid, out float defaultAdvance))
                doc.TryMapShapedGlyph(fontName, 0, out subsetGid, out defaultAdvance);

            placements.Add(new PdfGlyphPlacement(subsetGid,
                g.XOffset * toThousandths, g.YOffset * toThousandths,
                g.XAdvance * toThousandths - defaultAdvance));
        }

        page.AddPositionedGlyphsCID(fontName, placements, pdfX, pdfY, pdfFontSize,
            colorR, colorG, colorB, letterSpacing);
    }
}
