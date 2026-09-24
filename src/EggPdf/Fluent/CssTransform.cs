using System;
using System.Globalization;

namespace EggPdf.Fluent;

/// <summary>A 2D transform, composable with <see cref="Then"/>: <c>CssTransform.Rotate(10).Then(CssTransform.Scale(1.2f))</c>.</summary>
public readonly struct CssTransform
{
    private readonly string? _css;

    private CssTransform(string css) => _css = css;

    internal string ToCss() => _css ?? "none";

    private static string N(float v) => CssText.Number(v);

    /// <summary>Rotates clockwise by <paramref name="degrees"/> around the box's center.</summary>
    public static CssTransform Rotate(float degrees) => new CssTransform("rotate(" + N(degrees) + "deg)");

    /// <summary>Scales uniformly by <paramref name="factor"/> (1 = unchanged, 2 = double size).</summary>
    public static CssTransform Scale(float factor) => new CssTransform("scale(" + N(factor) + ")");

    /// <summary>Scales by separate horizontal and vertical factors.</summary>
    public static CssTransform Scale(float x, float y) => new CssTransform("scale(" + N(x) + "," + N(y) + ")");

    /// <summary>Moves the box by a horizontal and a vertical offset.</summary>
    public static CssTransform Translate(Length x, Length y) => new CssTransform("translate(" + x.ToCss() + "," + y.ToCss() + ")");

    /// <summary>Skews (slants) horizontally by <paramref name="degrees"/>.</summary>
    public static CssTransform SkewX(float degrees) => new CssTransform("skewX(" + N(degrees) + "deg)");

    /// <summary>Skews (slants) vertically by <paramref name="degrees"/>.</summary>
    public static CssTransform SkewY(float degrees) => new CssTransform("skewY(" + N(degrees) + "deg)");

    /// <summary>Applies <paramref name="next"/> after this transform.</summary>
    public CssTransform Then(CssTransform next)
    {
        if (_css == null) return next;
        if (next._css == null) return this;
        return new CssTransform(_css + " " + next._css);
    }
}
