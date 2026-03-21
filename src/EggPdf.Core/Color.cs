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

    public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;
    public override bool Equals(object? obj) => obj is Color other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    public static bool operator ==(Color left, Color right) => left.Equals(right);
    public static bool operator !=(Color left, Color right) => !left.Equals(right);
    public override string ToString() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}
