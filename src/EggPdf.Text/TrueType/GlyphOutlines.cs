using System;
using System.Collections.Generic;

namespace EggPdf.Text.TrueType;

/// <summary>
/// Glyph outlines from a TrueType font's glyf table, flattened to polylines in font units (y up).
/// Quadratic curves are subdivided; composite glyphs are resolved through their components. Used by
/// renderers that must rasterize text themselves (SVG filters), not by the normal PDF text path.
/// </summary>
public static class GlyphOutlines
{
    private const int MaxDepth = 6;

    /// <summary>The closed contours of a glyph (empty for a blank glyph or a font this reader can't handle).</summary>
    public static List<List<(float x, float y)>> Get(FontData font, int glyphId)
    {
        var contours = new List<List<(float x, float y)>>();
        var d = font.RawData;
        if (d == null || d.Length < 12) return contours;

        try
        {
            if (!TryFindTables(d, out int glyf, out int loca, out bool longLoca, out int glyphCount)) return contours;
            Collect(d, glyf, loca, longLoca, glyphCount, glyphId, 1, 0, 0, 1, 0, 0, contours, 0);
        }
        catch (IndexOutOfRangeException)
        {
            contours.Clear(); // a corrupt glyf table draws nothing rather than throwing
        }
        return contours;
    }

    private static bool TryFindTables(byte[] d, out int glyf, out int loca, out bool longLoca, out int glyphCount)
    {
        glyf = loca = glyphCount = 0; longLoca = false;
        int head = 0, maxp = 0;
        int tables = (d[4] << 8) | d[5];
        for (int i = 0; i < tables; i++)
        {
            int r = 12 + i * 16;
            string tag = "" + (char)d[r] + (char)d[r + 1] + (char)d[r + 2] + (char)d[r + 3];
            int off = (d[r + 8] << 24) | (d[r + 9] << 16) | (d[r + 10] << 8) | d[r + 11];
            if (tag == "glyf") glyf = off; else if (tag == "loca") loca = off; else if (tag == "head") head = off; else if (tag == "maxp") maxp = off;
        }
        if (glyf == 0 || loca == 0 || head == 0 || maxp == 0) return false;
        longLoca = ((d[head + 50] << 8) | d[head + 51]) != 0;
        glyphCount = (d[maxp + 4] << 8) | d[maxp + 5];
        return true;
    }

