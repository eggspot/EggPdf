using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class FontVariantTests
{
    [Fact]
    public void FontVariant_SmallCaps_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-variant: small-caps'>Hello World</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("font-variant").Should().Be("small-caps",
            "font-variant: small-caps should be preserved in computed style");
    }

    [Fact]
    public void FontVariantCaps_SmallCaps_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-variant-caps: small-caps'>Hello</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("font-variant-caps").Should().Be("small-caps",
            "font-variant-caps: small-caps should be stored in style");
    }

    [Fact]
    public void FontVariantCaps_AllSmallCaps_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<h1 style='font-variant-caps: all-small-caps'>HEADING</h1>", 400, 600);
        var h1 = root.FindByTag("h1");
        h1.Should().NotBeNull();
        h1!.Style.Get("font-variant-caps").Should().Be("all-small-caps");
    }

    [Fact]
    public void FontVariantNumeric_OldstyleNums_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-variant-numeric: oldstyle-nums'>123</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("font-variant-numeric").Should().Be("oldstyle-nums");
    }

    [Fact]
    public void FontVariantNumeric_TabularNums_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-variant-numeric: tabular-nums'>1234.56</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("font-variant-numeric").Should().Be("tabular-nums");
    }

    [Fact]
    public void FontVariant_Normal_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-variant: normal'>Text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("font-variant").Should().Be("normal");
    }

    [Fact]
    public void FontVariant_Inherited_FromParent()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='font-variant: small-caps'><span>child</span></div>", 400, 600);
        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Style.Get("font-variant").Should().Be("small-caps",
            "font-variant is inherited and should propagate to children");
    }

    [Fact]
    public void FontFeatureSettings_Liga0_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-feature-settings: \"liga\" 0'>Text</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("font-feature-settings").Should().Contain("liga",
            "font-feature-settings should be preserved in computed style");
    }

    [Fact]
    public void FontFeatureSettings_MultipleFeatures_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style=\"font-feature-settings: 'smcp' 1, 'kern' 0\">Text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("font-feature-settings").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FontFeatureSettings_Inherited_FromParent()
    {
        var root = LayoutTestHelper.Layout(
            "<div style=\"font-feature-settings: 'liga' 0\"><span>text</span></div>", 400, 600);
        var span = root.FindByTag("span");
        span!.Style.Get("font-feature-settings").Should().Contain("liga",
            "font-feature-settings is inherited");
    }

    [Fact]
    public void FontSynthesis_None_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-synthesis: none'>Text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("font-synthesis").Should().Be("none");
    }

    [Fact]
    public void FontSynthesis_Weight_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-synthesis: weight'>Text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("font-synthesis").Should().Be("weight");
    }

    [Fact]
    public void FontSynthesis_Inherited_FromParent()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='font-synthesis: none'><span>child</span></div>", 400, 600);
        var span = root.FindByTag("span");
        span!.Style.Get("font-synthesis").Should().Be("none",
            "font-synthesis is inherited");
    }

    [Fact]
    public void FontSizeAdjust_NumericValue_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-size-adjust: 0.58'>Text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("font-size-adjust").Should().Be("0.58");
    }

    [Fact]
    public void FontSizeAdjust_None_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-size-adjust: none'>Text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("font-size-adjust").Should().Be("none");
    }

    [Fact]
    public void FontSizeAdjust_WithFontSize_LaysOutTheParagraph()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-size: 20px; font-size-adjust: 0.5'>Hello</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Height.Should().BeGreaterThan(0);
    }
}
