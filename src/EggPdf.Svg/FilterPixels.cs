using System;

namespace EggPdf.Svg;

/// <summary>
/// The pixel operations behind SVG filter primitives. Every function takes and returns
/// premultiplied-alpha <see cref="FilterImage"/>s that are already in the color space the
/// primitive works in; the graph (<see cref="SvgFilter"/>) does the space conversions.
/// </summary>
internal static class FilterPixels
{
    // ── Gaussian blur ─────────────────────────────────────────────────────────

    /// <summary>
    /// feGaussianBlur as the Filter Effects spec computes it: a true Gaussian kernel for small
    /// deviations, three successive box blurs (an excellent, O(n) approximation) from sigma 2 up.
    /// </summary>
    public static FilterImage Blur(FilterImage src, float sigmaX, float sigmaY)
    {
        var img = src.Clone();
        if (sigmaX > 0f) BlurAxis(img, sigmaX, horizontal: true);
        if (sigmaY > 0f) BlurAxis(img, sigmaY, horizontal: false);
        return img;
    }

    private static void BlurAxis(FilterImage img, float sigma, bool horizontal)
    {
        int lineLen = horizontal ? img.Width : img.Height;
        int lineCount = horizontal ? img.Height : img.Width;
        int elem = horizontal ? 4 : img.Width * 4;
        int line = horizontal ? img.Width * 4 : 4;

        if (sigma < 2f)
        {
            GaussianPass(img.Px, lineCount, lineLen, elem, line, sigma);
            return;
        }

        int d = (int)Math.Floor(sigma * 3.0 * Math.Sqrt(2.0 * Math.PI) / 4.0 + 0.5);
        var tmp = new float[img.Px.Length];
        if (d % 2 == 1)
        {
            int r = d / 2;
            BoxPass(img.Px, tmp, lineCount, lineLen, elem, line, r, r);
            BoxPass(tmp, img.Px, lineCount, lineLen, elem, line, r, r);
            BoxPass(img.Px, tmp, lineCount, lineLen, elem, line, r, r);
            Array.Copy(tmp, img.Px, tmp.Length);
        }
        else
        {
            int r = d / 2;
            BoxPass(img.Px, tmp, lineCount, lineLen, elem, line, r, r - 1);   // window offset left
            BoxPass(tmp, img.Px, lineCount, lineLen, elem, line, r - 1, r);   // window offset right
            BoxPass(img.Px, tmp, lineCount, lineLen, elem, line, r, r);       // size d+1, centred
            Array.Copy(tmp, img.Px, tmp.Length);
        }
    }

    /// <summary>Moving-average over the window [i-left, i+right] with transparent black outside the image.</summary>
    private static void BoxPass(float[] src, float[] dst, int lineCount, int lineLen, int elem, int line, int left, int right)
    {
        float inv = 1f / (left + right + 1);
        for (int l = 0; l < lineCount; l++)
        {
            int baseIdx = l * line;
            for (int c = 0; c < 4; c++)
            {
                float sum = 0f;
                for (int k = 0; k <= right && k < lineLen; k++) sum += src[baseIdx + k * elem + c];
                for (int i = 0; i < lineLen; i++)
                {
                    dst[baseIdx + i * elem + c] = sum * inv;
                    int add = i + right + 1, drop = i - left;
                    if (add < lineLen) sum += src[baseIdx + add * elem + c];
                    if (drop >= 0) sum -= src[baseIdx + drop * elem + c];
                }
            }
        }
    }

    private static void GaussianPass(float[] px, int lineCount, int lineLen, int elem, int line, float sigma)
    {
        int radius = Math.Max(1, (int)Math.Ceiling(sigma * 3f));
        var kernel = new float[radius * 2 + 1];
        float sum = 0f;
        for (int i = 0; i < kernel.Length; i++)
        {
            int x = i - radius;
            kernel[i] = (float)Math.Exp(-(x * x) / (2f * sigma * sigma));
            sum += kernel[i];
        }
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;

        var tmp = new float[lineLen * 4];
        for (int l = 0; l < lineCount; l++)
        {
            int baseIdx = l * line;
            for (int i = 0; i < lineLen; i++)
            {
                for (int c = 0; c < 4; c++)
                {
                    float acc = 0f;
                    int lo = Math.Max(0, i - radius), hi = Math.Min(lineLen - 1, i + radius);
                    for (int s = lo; s <= hi; s++) acc += px[baseIdx + s * elem + c] * kernel[s - i + radius];
                    tmp[i * 4 + c] = acc;
                }
            }
            for (int i = 0; i < lineLen; i++)
                for (int c = 0; c < 4; c++) px[baseIdx + i * elem + c] = tmp[i * 4 + c];
        }
    }

