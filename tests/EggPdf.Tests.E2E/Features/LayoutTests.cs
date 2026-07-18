using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.E2E.Features;

/// <summary>
/// E2E tests for layout rendering through the HTTP API.
/// Covers: flexbox, grid, float, positioning, z-index, tables, lists.
/// </summary>
[Collection("E2E")]
public class LayoutTests
{
    private readonly ServiceFixture _fixture;
    private readonly HttpClient _client = new();

    public LayoutTests(ServiceFixture fixture) { _fixture = fixture; }

    private async Task<string> RenderPdf(string html)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        return PdfTextDecoder.DecodeWithText(bytes);
    }

    // === Flexbox ===

    [Fact]
    public async Task Flex_Row()
    {
        var text = await RenderPdf(@"
            <div style='display: flex; flex-direction: row'>
                <div style='width: 100px; height: 50px; background-color: red'>A</div>
                <div style='width: 100px; height: 50px; background-color: blue'>B</div>
            </div>");
        text.Should().Contain("A");
        text.Should().Contain("B");
    }

    [Fact]
    public async Task Flex_Column()
    {
        var text = await RenderPdf(@"
            <div style='display: flex; flex-direction: column'>
                <div style='height: 50px; background-color: #eee'>Row 1</div>
                <div style='height: 50px; background-color: #ddd'>Row 2</div>
            </div>");
        text.Should().Contain("Row 1");
        text.Should().Contain("Row 2");
    }

    [Fact]
    public async Task Flex_Grow()
    {
        var text = await RenderPdf(@"
            <div style='display: flex; width: 400px'>
                <div style='flex-grow: 1; background-color: #eee; height: 50px'>Grow 1</div>
                <div style='flex-grow: 2; background-color: #ddd; height: 50px'>Grow 2</div>
            </div>");
        text.Should().Contain("Grow 1");
        text.Should().Contain("Grow 2");
    }

    [Fact]
    public async Task Flex_JustifyContent_SpaceBetween()
    {
        var text = await RenderPdf(@"
            <div style='display: flex; justify-content: space-between; width: 400px'>
                <div style='width: 80px; height: 40px; background-color: red'>Left</div>
                <div style='width: 80px; height: 40px; background-color: blue'>Right</div>
            </div>");
        text.Should().Contain("Left");
        text.Should().Contain("Right");
    }

    [Fact]
    public async Task Flex_AlignItems_Center()
    {
        var text = await RenderPdf(@"
            <div style='display: flex; align-items: center; height: 200px'>
                <div style='width: 100px; height: 50px; background-color: green'>Centered</div>
            </div>");
        text.Should().Contain("Centered");
    }

    [Fact]
    public async Task Flex_Wrap()
    {
        var text = await RenderPdf(@"
            <div style='display: flex; flex-wrap: wrap; width: 200px'>
                <div style='width: 120px; height: 50px; background-color: #f00'>Item 1</div>
                <div style='width: 120px; height: 50px; background-color: #0f0'>Item 2</div>
            </div>");
        text.Should().Contain("Item 1");
        text.Should().Contain("Item 2");
    }

    [Fact]
    public async Task Flex_Gap()
    {
        var text = await RenderPdf(@"
            <div style='display: flex; gap: 20px'>
                <div style='width: 100px; height: 50px; background-color: #eee'>Gap A</div>
                <div style='width: 100px; height: 50px; background-color: #ddd'>Gap B</div>
            </div>");
        text.Should().Contain("Gap A");
        text.Should().Contain("Gap B");
    }

    [Fact]
    public async Task Flex_Nested()
    {
        var text = await RenderPdf(@"
            <div style='display: flex; flex-direction: column'>
                <div style='display: flex; flex-direction: row'>
                    <div style='width: 100px; height: 40px; background-color: #f00'>Nested A</div>
                    <div style='width: 100px; height: 40px; background-color: #0f0'>Nested B</div>
                </div>
                <div style='height: 40px; background-color: #00f'>Bottom</div>
            </div>");
        text.Should().Contain("Nested A");
        text.Should().Contain("Nested B");
        text.Should().Contain("Bottom");
    }

    // === Grid ===

    [Fact]
    public async Task Grid_BasicColumns()
    {
        var text = await RenderPdf(@"
            <div style='display: grid; grid-template-columns: 1fr 1fr'>
                <div style='background-color: #f00; height: 50px'>Cell 1</div>
                <div style='background-color: #0f0; height: 50px'>Cell 2</div>
                <div style='background-color: #00f; height: 50px'>Cell 3</div>
                <div style='background-color: #ff0; height: 50px'>Cell 4</div>
            </div>");
        text.Should().Contain("Cell 1");
        text.Should().Contain("Cell 2");
        text.Should().Contain("Cell 3");
        text.Should().Contain("Cell 4");
    }

    [Fact]
    public async Task Grid_Gap()
    {
        var text = await RenderPdf(@"
            <div style='display: grid; grid-template-columns: 1fr 1fr; gap: 10px'>
                <div style='background-color: #eee; height: 50px'>G1</div>
                <div style='background-color: #ddd; height: 50px'>G2</div>
            </div>");
        text.Should().Contain("G1");
        text.Should().Contain("G2");
    }

    [Fact]
    public async Task Grid_ColumnSpan()
    {
        var text = await RenderPdf(@"
            <div style='display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 10px'>
                <div style='grid-column: 1 / 4; background-color: #333; color: white; height: 50px'>Header</div>
                <div style='background-color: #eee; height: 100px'>Side</div>
                <div style='grid-column: span 2; background-color: #f5f5f5; height: 100px'>Main</div>
            </div>");
        text.Should().Contain("Header");
        text.Should().Contain("Side");
        text.Should().Contain("Main");
    }

    // === Float ===

    [Fact]
    public async Task Float_Left()
    {
        var text = await RenderPdf(@"
            <div style='float: left; width: 100px; height: 100px; background-color: red'>Floated</div>
            <p>Text wrapping around the floated element.</p>");
        text.Should().Contain("Floated");
        text.Should().Contain("Text wrapping");
    }

    [Fact]
    public async Task Float_Right()
    {
        var text = await RenderPdf(@"
            <div style='float: right; width: 100px; height: 100px; background-color: blue'>Right float</div>
            <p>Text on the left side.</p>");
        text.Should().Contain("Right float");
        text.Should().Contain("Text on the left");
    }

    [Fact]
    public async Task Float_MultipleFloats()
    {
        var text = await RenderPdf(@"
            <div style='float: left; width: 100px; height: 60px; background-color: red'>A</div>
            <div style='float: left; width: 100px; height: 60px; background-color: green'>B</div>
            <div style='float: right; width: 100px; height: 60px; background-color: blue'>C</div>
            <p>Content flows between the floats.</p>");
        text.Should().Contain("A");
        text.Should().Contain("B");
        text.Should().Contain("C");
        text.Should().Contain("Content flows");
    }

    [Fact]
    public async Task Clear_Both()
    {
        var text = await RenderPdf(@"
            <div style='float: left; width: 100px; height: 50px; background-color: red'>Float</div>
            <div style='clear: both'>Cleared</div>");
        text.Should().Contain("Float");
        text.Should().Contain("Cleared");
    }

    // === Positioning ===

    [Fact]
    public async Task Position_Relative()
    {
        var text = await RenderPdf(@"
            <div style='position: relative; top: 20px; left: 10px'>Relative</div>");
        text.Should().Contain("Relative");
    }

    [Fact]
    public async Task Position_Absolute()
    {
        var text = await RenderPdf(@"
            <div style='position: relative; width: 200px; height: 200px'>
                <div style='position: absolute; top: 10px; left: 10px; width: 50px; height: 50px; background: red'>Abs</div>
            </div>");
        text.Should().Contain("Abs");
    }

    [Fact]
    public async Task Position_Fixed()
    {
        var text = await RenderPdf(@"
            <div style='position: fixed; top: 0; left: 0; width: 100%; background: #333; color: white'>Fixed header</div>
            <p>Content below</p>");
        text.Should().Contain("Fixed header");
        text.Should().Contain("Content below");
    }

    [Fact]
    public async Task ZIndex_StackingOrder()
    {
        var text = await RenderPdf(@"
            <div style='position: relative; width: 200px; height: 200px'>
                <div style='position: absolute; z-index: 2; top: 10px; left: 10px; width: 50px; height: 50px; background: red'>Top</div>
                <div style='position: absolute; z-index: 1; top: 20px; left: 20px; width: 50px; height: 50px; background: blue'>Below</div>
            </div>");
        text.Should().Contain("Top");
        text.Should().Contain("Below");
    }

    // === Tables ===

    [Fact]
    public async Task Table_BasicWithHeaders()
    {
        var text = await RenderPdf(@"
            <table><thead><tr><th>Name</th><th>Value</th></tr></thead>
            <tbody><tr><td>Alpha</td><td>100</td></tr>
            <tr><td>Beta</td><td>200</td></tr></tbody></table>");
        text.Should().Contain("Name");
        text.Should().Contain("Value");
        text.Should().Contain("Alpha");
        text.Should().Contain("Beta");
    }

    [Fact]
    public async Task Table_VerticalAlignMiddle()
    {
        var text = await RenderPdf(@"<table style='height: 100px;'>
            <tr>
                <td style='vertical-align: middle; height: 100px;'>Center</td>
                <td style='vertical-align: bottom; height: 100px;'>Bottom</td>
            </tr>
        </table>");
        text.Should().Contain("Center");
        text.Should().Contain("Bottom");
    }

    [Fact]
    public async Task Table_StyledInvoice()
    {
        var text = await RenderPdf(@"<table style='width: 100%; border-collapse: collapse'>
            <tr><th style='border: 1px solid #ddd; padding: 8px; background-color: #4CAF50; color: white'>Item</th>
                <th style='border: 1px solid #ddd; padding: 8px; background-color: #4CAF50; color: white'>Price</th></tr>
            <tr><td style='border: 1px solid #ddd; padding: 8px'>Widget</td>
                <td style='border: 1px solid #ddd; padding: 8px'>$50</td></tr>
        </table>");
        text.Should().Contain("Item");
        text.Should().Contain("Widget");
        text.Should().Contain("$50");
    }

    // === Lists ===

    [Fact]
    public async Task UnorderedList_BulletsRendered()
    {
        var text = await RenderPdf("<ul><li>First item</li><li>Second item</li></ul>");
        text.Should().Contain("First item");
        text.Should().Contain("Second item");
    }

    [Fact]
    public async Task OrderedList_NumbersRendered()
    {
        var text = await RenderPdf("<ol><li>Step one</li><li>Step two</li></ol>");
        text.Should().Contain("Step one");
        text.Should().Contain("Step two");
        text.Should().Contain("1.");
        text.Should().Contain("2.");
    }

    [Fact]
    public async Task NestedLists()
    {
        var text = await RenderPdf(@"
            <ul>
                <li>Parent item
                    <ul><li>Child item</li></ul>
                </li>
            </ul>");
        text.Should().Contain("Parent item");
        text.Should().Contain("Child item");
    }

    // === Table with border-collapse: collapse ===

    [Fact]
    public async Task Table_BorderCollapse_CellContentPresent()
    {
        var text = await RenderPdf(@"<html><head><style>
            table { width: 100%; border-collapse: collapse; }
            th, td { border: 1px solid #ddd; padding: 10px; }
            th { background: #6c5ce7; color: white; }
        </style></head><body>
        <table><thead><tr><th>Item</th><th>Qty</th><th>Price</th><th>Total</th></tr></thead>
        <tbody><tr><td>Widget</td><td>40</td><td>$150</td><td>$6000</td></tr>
        <tr><td>Design</td><td>20</td><td>$120</td><td>$2400</td></tr></tbody></table>
        </body></html>");
        text.Should().Contain("Item");
        text.Should().Contain("Widget");
        text.Should().Contain("$6000");
        text.Should().Contain("Design");
        text.Should().Contain("$2400");
        // Table header should use purple background
        text.Should().Contain("0.42 0.36 0.91 rg");
        // Table header text should be white
        text.Should().Contain("1.00 1.00 1.00 rg");
    }

    [Fact]
    public async Task Table_WithExplicitWidths_ColumnsRendered()
    {
        var text = await RenderPdf(@"<table style='width: 100%; border-collapse: collapse'>
            <tr><td style='width: 50%; border: 1px solid #ddd'>Wide column</td>
                <td style='border: 1px solid #ddd'>Narrow column</td></tr>
        </table>");
        text.Should().Contain("Wide column");
        text.Should().Contain("Narrow column");
    }

    // === Overflow ===

    [Fact]
    public async Task OverflowHidden()
    {
        var text = await RenderPdf("<div style='overflow: hidden; width: 100px; height: 50px; background-color: #eee'>Clipped</div>");
        text.Should().Contain("Clipped");
    }

    // === Outline ===

    [Fact]
    public async Task Outline_RenderedAroundElement()
    {
        var text = await RenderPdf("<p style='outline: 2px solid blue'>Outlined</p>");
        text.Should().Contain("Outlined");
    }

    // === Inline Block ===

    [Fact]
    public async Task DisplayInlineBlock()
    {
        var text = await RenderPdf(@"
            <span style='display: inline-block; width: 100px; height: 50px; background-color: red'>Inline A</span>
            <span style='display: inline-block; width: 100px; height: 50px; background-color: blue'>Inline B</span>");
        text.Should().Contain("Inline A");
        text.Should().Contain("Inline B");
    }
}
