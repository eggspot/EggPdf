using System.Linq;
using System.Text.RegularExpressions;
using EggPdf.Html;
using EggPdf.Html.Dom;
using EggPdf.Svg;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Svg;

public class SvgPresentationTests
{
    private static SvgRenderResult Render(string svgMarkup, float width = 100, float height = 100)
    {
        var doc = HtmlParser.Parse("<html><body>" + svgMarkup + "</body></html>");
        var svgEl = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "svg");
        return SvgRenderer.RenderWithResources(SvgParser.Parse(svgEl)!, 0, 0, width, height);
    }

    private static string FillBefore(string commands, string pathOperator)
    {
        int at = commands.IndexOf(pathOperator + "\n", System.StringComparison.Ordinal);
        at.Should().BeGreaterThan(-1);
        var fills = Regex.Matches(commands.Substring(0, at), @"(\d\.\d\d \d\.\d\d \d\.\d\d) rg");
        fills.Count.Should().BeGreaterThan(0, "a fill colour must be set before the shape");
        return fills[fills.Count - 1].Groups[1].Value;
    }

    [Fact]
    public void Fill_InheritedFromGroup_PaintsChildShape()
    {
        var result = Render("<svg viewBox='0 0 10 10'><g fill='red'><rect width='10' height='10'/></g></svg>");

        FillBefore(result.Commands, " re").Should().Be("1.00 0.00 0.00");
    }

    [Fact]
    public void Fill_FromStyleAttribute_OverridesPresentationAttribute()
    {
        var result = Render("<svg viewBox='0 0 10 10'><rect fill='red' style='fill:#00ff00' width='10' height='10'/></svg>");

        FillBefore(result.Commands, " re").Should().Be("0.00 1.00 0.00");
    }

    [Fact]
    public void Fill_Missing_IsBlackEvenAfterAColouredSibling()
    {
        var result = Render("<svg viewBox='0 0 10 10'><rect fill='red' width='5' height='5'/><circle cx='5' cy='5' r='2'/></svg>");

        var afterRect = result.Commands.Substring(result.Commands.IndexOf(" re\n", System.StringComparison.Ordinal));
        afterRect.Should().Contain("0.00 0.00 0.00 rg", "an unfilled shape paints black, not the previous colour");
    }

    [Fact]
    public void Fill_CurrentColor_ResolvesToInheritedColor()
    {
        var result = Render("<svg viewBox='0 0 10 10'><g color='blue'><rect fill='currentColor' width='10' height='10'/></g></svg>");

        FillBefore(result.Commands, " re").Should().Be("0.00 0.00 1.00");
    }

    [Fact]
    public void Fill_GradientReference_FallsBackToFirstStopColour()
    {
        var result = Render("<svg viewBox='0 0 10 10'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs>" +
                            "<rect fill='url(#g)' width='10' height='10'/></svg>");

        FillBefore(result.Commands, " re").Should().Be("1.00 0.00 0.00");
    }

    [Fact]
    public void Viewport_WithoutViewBox_MapsUserUnitsToCssPixels()
    {
        var result = Render("<svg width='100' height='100'><rect width='10' height='10'/></svg>", 75, 75);

        result.Commands.Should().Contain("0.75 0 0 -0.75 ");
    }

    [Theory]
    [InlineData("", "1 0 0 -1 0 75 cm")]
    [InlineData(" preserveAspectRatio='xMinYMin meet'", "1 0 0 -1 0 100 cm")]
    [InlineData(" preserveAspectRatio='none'", "1 0 0 -2 0 100 cm")]
    [InlineData(" preserveAspectRatio='xMidYMid slice'", "2 0 0 -2 -50 100 cm")]
    public void Viewport_PreserveAspectRatio_PlacesTheViewBox(string attribute, string expectedMatrix)
    {
        var result = Render("<svg viewBox='0 0 100 50'" + attribute + "><rect width='10' height='10'/></svg>");

        result.Commands.Should().Contain(expectedMatrix);
    }

    [Fact]
    public void Viewport_IsClippedToTheTargetBox()
    {
        var result = Render("<svg viewBox='0 0 10 10'><rect width='100' height='100'/></svg>");

        result.Commands.Should().Contain("0.00 0.00 100.00 100.00 re W n");
    }

    [Fact]
    public void Use_ReferencedElement_IsDrawnAtItsOffset()
    {
        var result = Render("<svg viewBox='0 0 100 100'><defs><rect id='r' width='5' height='5' fill='red'/></defs><use href='#r' x='10' y='20'/></svg>");

        result.Commands.Should().Contain("1 0 0 1 10.00 20.00 cm");
        FillBefore(result.Commands, " re").Should().Be("1.00 0.00 0.00");
    }

    [Fact]
    public void Use_SelfReference_DoesNotRecurseForever()
    {
        var act = () => Render("<svg viewBox='0 0 10 10'><g id='loop'><use href='#loop'/></g></svg>");

        act.Should().NotThrow();
    }

    [Fact]
    public void Text_IsDrawnUprightWithARegisteredStandardFont()
    {
        var result = Render("<svg viewBox='0 0 100 100'><text x='10' y='20' font-family='serif' font-weight='bold'>Hi</text></svg>");

        result.Commands.Should().Contain("/Std-Times-Bold");
        result.Commands.Should().Contain("1 0 0 -1 10.00 20.00 Tm");
        result.UsedFonts.Should().Contain("Std-Times-Bold");
    }

    [Fact]
    public void Render_InlineSvgText_WritesTheFontResource()
    {
        var pdf = EggPdf.HtmlToPdf.Render("<svg width='100' height='40' viewBox='0 0 100 40'><text x='5' y='20'>Label</text></svg>");

        var text = PdfAssert.ValidPdf(pdf, "(Label) Tj");
        text.Should().Contain("/BaseFont /Helvetica");
    }
}