    // ── Geometry ──────────────────────────────────────────────────────────────

    /// <summary>feOffset: shift the image by whole pixels; uncovered pixels become transparent.</summary>
    public static FilterImage Offset(FilterImage src, float dx, float dy)
    {
        int ox = (int)Math.Round(dx), oy = (int)Math.Round(dy);
        var result = new FilterImage(src.Width, src.Height, src.Linear);
        for (int y = 0; y < src.Height; y++)
        {
            int sy = y - oy;
            if (sy < 0 || sy >= src.Height) continue;
            for (int x = 0; x < src.Width; x++)
            {
                int sx = x - ox;
                if (sx < 0 || sx >= src.Width) continue;
                Array.Copy(src.Px, (sy * src.Width + sx) * 4, result.Px, (y * src.Width + x) * 4, 4);
            }
        }
        return result;
    }

    /// <summary>feMorphology: per-channel min (erode) or max (dilate) over a (2rx+1) x (2ry+1) window.</summary>
    public static FilterImage Morphology(FilterImage src, bool dilate, int radiusX, int radiusY)
    {
        var current = src;
        if (radiusX > 0) current = MorphAxis(current, dilate, radiusX, horizontal: true);
        if (radiusY > 0) current = MorphAxis(current, dilate, radiusY, horizontal: false);
        return current;
    }

