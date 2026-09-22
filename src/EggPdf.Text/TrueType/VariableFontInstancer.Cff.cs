using System;
using System.Collections.Generic;
using static EggPdf.Text.TrueType.SfntWriter;

namespace EggPdf.Text.TrueType;

public static partial class VariableFontInstancer
{
    /// <summary>
    /// Convert an OpenType font with PostScript outlines (CFF or CFF2) to an ordinary TrueType font: charstrings are
    /// interpreted, cubic curves approximated by quadratics, and everything else (cmap, GSUB/GPOS, name...) is kept.
    /// For a variable CFF2 font <paramref name="coords"/> (normalized) selects the instance; null means the default.
    /// </summary>
    internal static byte[]? ConvertCff(Sfnt sfnt, float[]? coords)
    {
        bool cff2 = sfnt.Has("CFF2");
        if (!cff2 && !sfnt.Has("CFF ")) return null;
        if (!sfnt.Has("head") || !sfnt.Has("hhea") || !sfnt.Has("hmtx") || !sfnt.Has("maxp")) return null;

        var d = sfnt.Data;
        var cff = CffFont.TryParse(d, sfnt.Offset(cff2 ? "CFF2" : "CFF "), cff2, coords);
        if (cff == null) return null;

        int numGlyphs = U16(d, sfnt.Offset("maxp") + 4);
        int numHMetrics = U16(d, sfnt.Offset("hhea") + 34);
        int unitsPerEm = U16(d, sfnt.Offset("head") + 18);
        int hmtx = sfnt.Offset("hmtx");
        var hvar = coords != null && sfnt.Has("HVAR") ? new HvarTable(d, sfnt.Offset("HVAR"), coords) : null;
        float tolerance = Math.Max(0.25f, unitsPerEm / 2000f);

        var glyphs = new InstancedGlyph?[numGlyphs];
        for (int gid = 0; gid < numGlyphs; gid++)
        {
            var glyph = BuildCffGlyph(cff.GetContours(gid, tolerance));
            glyph.Advance = U16(d, hmtx + Math.Min(gid, numHMetrics - 1) * 4);
            if (hvar != null) glyph.Advance = Math.Max(0, (int)Math.Round(glyph.Advance + hvar.AdvanceDelta(gid)));
            glyphs[gid] = glyph;
        }
        return AssembleFont(sfnt, glyphs, numHMetrics);
    }

    private static InstancedGlyph BuildCffGlyph(List<List<OutlinePoint>> contours)
    {
        var glyph = new InstancedGlyph();
        if (contours.Count == 0) return glyph;

        var xs = new List<int>(); var ys = new List<int>(); var flags = new List<byte>(); var ends = new int[contours.Count];
        for (int c = 0; c < contours.Count; c++)
        {
            var points = contours[c];
            // PostScript outlines run counter-clockwise, TrueType clockwise
            for (int i = points.Count - 1; i >= 0; i--)
            {
                xs.Add((int)Math.Round(points[i].X)); ys.Add((int)Math.Round(points[i].Y));
                flags.Add(points[i].OnCurve ? (byte)1 : (byte)0);
            }
            ends[c] = xs.Count - 1;
        }

        int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
        for (int i = 0; i < xs.Count; i++)
        {
            xMin = Math.Min(xMin, xs[i]); xMax = Math.Max(xMax, xs[i]);
            yMin = Math.Min(yMin, ys[i]); yMax = Math.Max(yMax, ys[i]);
        }

        glyph.HasOutline = true;
        glyph.XMin = xMin; glyph.YMin = yMin; glyph.XMax = xMax; glyph.YMax = yMax;
        glyph.LeftSideBearing = xMin;
        glyph.PointsX = xs.ToArray(); glyph.PointsY = ys.ToArray();
        glyph.Data = WriteSimpleGlyph(ends, glyph.PointsX, glyph.PointsY, flags.ToArray(), xMin, yMin, xMax, yMax);
        return glyph;
    }
}
