using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// End-to-end tests for CSS Grid layout: HTML-to-PDF pipeline.
/// Verifies that grid containers produce correct PDF output.
/// </summary>
public class GridE2ETests
{
    [Fact]
    public async Task GridLayout_ProducesValidPdf()
    {
        var html = @"
            <div style='display: grid; grid-template-columns: 1fr 1fr'>
                <div style='background-color: #f00; height: 50px'>Cell 1</div>
                <div style='background-color: #0f0; height: 50px'>Cell 2</div>
                <div style='background-color: #00f; height: 50px'>Cell 3</div>
                <div style='background-color: #ff0; height: 50px'>Cell 4</div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("Cell 1");
        text.Should().Contain("Cell 2");
        text.Should().Contain("Cell 3");
        text.Should().Contain("Cell 4");
    }

    [Fact]
    public async Task GridDashboard_ComplexLayout()
    {
        var html = @"
            <div style='display: grid; grid-template-columns: 200px 1fr 1fr; gap: 10px; padding: 20px'>
                <div style='grid-column: 1 / 4; background-color: #333; color: white; padding: 10px; height: 60px'>
                    Dashboard Header
                </div>
                <div style='background-color: #eee; padding: 10px; height: 200px'>
                    Sidebar Navigation
                </div>
                <div style='grid-column: span 2; background-color: #f5f5f5; padding: 10px; height: 200px'>
                    Main Content Area
                </div>
                <div style='grid-column: 1 / 4; background-color: #333; color: white; padding: 10px; height: 40px'>
                    Footer
                </div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("Dashboard Header");
        text.Should().Contain("Sidebar Navigation");
        text.Should().Contain("Main Content Area");
        text.Should().Contain("Footer");
    }
}
