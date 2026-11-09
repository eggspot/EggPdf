using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class PaintGeometryTests
{
    private static string Content(string body) =>
        PdfAssert.ValidPdf(HtmlToPdf.Render("<html><body style='margin:0'>" + body + "</body></html>"));

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    [Fact]
    public void TranslatePercent_ResolvesAgainstTheBoxSize()
    {
        var content = Content("<div style='margin-left:300px;width:200px;height:100px;transform:translate(-50%,-50%);background:#f00'></div>");

        content.Should().Contain("-75.00 37.50 cm");
    }

    [Fact]
    public void Outline_IsDrawnEntirelyOutsideTheBorderBox()
    {
        var content = Content("<div style='margin:20px;width:100px;height:40px;outline:4px solid #00f'></div>");

        var stroke = Regex.Match(content, @"(-?\d+\.\d+) (-?\d+\.\d+) (\d+\.\d+) (\d+\.\d+) re S");
        stroke.Success.Should().BeTrue();
        F(stroke.Groups[1].Value).Should().BeApproximately(13.5f, 0.02f, "the 3pt-wide stroke is centred 1.5pt outside the 15pt border edge");
        F(stroke.Groups[3].Value).Should().BeApproximately(78f, 0.02f);
    }

    [Fact]
    public void WavyUnderline_ControlPointsStayInsideEachHalfWave()
    {
        var content = Content("<p style='margin:0;font-family:Helvetica;text-decoration:underline wavy'>wavy underline text</p>");

        var curves = Regex.Matches(content, @"(-?\d+\.\d+) (-?\d+\.\d+) (-?\d+\.\d+) (-?\d+\.\d+) (-?\d+\.\d+) (-?\d+\.\d+) c");
        curves.Count.Should().BeGreaterThan(2);
        foreach (Match c in curves)
        {
            F(c.Groups[1].Value).Should().BeLessThanOrEqualTo(F(c.Groups[5].Value) + 0.01f);
            F(c.Groups[3].Value).Should().BeLessThanOrEqualTo(F(c.Groups[5].Value) + 0.01f);
        }
    }

    [Fact]
    public void RoundedCorners_RadiusThatFitsItsSides_IsNotShrunk()
    {
        var content = Content("<div style='width:100px;height:40px;background:#0f0;border-top-left-radius:30px'></div>");

        content.Should().Contain("22.50 ", "a 30px corner fits the 40px side when the adjacent corner is square");
        Regex.IsMatch(content, @"22\.50 \d+\.\d+ m").Should().BeTrue();
    }

    [Fact]
    public void RoundedCorners_RadiiThatOverflowASide_ScaleTogether()
    {
        var content = Content("<div style='width:100px;height:40px;background:#0f0;border-radius:40px'></div>");

        Regex.IsMatch(content, @"15\.00 \d+\.\d+ m").Should().BeTrue("40px radii on a 40px side scale by 0.5 to 20px = 15pt");
    }

    [Fact]
    public void DecorationColorCurrentcolorLowercase_UsesTheTextColor()
    {
        var content = Content("<p style='margin:0;color:rgb(255,0,0);text-decoration:underline;text-decoration-color:currentcolor'>red</p>");

        content.Should().Contain("1.00 0.00 0.00 RG");
        content.Should().NotContain("0.00 0.00 0.00 RG");
    }

    [Fact]
    public void BorderWithoutColor_UsesTheTextColor()
    {
        var content = Content("<div style='color:rgb(0,0,255);border:3px solid;width:50px;height:20px'></div>");

        content.Should().Contain("0.00 0.00 1.00 RG");
    }

    [Fact]
    public void BorderImageGradient_IsClippedToTheBorderRing()
    {
        var content = Content("<div style='width:100px;height:60px;border:10px solid;border-image-source:linear-gradient(red, blue);border-image-slice:1'></div>");

        content.Should().Contain("W* n");
    }

    [Fact]
    public void BoxShadow_IsClippedOutOfTheBorderBox()
    {
        var content = Content("<div style='margin:20px;width:100px;height:40px;box-shadow:4px 4px 0 rgba(0,0,0,0.5)'></div>");

        content.Should().Contain("W* n");
    }

    [Fact]
    public void TextShadowWithoutColor_UsesTheTextColorOpaque()
    {
        var content = Content("<p style='margin:0;font-family:Helvetica;color:rgb(0,0,255);text-shadow:1px 1px'>shadow</p>");

        Regex.Matches(content, @"0\.00 0\.00 1\.00 rg").Count.Should().BeGreaterThanOrEqualTo(2);
        content.Should().NotContain("/GS50 gs");
    }

    [Fact]
    public void ContainContent_ClipsDescendants()
    {
        var content = Content("<div style='contain:content;width:100px;height:20px'><div style='height:100px;background:#f00'></div></div>");

        content.Should().Contain("re W n");
    }

    [Fact]
    public void OverflowXHidden_ClipsDescendants()
    {
        var content = Content("<div style='overflow-x:hidden;overflow-y:hidden;width:100px;height:20px'><div style='height:100px;background:#f00'></div></div>");

        content.Should().Contain("re W n");
    }
}