    private static FilterImage MorphAxis(FilterImage src, bool dilate, int radius, bool horizontal)
    {
        var result = new FilterImage(src.Width, src.Height, src.Linear);
        int w = src.Width, h = src.Height;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < 4; c++)
                {
                    float best = dilate ? 0f : 1f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = horizontal ? x + k : x, sy = horizontal ? y : y + k;
                        // Outside the image is transparent black: it wins every erode, loses every dilate
                        float v = sx < 0 || sx >= w || sy < 0 || sy >= h ? 0f : src.Px[(sy * w + sx) * 4 + c];
                        best = dilate ? Math.Max(best, v) : Math.Min(best, v);
                    }
                    result.Px[(y * w + x) * 4 + c] = best;
                }
            }
        }
        return result;
    }

    // ── Color ─────────────────────────────────────────────────────────────────

    public static FilterImage Flood(int width, int height, bool linear, float r, float g, float b, float a)
    {
        var img = new FilterImage(width, height, linear);
        for (int i = 0; i < img.Px.Length; i += 4)
        {
            img.Px[i] = r * a; img.Px[i + 1] = g * a; img.Px[i + 2] = b * a; img.Px[i + 3] = a;
        }
        return img;
    }

    /// <summary>The image's alpha channel as an opaque-colored-black picture (SourceAlpha).</summary>
    public static FilterImage AlphaOnly(FilterImage src)
    {
        var img = new FilterImage(src.Width, src.Height, src.Linear);
        for (int i = 3; i < img.Px.Length; i += 4) img.Px[i] = src.Px[i];
        return img;
    }

    /// <summary>feColorMatrix with a row-major 4x5 matrix over unpremultiplied RGBA (offsets in 0..1 units).</summary>
    public static FilterImage ColorMatrix(FilterImage src, float[] m)
    {
        var result = new FilterImage(src.Width, src.Height, src.Linear);
        for (int i = 0; i < src.Px.Length; i += 4)
        {
            float a = src.Px[i + 3];
            float r = 0, g = 0, b = 0;
            if (a > 0f) { r = src.Px[i] / a; g = src.Px[i + 1] / a; b = src.Px[i + 2] / a; }

            float nr = m[0] * r + m[1] * g + m[2] * b + m[3] * a + m[4];
            float ng = m[5] * r + m[6] * g + m[7] * b + m[8] * a + m[9];
            float nb = m[10] * r + m[11] * g + m[12] * b + m[13] * a + m[14];
            float na = Clamp01(m[15] * r + m[16] * g + m[17] * b + m[18] * a + m[19]);
            result.Px[i] = Clamp01(nr) * na;
            result.Px[i + 1] = Clamp01(ng) * na;
            result.Px[i + 2] = Clamp01(nb) * na;
            result.Px[i + 3] = na;
        }
        return result;
    }

    public static float[] SaturateMatrix(float s) => new[]
    {
        0.213f + 0.787f * s, 0.715f - 0.715f * s, 0.072f - 0.072f * s, 0, 0,
        0.213f - 0.213f * s, 0.715f + 0.285f * s, 0.072f - 0.072f * s, 0, 0,
        0.213f - 0.213f * s, 0.715f - 0.715f * s, 0.072f + 0.928f * s, 0, 0,
        0, 0, 0, 1, 0,
    };

    public static float[] HueRotateMatrix(float degrees)
    {
        float rad = degrees * (float)Math.PI / 180f;
        float c = (float)Math.Cos(rad), s = (float)Math.Sin(rad);
        return new[]
        {
            0.213f + c * 0.787f - s * 0.213f, 0.715f - c * 0.715f - s * 0.715f, 0.072f - c * 0.072f + s * 0.928f, 0, 0,
            0.213f - c * 0.213f + s * 0.143f, 0.715f + c * 0.285f + s * 0.140f, 0.072f - c * 0.072f - s * 0.283f, 0, 0,
            0.213f - c * 0.213f - s * 0.787f, 0.715f - c * 0.715f + s * 0.715f, 0.072f + c * 0.928f + s * 0.072f, 0, 0,
            0, 0, 0, 1, 0,
        };
    }

    public static readonly float[] LuminanceToAlphaMatrix =
    {
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0.2125f, 0.7154f, 0.0721f, 0, 0,
    };

    /// <summary>A transfer curve for one channel of feComponentTransfer.</summary>
    internal sealed class TransferFunction
    {
        public string Type = "identity";
        public float[] Table = Array.Empty<float>();
        public float Slope = 1f, Intercept, Amplitude = 1f, Exponent = 1f, Offset;

        public float Apply(float c)
        {
            switch (Type)
            {
                case "table":
                {
                    int n = Table.Length - 1;
                    if (n < 0) return c;
                    if (n == 0) return Table[0];
                    int k = Math.Min(n - 1, (int)(c * n));
                    return Table[k] + (c * n - k) * (Table[k + 1] - Table[k]);
                }
                case "discrete":
                {
                    int n = Table.Length;
                    if (n == 0) return c;
                    return Table[Math.Min(n - 1, (int)(c * n))];
                }
                case "linear": return Slope * c + Intercept;
                case "gamma": return Amplitude * (float)Math.Pow(c, Exponent) + Offset;
                default: return c;
            }
        }
    }

    /// <summary>feComponentTransfer: independent per-channel curves on unpremultiplied color (null = identity).</summary>
    public static FilterImage ComponentTransfer(FilterImage src, TransferFunction?[] funcs)
    {
        var result = new FilterImage(src.Width, src.Height, src.Linear);
        for (int i = 0; i < src.Px.Length; i += 4)
        {
            float a = src.Px[i + 3];
            float[] ch = { 0, 0, 0, a };
            if (a > 0f) { ch[0] = src.Px[i] / a; ch[1] = src.Px[i + 1] / a; ch[2] = src.Px[i + 2] / a; }
            for (int c = 0; c < 4; c++)
                if (funcs[c] != null) ch[c] = Clamp01(funcs[c]!.Apply(ch[c]));
            float na = ch[3];
            result.Px[i] = ch[0] * na; result.Px[i + 1] = ch[1] * na; result.Px[i + 2] = ch[2] * na; result.Px[i + 3] = na;
        }
        return result;
    }

    // ── Compositing ───────────────────────────────────────────────────────────

    /// <summary>feMerge / source-over: <paramref name="top"/> drawn over <paramref name="bottom"/>.</summary>
    public static FilterImage Over(FilterImage top, FilterImage bottom)
    {
        var result = new FilterImage(top.Width, top.Height, top.Linear);
        for (int i = 0; i < result.Px.Length; i += 4)
        {
            float inv = 1f - top.Px[i + 3];
            for (int c = 0; c < 4; c++) result.Px[i + c] = top.Px[i + c] + bottom.Px[i + c] * inv;
        }
        return result;
    }

    /// <summary>feComposite. <paramref name="src"/> is <c>in</c>, <paramref name="dst"/> is <c>in2</c>.</summary>
    public static FilterImage Composite(FilterImage src, FilterImage dst, string op, float k1, float k2, float k3, float k4)
    {
        var result = new FilterImage(src.Width, src.Height, src.Linear);
        for (int i = 0; i < result.Px.Length; i += 4)
        {
            float sa = src.Px[i + 3], da = dst.Px[i + 3];
            for (int c = 0; c < 4; c++)
            {
                float s = src.Px[i + c], d = dst.Px[i + c], v;
                switch (op)
                {
                    case "in": v = s * da; break;
                    case "out": v = s * (1f - da); break;
                    case "atop": v = s * da + d * (1f - sa); break;
                    case "xor": v = s * (1f - da) + d * (1f - sa); break;
                    case "arithmetic": v = k1 * s * d + k2 * s + k3 * d + k4; break;
                    default: v = s + d * (1f - sa); break; // over
                }
                result.Px[i + c] = Clamp01(v);
            }
            // Premultiplied colour can never exceed alpha (arithmetic can violate that)
            float a = result.Px[i + 3];
            for (int c = 0; c < 3; c++) if (result.Px[i + c] > a) result.Px[i + c] = a;
        }
        return result;
    }

    /// <summary>feBlend: <paramref name="top"/> is <c>in</c>, <paramref name="backdrop"/> is <c>in2</c>.</summary>
    public static FilterImage Blend(FilterImage top, FilterImage backdrop, string mode)
    {
        var result = new FilterImage(top.Width, top.Height, top.Linear);
        for (int i = 0; i < result.Px.Length; i += 4)
        {
            float sa = top.Px[i + 3], ba = backdrop.Px[i + 3];
            result.Px[i + 3] = sa + ba - sa * ba;
            for (int c = 0; c < 3; c++)
            {
                float sp = top.Px[i + c], bp = backdrop.Px[i + c];
                float cs = sa > 0f ? sp / sa : 0f, cb = ba > 0f ? bp / ba : 0f;
                result.Px[i + c] = (1f - ba) * sp + (1f - sa) * bp + sa * ba * BlendChannel(mode, cb, cs);
            }
        }
        return result;
    }

    /// <summary>The separable blend functions of Compositing and Blending Level 1 (cb backdrop, cs source).</summary>
    private static float BlendChannel(string mode, float cb, float cs)
    {
        switch (mode)
        {
            case "multiply": return cb * cs;
            case "screen": return cb + cs - cb * cs;
            case "darken": return Math.Min(cb, cs);
            case "lighten": return Math.Max(cb, cs);
            case "overlay": return HardLight(cs, cb);
            case "hard-light": return HardLight(cb, cs);
            case "color-dodge": return cb == 0f ? 0f : cs >= 1f ? 1f : Math.Min(1f, cb / (1f - cs));
            case "color-burn": return cb >= 1f ? 1f : cs <= 0f ? 0f : 1f - Math.Min(1f, (1f - cb) / cs);
            case "soft-light":
            {
                if (cs <= 0.5f) return cb - (1f - 2f * cs) * cb * (1f - cb);
                float d = cb <= 0.25f ? ((16f * cb - 12f) * cb + 4f) * cb : (float)Math.Sqrt(cb);
                return cb + (2f * cs - 1f) * (d - cb);
            }
            case "difference": return Math.Abs(cb - cs);
            case "exclusion": return cb + cs - 2f * cb * cs;
            default: return cs; // normal
        }
    }

    private static float HardLight(float cb, float cs)
        => cs <= 0.5f ? cb * 2f * cs : cb + (2f * cs - 1f) - cb * (2f * cs - 1f);

    public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
}
