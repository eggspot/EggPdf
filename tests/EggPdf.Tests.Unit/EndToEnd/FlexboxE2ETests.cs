using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// End-to-end tests for flexbox layout: HTML-to-PDF pipeline.
/// Verifies that flex containers produce correct PDF output.
/// </summary>
public class FlexboxE2ETests
{
    [Fact]
    public async Task FlexRow_ProducesValidPdf()
    {
        var html = @"
            <div style='display: flex; flex-direction: row'>
                <div style='width: 100px; height: 50px; background-color: red'>A</div>
                <div style='width: 100px; height: 50px; background-color: blue'>B</div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("A");
        text.Should().Contain("B");
    }

    [Fact]
    public async Task FlexColumn_ProducesValidPdf()
    {
        var html = @"
            <div style='display: flex; flex-direction: column'>
                <div style='height: 50px; background-color: #eee'>Row 1</div>
                <div style='height: 50px; background-color: #ddd'>Row 2</div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Row 1");
        text.Should().Contain("Row 2");
    }

    [Fact]
    public async Task FlexGrow_ItemsExpandToFill()
    {
        var html = @"
            <div style='display: flex; width: 600px'>
                <div style='flex-grow: 1; background-color: #f00'>Grow 1</div>
                <div style='flex-grow: 2; background-color: #0f0'>Grow 2</div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Grow 1");
        text.Should().Contain("Grow 2");
    }

    [Fact]
    public async Task JustifyCenter_ProducesValidPdf()
    {
        var html = @"
            <div style='display: flex; justify-content: center; width: 600px'>
                <div style='width: 100px; height: 50px; background-color: #ccc'>Centered</div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Centered");
    }

    [Fact]
    public async Task JustifySpaceBetween_ProducesValidPdf()
    {
        var html = @"
            <div style='display: flex; justify-content: space-between; width: 600px'>
                <div style='width: 100px; background-color: #aaa'>Left</div>
                <div style='width: 100px; background-color: #bbb'>Right</div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Left");
        text.Should().Contain("Right");
    }

    [Fact]
    public async Task FlexWrap_MultiLine()
    {
        var html = @"
            <div style='display: flex; flex-wrap: wrap; width: 300px'>
                <div style='width: 150px; height: 40px; background-color: #eee'>Item 1</div>
                <div style='width: 150px; height: 40px; background-color: #ddd'>Item 2</div>
                <div style='width: 150px; height: 40px; background-color: #ccc'>Item 3</div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Item 1");
        text.Should().Contain("Item 2");
        text.Should().Contain("Item 3");
    }

    [Fact]
    public async Task FlexGap_ProducesValidPdf()
    {
        var html = @"
            <div style='display: flex; gap: 20px'>
                <div style='width: 100px; height: 50px; background-color: #f0f0f0'>A</div>
                <div style='width: 100px; height: 50px; background-color: #e0e0e0'>B</div>
                <div style='width: 100px; height: 50px; background-color: #d0d0d0'>C</div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("A");
        text.Should().Contain("B");
        text.Should().Contain("C");
    }

    [Fact]
    public async Task NestedFlex_ProducesValidPdf()
    {
        var html = @"
            <div style='display: flex'>
                <div style='display: flex; flex-direction: column; flex-grow: 1'>
                    <div>Nested 1</div>
                    <div>Nested 2</div>
                </div>
                <div style='flex-grow: 1'>Side</div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Nested 1");
        text.Should().Contain("Side");
    }

    [Fact]
    public async Task FlexDashboard_ComplexLayout()
    {
        // Real-world dashboard-like layout
        var html = @"
            <div style='display: flex; flex-direction: column; width: 500px'>
                <div style='display: flex; justify-content: space-between; background-color: #333; padding: 10px'>
                    <span style='color: white'>Dashboard</span>
                    <span style='color: white'>Logout</span>
                </div>
                <div style='display: flex; flex-grow: 1'>
                    <div style='width: 150px; background-color: #f0f0f0; padding: 10px'>
                        <div>Menu Item 1</div>
                        <div>Menu Item 2</div>
                    </div>
                    <div style='flex-grow: 1; padding: 10px'>
                        <h2>Content Area</h2>
                        <p>Main content goes here</p>
                    </div>
                </div>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Dashboard");
        text.Should().Contain("Menu Item 1");
        text.Should().Contain("Content Area");
    }
}
