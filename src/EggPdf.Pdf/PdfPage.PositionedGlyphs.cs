using System.Collections.Generic;

namespace EggPdf.Pdf;

/// <summary>
/// One shaped glyph for <see cref="PdfPage.AddPositionedGlyphsCID"/>. All distances are in 1/1000 of
/// the font size (PDF glyph space): the offset of the glyph's origin from its pen position, and how
/// much the pen's advance differs from the font dictionary's default width for that glyph.
/// </summary>
public readonly struct PdfGlyphPlacement
{
    public readonly ushort GlyphId;
    public readonly float XOffset, YOffset, AdvanceDelta;

    public PdfGlyphPlacement(ushort glyphId, float xOffset, float yOffset, float advanceDelta)
    {
        GlyphId = glyphId; XOffset = xOffset; YOffset = yOffset; AdvanceDelta = advanceDelta;
    }
}

public partial class PdfPage
{
    /// <summary>
    /// Paint shaped glyphs with per-glyph offsets and advances. Horizontal offsets/advances use TJ
    /// adjustments (a positive TJ number moves the pen left); vertical offsets use text rise (Ts).
    /// </summary>
    public void AddPositionedGlyphsCID(string fontName, IList<PdfGlyphPlacement> glyphs,
        float x, float y, float fontSize,
        float colorR = 0, float colorG = 0, float colorB = 0, float letterSpacing = 0)
    {
        if (glyphs.Count == 0) return;
        UsedFonts.Add(fontName);
        ContentStream.AppendOpLine($"{F(colorR)} {F(colorG)} {F(colorB)} rg");
        ContentStream.Append($"BT /{fontName} {F(fontSize)} Tf ");
        ContentStream.Append($"{F(letterSpacing)} Tc ");
        ContentStream.Append($"{F(x)} {F(y)} Td ");

        float rise = 0f;
        bool arrayOpen = false;
        float pending = 0f; // TJ adjustment owed before the next glyph (thousandths, TJ sign convention)

        for (int i = 0; i < glyphs.Count; i++)
        {
            var g = glyphs[i];
            if (g.YOffset != rise)
            {
                if (arrayOpen) { ContentStream.Append("] TJ "); arrayOpen = false; }
                ContentStream.Append($"{F(g.YOffset * fontSize / 1000f)} Ts ");
                rise = g.YOffset;
            }
            if (!arrayOpen) { ContentStream.Append('['); arrayOpen = true; }

            float before = pending - g.XOffset;
            if (before != 0f) ContentStream.Append($"{F(before)} ");
            ContentStream.Append('<');
            ContentStream.Append(g.GlyphId.ToString("X4"));
            ContentStream.Append("> ");

            // Net pen movement must equal the shaped advance: undo the offset shift and the
            // difference between the default width the font applies and the shaped advance.
            pending = g.XOffset - g.AdvanceDelta;
        }
        if (pending != 0f && arrayOpen) ContentStream.Append($"{F(pending)} ");
        if (arrayOpen) ContentStream.Append("] TJ ");
        if (rise != 0f) ContentStream.Append("0 Ts ");
        ContentStream.AppendOpLine("ET");
    }
}
