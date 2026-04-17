using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CalcTests
{
    private const float DefaultFontSize = 16f;

    [Fact]
    public void Calc_SimpleAddition()
    {
        // calc(100px + 20px) = 120px
        float result = CalcResolver.Resolve("calc(100px + 20px)", 0, DefaultFontSize);
        result.Should().BeApproximately(120f, 0.1f);
    }

    [Fact]
    public void Calc_Subtraction()
    {
        // calc(100% - 20px) with containing=500 -> 500 - 20 = 480px
        float result = CalcResolver.Resolve("calc(100% - 20px)", 500, DefaultFontSize);
        result.Should().BeApproximately(480f, 0.1f);
    }

    [Fact]
    public void Calc_Multiplication()
    {
        // calc(10px * 3) = 30px
        float result = CalcResolver.Resolve("calc(10px * 3)", 0, DefaultFontSize);
        result.Should().BeApproximately(30f, 0.1f);
    }

    [Fact]
    public void Calc_Division()
    {
        // calc(100px / 4) = 25px
        float result = CalcResolver.Resolve("calc(100px / 4)", 0, DefaultFontSize);
        result.Should().BeApproximately(25f, 0.1f);
    }

    [Fact]
    public void Calc_MixedUnits()
    {
        // calc(50% + 10px) with containing=200 -> 100 + 10 = 110px
        float result = CalcResolver.Resolve("calc(50% + 10px)", 200, DefaultFontSize);
        result.Should().BeApproximately(110f, 0.1f);
    }

    [Fact]
    public void Calc_Nested()
    {
        // calc(calc(100px + 20px) * 2) = 120 * 2 = 240px
        float result = CalcResolver.Resolve("calc(calc(100px + 20px) * 2)", 0, DefaultFontSize);
        result.Should().BeApproximately(240f, 0.1f);
    }

    [Fact]
    public void Min_Function()
    {
        // min(100px, 50px) = 50px
        float result = CalcResolver.Resolve("min(100px, 50px)", 0, DefaultFontSize);
        result.Should().BeApproximately(50f, 0.1f);
    }

    [Fact]
    public void Max_Function()
    {
        // max(100px, 50px) = 100px
        float result = CalcResolver.Resolve("max(100px, 50px)", 0, DefaultFontSize);
        result.Should().BeApproximately(100f, 0.1f);
    }

    [Fact]
    public void Clamp_Function()
    {
        // clamp(10px, 50px, 100px) = 50px (value within range)
        float result = CalcResolver.Resolve("clamp(10px, 50px, 100px)", 0, DefaultFontSize);
        result.Should().BeApproximately(50f, 0.1f);
    }

    [Fact]
    public void Clamp_BelowMin()
    {
        // clamp(30px, 10px, 100px) = 30px (value below min)
        float result = CalcResolver.Resolve("clamp(30px, 10px, 100px)", 0, DefaultFontSize);
        result.Should().BeApproximately(30f, 0.1f);
    }

    [Fact]
    public void Clamp_AboveMax()
    {
        // clamp(10px, 200px, 100px) = 100px (value above max)
        float result = CalcResolver.Resolve("clamp(10px, 200px, 100px)", 0, DefaultFontSize);
        result.Should().BeApproximately(100f, 0.1f);
    }

    [Fact]
    public void Calc_EmUnit()
    {
        // calc(2em + 10px) with fontSize=16 -> 32 + 10 = 42px
        float result = CalcResolver.Resolve("calc(2em + 10px)", 0, 16f);
        result.Should().BeApproximately(42f, 0.1f);
    }

    [Fact]
    public void Calc_RemUnit()
    {
        // calc(1rem + 10px) -> 16 + 10 = 26px (rem always 16px)
        float result = CalcResolver.Resolve("calc(1rem + 10px)", 0, DefaultFontSize);
        result.Should().BeApproximately(26f, 0.1f);
    }

    [Fact]
    public void Calc_DivisionByZero()
    {
        // calc(100px / 0) should return 0 (graceful)
        float result = CalcResolver.Resolve("calc(100px / 0)", 0, DefaultFontSize);
        result.Should().Be(0f);
    }

    [Fact]
    public void Calc_InvalidExpression_ReturnsZero()
    {
        float result = CalcResolver.Resolve("calc()", 0, DefaultFontSize);
        result.Should().Be(0f);
    }

    [Fact]
    public void Calc_PrecedenceMultiplicationBeforeAddition()
    {
        // calc(10px + 5px * 2) = 10 + 10 = 20px (not 30)
        float result = CalcResolver.Resolve("calc(10px + 5px * 2)", 0, DefaultFontSize);
        result.Should().BeApproximately(20f, 0.1f);
    }

    [Fact]
    public void IsMathFunction_Detects()
    {
        CalcResolver.IsMathFunction("calc(100px + 20px)").Should().BeTrue();
        CalcResolver.IsMathFunction("min(10px, 20px)").Should().BeTrue();
        CalcResolver.IsMathFunction("max(10px, 20px)").Should().BeTrue();
        CalcResolver.IsMathFunction("clamp(10px, 50px, 100px)").Should().BeTrue();
    }

    [Fact]
    public void IsMathFunction_NonCalc()
    {
        CalcResolver.IsMathFunction("100px").Should().BeFalse();
        CalcResolver.IsMathFunction("auto").Should().BeFalse();
        CalcResolver.IsMathFunction("").Should().BeFalse();
        CalcResolver.IsMathFunction(null).Should().BeFalse();
    }

    [Fact]
    public void Min_MixedUnits()
    {
        // min(50%, 100px) with containing=150 -> min(75, 100) = 75
        float result = CalcResolver.Resolve("min(50%, 100px)", 150, DefaultFontSize);
        result.Should().BeApproximately(75f, 0.1f);
    }

    [Fact]
    public void Max_MixedUnits()
    {
        // max(50%, 100px) with containing=150 -> max(75, 100) = 100
        float result = CalcResolver.Resolve("max(50%, 100px)", 150, DefaultFontSize);
        result.Should().BeApproximately(100f, 0.1f);
    }

    // ── Trigonometric functions ──────────────────────────────────────────────

    [Fact]
    public void Sin_Zero_ReturnsZero()
    {
        float r = CalcResolver.Resolve("sin(0deg)", 100, DefaultFontSize);
        r.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void Sin_90deg_ReturnsOne()
    {
        float r = CalcResolver.Resolve("sin(90deg)", 100, DefaultFontSize);
        r.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Cos_Zero_ReturnsOne()
    {
        float r = CalcResolver.Resolve("cos(0deg)", 100, DefaultFontSize);
        r.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Cos_90deg_ReturnsZero()
    {
        float r = CalcResolver.Resolve("cos(90deg)", 100, DefaultFontSize);
        r.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void Tan_45deg_ReturnsOne()
    {
        float r = CalcResolver.Resolve("tan(45deg)", 100, DefaultFontSize);
        r.Should().BeApproximately(1f, 0.01f);
    }

    // ── Power / root functions ────────────────────────────────────────────────

    [Fact]
    public void Sqrt_Four_ReturnsTwo()
    {
        float r = CalcResolver.Resolve("sqrt(4)", 100, DefaultFontSize);
        r.Should().BeApproximately(2f, 0.001f);
    }

    [Fact]
    public void Pow_TwoThree_ReturnsEight()
    {
        float r = CalcResolver.Resolve("pow(2, 3)", 100, DefaultFontSize);
        r.Should().BeApproximately(8f, 0.001f);
    }

    [Fact]
    public void Hypot_ThreeFour_ReturnsFive()
    {
        float r = CalcResolver.Resolve("hypot(3px, 4px)", 100, DefaultFontSize);
        r.Should().BeApproximately(5f, 0.01f);
    }

    // ── Abs / sign / round ───────────────────────────────────────────────────

    [Fact]
    public void Abs_Negative_ReturnsPositive()
    {
        float r = CalcResolver.Resolve("abs(-10px)", 100, DefaultFontSize);
        r.Should().BeApproximately(10f, 0.001f);
    }

    [Fact]
    public void Sign_Negative_ReturnsMinusOne()
    {
        float r = CalcResolver.Resolve("sign(-5px)", 100, DefaultFontSize);
        r.Should().BeApproximately(-1f, 0.001f);
    }

    [Fact]
    public void Round_Nearest_Rounds()
    {
        // round(nearest, 12.6px, 5px) → rounds 12.6 to nearest multiple of 5 → 15
        float r = CalcResolver.Resolve("round(nearest, 12.6px, 5px)", 100, DefaultFontSize);
        r.Should().BeApproximately(15f, 0.001f);
    }

    // ── Log / exp ────────────────────────────────────────────────────────────

    [Fact]
    public void Log_E_ReturnsOne()
    {
        // log(e) = 1 (natural log)
        float e = (float)System.Math.E;
        float r = CalcResolver.Resolve($"log({e.ToString(System.Globalization.CultureInfo.InvariantCulture)})", 100, DefaultFontSize);
        r.Should().BeApproximately(1f, 0.01f);
    }

    [Fact]
    public void Exp_One_ReturnsE()
    {
        float r = CalcResolver.Resolve("exp(1)", 100, DefaultFontSize);
        r.Should().BeApproximately((float)System.Math.E, 0.001f);
    }

    // ── Mod / rem ─────────────────────────────────────────────────────────────

    [Fact]
    public void Mod_TenThree_ReturnsOne()
    {
        float r = CalcResolver.Resolve("mod(10px, 3px)", 100, DefaultFontSize);
        r.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Rem_TenThree_ReturnsOne()
    {
        float r = CalcResolver.Resolve("rem(10px, 3px)", 100, DefaultFontSize);
        r.Should().BeApproximately(1f, 0.001f);
    }

    // ── Calc with math functions ─────────────────────────────────────────────

    [Fact]
    public void Calc_With_Sin()
    {
        // calc(100px * sin(90deg)) = 100
        float r = CalcResolver.Resolve("calc(100px * sin(90deg))", 0, DefaultFontSize);
        r.Should().BeApproximately(100f, 0.5f);
    }
}
