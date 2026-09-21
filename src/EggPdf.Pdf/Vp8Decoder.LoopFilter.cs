namespace EggPdf.Pdf;

// The in-loop deblocking filter (RFC 6386 section 15): applied macroblock by macroblock in raster
// order, each one first across its left edge and inner vertical edges, then across its top edge
// and inner horizontal edges. Pixel arithmetic follows the specification's integer formulas.
internal sealed partial class Vp8Decoder
{
    private static int Sclip1(int v) => v < -128 ? -128 : v > 127 ? 127 : v;
    private static int Sclip2(int v) => v < -16 ? -16 : v > 15 ? 15 : v;
    private static int Abs(int v) => v < 0 ? -v : v;

    private void ApplyLoopFilter()
    {
        var f = _frame;
        int ys = f.YStride, uvs = f.UvStride;
        for (int mbY = 0; mbY < _mbH; mbY++)
        {
            for (int mbX = 0; mbX < _mbW; mbX++)
            {
                int idx = mbY * _mbW + mbX;
                int limit = _filterLimit[idx];
                if (limit == 0) continue;

                int interior = _filterInterior[idx], hev = _filterHev[idx];
                bool inner = _filterInner[idx];
                int y = mbY * 16 * ys + mbX * 16;
                int uv = mbY * 8 * uvs + mbX * 8;

                if (_filterType == 1)
                {
                    if (mbX > 0) SimpleFilter16(f.Y, y, 1, ys, limit + 4);
                    if (inner) for (int k = 1; k <= 3; k++) SimpleFilter16(f.Y, y + 4 * k, 1, ys, limit);
                    if (mbY > 0) SimpleFilter16(f.Y, y, ys, 1, limit + 4);
                    if (inner) for (int k = 1; k <= 3; k++) SimpleFilter16(f.Y, y + 4 * k * ys, ys, 1, limit);
                }
                else
                {
                    if (mbX > 0)
                    {
                        FilterLoop26(f.Y, y, 1, ys, 16, limit + 4, interior, hev);
                        FilterLoop26(f.U, uv, 1, uvs, 8, limit + 4, interior, hev);
                        FilterLoop26(f.V, uv, 1, uvs, 8, limit + 4, interior, hev);
                    }
                    if (inner)
                    {
                        for (int k = 1; k <= 3; k++) FilterLoop24(f.Y, y + 4 * k, 1, ys, 16, limit, interior, hev);
                        FilterLoop24(f.U, uv + 4, 1, uvs, 8, limit, interior, hev);
                        FilterLoop24(f.V, uv + 4, 1, uvs, 8, limit, interior, hev);
                    }
                    if (mbY > 0)
                    {
                        FilterLoop26(f.Y, y, ys, 1, 16, limit + 4, interior, hev);
                        FilterLoop26(f.U, uv, uvs, 1, 8, limit + 4, interior, hev);
                        FilterLoop26(f.V, uv, uvs, 1, 8, limit + 4, interior, hev);
                    }
                    if (inner)
                    {
                        for (int k = 1; k <= 3; k++) FilterLoop24(f.Y, y + 4 * k * ys, ys, 1, 16, limit, interior, hev);
                        FilterLoop24(f.U, uv + 4 * uvs, uvs, 1, 8, limit, interior, hev);
                        FilterLoop24(f.V, uv + 4 * uvs, uvs, 1, 8, limit, interior, hev);
                    }
                }
            }
        }
    }

    /// <summary>Filter 16 positions along an edge. <paramref name="across"/> steps over the edge, <paramref name="along"/> steps along it.</summary>
    private static void SimpleFilter16(byte[] p, int pos, int across, int along, int thresh)
    {
        int thresh2 = 2 * thresh + 1;
        for (int i = 0; i < 16; i++)
        {
            int at = pos + i * along;
            if (NeedsFilter(p, at, across, thresh2)) DoFilter2(p, at, across);
        }
    }

    private static void FilterLoop26(byte[] p, int pos, int across, int along, int size, int thresh, int ithresh, int hevThresh)
    {
        int thresh2 = 2 * thresh + 1;
        for (int i = 0; i < size; i++, pos += along)
        {
            if (!NeedsFilter2(p, pos, across, thresh2, ithresh)) continue;
            if (Hev(p, pos, across, hevThresh)) DoFilter2(p, pos, across);
            else DoFilter6(p, pos, across);
        }
    }

