using System.Collections.Generic;

namespace EggPdf.Paint;

public static partial class BoxPainter
{
    /// <summary>
    /// Append the glyphs for an Arabic presentation form's base letter(s) -- used when neither the
    /// main nor the fallback font carries the shaped glyph. All-or-nothing: returns false (leaving
    /// the run state untouched) unless every base letter resolves, so the caller can fall back to
    /// .notdef for the original codepoint instead of emitting half a ligature.
    /// </summary>
    private static bool TryAppendBaseFormGlyphs(string baseText, string mainFont, string fallbackFont,
        List<(string fontName, ushort[] glyphIds)> runs, List<ushort> current, ref string currentFont)
    {
        var resolved = new (string font, ushort gid)[baseText.Length];
        for (int i = 0; i < baseText.Length; i++)
        {
            int baseCp = baseText[i];
            if (CurrentPdfDoc!.TryGetGlyphId(mainFont, baseCp, out var gid) && gid > 0)
                resolved[i] = (mainFont, gid);
            else if (CurrentPdfDoc.TryGetGlyphId(fallbackFont, baseCp, out var fbGid) && fbGid > 0)
                resolved[i] = (fallbackFont, fbGid);
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
