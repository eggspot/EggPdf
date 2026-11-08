# Implementation notes

Reasons behind code that isn't obvious from the code alone. They used to live in code comments; the
project keeps code comment-free (see CLAUDE.md), so they live here, grouped by file.

## EggPdf.Layout/TextMeasurer.cs

- `MeasureWidth` measures the shaped form the painter will draw: Arabic text first goes through
  `ArabicShaper.Shape` (medial forms are narrower than isolated ones, lam-alef ligatures merge two
  letters), and complex scripts (Thai, Indic, Arabic with marks) are measured as the sum of the
  GSUB/GPOS-shaped advances (conjuncts collapse glyphs, marks are zero-width), not per code point.
- `NonLatinAdjustment` corrects the built-in-font estimate for characters above U+00FF: the AFM table
  charges each such UTF-16 unit one flat width, while the PDF paints the real glyph advance that
  `GlyphAdvanceProvider` reports.
- `ComplexMetrics` gives ascent/descent/line gap only for shaped complex scripts: tall fonts
  (Myanmar, Khmer, Indic) need far more than the 1.2em default line box or lines collide.
- `HyphenateWordToFit` tries break points right to left and keeps the rightmost one that fits.
- The shared English hyphenator is immutable after construction, so one instance serves all threads.

## EggPdf.Pdf/PdfPrintProduction.cs

- The MediaBox is the trim size plus bleed, plus the crop-mark area when marks are on; the TrimBox is
  the final page size; the BleedBox is the TrimBox grown by the bleed.
