using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class CalcResolverTests
{
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

    [Fact]
    public void Calc_Multiply_ReturnsProduct()
        => CalcResolver.Resolve("calc(10px * 3)", 600, 16).Should().BeApproximately(30, 0.1f);

    [Fact]
    public void Calc_Divide_ReturnsQuotient()
        => CalcResolver.Resolve("calc(90px / 3)", 600, 16).Should().BeApproximately(30, 0.1f);

    [Fact]
    public void Calc_OperatorPrecedence_MultBeforeAdd()
        => CalcResolver.Resolve("calc(10px + 2 * 5px)", 600, 16).Should().BeApproximately(20, 0.1f,
            "multiplication binds first: 10 + (2 * 5) = 20");

    [Fact]
    public void Calc_EmUnit_UsesProvidedFontSize()
        => CalcResolver.Resolve("calc(2em)", 600, 20).Should().BeApproximately(40, 0.1f,
            "2em at a 20px font size is 40px");

    [Fact]
    public void Calc_RemUnit_UsesDefaultFontSize16()
        => CalcResolver.Resolve("calc(2rem)", 600, 20).Should().BeApproximately(32, 0.1f,
            "rem uses the 16px root font size, not the element's 20px");

    [Fact]
    public void Calc_PtUnit_ConvertsToPixels()
        => CalcResolver.Resolve("calc(12pt)", 600, 16).Should().BeApproximately(16, 0.1f,
            "12pt is 12 * 96/72 = 16px");

    [Fact]
    public void Calc_CmUnit_ConvertsToPixels()
        => CalcResolver.Resolve("calc(1cm)", 600, 16).Should().BeApproximately(37.8f, 0.5f,
            "1cm is 96/2.54 = ~37.8px");

    [Fact]
    public void Calc_MmUnit_ConvertsToPixels()
        => CalcResolver.Resolve("calc(10mm)", 600, 16).Should().BeApproximately(37.8f, 0.5f,
            "10mm is 1cm, ~37.8px");

    [Fact]
    public void Calc_InUnit_ConvertsToPixels()
        => CalcResolver.Resolve("calc(1in)", 600, 16).Should().BeApproximately(96, 0.1f);

    [Fact]
    public void Min_TwoValues_ReturnsSmaller()
        => CalcResolver.Resolve("min(200px, 100px)", 600, 16).Should().BeApproximately(100, 0.1f);

    [Fact]
    public void Min_PercentAndPx_ReturnsSmaller()
        => CalcResolver.Resolve("min(50%, 400px)", 600, 16).Should().BeApproximately(300, 0.1f,
            "50% of 600 is 300, smaller than 400");

    [Fact]
    public void Min_ThreeValues_ReturnsSmallest()
        => CalcResolver.Resolve("min(300px, 100px, 200px)", 600, 16).Should().BeApproximately(100, 0.1f);

    [Fact]
    public void Max_TwoValues_ReturnsLarger()
        => CalcResolver.Resolve("max(200px, 100px)", 600, 16).Should().BeApproximately(200, 0.1f);

    [Fact]
    public void Max_PercentAndPx_ReturnsLarger()
        => CalcResolver.Resolve("max(10%, 400px)", 600, 16).Should().BeApproximately(400, 0.1f,
            "10% of 600 is 60, smaller than 400");

    [Fact]
    public void Clamp_ValueWithinRange_ReturnsValue()
        => CalcResolver.Resolve("clamp(100px, 300px, 500px)", 600, 16).Should().BeApproximately(300, 0.1f);

    [Fact]
    public void Clamp_ValueBelowMin_ReturnsMin()
        => CalcResolver.Resolve("clamp(200px, 50px, 500px)", 600, 16).Should().BeApproximately(200, 0.1f);

    [Fact]
    public void Clamp_ValueAboveMax_ReturnsMax()
        => CalcResolver.Resolve("clamp(100px, 600px, 400px)", 600, 16).Should().BeApproximately(400, 0.1f);

    [Fact]
    public void Clamp_WithPercent_UsesContainingSize()
        => CalcResolver.Resolve("clamp(50px, 50%, 300px)", 600, 16).Should().BeApproximately(300, 0.1f,
            "50% of 600 is 300, inside [50, 300]");

    [Fact]
    public void Layout_CalcWidth_PercentMinusPx()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 400px'>" +
            "  <div style='width: calc(100% - 40px); height: 50px'></div>" +
            "</div>", 600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCount(2);
        divs[1].Width.Should().BeApproximately(360, 2f, "100% of the 400px parent minus 40px");
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
        var root = LayoutTestHelper.Layout(
            "<div style='width: min(200px, 400px); height: 50px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(200, 2f);
    }

    [Fact]
    public void Layout_MaxWidth_UsesMaxFunction()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: max(300px, 100px); height: 50px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(300, 2f);
    }

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
