using System;

namespace EggPdf.Fluent;

/// <summary><c>overflow</c>: what happens to content that doesn't fit the box.</summary>
public enum OverflowMode { Visible, Hidden, Clip, Scroll, Auto }

/// <summary><c>position</c>.</summary>
public enum PositionMode { Static, Relative, Absolute, Fixed, Sticky }

/// <summary><c>float</c>.</summary>
public enum FloatSide { None, Left, Right }

/// <summary>Flex <c>justify-content</c>: distribution along the row.</summary>
public enum FlexJustify { Start, Center, End, SpaceBetween, SpaceAround, SpaceEvenly }

/// <summary>Flex <c>align-items</c>: alignment across the row's height.</summary>
public enum FlexAlign { Stretch, Start, Center, End, Baseline }

/// <summary><c>text-transform</c>.</summary>
public enum TextCase { None, Uppercase, Lowercase, Capitalize }

/// <summary>Border line style.</summary>
public enum BorderLineStyle { None, Solid, Dashed, Dotted, Double, Groove, Ridge, Inset, Outset }

/// <summary><c>white-space</c>.</summary>
public enum WhiteSpaceMode { Normal, NoWrap, Pre, PreWrap, PreLine }

/// <summary><c>display</c>.</summary>
public enum DisplayMode { None, Block, Inline, InlineBlock, Flex, Grid }

/// <summary>Text direction (<c>dir</c>).</summary>
public enum TextDirection { Ltr, Rtl }

/// <summary>Heading level; the value is the N of &lt;hN&gt;.</summary>
public enum HeadingLevel { H1 = 1, H2, H3, H4, H5, H6 }

/// <summary>The CSS text each keyword enum lowers to. Exhaustive: an unmapped member throws rather than emitting nothing.</summary>
internal static class CssKeywords
{
    public static string ToCss(this OverflowMode v) => v switch
    {
        OverflowMode.Visible => "visible", OverflowMode.Hidden => "hidden", OverflowMode.Clip => "clip",
        OverflowMode.Scroll => "scroll", OverflowMode.Auto => "auto",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static string ToCss(this PositionMode v) => v switch
    {
        PositionMode.Static => "static", PositionMode.Relative => "relative", PositionMode.Absolute => "absolute",
        PositionMode.Fixed => "fixed", PositionMode.Sticky => "sticky",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static string ToCss(this FloatSide v) => v switch
    {
        FloatSide.None => "none", FloatSide.Left => "left", FloatSide.Right => "right",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static string ToCss(this FlexJustify v) => v switch
    {
        FlexJustify.Start => "flex-start", FlexJustify.Center => "center", FlexJustify.End => "flex-end",
        FlexJustify.SpaceBetween => "space-between", FlexJustify.SpaceAround => "space-around",
        FlexJustify.SpaceEvenly => "space-evenly",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static string ToCss(this FlexAlign v) => v switch
    {
        FlexAlign.Stretch => "stretch", FlexAlign.Start => "flex-start", FlexAlign.Center => "center",
        FlexAlign.End => "flex-end", FlexAlign.Baseline => "baseline",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static string ToCss(this TextCase v) => v switch
    {
        TextCase.None => "none", TextCase.Uppercase => "uppercase", TextCase.Lowercase => "lowercase",
        TextCase.Capitalize => "capitalize",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static string ToCss(this BorderLineStyle v) => v switch
    {
        BorderLineStyle.None => "none", BorderLineStyle.Solid => "solid", BorderLineStyle.Dashed => "dashed",
        BorderLineStyle.Dotted => "dotted", BorderLineStyle.Double => "double", BorderLineStyle.Groove => "groove",
        BorderLineStyle.Ridge => "ridge", BorderLineStyle.Inset => "inset", BorderLineStyle.Outset => "outset",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static string ToCss(this WhiteSpaceMode v) => v switch
    {
        WhiteSpaceMode.Normal => "normal", WhiteSpaceMode.NoWrap => "nowrap", WhiteSpaceMode.Pre => "pre",
        WhiteSpaceMode.PreWrap => "pre-wrap", WhiteSpaceMode.PreLine => "pre-line",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static string ToCss(this DisplayMode v) => v switch
    {
        DisplayMode.None => "none", DisplayMode.Block => "block", DisplayMode.Inline => "inline",
        DisplayMode.InlineBlock => "inline-block", DisplayMode.Flex => "flex", DisplayMode.Grid => "grid",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static string ToCss(this TextDirection v) => v switch
    {
        TextDirection.Ltr => "ltr", TextDirection.Rtl => "rtl",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };
}
