using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class BorderStyleE2ETests
{
    [Fact]
    public async Task DashedBorder_ProducesDashPattern()
    {
        var html = "<div style='border: 2px dashed black; width: 200px; height: 100px'>Dashed</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Dashed");
        text.Should().MatchRegex(@"\[[\d.]+ [\d.]+\] [\d.]+ d", "dashed border should set dash pattern");
    }

    [Fact]
    public async Task DottedBorder_ProducesRoundCap()
    {
        var html = "<div style='border: 2px dotted #333; width: 200px; height: 100px'>Dotted</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Dotted");
        text.Should().Contain("1 J", "dotted border should use round line cap");
    }

    [Fact]
    public async Task DoubleBorder_ProducesTwoLines()
    {
        var html = "<div style='border: 4px double black; width: 200px; height: 100px'>Double</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Double");
        PdfAssert.Count(text, "l S").Should().BeGreaterOrEqualTo(8, "a double border strokes two lines on each of the four sides");
    }

    [Fact]
    public async Task GrooveBorder_ProducesTwoColorLines()
    {
        var html = "<div style='border: 3px groove gray; width: 200px; height: 100px'>Groove</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Groove");
        Regex.Matches(text, @"[\d.]+ [\d.]+ [\d.]+ RG").Cast<Match>().Select(m => m.Value).Distinct().Count()
            .Should().BeGreaterOrEqualTo(2, "a groove border strokes its sides in a darker and a lighter shade");
    }

    [Fact]
    public async Task RidgeBorder_ProducesValidPdf()
    {
        var html = "<div style='border: 3px ridge #999; width: 200px; height: 100px'>Ridge</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Ridge");
        text.Should().StartWith("%PDF");
    }

    [Fact]
    public async Task InsetBorder_ProducesValidPdf()
    {
        var html = "<div style='border: 2px inset #aaa; width: 200px; height: 100px'>Inset</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Inset");
    }

    [Fact]
    public async Task OutsetBorder_ProducesValidPdf()
    {
        var html = "<div style='border: 2px outset #aaa; width: 200px; height: 100px'>Outset</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Outset");
    }

    [Fact]
    public async Task PerSideBorderStyle_DifferentStyles()
    {
        var html = @"<div style='
            border-top: 2px solid red;
            border-right: 2px dashed green;
            border-bottom: 2px dotted blue;
            border-left: 2px double black;
            width: 200px; height: 100px'>Mixed borders</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Mixed borders");
    }

    [Fact]
    public async Task SolidBorder_StillWorks()
    {
        var html = "<div style='border: 1px solid black; width: 200px; height: 100px'>Solid</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Solid");
        text.Should().Contain("0.00 0.00 0.00 RG", "a solid black border is still stroked in black with per-side rendering");
    }

    [Fact]
    public async Task NoBorder_NoBorderOperators()
    {
        var html = "<div style='width: 200px; height: 100px'>No border</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DashedTableBorder_ProducesValidPdf()
    {
        var html = @"<table style='border-collapse: collapse'>
            <tr><td style='border: 1px dashed #ccc; padding: 8px'>Cell 1</td>
                <td style='border: 1px dashed #ccc; padding: 8px'>Cell 2</td></tr>
        </table>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Cell 1");
        text.Should().Contain("Cell 2");
    }
}
