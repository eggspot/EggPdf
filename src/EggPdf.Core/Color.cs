using System;
using System.Collections.Generic;
using System.Globalization;

namespace EggPdf.Core;

/// <summary>
/// Represents an RGBA color value.
/// </summary>
public readonly partial struct Color : IEquatable<Color>
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte A { get; }

    public bool IsOpaque => A == 255;
    public bool IsTransparent => A == 0;

    private Color(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static Color FromRgb(byte r, byte g, byte b) => new(r, g, b, 255);
    public static Color FromRgba(byte r, byte g, byte b, byte a) => new(r, g, b, a);

    public static readonly Color Transparent = new(0, 0, 0, 0);
    public static readonly Color Black = FromRgb(0, 0, 0);
    public static readonly Color White = FromRgb(255, 255, 255);

    public static Color FromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            throw new ArgumentException("Hex color string cannot be null or empty.", nameof(hex));

        int offset = hex[0] == '#' ? 1 : 0;
        int len = hex.Length - offset;

        switch (len)
        {
            case 3:
                return FromRgb(
                    ParseHexPair(hex[offset], hex[offset]),
                    ParseHexPair(hex[offset + 1], hex[offset + 1]),
                    ParseHexPair(hex[offset + 2], hex[offset + 2]));
            case 4:
                return FromRgba(
                    ParseHexPair(hex[offset], hex[offset]),
                    ParseHexPair(hex[offset + 1], hex[offset + 1]),
                    ParseHexPair(hex[offset + 2], hex[offset + 2]),
                    ParseHexPair(hex[offset + 3], hex[offset + 3]));
            case 6:
                return FromRgb(
                    ParseHexPair(hex[offset], hex[offset + 1]),
                    ParseHexPair(hex[offset + 2], hex[offset + 3]),
                    ParseHexPair(hex[offset + 4], hex[offset + 5]));
            case 8:
                return FromRgba(
                    ParseHexPair(hex[offset], hex[offset + 1]),
                    ParseHexPair(hex[offset + 2], hex[offset + 3]),
                    ParseHexPair(hex[offset + 4], hex[offset + 5]),
                    ParseHexPair(hex[offset + 6], hex[offset + 7]));
            default:
                throw new ArgumentException($"Invalid hex color: {hex}", nameof(hex));
        }
    }

    private static byte ParseHexPair(char hi, char lo)
        => (byte)((HexVal(hi) << 4) | HexVal(lo));

    private static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new ArgumentException($"Invalid hex character: {c}")
    };

    public static Color FromName(string name)
        => TryParseNamed(name) ?? throw new ArgumentException($"Unknown color name: {name}", nameof(name));

    public static Color? TryParseNamed(string name)
        => CssNamedColors.TryGet(name, out var color) ? color : null;

    /// <summary>
    /// Try to parse any CSS color value: hex, named, rgb(), rgba(), hsl(), hsla(), hwb(), lab(), lch(), oklab(), oklch(),
    /// color(), color-mix() and light-dark().
    /// Returns null if the value cannot be parsed.
    /// </summary>
    public static Color? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase))
            return Transparent;

        if (value[0] == '#')
        {
            try { return FromHex(value); }
            catch { return null; }
        }

        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            return TryParseRgbFunction(value);

        if (value.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
            return TryParseHslFunction(value);

        if (value.StartsWith("color-mix(", StringComparison.OrdinalIgnoreCase))
            return TryParseColorMix(value);

        if (value.StartsWith("light-dark(", StringComparison.OrdinalIgnoreCase))
            return TryParseLightDark(value);

        if (value.StartsWith("hwb(", StringComparison.OrdinalIgnoreCase))
            return TryParseHwb(value);

        if (value.StartsWith("oklch(", StringComparison.OrdinalIgnoreCase))
            return TryParseOklch(value);

        if (value.StartsWith("oklab(", StringComparison.OrdinalIgnoreCase))
            return TryParseOklab(value);

        if (value.StartsWith("lch(", StringComparison.OrdinalIgnoreCase))
            return TryParseLch(value);

        if (value.StartsWith("lab(", StringComparison.OrdinalIgnoreCase))
            return TryParseLab(value);

        if (value.StartsWith("color(", StringComparison.OrdinalIgnoreCase))
            return TryParseColorFunction(value);

        return TryParseNamed(value);
    }

    private static Color? TryParseRgbFunction(string value)
    {
        var inner = ExtractInner(value); if (inner == null) return null;
        float? alpha = SplitAlpha(ref inner);

        var parts = inner.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;

        byte r = ClampByte(ParseColorNumber(parts[0].Trim(), false));
        byte g = ClampByte(ParseColorNumber(parts[1].Trim(), false));
        byte b = ClampByte(ParseColorNumber(parts[2].Trim(), false));

        if (parts.Length >= 4 && !alpha.HasValue)
            alpha = ParseColorNumber(parts[3].Trim(), true);

        return FromRgba(r, g, b, AlphaByte(alpha));
    }

    private static Color? TryParseHslFunction(string value)
    {
        var inner = ExtractInner(value); if (inner == null) return null;
        float? alpha = SplitAlpha(ref inner);

        var parts = inner.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;

        float h = ParseHue(parts[0]);
        float s = ParseScaledComponent(parts[1], 100f) / 100f;
        float l = ParseScaledComponent(parts[2], 100f) / 100f;

        if (parts.Length >= 4 && !alpha.HasValue)
            alpha = ParseColorNumber(parts[3].Trim(), true);

        HslToRgb(h, s, l, out byte r, out byte g, out byte b);
        return FromRgba(r, g, b, AlphaByte(alpha));
    }

    /// <summary>Removes a "/ alpha" suffix from <paramref name="inner"/> and returns the alpha (0-1), or null when there is none.</summary>
    private static float? SplitAlpha(ref string inner)
    {
        int slashIdx = inner.IndexOf('/');
        if (slashIdx < 0) return null;
        float alpha = ParseColorNumber(inner.Substring(slashIdx + 1).Trim(), true);
        inner = inner.Substring(0, slashIdx).Trim();
        return alpha;
    }

    private static byte AlphaByte(float? alpha) => alpha.HasValue ? ClampByte(alpha.Value * 255f) : (byte)255;

    private static bool EndsWithPercent(string s) => s.Length > 0 && s[s.Length - 1] == '%';

    private static float ParseColorNumber(string s, bool isAlpha)
    {
        s = s.Trim();
        if (EndsWithPercent(s))
        {
            if (float.TryParse(s.Substring(0, s.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                return isAlpha ? pct / 100f : pct / 100f * 255f;
            return 0;
        }
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            return val;
        return 0;
    }

    /// <summary>A number, or a percentage of <paramref name="hundredPercent"/> ("none" is 0).</summary>
    private static float ParseScaledComponent(string s, float hundredPercent)
    {
        s = s.Trim();
        bool isPercent = EndsWithPercent(s);
        if (isPercent) s = s.Substring(0, s.Length - 1);
        if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            return 0;
        return isPercent ? val / 100f * hundredPercent : val;
    }

    /// <summary>A CSS hue in degrees: a bare number is degrees; deg, grad, rad and turn units are converted.</summary>
    private static float ParseHue(string token)
    {
        token = token.Trim();
        if (token.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
            return ParseScaledComponent(token.Substring(0, token.Length - 3), 0f);
        if (token.EndsWith("grad", StringComparison.OrdinalIgnoreCase))
            return ParseScaledComponent(token.Substring(0, token.Length - 4), 0f) * 0.9f;
        if (token.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
            return (float)(ParseScaledComponent(token.Substring(0, token.Length - 3), 0f) * 180.0 / Math.PI);
        if (token.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
            return ParseScaledComponent(token.Substring(0, token.Length - 4), 0f) * 360f;
        return ParseScaledComponent(token, 0f);
    }

    private static byte ClampByte(float value)
    {
        if (value <= 0) return 0;
        if (value >= 255) return 255;
        return (byte)Math.Round(value);
    }

    private static void HslToRgb(float h, float s, float l, out byte r, out byte g, out byte b)
    {
        h = ((h % 360) + 360) % 360;
        float c = (1 - Math.Abs(2 * l - 1)) * s;
        float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
        float m = l - c / 2;
        float r1, g1, b1;

        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        r = ClampByte((r1 + m) * 255f);
        g = ClampByte((g1 + m) * 255f);
        b = ClampByte((b1 + m) * 255f);
    }

    /// <summary>light-dark(light, dark): a PDF is always rendered in the light color scheme, so this is the first argument.</summary>
    private static Color? TryParseLightDark(string value)
    {
        var inner = ExtractInner(value); if (inner == null) return null;
        var parts = SplitTopLevel(inner, ',');
        return parts.Count == 2 ? TryParse(parts[0]) : null;
    }

    private static Color? TryParseHwb(string value)
    {
        var inner = ExtractInner(value); if (inner == null) return null;
        float? alpha = SplitAlpha(ref inner);
        var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        float h = ParseHue(parts[0]);
        float w = ParseScaledComponent(parts[1], 100f) / 100f;
        float b = ParseScaledComponent(parts[2], 100f) / 100f;
        if (w + b > 1f) { float s = w + b; w /= s; b /= s; }
        HslToRgb(h, 1f, 0.5f, out byte r0, out byte g0, out byte b0);
        float rf = r0 / 255f, gf = g0 / 255f, bf = b0 / 255f;
        rf = rf * (1f - w - b) + w;
        gf = gf * (1f - w - b) + w;
        bf = bf * (1f - w - b) + w;
        return FromRgba(ClampByte(rf * 255f), ClampByte(gf * 255f), ClampByte(bf * 255f), AlphaByte(alpha));
    }

    private const float OklabLightnessAt100Percent = 1f;
    private const float OklabAxisAt100Percent = 0.4f;
    private const float LabLightnessAt100Percent = 100f;
    private const float LabAxisAt100Percent = 125f;
    private const float LchChromaAt100Percent = 150f;

    private static Color? TryParseOklab(string value)
    {
        var inner = ExtractInner(value); if (inner == null) return null;
        float? alpha = SplitAlpha(ref inner);
        var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        float L = ParseScaledComponent(parts[0], OklabLightnessAt100Percent);
        float a = ParseScaledComponent(parts[1], OklabAxisAt100Percent);
        float b = ParseScaledComponent(parts[2], OklabAxisAt100Percent);
        return FromOklab(L, a, b, alpha);
    }

    private static Color? TryParseOklch(string value)
    {
        var inner = ExtractInner(value); if (inner == null) return null;
        float? alpha = SplitAlpha(ref inner);
        var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        float L = ParseScaledComponent(parts[0], OklabLightnessAt100Percent);
        float C = ParseScaledComponent(parts[1], OklabAxisAt100Percent);
        double hRad = ParseHue(parts[2]) * Math.PI / 180.0;
        return FromOklab(L, (float)(C * Math.Cos(hRad)), (float)(C * Math.Sin(hRad)), alpha);
    }

    private static Color? TryParseLab(string value)
    {
        var inner = ExtractInner(value); if (inner == null) return null;
        float? alpha = SplitAlpha(ref inner);
        var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        float L = ParseScaledComponent(parts[0], LabLightnessAt100Percent);
        float a = ParseScaledComponent(parts[1], LabAxisAt100Percent);
        float b = ParseScaledComponent(parts[2], LabAxisAt100Percent);
        return FromCieLab(L, a, b, alpha);
    }

    private static Color? TryParseLch(string value)
    {
        var inner = ExtractInner(value); if (inner == null) return null;
        float? alpha = SplitAlpha(ref inner);
        var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        float L = ParseScaledComponent(parts[0], LabLightnessAt100Percent);
        float C = ParseScaledComponent(parts[1], LchChromaAt100Percent);
        double hRad = ParseHue(parts[2]) * Math.PI / 180.0;
        return FromCieLab(L, (float)(C * Math.Cos(hRad)), (float)(C * Math.Sin(hRad)), alpha);
    }

    private static Color FromOklab(float L, float a, float b, float? alpha)
    {
        OklabToSrgb(L, a, b, out double r, out double g, out double bl);
        return FromUnitRgb(r, g, bl, alpha);
    }

    private static Color FromCieLab(float L, float a, float b, float? alpha)
    {
        CieLabToSrgb(L, a, b, out double r, out double g, out double bl);
        return FromUnitRgb(r, g, bl, alpha);
    }

    private static Color FromUnitRgb(double r, double g, double b, float? alpha)
        => FromRgba(ClampByte((float)(r * 255.0)), ClampByte((float)(g * 255.0)), ClampByte((float)(b * 255.0)), AlphaByte(alpha));

    private static string? ExtractInner(string value)
    {
        int op = value.IndexOf('(');
        int cp = value.LastIndexOf(')');
        if (op < 0 || cp <= op) return null;
        return value.Substring(op + 1, cp - op - 1).Trim();
    }

    /// <summary>Splits on <paramref name="separator"/> outside parentheses, trimming each part.</summary>
    private static List<string> SplitTopLevel(string value, char separator)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == separator && depth == 0)
            {
                parts.Add(value.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        parts.Add(value.Substring(start).Trim());
        return parts;
    }

    public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;
    public override bool Equals(object? obj) => obj is Color other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    public static bool operator ==(Color left, Color right) => left.Equals(right);
    public static bool operator !=(Color left, Color right) => !left.Equals(right);
    public override string ToString() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}
