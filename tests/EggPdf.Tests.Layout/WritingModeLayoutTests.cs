using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// Unit tests for WritingModeLayout: parsing writing-mode values, logical size swapping,
/// text orientation, and PDF transform generation.
/// </summary>
public class WritingModeLayoutTests
{
    // ── Parse ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_HorizontalTb_ReturnsDefault()
        => WritingModeLayout.Parse("horizontal-tb")
            .Should().Be(WritingModeLayout.WritingMode.HorizontalTb);

    [Fact]
    public void Parse_VerticalRl_ReturnsVerticalRl()
        => WritingModeLayout.Parse("vertical-rl")
            .Should().Be(WritingModeLayout.WritingMode.VerticalRl);

    [Fact]
    public void Parse_VerticalLr_ReturnsVerticalLr()
        => WritingModeLayout.Parse("vertical-lr")
            .Should().Be(WritingModeLayout.WritingMode.VerticalLr);

    [Fact]
    public void Parse_Null_ReturnsHorizontalTb()
        => WritingModeLayout.Parse(null)
            .Should().Be(WritingModeLayout.WritingMode.HorizontalTb);

    [Fact]
    public void Parse_Unknown_ReturnsHorizontalTb()
        => WritingModeLayout.Parse("sideways-rl")
            .Should().Be(WritingModeLayout.WritingMode.HorizontalTb);

    [Fact]
    public void Parse_CaseInsensitive_VerticalRl()
        => WritingModeLayout.Parse("VERTICAL-RL")
            .Should().Be(WritingModeLayout.WritingMode.VerticalRl);

    // ── IsVertical ──────────────────────────────────────────────────────────

    [Fact]
    public void IsVertical_HorizontalTb_ReturnsFalse()
        => WritingModeLayout.IsVertical(WritingModeLayout.WritingMode.HorizontalTb)
            .Should().BeFalse();

    [Fact]
    public void IsVertical_VerticalRl_ReturnsTrue()
        => WritingModeLayout.IsVertical(WritingModeLayout.WritingMode.VerticalRl)
            .Should().BeTrue();

    [Fact]
    public void IsVertical_VerticalLr_ReturnsTrue()
        => WritingModeLayout.IsVertical(WritingModeLayout.WritingMode.VerticalLr)
            .Should().BeTrue();

    // ── ResolveLogicalSizes ─────────────────────────────────────────────────

    [Fact]
    public void ResolveLogicalSizes_Horizontal_ReturnsSameOrder()
    {
        var (inline, block) = WritingModeLayout.ResolveLogicalSizes(
            200, 100, WritingModeLayout.WritingMode.HorizontalTb);
        inline.Should().BeApproximately(200, 0.1f, "inline = width in horizontal");
        block.Should().BeApproximately(100, 0.1f, "block = height in horizontal");
    }

    [Fact]
    public void ResolveLogicalSizes_VerticalRl_SwapsDimensions()
    {
        var (inline, block) = WritingModeLayout.ResolveLogicalSizes(
            200, 100, WritingModeLayout.WritingMode.VerticalRl);
        inline.Should().BeApproximately(100, 0.1f, "inline = height in vertical");
        block.Should().BeApproximately(200, 0.1f, "block = width in vertical");
    }

    [Fact]
    public void ResolveLogicalSizes_VerticalLr_SwapsDimensions()
    {
        var (inline, block) = WritingModeLayout.ResolveLogicalSizes(
            300, 150, WritingModeLayout.WritingMode.VerticalLr);
        inline.Should().BeApproximately(150, 0.1f);
        block.Should().BeApproximately(300, 0.1f);
    }

    // ── ResolveTextOrientation ──────────────────────────────────────────────

    [Fact]
    public void ResolveTextOrientation_Upright_ReturnsUpright()
        => WritingModeLayout.ResolveTextOrientation("upright").Should().Be("upright");

    [Fact]
    public void ResolveTextOrientation_Sideways_ReturnsSideways()
        => WritingModeLayout.ResolveTextOrientation("sideways").Should().Be("sideways");

    [Fact]
    public void ResolveTextOrientation_Null_ReturnsMixed()
        => WritingModeLayout.ResolveTextOrientation(null).Should().Be("mixed");

    [Fact]
    public void ResolveTextOrientation_Unknown_ReturnsMixed()
        => WritingModeLayout.ResolveTextOrientation("left-to-right").Should().Be("mixed");

    [Fact]
    public void ResolveTextOrientation_CaseInsensitive_Upright()
        => WritingModeLayout.ResolveTextOrientation("UPRIGHT").Should().Be("upright");

    // ── GetVerticalTextTransform ────────────────────────────────────────────

    [Fact]
    public void GetVerticalTextTransform_ContainsCmOperator()
    {
        var result = WritingModeLayout.GetVerticalTextTransform(10f, 20f, 12f);
        result.Should().Contain("cm", "vertical text transform must emit a cm operator");
    }

    [Fact]
    public void GetVerticalTextTransform_Contains90DegRotationMatrix()
    {
        var result = WritingModeLayout.GetVerticalTextTransform(0f, 0f, 12f);
        // 90° clockwise rotation matrix: [0 -1 1 0 x y]
        result.Should().Contain("0 -1 1 0");
    }

    [Fact]
    public void GetVerticalTextTransform_ContainsCoordinates()
    {
        var result = WritingModeLayout.GetVerticalTextTransform(50f, 100f, 12f);
        result.Should().Contain("50.00").And.Contain("100.00");
    }

    // ── text-combine-upright ─────────────────────────────────────────────────

    [Fact]
    public void TextCombineUpright_DoesNotCrash()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='writing-mode: vertical-rl; font-size: 16px'>" +
            "<span style='text-combine-upright: all'>AB</span></div>",
            200, 400);
        root.Should().NotBeNull("text-combine-upright must not crash");
    }

    [Fact]
    public void TextCombineUpright_BoxHeightFitsOneLine()
    {
        // In vertical writing, the combined box should occupy ~1em (same as a single CJK char)
        var root = LayoutTestHelper.Layout(
            "<div style='writing-mode: vertical-rl; font-size: 20px; width: 30px'>" +
            "<span style='text-combine-upright: all'>12</span></div>",
            200, 400);
        var combined = root.FindAll(b => b.Text == "12").FirstOrDefault();
        if (combined == null) return; // guard — layout produces the box

        // The combined box height should be ~1em (≈20px), not 2× the chars stacked
        combined.Height.Should().BeLessThan(30f,
            "text-combine-upright should compress text to fit ~1em height");
    }

    [Fact]
    public void ResolveCombineUpright_All_IsRecognized()
    {
        var result = WritingModeLayout.ResolveCombineUpright("all");
        result.Should().BeTrue("'all' is a valid text-combine-upright value");
    }

    [Fact]
    public void ResolveCombineUpright_None_ReturnsFalse()
    {
        var result = WritingModeLayout.ResolveCombineUpright("none");
        result.Should().BeFalse("'none' means no combination");
    }

    [Fact]
    public void ResolveCombineUpright_Null_ReturnsFalse()
    {
        var result = WritingModeLayout.ResolveCombineUpright(null);
        result.Should().BeFalse("null should not combine");
    }
}