    private static int I16(byte[] d, int o) => (short)((d[o] << 8) | d[o + 1]);
    private static int U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];

    private static void Collect(byte[] d, int glyf, int loca, bool longLoca, int glyphCount, int gid,
        float a, float b, float c, float dd, float tx, float ty, List<List<(float x, float y)>> output, int depth)
    {
        if (gid < 0 || gid >= glyphCount || depth > MaxDepth) return;
        int start = longLoca ? (int)((uint)((d[loca + gid * 4] << 24) | (d[loca + gid * 4 + 1] << 16) | (d[loca + gid * 4 + 2] << 8) | d[loca + gid * 4 + 3]))
                             : U16(d, loca + gid * 2) * 2;
        int end = longLoca ? (int)((uint)((d[loca + gid * 4 + 4] << 24) | (d[loca + gid * 4 + 5] << 16) | (d[loca + gid * 4 + 6] << 8) | d[loca + gid * 4 + 7]))
                           : U16(d, loca + gid * 2 + 2) * 2;
        if (end <= start) return;

        int g = glyf + start;
        int contours = I16(d, g);
        if (contours >= 0) AddSimple(d, g, contours, a, b, c, dd, tx, ty, output);
        else AddComposite(d, glyf, loca, longLoca, glyphCount, g + 10, a, b, c, dd, tx, ty, output, depth);
    }

    private static void AddSimple(byte[] d, int g, int contourCount, float a, float b, float c, float dd, float tx, float ty,
        List<List<(float x, float y)>> output)
    {
        if (contourCount == 0) return;
        var ends = new int[contourCount];
        for (int i = 0; i < contourCount; i++) ends[i] = U16(d, g + 10 + i * 2);
        int n = ends[contourCount - 1] + 1;

        int p = g + 10 + contourCount * 2;
        p += 2 + U16(d, p); // instructions

        var flags = new byte[n];
        for (int i = 0; i < n;)
        {
            byte f = d[p++];
            flags[i++] = f;
            if ((f & 0x08) != 0)
            {
                int repeat = d[p++];
                while (repeat-- > 0 && i < n) flags[i++] = f;
            }
        }

        var xs = new float[n]; var ys = new float[n];
        int v = 0;
        for (int i = 0; i < n; i++)
        {
            byte f = flags[i];
            if ((f & 0x02) != 0) { int s = d[p++]; v += (f & 0x10) != 0 ? s : -s; }
            else if ((f & 0x10) == 0) { v += I16(d, p); p += 2; }
            xs[i] = v;
        }
        v = 0;
        for (int i = 0; i < n; i++)
        {
            byte f = flags[i];
            if ((f & 0x04) != 0) { int s = d[p++]; v += (f & 0x20) != 0 ? s : -s; }
            else if ((f & 0x20) == 0) { v += I16(d, p); p += 2; }
            ys[i] = v;
        }

        int first = 0;
        foreach (int last in ends)
        {
            var contour = FlattenContour(xs, ys, flags, first, last);
            for (int i = 0; i < contour.Count; i++)
            {
                var (x, y) = contour[i];
                contour[i] = (a * x + c * y + tx, b * x + dd * y + ty);
            }
            if (contour.Count >= 3) output.Add(contour);
            first = last + 1;
        }
    }

    /// <summary>Expand on/off-curve points (with implied on-curve midpoints) into a polyline.</summary>
    private static List<(float x, float y)> FlattenContour(float[] xs, float[] ys, byte[] flags, int first, int last)
    {
        var result = new List<(float x, float y)>();
        int count = last - first + 1;
        if (count <= 0) return result;

        // Rotate to start at an on-curve point (or synthesize one between two off-curve points)
        int startIndex = -1;
        for (int i = 0; i < count; i++) if ((flags[first + i] & 1) != 0) { startIndex = i; break; }

        float sx, sy; int begin;
        if (startIndex >= 0) { sx = xs[first + startIndex]; sy = ys[first + startIndex]; begin = startIndex + 1; }
        else { sx = (xs[first] + xs[first + count - 1]) / 2f; sy = (ys[first] + ys[first + count - 1]) / 2f; begin = 0; }

        result.Add((sx, sy));
        float cx = sx, cy = sy;
        bool havePending = false; float px = 0, py = 0;
        int total = startIndex >= 0 ? count - 1 : count;

        for (int k = 0; k < total; k++)
        {
            int idx = first + (begin + k) % count;
            float x = xs[idx], y = ys[idx];
            bool on = (flags[idx] & 1) != 0;
            if (on)
            {
                if (havePending) { Quad(result, cx, cy, px, py, x, y); havePending = false; }
                else result.Add((x, y));
                cx = x; cy = y;
            }
            else
            {
                if (havePending)
                {
                    float mx = (px + x) / 2f, my = (py + y) / 2f;
                    Quad(result, cx, cy, px, py, mx, my);
                    cx = mx; cy = my;
                }
                px = x; py = y; havePending = true;
            }
        }
        if (havePending) Quad(result, cx, cy, px, py, sx, sy);
        return result;
    }

    private static void Quad(List<(float x, float y)> pts, float x0, float y0, float cx, float cy, float x1, float y1)
    {
        const int steps = 8;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps, u = 1 - t;
            pts.Add((u * u * x0 + 2 * u * t * cx + t * t * x1, u * u * y0 + 2 * u * t * cy + t * t * y1));
        }
    }

    private static void AddComposite(byte[] d, int glyf, int loca, bool longLoca, int glyphCount, int p,
        float a, float b, float c, float dd, float tx, float ty, List<List<(float x, float y)>> output, int depth)
    {
        int flags;
        var placed = new List<(float x, float y)>(); // points placed so far, for anchor-point components
        do
        {
            flags = U16(d, p);
            int child = U16(d, p + 2);
            p += 4;
            bool words = (flags & 0x0001) != 0, xy = (flags & 0x0002) != 0;
            int arg1, arg2;
            if (words) { arg1 = xy ? I16(d, p) : U16(d, p); arg2 = xy ? I16(d, p + 2) : U16(d, p + 2); p += 4; }
            else { arg1 = xy ? (sbyte)d[p] : d[p]; arg2 = xy ? (sbyte)d[p + 1] : d[p + 1]; p += 2; }

            float ca = 1, cb = 0, cc = 0, cd = 1;
            if ((flags & 0x0008) != 0) { ca = cd = I16(d, p) / 16384f; p += 2; }
            else if ((flags & 0x0040) != 0) { ca = I16(d, p) / 16384f; cd = I16(d, p + 2) / 16384f; p += 4; }
            else if ((flags & 0x0080) != 0) { ca = I16(d, p) / 16384f; cb = I16(d, p + 2) / 16384f; cc = I16(d, p + 4) / 16384f; cd = I16(d, p + 6) / 16384f; p += 8; }

            // Component transform, then the parent's: offset (arg1, arg2) is applied in the parent's space
            float ox = xy ? arg1 : 0, oy = xy ? arg2 : 0;
            var local = new List<List<(float x, float y)>>();
            Collect(d, glyf, loca, longLoca, glyphCount, child, ca, cb, cc, cd, 0, 0, local, depth + 1);

            if (!xy && arg1 < placed.Count)
            {
                // Anchor-point positioning: align the child's point arg2 with the parent's point arg1
                int running = 0;
                foreach (var contour in local)
                {
                    if (arg2 < running + contour.Count) { ox = placed[arg1].x - contour[arg2 - running].x; oy = placed[arg1].y - contour[arg2 - running].y; break; }
                    running += contour.Count;
                }
            }

            foreach (var contour in local)
            {
                for (int i = 0; i < contour.Count; i++)
                {
                    var (x, y) = contour[i];
                    x += ox; y += oy;
                    placed.Add((x, y));
                    contour[i] = (a * x + c * y + tx, b * x + dd * y + ty);
                }
                output.Add(contour);
            }
        } while ((flags & 0x0020) != 0);
    }
}
