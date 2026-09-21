using System;

namespace EggPdf.Svg;

/// <summary>A lighting-filter light source, with positions already mapped to canvas pixels.</summary>
internal sealed class FilterLight
{
    public string Kind = "distant";                 // distant | point | spot
    public float Azimuth, Elevation;                // degrees (distant)
    public float X, Y, Z;                           // position (point, spot)
    public float PointsAtX, PointsAtY, PointsAtZ;   // spot target
    public float SpotExponent = 1f;
    public float? ConeAngle;                        // degrees; null = unlimited
}

internal static partial class FilterPixels
{
    // ── feTurbulence (the reference implementation of the SVG specification) ──

    private const int BSize = 0x100, BMask = 0xff, PerlinN = 0x1000;

    private sealed class Lattice
    {
        public readonly int[] Map = new int[BSize + BSize + 2];
        public readonly double[][][] Gradient = new double[4][][];

        public Lattice(long seed)
        {
            const long RandM = 2147483647, RandA = 16807, RandQ = 127773, RandR = 2836;
            if (seed <= 0) seed = -(seed % (RandM - 1)) + 1;
            if (seed > RandM - 1) seed = RandM - 1;

            long Next(long s)
            {
                long r = RandA * (s % RandQ) - RandR * (s / RandQ);
                return r <= 0 ? r + RandM : r;
            }

            for (int k = 0; k < 4; k++)
            {
                Gradient[k] = new double[BSize + BSize + 2][];
                for (int i = 0; i < BSize; i++)
                {
                    Map[i] = i;
                    var g = new double[2];
                    for (int j = 0; j < 2; j++)
                    {
                        seed = Next(seed);
                        g[j] = (double)(((int)(seed % (BSize + BSize))) - BSize) / BSize;
                    }
                    double s2 = Math.Sqrt(g[0] * g[0] + g[1] * g[1]);
                    g[0] /= s2; g[1] /= s2;
                    Gradient[k][i] = g;
                }
            }
            for (int i = BSize - 1; i > 0; i--)
            {
                int k = Map[i];
                seed = Next(seed);
                int j = (int)(seed % BSize);
                Map[i] = Map[j];
                Map[j] = k;
            }
            for (int i = 0; i < BSize + 2; i++)
            {
                Map[BSize + i] = Map[i];
                for (int k = 0; k < 4; k++) Gradient[k][BSize + i] = Gradient[k][i];
            }
        }
    }

    private struct Stitch { public int Width, Height, WrapX, WrapY; }

    private static double SCurve(double t) => t * t * (3.0 - 2.0 * t);
    private static double Lerp(double t, double a, double b) => a + t * (b - a);

    private static double Noise2(Lattice l, int channel, double vx, double vy, bool stitching, Stitch st)
    {
        double t = vx + PerlinN;
        int bx0 = (int)t, bx1;
        double rx0 = t - (int)t, rx1 = rx0 - 1.0;
        bx1 = bx0 + 1;
        t = vy + PerlinN;
        int by0 = (int)t, by1;
        double ry0 = t - (int)t, ry1 = ry0 - 1.0;
        by1 = by0 + 1;

        if (stitching)
        {
            if (bx0 >= st.WrapX) bx0 -= st.Width;
            if (bx1 >= st.WrapX) bx1 -= st.Width;
            if (by0 >= st.WrapY) by0 -= st.Height;
            if (by1 >= st.WrapY) by1 -= st.Height;
        }
        bx0 &= BMask; bx1 &= BMask; by0 &= BMask; by1 &= BMask;

        int i = l.Map[bx0], j = l.Map[bx1];
        int b00 = l.Map[i + by0], b10 = l.Map[j + by0], b01 = l.Map[i + by1], b11 = l.Map[j + by1];
        double sx = SCurve(rx0), sy = SCurve(ry0);
        var g = l.Gradient[channel];

        double u = rx0 * g[b00][0] + ry0 * g[b00][1];
        double v = rx1 * g[b10][0] + ry0 * g[b10][1];
        double a = Lerp(sx, u, v);
        u = rx0 * g[b01][0] + ry1 * g[b01][1];
        v = rx1 * g[b11][0] + ry1 * g[b11][1];
        double b = Lerp(sx, u, v);
        return Lerp(sy, a, b);
    }

