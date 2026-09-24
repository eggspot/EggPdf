using System;
using System.Globalization;

namespace EggPdf.Fluent;

/// <summary>One column of a <see cref="Container.Grid(GridTrack[], System.Action{GridDescriptor})"/>.</summary>
public readonly struct GridTrack
{
    private readonly string? _css;

    private GridTrack(string css) => _css = css;

    internal string ToCss() => _css ?? "auto";

    /// <summary>A flexible share of the remaining space (<c>Nfr</c>).</summary>
    public static GridTrack Fr(float share)
    {
        if (!(share > 0) || float.IsInfinity(share))
            throw new ArgumentOutOfRangeException(nameof(share), share, "Fr share must be a positive finite number.");
        return new GridTrack(CssText.Number(share) + "fr");
    }

    /// <summary>A fixed-size column.</summary>
    public static GridTrack Fixed(Length size) => new GridTrack(size.ToCss());

    /// <summary>Sized to its content.</summary>
    public static GridTrack Auto => new GridTrack("auto");
}
