using System;
using System.Linq;
using EggPdf.Html;
using EggPdf.Html.Dom;
using EggPdf.Svg;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Svg;

public class SvgShapePaintTests
{
    private static SvgRenderResult Render(string svgMarkup, float width = 100, float height = 100)
    {
        var doc = HtmlParser.Parse("<html><body>" + svgMarkup + "</body></html>");
        var svgEl = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "svg");
        return SvgRenderer.RenderWithResources(SvgParser.Parse(svgEl)!, 0, 0, width, height);
    }

    private static string Svg(string content) => "<svg viewBox='0 0 100 100'>" + content + "</svg>";

    [Fact]
    public void FillNone_WithoutStroke_PaintsNothing()
    {
        var result = Render(Svg("<rect fill='none' width='10' height='10'/>"));

        result.Commands.Should().NotContain("10.00 10.00 re");
    }

    [Fact]
    public void Line_WithoutStroke_PaintsNothing()
    {
        var result = Render(Svg("<line x1='0' y1='0' x2='10' y2='10'/>"));

        result.Commands.Should().NotContain(" l S");
    }

    [Fact]
    public void StrokeWidth_DoesNotLeakIntoTheNextShape()
    {
        var result = Render(Svg("<line x1='0' y1='0' x2='10' y2='0' stroke='red' stroke-width='5'/>" +
                                "<line x1='0' y1='5' x2='10' y2='5' stroke='blue'/>"));

        int wide = result.Commands.IndexOf("5.00 w", StringComparison.Ordinal);
        int blue = result.Commands.IndexOf("0.00 0.00 1.00 RG", StringComparison.Ordinal);
        wide.Should().BeGreaterThan(-1);
        blue.Should().BeGreaterThan(wide);
        result.Commands.Substring(wide, blue - wide).Should().Contain("Q\n", "each shape restores the graphics state it changed");
    }

    [Theory]
    [InlineData("<rect fill='red' fill-opacity='0.5' width='10' height='10'/>", "GSA50_100")]
    [InlineData("<rect fill='rgba(255,0,0,0.5)' width='10' height='10'/>", "GSA50_100")]
    [InlineData("<g opacity='0.5'><rect fill='red' opacity='0.5' width='10' height='10'/></g>", "GSA25_100")]
    [InlineData("<rect fill='red' stroke='blue' stroke-opacity='0.4' width='10' height='10'/>", "GSA100_40")]
    public void Opacity_SelectsAGraphicsStateWithTheCombinedAlpha(string shape, string expectedState)
    {
        var result = Render(Svg(shape));

        result.UsedExtGStates.Should().ContainSingle().Which.Should().Be(expectedState);
        result.Commands.Should().Contain("/" + expectedState + " gs");
    }

    [Theory]
    [InlineData("<rect fill='transparent' width='10' height='10'/>")]
    [InlineData("<rect fill='red' opacity='0' width='10' height='10'/>")]
    [InlineData("<rect fill='red' display='none' width='10' height='10'/>")]
    [InlineData("<rect fill='red' visibility='hidden' width='10' height='10'/>")]
    [InlineData("<defs><rect fill='red' width='10' height='10'/></defs>")]
    [InlineData("<clipPath id='c'><rect fill='red' width='10' height='10'/></clipPath>")]
    [InlineData("<symbol id='s'><rect fill='red' width='10' height='10'/></symbol>")]
    [InlineData("<mask id='m'><rect fill='red' width='10' height='10'/></mask>")]
    public void InvisibleOrReferenceOnlyShapes_AreNotPainted(string markup)
    {
        var result = Render(Svg(markup));

        result.Commands.Should().NotContain("10.00 10.00 re");
    }

    [Fact]
    public void FillRuleEvenOdd_UsesTheEvenOddFillOperator()
    {
        var result = Render(Svg("<path fill-rule='evenodd' d='M0 0H10V10H0Z M2 2H8V8H2Z'/>"));

        result.Commands.Should().Contain("\nf*\n");
    }

    [Fact]
    public void InvalidColour_DoesNotThrowAndFallsBackToBlack()
    {
        var result = Render(Svg("<rect fill='#zzz' width='10' height='10'/>"));

        result.Commands.Should().Contain("0.00 0.00 0.00 rg");
    }

    [Fact]
    public void Use_OfASymbol_InheritsTheFillOfTheUseElement()
    {
        var result = Render(Svg("<symbol id='s'><path d='M0 0H10V10Z'/></symbol><use href='#s' fill='red'/>"));

        result.Commands.Should().Contain("1.00 0.00 0.00 rg");
        result.Commands.Split(new[] { " m " }, StringSplitOptions.None).Length.Should().Be(2, "the symbol is drawn once, through the use");
    }

    [Fact]
    public void Percentages_ResolveAgainstTheViewBox()
    {
        var result = Render("<svg viewBox='0 0 200 100'><rect width='50%' height='100%'/></svg>");

        result.Commands.Should().Contain("0.00 0.00 100.00 100.00 re");
    }

    [Fact]
    public void StrokeDashArrayAndLineCap_AreApplied()
    {
        var result = Render(Svg("<line x1='0' y1='0' x2='50' y2='0' stroke='black' stroke-dasharray='4 2 1' stroke-linecap='round'/>"));

        result.Commands.Should().Contain("[4.00 2.00 1.00 4.00 2.00 1.00] 0.00 d");
        result.Commands.Should().Contain("1 J");
    }

    [Fact]
    public void LargeViewBox_KeepsTheScaleAccurate()
    {
        var result = Render("<svg viewBox='0 0 1000 1000'><rect width='10' height='10'/></svg>", 75, 75);

        result.Commands.Should().Contain("0.075 0 0 -0.075 0 75 cm");
    }

    [Theory]
    [InlineData("rotate(90 10 10)", "0 1 -1 0 20 0 cm")]
    [InlineData("skewX(45)", "1 0 1 1 0 0 cm")]
    [InlineData("translate(5) scale(2)", "2 0 0 2 5 0 cm")]
    public void Transform_FunctionsComposeIntoOneMatrix(string transform, string expected)
    {
        var result = Render(Svg("<rect transform='" + transform + "' width='10' height='10'/>"));

        result.Commands.Should().Contain(expected);
    }

    [Fact]
    public void PathArc_IsDrawnAsCurves()
    {
        var result = Render(Svg("<path d='M0 0 A10 10 0 0 1 20 0'/>"));

        result.Commands.Should().Contain(" c ");
        result.Commands.Should().NotContain("20.00 0.00 l");
        result.Commands.Should().Contain("20.00 0.00 c");
    }

    [Theory]
    [InlineData("M.5.5L1.5.5", "0.50 0.50 m 1.50 0.50 l")]
    [InlineData("M0 0a5 5 0 0010 0", "10.00 0.00 c")]
    [InlineData("M0 0Q10 10 20 0T40 0", "40.00 0.00 c")]
    [InlineData("M0 0 10 0 10 10z", "10.00 0.00 l 10.00 10.00 l h")]
    public void PathData_CompactAndImplicitForms_AreRead(string d, string expected)
    {
        var result = Render(Svg("<path d='" + d + "'/>"));

        result.Commands.Should().Contain(expected);
    }

    [Theory]
    [InlineData("M0 0L10 10Z 5 5")]
    [InlineData("M0 0 L10 10 # 20 20")]
    [InlineData("L10 10")]
    [InlineData("M0 0 C 1 2")]
    public void PathData_WithAnError_RendersUpToTheErrorAndStops(string d)
    {
        var act = () => Render(Svg("<path d='" + d + "'/>"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Polyline_WithAnInvalidPoint_StopsAtIt()
    {
        var result = Render(Svg("<polyline stroke='black' points='0,0 10,10 x 20,20'/>"));

        result.Commands.Should().Contain("10.00 10.00 l").And.NotContain("20.00 20.00");
    }

    [Fact]
    public void Render_TranslucentSvgShape_WritesTheGraphicsStateResource()
    {
        var pdf = EggPdf.HtmlToPdf.Render("<svg width='10' height='10'><rect width='10' height='10' fill='red' fill-opacity='0.5'/></svg>");

        var text = PdfAssert.ValidPdf(pdf, "/GSA50_100 gs");
        text.Should().Contain("/ca 0.50 /CA 1.00");
    }

    [Fact]
    public void Render_InlineSvgCurrentColor_UsesTheHtmlColor()
    {
        var pdf = EggPdf.HtmlToPdf.Render("<div style='color:#0000ff'><svg width='10' height='10'><rect width='10' height='10' fill='currentColor'/></svg></div>");

        PdfAssert.ValidPdf(pdf).Should().Contain("0.00 0.00 1.00 rg");
    }

    [Fact]
    public void Render_SvgContent_UsesLineFeedsOnly()
    {
        var result = Render(Svg("<rect fill='red' width='10' height='10'/><text x='1' y='5'>A</text>"));

        result.Commands.Should().NotContain("\r");
    }
}
