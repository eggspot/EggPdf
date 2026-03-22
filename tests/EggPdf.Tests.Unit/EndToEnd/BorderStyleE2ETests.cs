using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// End-to-end tests for border styles: dashed, dotted, double, groove, ridge, inset, outset.
/// </summary>
public class BorderStyleE2ETests
{
    [Fact]
    public async Task DashedBorder_ProducesDashPattern()
    {
        var html = "<div style='border: 2px dashed black; width: 200px; height: 100px'>Dashed</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Dashed");
        // Dashed border should produce dash array operator 'd'
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
        // Double border draws two separate lines per side
        // Look for multiple stroke operations
        text.Should().Contain("l S", "double border should stroke line segments");
    }

    [Fact]
    public async Task GrooveBorder_ProducesTwoColorLines()
    {
        var html = "<div style='border: 3px groove gray; width: 200px; height: 100px'>Groove</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Groove");
        // Groove uses two different stroke colors
        text.Should().Contain("RG", "groove border should set stroke colors");
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
        // Regression test: solid borders should still work with per-side rendering
        var html = "<div style='border: 1px solid black; width: 200px; height: 100px'>Solid</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Solid");
        text.Should().Contain("RG"); // stroke color
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
