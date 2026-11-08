using System.Collections.Generic;
using System.Text;
using EggPdf.Core;
using EggPdf.Css;
using EggPdf.Layout;
using EggPdf.Pdf;

namespace EggPdf.Paint;

public static partial class BoxPainter
{
    private const float SyntheticSmallCapsScale = 0.7f;

    private static void EmitTextRun(PdfPage page, string text, float pdfX, float pdfY, string fontName, float pdfFontSize,
        float r, float g, float b, float letterSpacing, float wordSpacing)
    {
        if (CurrentPdfDoc != null && CurrentPdfDoc.IsEmbeddedFont(fontName))
        {
            var glyphIds = CurrentPdfDoc.GetGlyphIds(fontName, text);
            if (glyphIds != null && glyphIds.Length > 0)
            {
                var fallbackFonts = CurrentPdfDoc.GetFallbackFonts(fontName);
                bool hasNotdef = false;
                for (int gi = 0; gi < glyphIds.Length; gi++)
                    if (glyphIds[gi] == 0) { hasNotdef = true; break; }

                if (hasNotdef && fallbackFonts.Count > 0)
                    page.AddTextRunsCID(BuildFontFallbackRuns(text, fontName, fallbackFonts), pdfX, pdfY, pdfFontSize,
                        r, g, b, letterSpacing, wordSpacing);
                else
                    page.AddTextCID(glyphIds, pdfX, pdfY, fontName, pdfFontSize, r, g, b, letterSpacing, wordSpacing);
                return;
            }
        }
        page.AddText(text, pdfX, pdfY, StripWeightSuffix(fontName), pdfFontSize, r, g, b, letterSpacing, wordSpacing);
    }

    private static void PaintSmallCaps(PdfPage page, string text, bool allSmallCaps, float pdfX, float pdfY, string fontName,
        float fontSizePx, ComputedStyle style, float r, float g, float b, float letterSpacing, float wordSpacing)
    {
        float x = pdfX;
        foreach (var (segment, reduced) in SmallCapsSegments(text, allSmallCaps))
        {
            float segmentSizePx = reduced ? fontSizePx * SyntheticSmallCapsScale : fontSizePx;
            EmitTextRun(page, segment, x, pdfY, fontName, segmentSizePx * PdfCoordinates.PxToPt, r, g, b, letterSpacing, wordSpacing);
            x += TextMeasurer.MeasureWidth(segment, segmentSizePx, style.FontFamily, style.FontWeight, style.Get("font-style"))
                 * PdfCoordinates.PxToPt
                 + letterSpacing * segment.Length + wordSpacing * CountSpaces(segment);
        }
    }

    private static List<(string Text, bool Reduced)> SmallCapsSegments(string text, bool allSmallCaps)
    {
        var segments = new List<(string, bool)>();
        var current = new StringBuilder();
        bool currentReduced = false;
        foreach (char c in text)
        {
            bool reduced = allSmallCaps ? char.IsLetter(c) : char.IsLower(c);
            if (current.Length > 0 && reduced != currentReduced)
            {
                segments.Add((current.ToString(), currentReduced));
                current.Clear();
            }
            currentReduced = reduced;
            current.Append(reduced ? char.ToUpperInvariant(c) : c);
        }
        if (current.Length > 0) segments.Add((current.ToString(), currentReduced));
        return segments;
    }

    private static int CountSpaces(string text)
    {
        int count = 0;
        foreach (char c in text)
            if (c == ' ') count++;
        return count;
    }
}