    private static double Turbulence(Lattice l, int channel, double px, double py, double fx, double fy,
        int octaves, bool fractal, bool stitching, double tileX, double tileY, double tileW, double tileH)
    {
        var st = new Stitch();
        if (stitching)
        {
            if (fx != 0.0)
            {
                double lo = Math.Floor(tileW * fx) / tileW, hi = Math.Ceiling(tileW * fx) / tileW;
                fx = fx / lo < hi / fx ? lo : hi;
            }
            if (fy != 0.0)
            {
                double lo = Math.Floor(tileH * fy) / tileH, hi = Math.Ceiling(tileH * fy) / tileH;
                fy = fy / lo < hi / fy ? lo : hi;
            }
            st.Width = (int)(tileW * fx + 0.5);
            st.WrapX = (int)(tileX * fx + PerlinN + st.Width);
            st.Height = (int)(tileH * fy + 0.5);
            st.WrapY = (int)(tileY * fy + PerlinN + st.Height);
        }

        double sum = 0, ratio = 1;
        double vx = px * fx, vy = py * fy;
        for (int o = 0; o < octaves; o++)
        {
            double n = Noise2(l, channel, vx, vy, stitching, st);
            sum += (fractal ? n : Math.Abs(n)) / ratio;
            vx *= 2; vy *= 2; ratio *= 2;
            if (stitching)
            {
                st.Width *= 2; st.WrapX = 2 * st.WrapX - PerlinN;
                st.Height *= 2; st.WrapY = 2 * st.WrapY - PerlinN;
            }
        }
        return sum;
    }

