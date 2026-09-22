using EggPdf.Css;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

/// <summary>
/// Tests for CSS Logical Properties Level 1 sub-properties beyond margin/padding/size/
/// text-align (already covered by EggPdf.Tests.Layout.LogicalPropertyTests): border color/
/// style, logical border-radius corners, and float:inline-start/inline-end.
/// </summary>
public class LogicalPropertyResolverTests
{
    // ── border-inline/block-*-color / -style ────────────────────────────────────

    [Fact]
    public void BorderInlineStartColor_LTR_MapsToBorderLeftColor()
    {
        var style = new ComputedStyle();
        style.Set("border-inline-start-color", "red");
        LogicalPropertyResolver.Resolve(style, isRTL: false);
        style.Get("border-left-color").Should().Be("red");
    }

    [Fact]
    public void BorderInlineStartColor_RTL_MapsToBorderRightColor()
    {
        var style = new ComputedStyle();
        style.Set("border-inline-start-color", "red");
        LogicalPropertyResolver.Resolve(style, isRTL: true);
        style.Get("border-right-color").Should().Be("red");
    }

    [Fact]
    public void BorderInlineEndStyle_RTL_MapsToBorderLeftStyle()
    {
        var style = new ComputedStyle();
        style.Set("border-inline-end-style", "dashed");
        LogicalPropertyResolver.Resolve(style, isRTL: true);
        style.Get("border-left-style").Should().Be("dashed");
    }

    [Fact]
    public void BorderBlockStartColor_MapsToBorderTopColor()
    {
        var style = new ComputedStyle();
        style.Set("border-block-start-color", "blue");
        LogicalPropertyResolver.Resolve(style, isRTL: false);
        style.Get("border-top-color").Should().Be("blue");
    }

    // ── logical border-radius corners ───────────────────────────────────────────

    [Fact]
    public void BorderStartStartRadius_LTR_MapsToTopLeft()
    {
        var style = new ComputedStyle();
        style.Set("border-start-start-radius", "8px");
        LogicalPropertyResolver.Resolve(style, isRTL: false);
        style.Get("border-top-left-radius").Should().Be("8px");
    }

    [Fact]
    public void BorderStartStartRadius_RTL_MapsToTopRight()
    {
        // In RTL, the inline-start (logical) edge is the physical right edge, so the
        // start-start corner (block-start + inline-start) becomes top-right, not top-left.
        var style = new ComputedStyle();
        style.Set("border-start-start-radius", "8px");
        LogicalPropertyResolver.Resolve(style, isRTL: true);
        style.Get("border-top-right-radius").Should().Be("8px");
    }

    [Fact]
    public void BorderEndEndRadius_RTL_MapsToBottomLeft()
    {
        var style = new ComputedStyle();
        style.Set("border-end-end-radius", "5px");
        LogicalPropertyResolver.Resolve(style, isRTL: true);
        style.Get("border-bottom-left-radius").Should().Be("5px");
    }

    // ── float: inline-start / inline-end ────────────────────────────────────────

    [Fact]
    public void FloatInlineStart_LTR_ResolvesToLeft()
    {
        var style = new ComputedStyle();
        style.Set("float", "inline-start");
        LogicalPropertyResolver.Resolve(style, isRTL: false);
        style.Get("float").Should().Be("left");
    }

    [Fact]
    public void FloatInlineStart_RTL_ResolvesToRight()
    {
        var style = new ComputedStyle();
        style.Set("float", "inline-start");
        LogicalPropertyResolver.Resolve(style, isRTL: true);
        style.Get("float").Should().Be("right");
    }

    [Fact]
    public void FloatInlineEnd_RTL_ResolvesToLeft()
    {
        var style = new ComputedStyle();
        style.Set("float", "inline-end");
        LogicalPropertyResolver.Resolve(style, isRTL: true);
        style.Get("float").Should().Be("left");
    }

    [Fact]
    public void FloatPhysicalValue_Unaffected()
    {
        var style = new ComputedStyle();
        style.Set("float", "left");
        LogicalPropertyResolver.Resolve(style, isRTL: true);
        style.Get("float").Should().Be("left", "a physical float value must never be reinterpreted");
    }
}
