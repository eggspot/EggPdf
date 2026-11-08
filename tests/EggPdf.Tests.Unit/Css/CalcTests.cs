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
        float result = CalcResolver.Resolve("calc(100px + 20px)", 0, DefaultFontSize);
        result.Should().BeApproximately(120f, 0.1f);
    }

    [Fact]
    public void Calc_Subtraction()
    {
        float result = CalcResolver.Resolve("calc(100% - 20px)", 500, DefaultFontSize);
        result.Should().BeApproximately(480f, 0.1f);
    }

    [Fact]
    public void Calc_Multiplication()
    {
        float result = CalcResolver.Resolve("calc(10px * 3)", 0, DefaultFontSize);
        result.Should().BeApproximately(30f, 0.1f);
    }

    [Fact]
    public void Calc_Division()
    {
        float result = CalcResolver.Resolve("calc(100px / 4)", 0, DefaultFontSize);
        result.Should().BeApproximately(25f, 0.1f);
    }

    [Fact]
    public void Calc_MixedUnits()
    {
        float result = CalcResolver.Resolve("calc(50% + 10px)", 200, DefaultFontSize);
        result.Should().BeApproximately(110f, 0.1f);
    }

    [Fact]
    public void Calc_Nested()
    {
        float result = CalcResolver.Resolve("calc(calc(100px + 20px) * 2)", 0, DefaultFontSize);
        result.Should().BeApproximately(240f, 0.1f);
    }

    [Fact]
    public void Min_Function()
    {
        float result = CalcResolver.Resolve("min(100px, 50px)", 0, DefaultFontSize);
        result.Should().BeApproximately(50f, 0.1f);
    }

    [Fact]
    public void Max_Function()
    {
        float result = CalcResolver.Resolve("max(100px, 50px)", 0, DefaultFontSize);
        result.Should().BeApproximately(100f, 0.1f);
    }

    [Fact]
    public void Clamp_Function()
    {
        float result = CalcResolver.Resolve("clamp(10px, 50px, 100px)", 0, DefaultFontSize);
        result.Should().BeApproximately(50f, 0.1f);
    }

    [Fact]
    public void Clamp_BelowMin()
    {
        float result = CalcResolver.Resolve("clamp(30px, 10px, 100px)", 0, DefaultFontSize);
        result.Should().BeApproximately(30f, 0.1f);
    }

    [Fact]
    public void Clamp_AboveMax()
    {
        float result = CalcResolver.Resolve("clamp(10px, 200px, 100px)", 0, DefaultFontSize);
        result.Should().BeApproximately(100f, 0.1f);
    }

    [Fact]
    public void Calc_EmUnit()
    {
        float result = CalcResolver.Resolve("calc(2em + 10px)", 0, 16f);
        result.Should().BeApproximately(42f, 0.1f);
    }

    [Fact]
    public void Calc_RemUnit()
    {
        float result = CalcResolver.Resolve("calc(1rem + 10px)", 0, DefaultFontSize);
        result.Should().BeApproximately(26f, 0.1f);
    }

    [Fact]
    public void Calc_DivisionByZero()
    {
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
        float result = CalcResolver.Resolve("min(50%, 100px)", 150, DefaultFontSize);
        result.Should().BeApproximately(75f, 0.1f);
    }

    [Fact]
    public void Max_MixedUnits()
    {
        float result = CalcResolver.Resolve("max(50%, 100px)", 150, DefaultFontSize);
        result.Should().BeApproximately(100f, 0.1f);
    }


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
        float r = CalcResolver.Resolve("round(nearest, 12.6px, 5px)", 100, DefaultFontSize);
        r.Should().BeApproximately(15f, 0.001f);
    }


    [Fact]
    public void Log_E_ReturnsOne()
    {
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


    [Fact]
    public void Calc_With_Sin()
    {
        float r = CalcResolver.Resolve("calc(100px * sin(90deg))", 0, DefaultFontSize);
        r.Should().BeApproximately(100f, 0.5f);
    }

    [Theory]
    [InlineData("calc((100% - 20px) / 2)", 500, 240)]
    [InlineData("calc(2 * (10px + 5px))", 0, 30)]
    [InlineData("calc(((10px)))", 0, 10)]
    [InlineData("calc((100% - 2 * 10px) / 4 + 1px)", 420, 101)]
    [InlineData("calc(100px - (20px - 5px))", 0, 85)]
    public void Calc_Parentheses_GroupSubExpressions(string expression, float containing, float expected)
    {
        CalcResolver.Resolve(expression, containing, DefaultFontSize).Should().BeApproximately(expected, 0.01f);
    }

    [Theory]
    [InlineData("calc(1e2px)", 100)]
    [InlineData("calc(1.5e1px + 5px)", 20)]
    [InlineData("calc(2E-1 * 50px)", 10)]
    [InlineData("calc(1em + 1e1px)", 26)]
    public void Calc_ScientificNotation_ParsesExponent(string expression, float expected)
    {
        CalcResolver.Resolve(expression, 0, DefaultFontSize).Should().BeApproximately(expected, 0.01f);
    }

    [Theory]
    [InlineData("calc(10px *)")]
    [InlineData("calc(10px * * 2)")]
    [InlineData("calc(10px / / 2)")]
    [InlineData("calc(* 10px)")]
    [InlineData("calc(10px + )")]
    [InlineData("calc((10px + 5px)")]
    [InlineData("calc(10px))")]
    [InlineData("calc(min(10px)")]
    [InlineData("round(,10px)")]
    [InlineData("round(  )")]
    [InlineData("round(up)")]
    [InlineData("log()")]
    [InlineData("atan2(1)")]
    [InlineData("pow(2)")]
    [InlineData("clamp(1px)")]
    [InlineData("sin()")]
    public void MathFunction_MalformedInput_DoesNotThrow(string expression)
    {
        var act = () => CalcResolver.Resolve(expression, 100, DefaultFontSize);

        act.Should().NotThrow();
    }

    [Fact]
    public void Calc_DeeplyNestedParentheses_DoesNotOverflowTheStack()
    {
        var expression = "calc(" + new string('(', 100000) + "1px" + new string(')', 100000) + ")";

        var act = () => CalcResolver.Resolve(expression, 100, DefaultFontSize);

        act.Should().NotThrow();
    }

    [Fact]
    public void Calc_DeeplyNestedFunctions_DoesNotOverflowTheStack()
    {
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < 20000; i++) builder.Append("calc(");
        builder.Append("1px");
        for (int i = 0; i < 20000; i++) builder.Append(')');

        var act = () => CalcResolver.Resolve(builder.ToString(), 100, DefaultFontSize);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("sin(1rad)", 0.8415)]
    [InlineData("sin(0.25turn)", 1)]
    [InlineData("sin(100grad)", 1)]
    [InlineData("cos(0.5turn)", -1)]
    [InlineData("tan(0.125turn)", 1)]
    [InlineData("sin(1)", 0.8415)]
    [InlineData("cos(3.14159265)", -1)]
    [InlineData("sin(calc(45deg + 45deg))", 1)]
    [InlineData("sin(calc(pi / 2))", 1)]
    [InlineData("cos(acos(0.5))", 0.5)]
    public void Trig_AngleUnitsAndPlainNumbers_UseTheRightUnit(string expression, float expected)
    {
        CalcResolver.Resolve(expression, 0, DefaultFontSize).Should().BeApproximately(expected, 0.001f);
    }

    [Theory]
    [InlineData("calc(pi * 1px)", 3.14159)]
    [InlineData("calc(e * 1px)", 2.71828)]
    public void Calc_Constants_PiAndE(string expression, float expected)
    {
        CalcResolver.Resolve(expression, 0, DefaultFontSize).Should().BeApproximately(expected, 0.001f);
    }

    [Theory]
    [InlineData("round(2.5)", 3)]
    [InlineData("round(down, 2.7)", 2)]
    [InlineData("round(UP, 21px, 10px)", 30)]
    [InlineData("round(to-zero, -25px, 10px)", -20)]
    [InlineData("round(23px, 10px)", 20)]
    public void Round_StrategyAndDefaultStep(string expression, float expected)
    {
        CalcResolver.Resolve(expression, 0, DefaultFontSize).Should().BeApproximately(expected, 0.001f);
    }
}
