using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Direct text children of a flex container form an anonymous flex item
/// (CSS Flexbox §4). They must render, not vanish (the empty-stamp-circle bug).
/// </summary>
public class FlexTextChildTests
{
    [Fact]
    public async Task FlexContainer_DirectText_IsRendered()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"display:flex;width:200px;height:100px\">STAMPTEXT</div></body></html>");

        Encoding.ASCII.GetString(pdf).Should().Contain("STAMPTEXT");
    }

    [Fact]
    public async Task FlexContainer_Border_IsPaintedOnceNotOnTextItem()
    {
        // The anonymous text item must not inherit paintable box decorations —
        // otherwise a bordered stamp circle renders a second inner circle.
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"display:flex;align-items:center;justify-content:center;" +
            "width:80px;height:80px;border:3px solid #ff0000;border-radius:50%\">STAMP</div></body></html>");

        var text = Encoding.ASCII.GetString(pdf);
        System.Text.RegularExpressions.Regex.Matches(text, "1\\.00 0\\.00 0\\.00 RG").Count
            .Should().Be(1, "the border must be stroked exactly once, on the container only");
    }

    [Fact]
    public async Task FlexContainer_TextWithBr_RendersAllLines()
    {
        // The certificate's stamp circle: centered multi-line text via <br>
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"display:flex;align-items:center;justify-content:center;" +
            "width:120px;height:120px;text-align:center\">LINEONE<br>LINETWO</div></body></html>");

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("LINEONE");
        text.Should().Contain("LINETWO");
    }
}
