using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// Unit tests for AspectRatioLayout: parsing and applying CSS aspect-ratio property.
/// </summary>
public class AspectRatioLayoutTests
{
    // ── ParseAspectRatio ────────────────────────────────────────────────────

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
        // "1" means 1/1 square
        => AspectRatioLayout.ParseAspectRatio("1").Should().BeApproximately(1f, 0.001f);

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
        // "auto 16/9" – the auto fallback is stripped, ratio still parsed
        => AspectRatioLayout.ParseAspectRatio("auto 16/9").Should().BeApproximately(16f / 9f, 0.001f);

    [Fact]
    public void Parse_ZeroDenominator_ReturnsNull()
        => AspectRatioLayout.ParseAspectRatio("16/0").Should().BeNull();

    [Fact]
    public void Parse_NegativeRatio_ReturnsNull()
        // Negative ratio is invalid; ParseFloat returns negative but ratio <= 0 guard catches it
        => AspectRatioLayout.ParseAspectRatio("-1").Should().BeNull();

    // ── ApplyAspectRatio ────────────────────────────────────────────────────

    [Fact]
    public void Apply_WidthSpecified_ComputesHeight()
    {
        // 16:9 with width 320 → height = 320 / (16/9) = 180
        var (w, h) = AspectRatioLayout.ApplyAspectRatio(320f, null, 16f / 9f, 600f);
        w.Should().BeApproximately(320, 0.1f);
        h.Should().BeApproximately(180, 0.5f);
    }

    [Fact]
    public void Apply_HeightSpecified_ComputesWidth()
    {
        // 16:9 with height 180 → width = 180 * (16/9) = 320
        var (w, h) = AspectRatioLayout.ApplyAspectRatio(null, 180f, 16f / 9f, 600f);
        w.Should().BeApproximately(320, 0.5f);
        h.Should().BeApproximately(180, 0.1f);
    }

    [Fact]
    public void Apply_NeitherSpecified_UsesContainingWidth()
    {
        // Both auto: use containing=400, height = 400 / (16/9) ≈ 225
        var (w, h) = AspectRatioLayout.ApplyAspectRatio(null, null, 16f / 9f, 400f);
        w.Should().BeApproximately(400, 0.1f);
        h.Should().BeApproximately(225, 1f);
    }

    [Fact]
    public void Apply_BothSpecified_RatioIgnored()
    {
        // Both specified explicitly → ratio is ignored, values returned as-is
        var (w, h) = AspectRatioLayout.ApplyAspectRatio(200f, 500f, 16f / 9f, 600f);
        w.Should().BeApproximately(200, 0.1f);
        h.Should().BeApproximately(500, 0.1f);
    }

    [Fact]
    public void Apply_SquareRatio_EqualDimensions()
    {
        var (w, h) = AspectRatioLayout.ApplyAspectRatio(100f, null, 1f, 600f);
        w.Should().BeApproximately(100, 0.1f);
        h.Should().BeApproximately(100, 0.1f);
    }

    // ── integration: aspect-ratio in layout ────────────────────────────────

    [Fact]
    public void Layout_AspectRatio_ElementExistsWithCorrectWidth()
    {
        // Layout engine parses aspect-ratio; height may come from content or ratio depending on implementation
        var root = LayoutTestHelper.Layout(
            "<div style='width: 320px; aspect-ratio: 16/9'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(320, 1f);
        // aspect-ratio is stored in style
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
    public void Layout_NoAspectRatio_HeightFromContent()
    {
        // Without aspect-ratio, explicit height is used
        var root = LayoutTestHelper.Layout(
            "<div style='width: 200px; height: 80px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(80, 1f);
    }

    [Fact]
    public void Layout_AspectRatio_WidthKnown_HeightComputed()
    {
        // width=320, aspect-ratio=16/9 → height must be 320*(9/16)=180
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
        // Both width and height specified — aspect-ratio is ignored per spec
        var root = LayoutTestHelper.Layout(
            "<div style='width: 200px; height: 50px; aspect-ratio: 16/9'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(50f, 2f,
            "explicit height should override aspect-ratio computation");
    }
}
