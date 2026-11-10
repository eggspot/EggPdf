using EggPdf.Css;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class LogicalPropertyResolverTests
{
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
        var style = new ComputedStyle();
        style.Set("border-start-start-radius", "8px");
        LogicalPropertyResolver.Resolve(style, isRTL: true);
        style.Get("border-top-right-radius").Should().Be("8px",
            "in RTL the inline-start edge is the physical right, so the block-start inline-start corner is top-right");
    }

    [Fact]
    public void BorderEndEndRadius_RTL_MapsToBottomLeft()
    {
        var style = new ComputedStyle();
        style.Set("border-end-end-radius", "5px");
        LogicalPropertyResolver.Resolve(style, isRTL: true);
        style.Get("border-bottom-left-radius").Should().Be("5px");
    }

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
