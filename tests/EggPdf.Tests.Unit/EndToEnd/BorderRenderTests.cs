using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class BorderRenderTests
{
    [Fact]
    public async Task BorderSolid_ProducesStrokeOperator()
    {
        var html = "<div style='border: 1px solid black; width: 200px; height: 100px'>Bordered</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Bordered");
        text.Should().Contain("0.00 0.00 0.00 RG", "the border is stroked in black");
        text.Should().MatchRegex(@"re S|l S", "the border is stroked as a rectangle or as line segments");
    }

    [Fact]
    public async Task BorderColor_Red_UsesRedStroke()
    {
        var html = "<div style='border: 2px solid red; width: 100px; height: 50px'>Red border</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Red border");
        text.Should().Contain("1.00 0.00 0.00 RG", "the stroke color is red");
    }

    [Fact]
    public async Task TableBorder_ProducesLines()
    {
        var html = @"<table style='border-collapse: collapse'>
            <tr><td style='border: 1px solid #ddd; padding: 8px'>Cell</td></tr>
        </table>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Cell");
        text.Should().Contain("0.87 0.87 0.87 RG", "the cell border is stroked in #ddd (221/255 = 0.87)");
    }

    [Fact]
    public async Task NoBorder_NoStrokeOperator()
    {
        var html = "<div style='width: 100px; height: 50px'>No border</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("No border");
        text.Should().NotContain("re S", "a box without a border strokes nothing");
        text.Should().NotContain("l S", "a box without a border strokes nothing");
    }
}
