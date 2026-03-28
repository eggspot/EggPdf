using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// End-to-end tests for absolute/fixed positioning: HTML-to-PDF pipeline.
/// Verifies that positioned elements produce correct PDF output.
/// </summary>
public class PositionedE2ETests
{
    [Fact]
    public async Task AbsolutePosition_ProducesValidPdf()
    {
        var html = @"
            <div style='position: relative; width: 400px; height: 400px; background-color: #eee'>
                <div style='position: absolute; top: 50px; left: 50px; width: 100px; height: 100px; background-color: red'>
                    Absolute Box
                </div>
                <p>Normal flow content</p>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("Absolute Box");
        text.Should().Contain("Normal flow content");
    }

    [Fact]
    public async Task FixedPosition_ProducesValidPdf()
    {
        var html = @"
            <div style='position: fixed; top: 20px; left: 20px; width: 200px; height: 50px; background-color: #ccc'>
                Fixed Header
            </div>
            <div style='margin-top: 80px'>
                <p>Body content below fixed element</p>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("Fixed Header");
        text.Should().Contain("Body content below fixed element");
    }

    [Fact]
    public async Task OverlappingAbsolute_BothRender()
    {
        var html = @"
            <div style='position: relative; width: 400px; height: 400px'>
                <div style='position: absolute; top: 10px; left: 10px; width: 150px; height: 100px; background-color: rgba(255,0,0,0.5)'>
                    First
                </div>
                <div style='position: absolute; top: 50px; left: 50px; width: 150px; height: 100px; background-color: rgba(0,0,255,0.5)'>
                    Second
                </div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("First");
        text.Should().Contain("Second");
    }
}