    /// <summary>
    /// feTurbulence over the canvas. <paramref name="unitsPerPx"/> and the origin map a pixel to user space,
    /// since the noise is defined in user coordinates. The result is premultiplied in the working color space.
    /// </summary>
    public static FilterImage Turbulence(int width, int height, bool linear, float baseFx, float baseFy, int octaves,
        int seed, bool fractal, bool stitch, float originX, float originY, float unitsPerPx,
        float tileX, float tileY, float tileW, float tileH)
    {
        var img = new FilterImage(width, height, linear);
        var lattice = new Lattice((long)Math.Round((double)seed));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double ux = originX + x * unitsPerPx, uy = originY + y * unitsPerPx;
                var c = new float[4];
                for (int ch = 0; ch < 4; ch++)
                {
                    double sum = Turbulence(lattice, ch, ux, uy, baseFx, baseFy, octaves, fractal, stitch, tileX, tileY, tileW, tileH);
                    double v = fractal ? (sum * 255.0 + 255.0) / 2.0 : sum * 255.0;
                    c[ch] = Clamp01((float)(v / 255.0));
                }
                int i = (y * width + x) * 4;
                img.Px[i] = c[0] * c[3]; img.Px[i + 1] = c[1] * c[3]; img.Px[i + 2] = c[2] * c[3]; img.Px[i + 3] = c[3];
            }
        }
        return img;
    }

    // ── feConvolveMatrix ─────────────────────────────────────────────────────

    public static FilterImage ConvolveMatrix(FilterImage src, int orderX, int orderY, float[] kernel, float divisor, float bias,
        int targetX, int targetY, string edgeMode, bool preserveAlpha)
    {
        int w = src.Width, h = src.Height;
        var result = new FilterImage(w, h, src.Linear);

        // Work on straight colour when alpha is preserved (the spec convolves un-premultiplied colour then)
        float[] source = src.Px;
        if (preserveAlpha)
        {
            source = new float[src.Px.Length];
            for (int i = 0; i < source.Length; i += 4)
            {
                float a = src.Px[i + 3];
                for (int c = 0; c < 3; c++) source[i + c] = a > 0f ? src.Px[i + c] / a : 0f;
                source[i + 3] = a;
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < orderY; j++)
                {
                    for (int i = 0; i < orderX; i++)
                    {
                        int sx = x - targetX + i, sy = y - targetY + j;
                        if (!MapEdge(ref sx, w, edgeMode) || !MapEdge(ref sy, h, edgeMode)) continue;

                        float k = kernel[(orderY - j - 1) * orderX + (orderX - i - 1)];
                        int p = (sy * w + sx) * 4;
                        r += source[p] * k; g += source[p + 1] * k; b += source[p + 2] * k; a += source[p + 3] * k;
                    }
                }

                int o = (y * w + x) * 4;
                if (preserveAlpha)
                {
                    float alpha = src.Px[o + 3];
                    result.Px[o] = Clamp01(r / divisor + bias) * alpha;
                    result.Px[o + 1] = Clamp01(g / divisor + bias) * alpha;
                    result.Px[o + 2] = Clamp01(b / divisor + bias) * alpha;
                    result.Px[o + 3] = alpha;
                }
                else
                {
                    float alpha = Clamp01(a / divisor + bias);
                    result.Px[o + 3] = alpha;
                    result.Px[o] = Math.Min(alpha, Clamp01(r / divisor + bias * alpha));
                    result.Px[o + 1] = Math.Min(alpha, Clamp01(g / divisor + bias * alpha));
                    result.Px[o + 2] = Math.Min(alpha, Clamp01(b / divisor + bias * alpha));
                }
            }
        }
        return result;
    }

    /// <summary>Resolve an out-of-range sample index per edgeMode; false means "treat as transparent black".</summary>
    private static bool MapEdge(ref int i, int size, string mode)
    {
        if (i >= 0 && i < size) return true;
        switch (mode)
        {
            case "wrap": i = ((i % size) + size) % size; return true;
            case "none": return false;
            default: i = i < 0 ? 0 : size - 1; return true; // duplicate
        }
    }

    // ── feDisplacementMap ────────────────────────────────────────────────────

    public static FilterImage DisplacementMap(FilterImage src, FilterImage map, float scale, char xChannel, char yChannel)
    {
        int w = src.Width, h = src.Height;
        var result = new FilterImage(w, h, src.Linear);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int m = (y * w + x) * 4;
                float ma = map.Px[m + 3];
                float dx = Channel(map.Px, m, ma, xChannel), dy = Channel(map.Px, m, ma, yChannel);
                int sx = (int)Math.Round(x + scale * (dx - 0.5f)), sy = (int)Math.Round(y + scale * (dy - 0.5f));
                if (sx < 0 || sx >= w || sy < 0 || sy >= h) continue;
                Array.Copy(src.Px, (sy * w + sx) * 4, result.Px, (y * w + x) * 4, 4);
            }
        }
        return result;
    }

    /// <summary>A map channel as straight (un-premultiplied) value.</summary>
    private static float Channel(float[] px, int at, float alpha, char selector)
    {
        switch (selector)
        {
            case 'A': return alpha;
            case 'R': return alpha > 0f ? px[at] / alpha : 0f;
            case 'G': return alpha > 0f ? px[at + 1] / alpha : 0f;
            default: return alpha > 0f ? px[at + 2] / alpha : 0f;
        }
    }

    // ── feImage ──────────────────────────────────────────────────────────────

    /// <summary>Draw <paramref name="bitmap"/> scaled (bilinear) into the rectangle (x, y, w, h) of <paramref name="dst"/>.</summary>
    public static void DrawBitmap(FilterImage dst, FilterImage bitmap, float x, float y, float w, float h)
    {
        if (w <= 0f || h <= 0f) return;
        int x0 = Math.Max(0, (int)Math.Floor(x)), x1 = Math.Min(dst.Width, (int)Math.Ceiling(x + w));
        int y0 = Math.Max(0, (int)Math.Floor(y)), y1 = Math.Min(dst.Height, (int)Math.Ceiling(y + h));
        for (int py = y0; py < y1; py++)
        {
            float v = (py + 0.5f - y) / h * bitmap.Height - 0.5f;
            for (int px = x0; px < x1; px++)
            {
                float u = (px + 0.5f - x) / w * bitmap.Width - 0.5f;
                int ix = (int)Math.Floor(u), iy = (int)Math.Floor(v);
                float fx = u - ix, fy = v - iy;
                int o = (py * dst.Width + px) * 4;
                for (int c = 0; c < 4; c++)
                {
                    float top = Lerp(bitmap, ix, iy, c, fx, ix + 1, iy), bottom = Lerp(bitmap, ix, iy + 1, c, fx, ix + 1, iy + 1);
                    dst.Px[o + c] = top + (bottom - top) * fy;
                }
            }
        }
    }

    private static float Lerp(FilterImage img, int x0, int y0, int c, float t, int x1, int y1)
    {
        float a = Sample(img, x0, y0, c), b = Sample(img, x1, y1, c);
        return a + (b - a) * t;
    }

    private static float Sample(FilterImage img, int x, int y, int c)
    {
        x = x < 0 ? 0 : x >= img.Width ? img.Width - 1 : x;
        y = y < 0 ? 0 : y >= img.Height ? img.Height - 1 : y;
        return img.Px[(y * img.Width + x) * 4 + c];
    }

    // ── feTile ───────────────────────────────────────────────────────────────

    /// <summary>Repeat the pixels of the rectangle [x0,x1) x [y0,y1) across the whole image.</summary>
    public static FilterImage Tile(FilterImage src, int x0, int y0, int x1, int y1)
    {
        int tw = x1 - x0, th = y1 - y0;
        var result = new FilterImage(src.Width, src.Height, src.Linear);
        if (tw <= 0 || th <= 0) return result;
        for (int y = 0; y < src.Height; y++)
        {
            int sy = y0 + (((y - y0) % th) + th) % th;
            for (int x = 0; x < src.Width; x++)
            {
                int sx = x0 + (((x - x0) % tw) + tw) % tw;
                Array.Copy(src.Px, (sy * src.Width + sx) * 4, result.Px, (y * src.Width + x) * 4, 4);
            }
        }
        return result;
    }

    // ── feDiffuseLighting / feSpecularLighting ───────────────────────────────

    /// <summary>
    /// Light the alpha channel treated as a height map (Sobel surface normals). Diffuse output is opaque;
    /// specular output is premultiplied with alpha = its strongest channel, per the specification.
    /// </summary>
    public static FilterImage Lighting(FilterImage src, bool specular, float surfaceScale, float constant, float specularExponent,
        float lr, float lg, float lb, FilterLight light)
    {
        int w = src.Width, h = src.Height;
        var result = new FilterImage(w, h, src.Linear);

        float A(int x, int y)
        {
            x = x < 0 ? 0 : x >= w ? w - 1 : x;
            y = y < 0 ? 0 : y >= h ? h - 1 : y;
            return src.Px[(y * w + x) * 4 + 3];
        }

        // Spot light axis
        float sx = 0, sy = 0, sz = 0;
        if (light.Kind == "spot")
        {
            sx = light.PointsAtX - light.X; sy = light.PointsAtY - light.Y; sz = light.PointsAtZ - light.Z;
            float sl = (float)Math.Sqrt(sx * sx + sy * sy + sz * sz);
            if (sl > 0f) { sx /= sl; sy /= sl; sz /= sl; }
        }
        float az = light.Azimuth * (float)Math.PI / 180f, el = light.Elevation * (float)Math.PI / 180f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float nx = -surfaceScale * 0.25f * ((A(x + 1, y - 1) + 2 * A(x + 1, y) + A(x + 1, y + 1)) - (A(x - 1, y - 1) + 2 * A(x - 1, y) + A(x - 1, y + 1)));
                float ny = -surfaceScale * 0.25f * ((A(x - 1, y + 1) + 2 * A(x, y + 1) + A(x + 1, y + 1)) - (A(x - 1, y - 1) + 2 * A(x, y - 1) + A(x + 1, y - 1)));
                float nl = (float)Math.Sqrt(nx * nx + ny * ny + 1f);
                nx /= nl; ny /= nl; float nz = 1f / nl;

                float lx, ly, lz;
                if (light.Kind == "distant")
                {
                    lx = (float)(Math.Cos(az) * Math.Cos(el)); ly = (float)(Math.Sin(az) * Math.Cos(el)); lz = (float)Math.Sin(el);
                }
                else
                {
                    float z = surfaceScale * A(x, y);
                    lx = light.X - x; ly = light.Y - y; lz = light.Z - z;
                    float ll = (float)Math.Sqrt(lx * lx + ly * ly + lz * lz);
                    if (ll > 0f) { lx /= ll; ly /= ll; lz /= ll; }
                }

                float cr = lr, cg = lg, cb = lb;
                if (light.Kind == "spot")
                {
                    float minusLdotS = -(lx * sx + ly * sy + lz * sz);
                    bool outside = minusLdotS <= 0f || (light.ConeAngle.HasValue && minusLdotS < Math.Cos(light.ConeAngle.Value * Math.PI / 180.0));
                    float k = outside ? 0f : (float)Math.Pow(minusLdotS, light.SpotExponent);
                    cr *= k; cg *= k; cb *= k;
                }

                int o = (y * w + x) * 4;
                if (!specular)
                {
                    float f = constant * Math.Max(0f, nx * lx + ny * ly + nz * lz);
                    result.Px[o] = Clamp01(f * cr); result.Px[o + 1] = Clamp01(f * cg); result.Px[o + 2] = Clamp01(f * cb);
                    result.Px[o + 3] = 1f;
                }
                else
                {
                    float hx = lx, hy = ly, hz = lz + 1f;
                    float hl = (float)Math.Sqrt(hx * hx + hy * hy + hz * hz);
                    float ndh = hl > 0f ? Math.Max(0f, (nx * hx + ny * hy + nz * hz) / hl) : 0f;
                    float f = constant * (float)Math.Pow(ndh, specularExponent);
                    float r = Clamp01(f * cr), g = Clamp01(f * cg), b = Clamp01(f * cb);
                    result.Px[o] = r; result.Px[o + 1] = g; result.Px[o + 2] = b;
                    result.Px[o + 3] = Math.Max(r, Math.Max(g, b));
                }
            }
        }
        return result;
    }
}
