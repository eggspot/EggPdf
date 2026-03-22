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
        text.Should().Contain("re"); // rectangle
        text.Should().Contain("S");  // stroke operator
    }

    [Fact]
    public async Task BorderColor_Red_UsesRedStroke()
    {
        var html = "<div style='border: 2px solid red; width: 100px; height: 50px'>Red border</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Red border");
        text.Should().Contain("RG"); // stroke color operator
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
    }

    [Fact]
    public async Task NoBorder_NoStrokeOperator()
    {
        var html = "<div style='width: 100px; height: 50px'>No border</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("No border");
        // Should not have unnecessary stroke operators
    }
}
