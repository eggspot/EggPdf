using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class AspectRatioLayoutTests
{
    [Fact]
    public void Parse_16by9_ReturnsCorrectRatio()
        => AspectRatioLayout.ParseAspectRatio("16/9").Should().BeApproximately(16f / 9f, 0.001f);

    [Fact]
    public void Parse_4by3_ReturnsCorrectRatio()
        => AspectRatioLayout.ParseAspectRatio("4/3").Should().BeApproximately(4f / 3f, 0.001f);

    [Fact]
    public void Parse_1by1_ReturnsOne()
        => AspectRatioLayout.ParseAspectRatio("1/1").Should().BeApproximately(1f, 0.001f);

    [Fact]
    public void Parse_SingleNumber_ReturnsRatio()
        => AspectRatioLayout.ParseAspectRatio("1").Should().BeApproximately(1f, 0.001f,
            "a single number n means n/1, so \"1\" is a square");

    [Fact]
    public void Parse_SingleDecimal_ReturnsRatio()
        => AspectRatioLayout.ParseAspectRatio("1.5").Should().BeApproximately(1.5f, 0.001f);

    [Fact]
    public void Parse_Auto_ReturnsNull()
        => AspectRatioLayout.ParseAspectRatio("auto").Should().BeNull();

    [Fact]
    public void Parse_Null_ReturnsNull()
        => AspectRatioLayout.ParseAspectRatio(null).Should().BeNull();

    [Fact]
    public void Parse_Empty_ReturnsNull()
        => AspectRatioLayout.ParseAspectRatio("").Should().BeNull();

    [Fact]
    public void Parse_AutoWith16by9_ReturnsRatio()
        => AspectRatioLayout.ParseAspectRatio("auto 16/9").Should().BeApproximately(16f / 9f, 0.001f,
            "the auto fallback keyword is dropped and the ratio still parses");

    [Fact]
    public void Parse_ZeroDenominator_ReturnsNull()
        => AspectRatioLayout.ParseAspectRatio("16/0").Should().BeNull();

    [Fact]
    public void Parse_NegativeRatio_ReturnsNull()
        => AspectRatioLayout.ParseAspectRatio("-1").Should().BeNull("a ratio must be positive");

    [Fact]
    public void Apply_WidthSpecified_ComputesHeight()
    {
        var (w, h) = AspectRatioLayout.ApplyAspectRatio(320f, null, 16f / 9f, 600f);
        w.Should().BeApproximately(320, 0.1f);
        h.Should().BeApproximately(180, 0.5f, "320 / (16/9) = 180");
    }

    [Fact]
    public void Apply_HeightSpecified_ComputesWidth()
    {
        var (w, h) = AspectRatioLayout.ApplyAspectRatio(null, 180f, 16f / 9f, 600f);
        w.Should().BeApproximately(320, 0.5f, "180 * (16/9) = 320");
        h.Should().BeApproximately(180, 0.1f);
    }

    [Fact]
    public void Apply_NeitherSpecified_UsesContainingWidth()
    {
        var (w, h) = AspectRatioLayout.ApplyAspectRatio(null, null, 16f / 9f, 400f);
        w.Should().BeApproximately(400, 0.1f, "an auto width fills the 400px containing block");
        h.Should().BeApproximately(225, 1f, "400 / (16/9) = 225");
    }

    [Fact]
    public void Apply_BothSpecified_RatioIgnored()
    {
        var (w, h) = AspectRatioLayout.ApplyAspectRatio(200f, 500f, 16f / 9f, 600f);
        w.Should().BeApproximately(200, 0.1f, "with both sizes given the ratio has nothing to compute");
        h.Should().BeApproximately(500, 0.1f, "with both sizes given the ratio has nothing to compute");
    }

    [Fact]
    public void Apply_SquareRatio_EqualDimensions()
    {
        var (w, h) = AspectRatioLayout.ApplyAspectRatio(100f, null, 1f, 600f);
        w.Should().BeApproximately(100, 0.1f);
        h.Should().BeApproximately(100, 0.1f);
    }

    [Fact]
    public void Layout_AspectRatio_ElementExistsWithCorrectWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 320px; aspect-ratio: 16/9'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(320, 1f);
        div.Style.Get("aspect-ratio").Should().Be("16/9");
    }

    [Fact]
    public void Layout_AspectRatioSquare_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px; aspect-ratio: 1'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(100, 1f);
        div.Style.Get("aspect-ratio").Should().Be("1");
    }

    [Fact]
    public void Layout_NoAspectRatio_ExplicitHeightUsed()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 200px; height: 80px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(80, 1f);
    }

    [Fact]
    public void Layout_AspectRatio_WidthKnown_HeightComputed()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 320px; aspect-ratio: 16/9'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(180f, 2f,
            "height should be computed as width / (16/9) = 180px");
    }

    [Fact]
    public void Layout_AspectRatioSquare_WidthKnown_HeightEqualsWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px; aspect-ratio: 1'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(100f, 2f,
            "square aspect-ratio: height should equal width");
    }

    [Fact]
    public void Layout_AspectRatio_ExplicitHeightWins()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 200px; height: 50px; aspect-ratio: 16/9'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(50f, 2f,
            "with both width and height specified, CSS ignores aspect-ratio");
    }
}