    private static void FilterLoop24(byte[] p, int pos, int across, int along, int size, int thresh, int ithresh, int hevThresh)
    {
        int thresh2 = 2 * thresh + 1;
        for (int i = 0; i < size; i++, pos += along)
        {
            if (!NeedsFilter2(p, pos, across, thresh2, ithresh)) continue;
            if (Hev(p, pos, across, hevThresh)) DoFilter2(p, pos, across);
            else DoFilter4(p, pos, across);
        }
    }

    private static bool NeedsFilter(byte[] p, int i, int step, int t)
    {
        int p1 = p[i - 2 * step], p0 = p[i - step], q0 = p[i], q1 = p[i + step];
        return 4 * Abs(p0 - q0) + Abs(p1 - q1) <= t;
    }

    private static bool NeedsFilter2(byte[] p, int i, int step, int t, int it)
    {
        int p3 = p[i - 4 * step], p2 = p[i - 3 * step], p1 = p[i - 2 * step], p0 = p[i - step];
        int q0 = p[i], q1 = p[i + step], q2 = p[i + 2 * step], q3 = p[i + 3 * step];
        if (4 * Abs(p0 - q0) + Abs(p1 - q1) > t) return false;
        return Abs(p3 - p2) <= it && Abs(p2 - p1) <= it && Abs(p1 - p0) <= it &&
               Abs(q3 - q2) <= it && Abs(q2 - q1) <= it && Abs(q1 - q0) <= it;
    }

    /// <summary>High edge variance: a large jump right next to the edge on either side.</summary>
    private static bool Hev(byte[] p, int i, int step, int thresh)
    {
        int p1 = p[i - 2 * step], p0 = p[i - step], q0 = p[i], q1 = p[i + step];
        return Abs(p1 - p0) > thresh || Abs(q1 - q0) > thresh;
    }

    // 4 pixels in, 2 pixels out
    private static void DoFilter2(byte[] p, int i, int step)
    {
        int p1 = p[i - 2 * step], p0 = p[i - step], q0 = p[i], q1 = p[i + step];
        int a = 3 * (q0 - p0) + Sclip1(p1 - q1);
        int a1 = Sclip2((a + 4) >> 3);
        int a2 = Sclip2((a + 3) >> 3);
        p[i - step] = Clip8(p0 + a2);
        p[i] = Clip8(q0 - a1);
    }

    // 4 pixels in, 4 pixels out
    private static void DoFilter4(byte[] p, int i, int step)
    {
        int p1 = p[i - 2 * step], p0 = p[i - step], q0 = p[i], q1 = p[i + step];
        int a = 3 * (q0 - p0);
        int a1 = Sclip2((a + 4) >> 3);
        int a2 = Sclip2((a + 3) >> 3);
        int a3 = (a1 + 1) >> 1;
        p[i - 2 * step] = Clip8(p1 + a3);
        p[i - step] = Clip8(p0 + a2);
        p[i] = Clip8(q0 - a1);
        p[i + step] = Clip8(q1 - a3);
    }

    // 6 pixels in, 6 pixels out
    private static void DoFilter6(byte[] p, int i, int step)
    {
        int p2 = p[i - 3 * step], p1 = p[i - 2 * step], p0 = p[i - step];
        int q0 = p[i], q1 = p[i + step], q2 = p[i + 2 * step];
        int a = Sclip1(3 * (q0 - p0) + Sclip1(p1 - q1));
        int a1 = (27 * a + 63) >> 7;
        int a2 = (18 * a + 63) >> 7;
        int a3 = (9 * a + 63) >> 7;
        p[i - 3 * step] = Clip8(p2 + a3);
        p[i - 2 * step] = Clip8(p1 + a2);
        p[i - step] = Clip8(p0 + a1);
        p[i] = Clip8(q0 - a1);
        p[i + step] = Clip8(q1 - a2);
        p[i + 2 * step] = Clip8(q2 - a3);
    }
}
