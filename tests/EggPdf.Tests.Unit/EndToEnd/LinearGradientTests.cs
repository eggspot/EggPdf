using System.Linq;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class LinearGradientTests
{
    private static string[] BandColours(string commands) =>
        Regex.Matches(commands, @"(\d\.\d\d \d\.\d\d \d\.\d\d) rg").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void ToBottom_StartsWithTheFirstColourAtTheTop()
    {
        var commands = PdfGradient.RenderLinearGradient("linear-gradient(red, blue)", 0, 0, 100, 50)!;

        commands.Should().Contain("0 -1 1 0 50 25 cm", "the gradient line points down the page");
        BandColours(commands).First().Should().Be("1.00 0.00 0.00");
        BandColours(commands).Last().Should().Be("0.00 0.00 1.00");
    }

    [Fact]
    public void DiagonalAngle_RotatesTheBands()
    {
        var commands = PdfGradient.RenderLinearGradient("linear-gradient(45deg, red, blue)", 0, 0, 100, 100)!;

        commands.Should().Contain("0.707107 0.707107 -0.707107 0.707107 50 50 cm");
    }

    [Fact]
    public void HardStop_HasNoBlendedBands()
    {
        var commands = PdfGradient.RenderLinearGradient("linear-gradient(to right, red 50%, blue 50%)", 0, 0, 100, 10)!;

        BandColours(commands).Should().OnlyContain(c => c == "1.00 0.00 0.00" || c == "0.00 0.00 1.00");
    }

    [Fact]
    public void TranslucentStops_SelectOpacityStates()
    {
        var states = new System.Collections.Generic.List<string>();
        var commands = PdfGradient.RenderLinearGradient("linear-gradient(rgba(255,0,0,0.5), rgba(255,0,0,0.5))", 0, 0, 100, 10, states)!;

        states.Should().Equal("GS50");
        commands.Should().Contain("/GS50 gs");
        BandColours(commands).Should().OnlyContain(c => c == "1.00 0.00 0.00");
    }

    [Fact]
    public void FadeToTransparent_KeepsTheColourInsteadOfTurningBlack()
    {
        var states = new System.Collections.Generic.List<string>();
        var commands = PdfGradient.RenderLinearGradient("linear-gradient(red, transparent)", 0, 0, 100, 100, states)!;

        BandColours(commands).Should().OnlyContain(c => c == "1.00 0.00 0.00");
    }

    [Fact]
    public void Render_TranslucentGradientBackground_WritesTheOpacityStates()
    {
        var pdf = HtmlToPdf.Render("<div style='width:100px;height:50px;background:linear-gradient(rgba(0,0,255,.25), rgba(0,0,255,.25))'></div>");

        PdfAssert.ValidPdf(pdf, "/GS25 gs").Should().Contain("/ca 0.25 /CA 0.25");
    }
}
