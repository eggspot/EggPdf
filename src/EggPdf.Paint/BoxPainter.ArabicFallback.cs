using System.Collections.Generic;

namespace EggPdf.Paint;

public static partial class BoxPainter
{
    /// <summary>The first fallback font (in registration order) that has a real glyph for the code point.</summary>
    private static bool TryFindFallbackGlyph(int codepoint, IReadOnlyList<string> fallbackFonts, out string fontName, out ushort glyphId)
    {
        for (int i = 0; i < fallbackFonts.Count; i++)
        {
            if (CurrentPdfDoc!.TryGetGlyphId(fallbackFonts[i], codepoint, out glyphId) && glyphId > 0)
            {
                fontName = fallbackFonts[i];
                return true;
            }
        }
        fontName = "";
        glyphId = 0;
        return false;
    }

    /// <summary>
    /// Append the glyphs for an Arabic presentation form's base letter(s) -- used when neither the
    /// main nor the fallback font carries the shaped glyph. All-or-nothing: returns false (leaving
    /// the run state untouched) unless every base letter resolves, so the caller can fall back to
    /// .notdef for the original codepoint instead of emitting half a ligature.
    /// </summary>
    private static bool TryAppendBaseFormGlyphs(string baseText, string mainFont, IReadOnlyList<string> fallbackFonts,
        List<(string fontName, ushort[] glyphIds)> runs, List<ushort> current, ref string currentFont)
    {
        var resolved = new (string font, ushort gid)[baseText.Length];
        for (int i = 0; i < baseText.Length; i++)
        {
            int baseCp = baseText[i];
            if (CurrentPdfDoc!.TryGetGlyphId(mainFont, baseCp, out var gid) && gid > 0)
                resolved[i] = (mainFont, gid);
            else if (TryFindFallbackGlyph(baseCp, fallbackFonts, out var fbFont, out var fbGid))
                resolved[i] = (fbFont, fbGid);
            else
                return false;
        }

        foreach (var (font, gid) in resolved)
        {
            if (font != currentFont && current.Count > 0)
            {
                runs.Add((currentFont, current.ToArray()));
                current.Clear();
            }
            currentFont = font;
            current.Add(gid);
        }
        return true;
    }
}
