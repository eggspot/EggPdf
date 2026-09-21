using System;

namespace EggPdf.Svg;

/// <summary>
/// A float RGBA bitmap with premultiplied alpha, the working format of the SVG filter graph.
/// <see cref="Linear"/> records whether the color channels are in linearRGB (the SVG default
/// <c>color-interpolation-filters</c>) or sRGB, so each primitive can convert its inputs to the
/// space it operates in.
/// </summary>
internal sealed class FilterImage
{
    public readonly int Width;
    public readonly int Height;
    /// <summary>R,G,B,A per pixel, row-major, premultiplied, each 0..1.</summary>
    public readonly float[] Px;
    public bool Linear;

    public FilterImage(int width, int height, bool linear = false)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Px = new float[Width * Height * 4];
        Linear = linear;
    }

    public FilterImage Clone()
    {
        var copy = new FilterImage(Width, Height, Linear);
        Array.Copy(Px, copy.Px, Px.Length);
        return copy;
    }

    /// <summary>This image expressed in the requested color space (returns this when it already is).</summary>
    public FilterImage InSpace(bool linear)
    {
        if (Linear == linear) return this;
        var result = new FilterImage(Width, Height, linear);
        for (int i = 0; i < Px.Length; i += 4)
        {
            float a = Px[i + 3];
            if (a <= 0f) continue;
            for (int c = 0; c < 3; c++)
            {
                float v = Math.Min(1f, Px[i + c] / a);
                v = linear ? SrgbToLinear(v) : LinearToSrgb(v);
                result.Px[i + c] = v * a;
            }
            result.Px[i + 3] = a;
        }
        return result;
    }

    public static float SrgbToLinear(float v)
        => v <= 0.04045f ? v / 12.92f : (float)Math.Pow((v + 0.055f) / 1.055f, 2.4);

    public static float LinearToSrgb(float v)
        => v <= 0.0031308f ? v * 12.92f : 1.055f * (float)Math.Pow(v, 1.0 / 2.4) - 0.055f;
}
