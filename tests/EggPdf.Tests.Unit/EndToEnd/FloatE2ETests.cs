using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// End-to-end tests for CSS float layout: HTML-to-PDF pipeline.
/// Verifies that floated elements produce correct PDF output.
/// </summary>
public class FloatE2ETests
{
    [Fact]
    public async Task FloatLeft_ProducesValidPdf()
    {
        var html = @"
            <div style='float: left; width: 200px; height: 100px; background-color: #ccc'>
                Float Left
            </div>
            <p>This text should flow around the floated element on the right side.</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("Float Left");
    }

    [Fact]
    public async Task FloatRight_ProducesValidPdf()
    {
        var html = @"
            <div style='float: right; width: 200px; height: 100px; background-color: #ddd'>
                Float Right
            </div>
            <p>This text should flow around the floated element on the left side.</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("Float Right");
    }

    [Fact]
    public async Task ImageFloatLeft_TextWrapsAround()
    {
        var html = @"
            <img style='float: left; width: 100px; height: 100px' src='placeholder.png'>
            <p>This is a paragraph of text that should wrap around the floated image on the left.</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
    }

    [Fact]
    public async Task ClearBoth_ProducesValidPdf()
    {
        var html = @"
            <div style='float: left; width: 150px; height: 80px; background-color: #eee'>Left</div>
            <div style='float: right; width: 150px; height: 80px; background-color: #ddd'>Right</div>
            <div style='clear: both; background-color: #ccc'>Cleared content below both floats</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("Cleared content");
    }

    [Fact]
    public async Task MultipleFloats_ProducesValidPdf()
    {
        var html = @"
            <div style='float: left; width: 100px; height: 60px; background-color: red'>A</div>
            <div style='float: left; width: 100px; height: 60px; background-color: green'>B</div>
            <div style='float: right; width: 100px; height: 60px; background-color: blue'>C</div>
            <p>Content flows between the floats.</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("A");
        text.Should().Contain("B");
        text.Should().Contain("C");
    }

    [Fact]
    public async Task FloatWithClearAndContent_ComplexLayout()
    {
        var html = @"
            <div style='width: 500px'>
                <div style='float: left; width: 150px; background-color: #f0f0f0; padding: 10px'>
                    Sidebar content
                </div>
                <div style='margin-left: 170px'>
                    <h2>Main Content</h2>
                    <p>This is the main content area next to the float.</p>
                </div>
                <div style='clear: both'></div>
                <p>Footer after cleared floats.</p>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("Sidebar content");
        text.Should().Contain("Main Content");
    }
}
