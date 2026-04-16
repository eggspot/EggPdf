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
            return TryParseColorMix(value);

        // hwb()
        if (value.StartsWith("hwb(", StringComparison.OrdinalIgnoreCase))
            return TryParseHwb(value);

        // oklch()
        if (value.StartsWith("oklch(", StringComparison.OrdinalIgnoreCase))
            return TryParseOklch(value);

        // oklab()
        if (value.StartsWith("oklab(", StringComparison.OrdinalIgnoreCase))
            return TryParseOklab(value);

        // lch()
        if (value.StartsWith("lch(", StringComparison.OrdinalIgnoreCase))
            return TryParseLch(value);

        // lab()
        if (value.StartsWith("lab(", StringComparison.OrdinalIgnoreCase))
            return TryParseLab(value);

        // color()
        if (value.StartsWith("color(", StringComparison.OrdinalIgnoreCase))
            return TryParseColorFunction(value);

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

    // ── hwb() ────────────────────────────────────────────────────────────────
    private static Color? TryParseHwb(string value)
    {
        // hwb(hue whiteness% blackness% [/ alpha])
        var inner = ExtractInner(value); if (inner == null) return null;
        float alpha = 1f;
        var slashIdx = inner.IndexOf('/');
        if (slashIdx >= 0)
        {
            alpha = ParseColorNumber(inner.Substring(slashIdx + 1).Trim(), true);
            inner = inner.Substring(0, slashIdx).Trim();
        }
        var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        float h = ParseColorNumber(parts[0].TrimEnd('d','e','g'), false);
        float w = ParseColorNumber(parts[1].TrimEnd('%'), false) / 100f;
        float b = ParseColorNumber(parts[2].TrimEnd('%'), false) / 100f;
        // Normalize: if w+b > 1, scale them
        if (w + b > 1f) { float s = w + b; w /= s; b /= s; }
        // HWB to RGB: start with hue, add white, add black
        HslToRgb(h, 1f, 0.5f, out byte r0, out byte g0, out byte b0);
        float rf = r0 / 255f, gf = g0 / 255f, bf = b0 / 255f;
        rf = rf * (1f - w - b) + w;
        gf = gf * (1f - w - b) + w;
        bf = bf * (1f - w - b) + w;
        return FromRgba(ClampByte(rf * 255f), ClampByte(gf * 255f), ClampByte(bf * 255f), ClampByte(alpha * 255f));
    }

    // ── oklab() ──────────────────────────────────────────────────────────────
    private static Color? TryParseOklab(string value)
    {
        // oklab(L a b [/ alpha])  L: 0-1, a/b: -0.5..0.5
        var inner = ExtractInner(value); if (inner == null) return null;
        float alpha = 1f;
        var slashIdx = inner.IndexOf('/');
        if (slashIdx >= 0) { alpha = ParseColorNumber(inner.Substring(slashIdx + 1).Trim(), true); inner = inner.Substring(0, slashIdx).Trim(); }
        var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        float L = ParseColorNumber(parts[0].TrimEnd('%'), false);
        if (parts[0].EndsWith("%")) L /= 100f;
        float a = ParseColorNumber(parts[1], false);
        float b = ParseColorNumber(parts[2], false);
        OklabToSrgb(L, a, b, out float r, out float g, out float bl);
        return FromRgba(ClampByte(r * 255f), ClampByte(g * 255f), ClampByte(bl * 255f), ClampByte(alpha * 255f));
    }

    // ── oklch() ──────────────────────────────────────────────────────────────
    private static Color? TryParseOklch(string value)
    {
        // oklch(L C H [/ alpha])  L: 0-1, C: 0+, H: 0-360deg
        var inner = ExtractInner(value); if (inner == null) return null;
        float alpha = 1f;
        var slashIdx = inner.IndexOf('/');
        if (slashIdx >= 0) { alpha = ParseColorNumber(inner.Substring(slashIdx + 1).Trim(), true); inner = inner.Substring(0, slashIdx).Trim(); }
        var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        float L = ParseColorNumber(parts[0].TrimEnd('%'), false);
        if (parts[0].EndsWith("%")) L /= 100f;
        float C = ParseColorNumber(parts[1], false);
        float H = ParseColorNumber(parts[2].TrimEnd('d','e','g'), false);
        float hRad = H * (float)Math.PI / 180f;
        float a = C * (float)Math.Cos(hRad);
        float b = C * (float)Math.Sin(hRad);
        OklabToSrgb(L, a, b, out float r, out float g, out float bl);
        return FromRgba(ClampByte(r * 255f), ClampByte(g * 255f), ClampByte(bl * 255f), ClampByte(alpha * 255f));
    }

    // ── lab() ────────────────────────────────────────────────────────────────
    private static Color? TryParseLab(string value)
    {
        // lab(L a b [/ alpha])  L: 0-100, a/b: -125..125
        var inner = ExtractInner(value); if (inner == null) return null;
        float alpha = 1f;
        var slashIdx = inner.IndexOf('/');
        if (slashIdx >= 0) { alpha = ParseColorNumber(inner.Substring(slashIdx + 1).Trim(), true); inner = inner.Substring(0, slashIdx).Trim(); }
        var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        float L = ParseColorNumber(parts[0].TrimEnd('%'), false);
        float a = ParseColorNumber(parts[1], false);
        float b = ParseColorNumber(parts[2], false);
        CieLabToSrgb(L, a, b, out float r, out float g, out float bl);
        return FromRgba(ClampByte(r * 255f), ClampByte(g * 255f), ClampByte(bl * 255f), ClampByte(alpha * 255f));
    }

    // ── lch() ────────────────────────────────────────────────────────────────
    private static Color? TryParseLch(string value)
    {
        // lch(L C H [/ alpha])  L: 0-100, C: 0+, H: 0-360deg
        var inner = ExtractInner(value); if (inner == null) return null;
        float alpha = 1f;
        var slashIdx = inner.IndexOf('/');
        if (slashIdx >= 0) { alpha = ParseColorNumber(inner.Substring(slashIdx + 1).Trim(), true); inner = inner.Substring(0, slashIdx).Trim(); }
        var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        float L = ParseColorNumber(parts[0].TrimEnd('%'), false);
        float C = ParseColorNumber(parts[1], false);
        float H = ParseColorNumber(parts[2].TrimEnd('d','e','g'), false);
        float hRad = H * (float)Math.PI / 180f;
        float a = C * (float)Math.Cos(hRad);
        float b = C * (float)Math.Sin(hRad);
        CieLabToSrgb(L, a, b, out float r, out float g, out float bl);
        return FromRgba(ClampByte(r * 255f), ClampByte(g * 255f), ClampByte(bl * 255f), ClampByte(alpha * 255f));
    }

    // ── color() ──────────────────────────────────────────────────────────────
    private static Color? TryParseColorFunction(string value)
    {
        // color(colorspace c1 c2 c3 [/ alpha])
        var inner = ExtractInner(value); if (inner == null) return null;
        float alpha = 1f;
        var slashIdx = inner.IndexOf('/');
        if (slashIdx >= 0) { alpha = ParseColorNumber(inner.Substring(slashIdx + 1).Trim(), true); inner = inner.Substring(0, slashIdx).Trim(); }
        var parts = inner.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return null;
        // parts[0] = colorspace, parts[1..3] = components
        float c1 = ParseColorNumber(parts[1], false);
        float c2 = ParseColorNumber(parts[2], false);
        float c3 = ParseColorNumber(parts[3], false);
        var cs = parts[0].ToLowerInvariant();
        float r, g, b;
        if (cs == "display-p3")
        {
            // Approximate display-p3 → sRGB via linear transform (simplified)
            r = Math.Max(0, Math.Min(1, 0.8225f * c1 + 0.1774f * c2 + 0.0001f * c3));
            g = Math.Max(0, Math.Min(1, 0.0332f * c1 + 0.9669f * c2 - 0.0001f * c3));
            b = Math.Max(0, Math.Min(1, 0.0171f * c1 + 0.0724f * c2 + 0.9108f * c3));
        }
        else // srgb, srgb-linear, a98-rgb, prophoto-rgb, rec2020 — all mapped directly for simplicity
        {
            r = Math.Max(0, Math.Min(1, c1));
            g = Math.Max(0, Math.Min(1, c2));
            b = Math.Max(0, Math.Min(1, c3));
        }
        return FromRgba(ClampByte(r * 255f), ClampByte(g * 255f), ClampByte(b * 255f), ClampByte(alpha * 255f));
    }

    // ── Color space conversion helpers ───────────────────────────────────────

    private static string? ExtractInner(string value)
    {
        int op = value.IndexOf('(');
        int cp = value.LastIndexOf(')');
        if (op < 0 || cp <= op) return null;
        return value.Substring(op + 1, cp - op - 1).Trim();
    }

    private static void OklabToSrgb(float L, float a, float b, out float r, out float g, out float bl)
    {
        // Oklab → LMS → linear sRGB → sRGB
        float l_ = L + 0.3963377774f * a + 0.2158037573f * b;
        float m_ = L - 0.1055613458f * a - 0.0638541728f * b;
        float s_ = L - 0.0894841775f * a - 1.2914855480f * b;
        float lc = l_ * l_ * l_;
        float mc = m_ * m_ * m_;
        float sc = s_ * s_ * s_;
        float lr = 4.0767416621f * lc - 3.3077115913f * mc + 0.2309699292f * sc;
        float lg = -1.2684380046f * lc + 2.6097574011f * mc - 0.3413193965f * sc;
        float lb = -0.0041960863f * lc - 0.7034186147f * mc + 1.7076147010f * sc;
        r = LinearToGamma(lr); g = LinearToGamma(lg); bl = LinearToGamma(lb);
        r = Math.Max(0, Math.Min(1, r));
        g = Math.Max(0, Math.Min(1, g));
        bl = Math.Max(0, Math.Min(1, bl));
    }

    private static void CieLabToSrgb(float L, float a, float b, out float r, out float g, out float bl)
    {
        // CIE Lab → XYZ D65 → linear sRGB → sRGB
        float fy = (L + 16f) / 116f;
        float fx = a / 500f + fy;
        float fz = fy - b / 200f;
        const float d = 6f / 29f;
        float X = (fx > d ? fx * fx * fx : 3f * d * d * (fx - 4f / 29f)) * 0.95047f;
        float Y = (fy > d ? fy * fy * fy : 3f * d * d * (fy - 4f / 29f)) * 1.00000f;
        float Z = (fz > d ? fz * fz * fz : 3f * d * d * (fz - 4f / 29f)) * 1.08883f;
        // XYZ D65 → linear sRGB
        float lr =  3.2404542f * X - 1.5371385f * Y - 0.4985314f * Z;
        float lg = -0.9692660f * X + 1.8760108f * Y + 0.0415560f * Z;
        float lb =  0.0556434f * X - 0.2040259f * Y + 1.0572252f * Z;
        r = LinearToGamma(lr); g = LinearToGamma(lg); bl = LinearToGamma(lb);
        r = Math.Max(0, Math.Min(1, r));
        g = Math.Max(0, Math.Min(1, g));
        bl = Math.Max(0, Math.Min(1, bl));
    }

    private static float LinearToGamma(float c)
    {
        if (c <= 0.0031308f) return 12.92f * c;
        return 1.055f * (float)Math.Pow(c, 1f / 2.4f) - 0.055f;
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
