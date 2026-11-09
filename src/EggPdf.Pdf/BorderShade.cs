using System;

namespace EggPdf.Pdf;

internal enum BorderSide
{
    Top,
    Right,
    Bottom,
    Left
}

internal static class BorderShade
{
    private const float VeryDarkLuminance = 0.014443844f;
    private const float VeryLightLuminance = 0.83077f;
    private const float ShadeStep = 0.33f;
    private const float LightenedBlack = 84f / 255f;
    private const float JustBelow256 = 255.99998f;

    public static bool DarkensSide(BorderSide side, bool inset) =>
        (side == BorderSide.Top || side == BorderSide.Left) == inset;

    public static (float R, float G, float B) InsetOutsetColor(bool darken, float r, float g, float b)
    {
        float luminance = RelativeLuminance(r, g, b);
        if (luminance <= VeryDarkLuminance)
        {
            var lighter = Light(r, g, b);
            return darken ? lighter : Light(lighter.R, lighter.G, lighter.B);
        }
        if (darken) return Dark(r, g, b);
        return luminance > VeryLightLuminance ? (r, g, b) : Light(r, g, b);
    }

    private static (float R, float G, float B) Dark(float r, float g, float b)
    {
        float v = Math.Max(r, Math.Max(g, b));
        float multiplier = v == 0f ? 0f : Math.Max(0f, (v - ShadeStep) / v);
        return (Quantize(r * multiplier), Quantize(g * multiplier), Quantize(b * multiplier));
    }

    private static (float R, float G, float B) Light(float r, float g, float b)
    {
        float v = Math.Max(r, Math.Max(g, b));
        if (v == 0f) return (LightenedBlack, LightenedBlack, LightenedBlack);
        float multiplier = Math.Min(1f, v + ShadeStep) / v;
        return (Quantize(r * multiplier), Quantize(g * multiplier), Quantize(b * multiplier));
    }

    private static float Quantize(float channel) => (int)(channel * JustBelow256) / 255f;

    private static float RelativeLuminance(float r, float g, float b) =>
        0.2126f * Linear(r) + 0.7152f * Linear(g) + 0.0722f * Linear(b);

    private static float Linear(float channel) =>
        channel <= 0.04045f ? channel / 12.92f : (float)Math.Pow((channel + 0.055f) / 1.055f, 2.4);
}
