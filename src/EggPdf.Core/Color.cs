using System;

namespace EggPdf.Core;

/// <summary>
/// Represents an RGBA color value.
/// </summary>
public readonly struct Color : IEquatable<Color>
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
            case 3: // #RGB
                return FromRgb(
                    ParseHexPair(hex[offset], hex[offset]),
                    ParseHexPair(hex[offset + 1], hex[offset + 1]),
                    ParseHexPair(hex[offset + 2], hex[offset + 2]));
            case 4: // #RGBA
                return FromRgba(
                    ParseHexPair(hex[offset], hex[offset]),
                    ParseHexPair(hex[offset + 1], hex[offset + 1]),
                    ParseHexPair(hex[offset + 2], hex[offset + 2]),
                    ParseHexPair(hex[offset + 3], hex[offset + 3]));
            case 6: // #RRGGBB
                return FromRgb(
                    ParseHexPair(hex[offset], hex[offset + 1]),
                    ParseHexPair(hex[offset + 2], hex[offset + 3]),
                    ParseHexPair(hex[offset + 4], hex[offset + 5]));
            case 8: // #RRGGBBAA
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
    /// Try to parse any CSS color value: hex, named, rgb(), rgba(), hsl(), hsla().
    /// Returns null if the value cannot be parsed.
    /// </summary>
    public static Color? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (value == "transparent")
            return Transparent;

        // Hex colors
        if (value.StartsWith("#"))
        {
            try { return FromHex(value); }
            catch { return null; }
        }

        // rgb() / rgba()
        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRgbFunction(value);
        }

        // hsl() / hsla()
        if (value.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseHslFunction(value);
        }

        // color-mix()
        if (value.StartsWith("color-mix(", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseColorMix(value);
        }

        // Named colors
        return TryParseNamed(value);
    }

    private static Color? TryParseRgbFunction(string value)
    {
        // rgb(255, 0, 0) or rgba(255, 0, 0, 0.5)
        // Also modern: rgb(255 0 0 / 0.5)
        int openParen = value.IndexOf('(');
        int closeParen = value.LastIndexOf(')');
        if (openParen < 0 || closeParen < 0) return null;

        var inner = value.Substring(openParen + 1, closeParen - openParen - 1).Trim();

        // Handle / separator for alpha: "255 0 0 / 0.5"
        float alpha = 1f;
        int slashIdx = inner.IndexOf('/');
        if (slashIdx >= 0)
        {
            var alphaPart = inner.Substring(slashIdx + 1).Trim();
            alpha = ParseColorNumber(alphaPart, true);
            inner = inner.Substring(0, slashIdx).Trim();
        }

        var parts = inner.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;

        byte r = ClampByte(ParseColorNumber(parts[0].Trim(), false));
        byte g = ClampByte(ParseColorNumber(parts[1].Trim(), false));
        byte b = ClampByte(ParseColorNumber(parts[2].Trim(), false));

        if (parts.Length >= 4 && slashIdx < 0)
            alpha = ParseColorNumber(parts[3].Trim(), true);

        return FromRgba(r, g, b, ClampByte(alpha * 255f));
    }

    private static Color? TryParseHslFunction(string value)
    {
        // hsl(120, 100%, 50%) or hsla(120, 100%, 50%, 0.5)
        int openParen = value.IndexOf('(');
        int closeParen = value.LastIndexOf(')');
        if (openParen < 0 || closeParen < 0) return null;

        var inner = value.Substring(openParen + 1, closeParen - openParen - 1).Trim();

        float alpha = 1f;
        int slashIdx = inner.IndexOf('/');
        if (slashIdx >= 0)
        {
            alpha = ParseColorNumber(inner.Substring(slashIdx + 1).Trim(), true);
            inner = inner.Substring(0, slashIdx).Trim();
        }

        var parts = inner.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;

        float h = ParseColorNumber(parts[0].Trim().TrimEnd('d', 'e', 'g'), false); // remove "deg"
        float s = ParseColorNumber(parts[1].Trim().TrimEnd('%'), false) / 100f;
        float l = ParseColorNumber(parts[2].Trim().TrimEnd('%'), false) / 100f;

        if (parts.Length >= 4 && slashIdx < 0)
            alpha = ParseColorNumber(parts[3].Trim(), true);

        // HSL to RGB conversion
        HslToRgb(h, s, l, out byte r, out byte g, out byte b);
        return FromRgba(r, g, b, ClampByte(alpha * 255f));
    }

    private static float ParseColorNumber(string s, bool isAlpha)
    {
        s = s.Trim();
        if (s.EndsWith("%"))
        {
            if (float.TryParse(s.Substring(0, s.Length - 1), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float pct))
                return isAlpha ? pct / 100f : pct / 100f * 255f;
            return 0;
        }
        if (float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float val))
            return val;
        return 0;
    }

    private static byte ClampByte(float value)
    {
        if (value <= 0) return 0;
        if (value >= 255) return 255;
        return (byte)Math.Round(value);
    }

    private static void HslToRgb(float h, float s, float l, out byte r, out byte g, out byte b)
    {
        h = ((h % 360) + 360) % 360; // normalize to 0-360
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

    private static Color? TryParseColorMix(string value)
    {
        // color-mix(in <colorspace>, <color1> [<pct>], <color2> [<pct>])
        // We support srgb, hsl (all treated as sRGB linear mix for now)
        int openParen = value.IndexOf('(');
        int closeParen = value.LastIndexOf(')');
        if (openParen < 0 || closeParen < 0) return null;

        var inner = value.Substring(openParen + 1, closeParen - openParen - 1).Trim();

        // Split on commas to get: "in srgb", "red 30%", "blue"
        var parts = inner.Split(',');
        if (parts.Length != 3) return null;

        // parts[0] = "in srgb" — ignore color space, always do sRGB
        // parts[1] = "red 30%"  parts[2] = "blue 70%"
        var (c1, p1) = ParseColorWithOptionalPct(parts[1].Trim());
        var (c2, p2) = ParseColorWithOptionalPct(parts[2].Trim());

        if (c1 == null || c2 == null) return null;

        // If neither has a percentage, default to 50/50
        float w1 = p1 ?? (p2.HasValue ? 1f - p2.Value : 0.5f);
        float w2 = p2 ?? (1f - w1);

        // Normalize so they sum to 1
        float total = w1 + w2;
        if (total <= 0) return null;
        w1 /= total;
        w2 /= total;

        byte r = ClampByte(c1.Value.R * w1 + c2.Value.R * w2);
        byte g = ClampByte(c1.Value.G * w1 + c2.Value.G * w2);
        byte b = ClampByte(c1.Value.B * w1 + c2.Value.B * w2);
        byte a = ClampByte(c1.Value.A * w1 + c2.Value.A * w2);
        return FromRgba(r, g, b, a);
    }

    private static (Color? color, float? pct) ParseColorWithOptionalPct(string token)
    {
        // token = "red 30%" or "blue" or "#ff0000 50%"
        // Find a trailing percentage
        float? pct = null;
        var lastSpace = token.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            var maybePct = token.Substring(lastSpace + 1).Trim();
            if (maybePct.EndsWith("%"))
            {
                if (float.TryParse(maybePct.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float p))
                {
                    pct = p / 100f;
                    token = token.Substring(0, lastSpace).Trim();
                }
            }
        }
        return (TryParse(token), pct);
    }

    public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;
    public override bool Equals(object? obj) => obj is Color other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    public static bool operator ==(Color left, Color right) => left.Equals(right);
    public static bool operator !=(Color left, Color right) => !left.Equals(right);
    public override string ToString() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}
