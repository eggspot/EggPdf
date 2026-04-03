using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// Unit tests for CalcResolver: calc(), min(), max(), clamp(), and unit conversions.
/// </summary>
public class CalcResolverTests
{
    // ── IsMathFunction ──────────────────────────────────────────────────────

    [Fact]
    public void IsMathFunction_Calc_ReturnsTrue()
        => CalcResolver.IsMathFunction("calc(100% - 20px)").Should().BeTrue();

    [Fact]
    public void IsMathFunction_Min_ReturnsTrue()
        => CalcResolver.IsMathFunction("min(100px, 50%)").Should().BeTrue();

    [Fact]
    public void IsMathFunction_Max_ReturnsTrue()
        => CalcResolver.IsMathFunction("max(100px, 50%)").Should().BeTrue();

    [Fact]
    public void IsMathFunction_Clamp_ReturnsTrue()
        => CalcResolver.IsMathFunction("clamp(10px, 50%, 200px)").Should().BeTrue();

    [Fact]
    public void IsMathFunction_PlainPx_ReturnsFalse()
        => CalcResolver.IsMathFunction("200px").Should().BeFalse();

    [Fact]
    public void IsMathFunction_Percent_ReturnsFalse()
        => CalcResolver.IsMathFunction("50%").Should().BeFalse();

    [Fact]
    public void IsMathFunction_Null_ReturnsFalse()
        => CalcResolver.IsMathFunction(null!).Should().BeFalse();

    [Fact]
    public void IsMathFunction_Empty_ReturnsFalse()
        => CalcResolver.IsMathFunction("").Should().BeFalse();

    // ── calc() addition / subtraction ───────────────────────────────────────

    [Fact]
    public void Calc_AddTwoPx_ReturnsSum()
        => CalcResolver.Resolve("calc(100px + 50px)", 600, 16).Should().BeApproximately(150, 0.1f);

    [Fact]
    public void Calc_SubtractPx_ReturnsDifference()
        => CalcResolver.Resolve("calc(200px - 80px)", 600, 16).Should().BeApproximately(120, 0.1f);

    [Fact]
    public void Calc_PercentMinusPx_UsesContainingSize()
        => CalcResolver.Resolve("calc(100% - 20px)", 600, 16).Should().BeApproximately(580, 0.1f);

    [Fact]
    public void Calc_50Percent_HalfOfContaining()
        => CalcResolver.Resolve("calc(50%)", 400, 16).Should().BeApproximately(200, 0.1f);

    // ── calc() multiplication / division ───────────────────────────────────

    [Fact]
    public void Calc_Multiply_ReturnsProduct()
        => CalcResolver.Resolve("calc(10px * 3)", 600, 16).Should().BeApproximately(30, 0.1f);

    [Fact]
    public void Calc_Divide_ReturnsQuotient()
        => CalcResolver.Resolve("calc(90px / 3)", 600, 16).Should().BeApproximately(30, 0.1f);

    [Fact]
    public void Calc_OperatorPrecedence_MultBeforeAdd()
        // calc(10px + 2 * 5px) = 10 + 10 = 20
        => CalcResolver.Resolve("calc(10px + 2 * 5px)", 600, 16).Should().BeApproximately(20, 0.1f);

    // ── calc() unit conversions ─────────────────────────────────────────────

    [Fact]
    public void Calc_EmUnit_UsesProvidedFontSize()
        // 2em with fontSize=20 = 40px
        => CalcResolver.Resolve("calc(2em)", 600, 20).Should().BeApproximately(40, 0.1f);

    [Fact]
    public void Calc_RemUnit_UsesDefaultFontSize16()
        // 2rem = 32px (rem always uses 16px root)
        => CalcResolver.Resolve("calc(2rem)", 600, 20).Should().BeApproximately(32, 0.1f);

    [Fact]
    public void Calc_PtUnit_ConvertsToPixels()
        // 12pt = 12 * 96/72 = 16px
        => CalcResolver.Resolve("calc(12pt)", 600, 16).Should().BeApproximately(16, 0.1f);

    [Fact]
    public void Calc_CmUnit_ConvertsToPixels()
        // 1cm ≈ 37.8px
        => CalcResolver.Resolve("calc(1cm)", 600, 16).Should().BeApproximately(37.8f, 0.5f);

    [Fact]
    public void Calc_MmUnit_ConvertsToPixels()
        // 10mm = 1cm ≈ 37.8px
        => CalcResolver.Resolve("calc(10mm)", 600, 16).Should().BeApproximately(37.8f, 0.5f);

