using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class FlexTextChildTests
{
    [Fact]
    public async Task FlexContainer_DirectText_IsRendered()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"display:flex;width:200px;height:100px\">STAMPTEXT</div></body></html>");

        Encoding.ASCII.GetString(pdf).Should().Contain("STAMPTEXT",
            "direct text in a flex container forms an anonymous flex item (CSS Flexbox §4) and must not vanish");
    }

    [Fact]
    public async Task FlexContainer_Border_IsPaintedOnceNotOnTextItem()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"display:flex;align-items:center;justify-content:center;" +
            "width:80px;height:80px;border:3px solid #ff0000;border-radius:50%\">STAMP</div></body></html>");

        var text = Encoding.ASCII.GetString(pdf);
        System.Text.RegularExpressions.Regex.Matches(text, "1\\.00 0\\.00 0\\.00 RG").Count
            .Should().Be(1,
                "the anonymous text item inherits no box decorations, so a bordered stamp circle gets no second inner circle");
    }

    [Fact]
    public async Task FlexContainer_TextWithBr_RendersAllLines()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"display:flex;align-items:center;justify-content:center;" +
            "width:120px;height:120px;text-align:center\">LINEONE<br>LINETWO</div></body></html>");

        var text = PdfAssert.ValidPdf(pdf);
        PdfAssert.TextPosition(text, "LINETWO").Y.Should().BeLessThan(PdfAssert.TextPosition(text, "LINEONE").Y,
            "a stamp circle's centered text keeps its <br> line break");
    }
}
