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
}