    [Fact]
    public void Calc_InUnit_ConvertsToPixels()
        // 1in = 96px
        => CalcResolver.Resolve("calc(1in)", 600, 16).Should().BeApproximately(96, 0.1f);

    // ── min() ───────────────────────────────────────────────────────────────

    [Fact]
    public void Min_TwoValues_ReturnsSmaller()
        => CalcResolver.Resolve("min(200px, 100px)", 600, 16).Should().BeApproximately(100, 0.1f);

    [Fact]
    public void Min_PercentAndPx_ReturnsSmaller()
        // min(50%, 400px) with containing=600 → min(300, 400) = 300
        => CalcResolver.Resolve("min(50%, 400px)", 600, 16).Should().BeApproximately(300, 0.1f);

    [Fact]
    public void Min_ThreeValues_ReturnsSmallest()
        => CalcResolver.Resolve("min(300px, 100px, 200px)", 600, 16).Should().BeApproximately(100, 0.1f);

    // ── max() ───────────────────────────────────────────────────────────────

    [Fact]
    public void Max_TwoValues_ReturnsLarger()
        => CalcResolver.Resolve("max(200px, 100px)", 600, 16).Should().BeApproximately(200, 0.1f);

    [Fact]
    public void Max_PercentAndPx_ReturnsLarger()
        // max(10%, 400px) with containing=600 → max(60, 400) = 400
        => CalcResolver.Resolve("max(10%, 400px)", 600, 16).Should().BeApproximately(400, 0.1f);

    // ── clamp() ─────────────────────────────────────────────────────────────

    [Fact]
    public void Clamp_ValueWithinRange_ReturnsValue()
        // clamp(100px, 300px, 500px) → 300
        => CalcResolver.Resolve("clamp(100px, 300px, 500px)", 600, 16).Should().BeApproximately(300, 0.1f);

    [Fact]
    public void Clamp_ValueBelowMin_ReturnsMin()
        // clamp(200px, 50px, 500px) → 200
        => CalcResolver.Resolve("clamp(200px, 50px, 500px)", 600, 16).Should().BeApproximately(200, 0.1f);

    [Fact]
    public void Clamp_ValueAboveMax_ReturnsMax()
        // clamp(100px, 600px, 400px) → 400
        => CalcResolver.Resolve("clamp(100px, 600px, 400px)", 600, 16).Should().BeApproximately(400, 0.1f);

    [Fact]
    public void Clamp_WithPercent_UsesContainingSize()
        // clamp(50px, 50%, 300px) with containing=600 → clamp(50, 300, 300) = 300
        => CalcResolver.Resolve("clamp(50px, 50%, 300px)", 600, 16).Should().BeApproximately(300, 0.1f);

    // ── integration: calc() in layout ──────────────────────────────────────

    [Fact]
    public void Layout_CalcWidth_PercentMinusPx()
    {
        // Use a fixed-width parent so calc(100% - 40px) resolves against 400px → 360px
        var root = LayoutTestHelper.Layout(
            "<div style='width: 400px'>" +
            "  <div style='width: calc(100% - 40px); height: 50px'></div>" +
            "</div>", 600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(2);
        divs[1].Width.Should().BeApproximately(360, 2f);
    }

    [Fact]
    public void Layout_CalcHeight_AddsPxValues()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px; height: calc(50px + 30px)'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(80, 2f);
    }

    [Fact]
    public void Layout_MinWidth_UsesMinFunction()
    {
        // min(200px, 400px) = 200px
        var root = LayoutTestHelper.Layout(
            "<div style='width: min(200px, 400px); height: 50px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(200, 2f);
    }

    [Fact]
    public void Layout_MaxWidth_UsesMaxFunction()
    {
        // max(300px, 100px) = 300px
        var root = LayoutTestHelper.Layout(
            "<div style='width: max(300px, 100px); height: 50px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(300, 2f);
    }

    // ── edge cases ──────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_PlainPx_ReturnsPxValue()
        => CalcResolver.Resolve("120px", 600, 16).Should().BeApproximately(120, 0.1f);

    [Fact]
    public void Resolve_Null_ReturnsZero()
        => CalcResolver.Resolve(null!, 600, 16).Should().Be(0);

    [Fact]
    public void Resolve_Empty_ReturnsZero()
        => CalcResolver.Resolve("", 600, 16).Should().Be(0);

    [Fact]
    public void Calc_DivisionByZero_ReturnsZero()
        => CalcResolver.Resolve("calc(100px / 0)", 600, 16).Should().Be(0);
}
