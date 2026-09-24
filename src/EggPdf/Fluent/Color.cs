using System;

namespace EggPdf.Fluent;

/// <summary>
/// A validated color. There is deliberately no implicit conversion from string: a typo like
/// "rde" must fail at the call site (<see cref="FromHex"/> throws) rather than be silently ignored
/// by the CSS engine. <c>default(Color)</c> is fully transparent.
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    private readonly string? _css;

    private Color(string css) => _css = css;

    internal string ToCss() => _css ?? "transparent";

    /// <summary>An opaque color from red, green and blue components (0-255).</summary>
    public static Color FromRgb(byte r, byte g, byte b) => new Color("#" + Hex(r) + Hex(g) + Hex(b));

    /// <summary>RGB plus alpha (0 = transparent, 255 = opaque).</summary>
    public static Color FromRgba(byte r, byte g, byte b, byte a) => new Color("#" + Hex(r) + Hex(g) + Hex(b) + Hex(a));

    /// <summary>Parses "#rgb", "#rgba", "#rrggbb" or "#rrggbbaa"; anything else throws.</summary>
    public static Color FromHex(string hex)
    {
        if (hex == null) throw new ArgumentNullException(nameof(hex));
        bool lengthOk = hex.Length == 4 || hex.Length == 5 || hex.Length == 7 || hex.Length == 9;
        if (!lengthOk || hex[0] != '#')
            throw new ArgumentException("Expected #rgb, #rgba, #rrggbb or #rrggbbaa, got \"" + hex + "\".", nameof(hex));
        for (int i = 1; i < hex.Length; i++)
            if (!Uri.IsHexDigit(hex[i]))
                throw new ArgumentException("\"" + hex + "\" contains a non-hex digit.", nameof(hex));
        return new Color(hex.ToLowerInvariant());
    }

    private static string Hex(byte v) => v.ToString("x2");

    public bool Equals(Color other) => ToCss() == other.ToCss();
    public override bool Equals(object? obj) => obj is Color c && Equals(c);
    public override int GetHashCode() => ToCss().GetHashCode();
    public override string ToString() => ToCss();
    public static bool operator ==(Color a, Color b) => a.Equals(b);
    public static bool operator !=(Color a, Color b) => !a.Equals(b);
}

/// <summary>Common named colors, as <see cref="Color"/> values.</summary>
public static class Colors
{
    public static readonly Color Transparent = default;
    public static readonly Color Black = Color.FromHex("#000000");
    public static readonly Color White = Color.FromHex("#ffffff");
    public static readonly Color Red = Color.FromHex("#ff0000");
    public static readonly Color Green = Color.FromHex("#008000");
    public static readonly Color Blue = Color.FromHex("#0000ff");
    public static readonly Color Yellow = Color.FromHex("#ffff00");
    public static readonly Color Orange = Color.FromHex("#ffa500");
    public static readonly Color Purple = Color.FromHex("#800080");
    public static readonly Color Pink = Color.FromHex("#ffc0cb");
    public static readonly Color Brown = Color.FromHex("#a52a2a");
    public static readonly Color Navy = Color.FromHex("#000080");
    public static readonly Color Teal = Color.FromHex("#008080");
    public static readonly Color Gray = Color.FromHex("#808080");
    public static readonly Color LightGray = Color.FromHex("#d3d3d3");
    public static readonly Color DarkGray = Color.FromHex("#a9a9a9");
}
