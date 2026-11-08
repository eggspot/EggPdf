using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class BorderSideColorTests
{
    private static async Task<string> RenderAscii(string body) =>
        Encoding.ASCII.GetString(await HtmlToPdf.RenderAsync("<html><body>" + body + "</body></html>"));

    [Fact]
    public async Task LeftBorderOnlyWithRadius_PaintsOnlyTheBlueLeftSide()
    {
        var content = await RenderAscii(
            "<div style='border-left:4px solid #0066cc;border-radius:4px;width:200px;height:40px'>Callout</div>");

        content.Should().Contain("0.00 0.40 0.80 RG", "the left border keeps its own colour");
        content.Should().NotContain("0.00 w", "no hairline may be stroked round the whole box");
        content.Should().NotContain("0.00 0.00 0.00 RG");
    }

    [Fact]
    public async Task UniformWidthWithOneDifferentSideColour_PaintsEachSideInItsColour()
    {
        var content = await RenderAscii(
            "<div style='border:2px solid black;border-left-color:blue;width:200px;height:40px'>Box</div>");

        content.Should().Contain("0.00 0.00 1.00 RG", "the left side is blue");
        content.Should().Contain("0.00 0.00 0.00 RG", "the other sides stay black");
    }

    [Fact]
    public async Task SameRedDifferentGreen_IsNotTreatedAsUniform()
    {
        var content = await RenderAscii(
            "<div style='border:2px solid #ff0000;border-right-color:#ffff00;width:200px;height:40px'>Box</div>");

        content.Should().Contain("1.00 1.00 0.00 RG", "the yellow right side must not be painted red");
    }

    [Fact]
    public async Task TranslucentBorderColour_UsesOpacity()
    {
        var content = await RenderAscii(
            "<div style='border:1px solid rgba(0,0,0,0.2);width:200px;height:40px'>Faint</div>");

        content.Should().Contain("/GS20 gs");
        content.Should().Contain("/GS100 gs", "opacity must be reset after the border");
    }

    [Fact]
    public async Task UniformBorderWithRadius_StillUsesOneRoundedStroke()
    {
        var content = await RenderAscii(
            "<div style='border:2px solid #0066cc;border-radius:8px;width:200px;height:40px'>Card</div>");

        content.Should().Contain("0.00 0.40 0.80 RG");
        content.Should().Contain(" c", "the rounded rectangle is drawn with curves");
    }
}
