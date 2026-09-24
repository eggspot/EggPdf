using System;
using System.Globalization;

namespace EggPdf.Fluent;

/// <summary>
/// A CSS length with its unit. A bare number converts implicitly to pixels, so <c>Width(200)</c>
/// still works; use <see cref="Mm"/>, <see cref="Percent"/> etc. for anything else. <c>default(Length)</c> is 0px.
/// </summary>
public readonly struct Length : IEquatable<Length>
{
    private readonly string? _css;

    private Length(string css) => _css = css;

    internal string ToCss() => _css ?? "0px";

    private static Length Of(float value, string unit)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Length must be a finite number.");
        return new Length(value.ToString(CultureInfo.InvariantCulture) + unit);
    }

    /// <summary>A zero length (0 px).</summary>
    public static Length Zero => default;

    /// <summary>The CSS keyword <c>auto</c> (the browser/engine picks the size).</summary>
    public static Length Auto => new Length("auto");

    /// <summary>A length in CSS pixels.</summary>
    public static Length Px(float value) => Of(value, "px");

    /// <summary>A length in points (1/72 inch).</summary>
    public static Length Pt(float value) => Of(value, "pt");

    /// <summary>A length in millimeters.</summary>
    public static Length Mm(float value) => Of(value, "mm");

    /// <summary>A length in centimeters.</summary>
    public static Length Cm(float value) => Of(value, "cm");

    /// <summary>A length in inches.</summary>
    public static Length In(float value) => Of(value, "in");

    /// <summary>A length relative to the element's font size (1 em = one font size).</summary>
    public static Length Em(float value) => Of(value, "em");

    /// <summary>A length as a percentage of the containing box.</summary>
    public static Length Percent(float value) => Of(value, "%");

    /// <summary>A bare number converts to pixels, so <c>Width(200)</c> means 200 px.</summary>
    public static implicit operator Length(float px) => Px(px);

    public bool Equals(Length other) => ToCss() == other.ToCss();
    public override bool Equals(object? obj) => obj is Length l && Equals(l);
    public override int GetHashCode() => ToCss().GetHashCode();
    public override string ToString() => ToCss();
    public static bool operator ==(Length a, Length b) => a.Equals(b);
    public static bool operator !=(Length a, Length b) => !a.Equals(b);
}
