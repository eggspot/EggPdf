using System;
using System.Linq;
using EggPdf.Html;
using EggPdf.Html.Dom;
using EggPdf.Svg;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Svg;

public class SvgTextTests
{
    private const float GlyphWidth = 10f;

    private static string Render(string svgContent)
    {
        var doc = HtmlParser.Parse("<html><body><svg viewBox='0 0 100 100'>" + svgContent + "</svg></body></html>");
        var svgEl = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "svg");
        return SvgRenderer.RenderWithResources(SvgParser.Parse(svgEl)!, 0, 0, 100, 100, null,
            (text, fontSize, fontName) => text.Length * GlyphWidth).Commands;
    }

    [Theory]
    [InlineData("start", "1 0 0 -1 50.00 20.00 Tm")]
    [InlineData("middle", "1 0 0 -1 40.00 20.00 Tm")]
    [InlineData("end", "1 0 0 -1 30.00 20.00 Tm")]
    public void TextAnchor_ShiftsTheTextByItsWidth(string anchor, string expectedPosition)
    {
        var commands = Render("<text x='50' y='20' text-anchor='" + anchor + "'>Hi</text>");

        commands.Should().Contain(expectedPosition);
    }

    [Fact]
    public void Tspan_ContinuesAfterThePrecedingTextWithItsOwnFill()
    {
        var commands = Render("<text x='10' y='20'>A<tspan fill='red'>B</tspan>C</text>");

        commands.Should().Contain("1 0 0 -1 10.00 20.00 Tm (A) Tj");
        commands.Should().Contain("1 0 0 -1 20.00 20.00 Tm (B) Tj");
        commands.Should().Contain("1 0 0 -1 30.00 20.00 Tm (C) Tj");
        int b = commands.IndexOf("(B) Tj", StringComparison.Ordinal);
        commands.Substring(0, b).Should().EndWith("1.00 0.00 0.00 rg\nBT /Std-Helvetica 16.00 Tf 1 0 0 -1 20.00 20.00 Tm ");
    }

    [Fact]
    public void Tspan_WithItsOwnPositionAndDy_StartsANewLine()
    {
        var commands = Render("<text x='10' y='20'>First<tspan x='10' dy='15'>Second</tspan></text>");

        commands.Should().Contain("1 0 0 -1 10.00 35.00 Tm (Second) Tj");
    }

    [Fact]
    public void Whitespace_IsCollapsedAndTrimmed()
    {
        var commands = Render("<text x='0' y='10'>\n    Hello\n    world   </text>");

        commands.Should().Contain("(Hello world) Tj");
    }

    [Fact]
    public void DominantBaselineCentral_MovesTheBaselineDown()
    {
        var commands = Render("<text x='0' y='20' dominant-baseline='central'>X</text>");

        commands.Should().Contain("1 0 0 -1 0.00 25.60 Tm");
    }

    [Fact]
    public void LatinText_IsEncodedForWinAnsi()
    {
        var commands = Render("<text x='0' y='10'>Caf" + (char)0xE9 + " " + (char)0x20AC + "5</text>");

        commands.Should().Contain("(Caf" + (char)0xE9 + " " + (char)0x80 + "5) Tj");
    }

    [Fact]
    public void Parentheses_AreEscaped()
    {
        var commands = Render("<text x='0' y='10'>f(x)</text>");

        commands.Should().Contain("(f\\(x\\)) Tj");
    }

    [Fact]
    public void FontFamilyList_UsesTheFirstFamily()
    {
        var commands = Render("<text x='0' y='10' font-family=\"Georgia, 'Helvetica Neue', sans-serif\">X</text>");

        commands.Should().Contain("/Std-Times-Roman");
    }

    [Fact]
    public void FillNone_DrawsNoText()
    {
        var commands = Render("<text x='0' y='10' fill='none'>X</text>");

        commands.Should().NotContain("(X) Tj");
    }

    [Fact]
    public void Render_InlineSvgText_InheritsTheHtmlFontSize()
    {
        var pdf = EggPdf.HtmlToPdf.Render("<div style='font-size:20px'><svg width='100' height='40' viewBox='0 0 100 40'><text x='5' y='20'>Label</text></svg></div>");

        var text = PdfAssert.ValidPdf(pdf, "(Label) Tj");
        text.Should().Contain("/Std-Helvetica 20.00 Tf");
        text.Should().Contain("/BaseFont /Helvetica ", "the resource is written as the standard Helvetica font");
    }
}
