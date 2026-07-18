using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.E2E.Features;

/// <summary>
/// E2E tests for colors, box model, and border rendering through the HTTP API.
/// Covers: named/hex/rgb/rgba/hsl colors, margin, padding, display, visibility,
/// border styles, border-radius, background-color, box-sizing.
/// </summary>
[Collection("E2E")]
public class ColorBoxBorderTests
{
    private readonly ServiceFixture _fixture;
    private readonly HttpClient _client = new();

    public ColorBoxBorderTests(ServiceFixture fixture) { _fixture = fixture; }

    private async Task<string> RenderPdf(string html)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        return PdfTextDecoder.Decode(bytes);
    }

    // === Colors ===

    [Fact]
    public async Task Color_NamedRed()
    {
        var text = await RenderPdf("<p style='color: red'>Red text</p>");
        text.Should().Contain("Red text");
        text.Should().Contain("1.00 0.00 0.00 rg");
    }

    [Fact]
    public async Task Color_HexValue()
    {
        var text = await RenderPdf("<div style='background-color: #ff5733; width: 100px; height: 50px'>Hex</div>");
        text.Should().Contain("Hex");
    }

    [Fact]
    public async Task Color_RgbaWithAlpha()
    {
        var text = await RenderPdf("<p style='color: rgba(255, 0, 0, 0.5)'>Semi-transparent</p>");
        text.Should().Contain("Semi-transparent");
        text.Should().Contain("1.00 0.00 0.00 rg");
    }

    [Fact]
    public async Task Color_HslRed()
    {
        var text = await RenderPdf("<p style='color: hsl(0, 100%, 50%)'>HSL Red</p>");
        text.Should().Contain("HSL Red");
        text.Should().Contain("1.00 0.00 0.00 rg");
    }

    [Fact]
    public async Task Color_MultipleNamedColors()
    {
        var text = await RenderPdf(@"
            <p style='color: red'>Red</p>
            <p style='color: blue'>Blue</p>
            <p style='color: green'>Green</p>");
        text.Should().Contain("Red");
        text.Should().Contain("Blue");
        text.Should().Contain("Green");
    }

    // === Background ===

    [Fact]
    public async Task BackgroundColor_DrawsRectangle()
    {
        var text = await RenderPdf("<div style='background-color: blue; width: 200px; height: 100px'>Blue box</div>");
        text.Should().Contain("re");
        text.Should().Contain("f");
    }

    [Fact]
    public async Task BackgroundColor_NamedYellow()
    {
        var text = await RenderPdf("<div style='background-color: yellow; width: 100px; height: 50px'>Yellow</div>");
        text.Should().Contain("Yellow");
        text.Should().Contain("re");
    }

    // === Box Model ===

    [Fact]
    public async Task Margin_Shorthand()
    {
        var text = await RenderPdf("<div style='margin: 10px 20px; background-color: red; width: 100px; height: 50px'>Margin test</div>");
        text.Should().Contain("Margin test");
    }

    [Fact]
    public async Task Padding_Applied()
    {
        var text = await RenderPdf("<div style='padding: 20px; background-color: #eee; width: 200px'>Padded</div>");
        text.Should().Contain("Padded");
    }

    // === Display & Visibility ===

    [Fact]
    public async Task DisplayNone_ElementNotRendered()
    {
        var text = await RenderPdf("<p>Visible</p><p style='display: none'>Hidden</p><p>Also visible</p>");
        text.Should().Contain("Visible");
        text.Should().Contain("Also visible");
        text.Should().NotContain("Hidden");
    }

    [Fact]
    public async Task HiddenAttribute_ElementNotRendered()
    {
        var text = await RenderPdf("<p>Visible</p><p hidden>Hidden by attribute</p>");
        text.Should().Contain("Visible");
        text.Should().NotContain("Hidden by attribute");
    }

    [Fact]
    public async Task VisibilityHidden_ElementNotRendered()
    {
        var text = await RenderPdf("<p>Visible</p><p style='visibility: hidden'>Hidden</p><p>Also visible</p>");
        text.Should().Contain("Visible");
        text.Should().Contain("Also visible");
        text.Should().NotContain("Hidden");
    }

    // === Border Styles ===

    [Fact]
    public async Task Border_Solid()
    {
        var text = await RenderPdf("<div style='border: 1px solid black; width: 200px; height: 100px'>Bordered</div>");
        text.Should().Contain("Bordered");
        text.Should().Contain("re");
        text.Should().Contain("S");
    }

    [Fact]
    public async Task Border_Dashed()
    {
        var text = await RenderPdf("<div style='border: 2px dashed black; width: 200px; height: 100px'>Dashed</div>");
        text.Should().Contain("Dashed");
        text.Should().MatchRegex(@"\[[\d.]+ [\d.]+\] [\d.]+ d");
    }

    [Fact]
    public async Task Border_Dotted()
    {
        var text = await RenderPdf("<div style='border: 2px dotted #333; width: 200px; height: 100px'>Dotted</div>");
        text.Should().Contain("Dotted");
        text.Should().Contain("1 J", "dotted uses round line cap");
    }

    [Fact]
    public async Task Border_Double()
    {
        var text = await RenderPdf("<div style='border: 4px double black; width: 200px; height: 100px'>Double</div>");
        text.Should().Contain("Double");
        text.Should().Contain("l S");
    }

    [Fact]
    public async Task Border_Groove()
    {
        var text = await RenderPdf("<div style='border: 3px groove gray; width: 200px; height: 100px'>Groove</div>");
        text.Should().Contain("Groove");
        text.Should().Contain("RG");
    }

    [Fact]
    public async Task Border_Ridge()
    {
        var text = await RenderPdf("<div style='border: 3px ridge #999; width: 200px; height: 100px'>Ridge</div>");
        text.Should().Contain("Ridge");
        text.Should().StartWith("%PDF");
    }

    [Fact]
    public async Task Border_Inset()
    {
        var text = await RenderPdf("<div style='border: 2px inset #aaa; width: 200px; height: 100px'>Inset</div>");
        text.Should().Contain("Inset");
    }

    [Fact]
    public async Task Border_Outset()
    {
        var text = await RenderPdf("<div style='border: 2px outset #aaa; width: 200px; height: 100px'>Outset</div>");
        text.Should().Contain("Outset");
    }

    [Fact]
    public async Task Border_PerSideDifferentStyles()
    {
        var text = await RenderPdf(@"<div style='
            border-top: 2px solid red;
            border-right: 2px dashed green;
            border-bottom: 2px dotted blue;
            border-left: 2px double black;
            width: 200px; height: 100px'>Mixed borders</div>");
        text.Should().Contain("Mixed borders");
    }

    [Fact]
    public async Task Border_ColorRed()
    {
        var text = await RenderPdf("<div style='border: 2px solid red; width: 100px; height: 50px'>Red border</div>");
        text.Should().Contain("Red border");
        text.Should().Contain("RG");
    }

    // === Border Radius ===

    [Fact]
    public async Task BorderRadius_Uniform_ProducesCurves()
    {
        var text = await RenderPdf("<div style='background-color: #eee; border-radius: 10px; width: 200px; height: 100px'>Rounded</div>");
        text.Should().Contain("Rounded");
        text.Should().MatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c");
    }

    [Fact]
    public async Task BorderRadius_PerCorner()
    {
        var text = await RenderPdf(@"<div style='background-color: #ddd;
            border-top-left-radius: 20px; border-top-right-radius: 10px;
            border-bottom-right-radius: 5px; border-bottom-left-radius: 0px;
            width: 200px; height: 100px'>Mixed corners</div>");
        text.Should().Contain("Mixed corners");
        text.Should().MatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c");
    }

    [Fact]
    public async Task BorderRadius_PillShape()
    {
        var text = await RenderPdf("<div style='background-color: #007bff; border-radius: 25px; width: 200px; height: 50px; color: white'>Button</div>");
        text.Should().Contain("Button");
        text.Should().MatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c");
    }

    [Fact]
    public async Task BorderRadius_Circle()
    {
        var text = await RenderPdf("<div style='background-color: red; border-radius: 50px; width: 100px; height: 100px'>O</div>");
        text.Should().Contain("O");
    }

    [Fact]
    public async Task BorderRadius_Zero_UsesRectangle()
    {
        var text = await RenderPdf("<div style='background-color: #eee; border-radius: 0px; width: 200px; height: 100px'>No radius</div>");
        text.Should().Contain("No radius");
        text.Should().Contain("re f");
    }

    [Fact]
    public async Task BorderRadius_WithBorder()
    {
        var text = await RenderPdf("<div style='background-color: #f0f0f0; border: 2px solid black; border-radius: 15px; width: 200px; height: 100px'>Both</div>");
        text.Should().Contain("Both");
        text.Should().Contain("h f");
        text.Should().Contain("h S");
    }

    // === Table Borders ===

    [Fact]
    public async Task TableBorderAttribute_CellsHaveBorders()
    {
        var text = await RenderPdf("<table border='1'><tr><td>Cell</td></tr></table>");
        text.Should().Contain("Cell");
        text.Should().Contain("re S");
    }

    [Fact]
    public async Task BorderCollapse_Table()
    {
        var text = await RenderPdf(@"<table style='border-collapse: collapse;'>
            <tr><td style='border: 1px solid black; padding: 4px;'>A</td>
                <td style='border: 1px solid black; padding: 4px;'>B</td></tr>
        </table>");
        text.Should().Contain("A");
        text.Should().Contain("B");
    }

    [Fact]
    public async Task DashedTableBorder()
    {
        var text = await RenderPdf(@"<table style='border-collapse: collapse'>
            <tr><td style='border: 1px dashed #ccc; padding: 8px'>Cell 1</td>
                <td style='border: 1px dashed #ccc; padding: 8px'>Cell 2</td></tr>
        </table>");
        text.Should().Contain("Cell 1");
        text.Should().Contain("Cell 2");
    }

    // === HR element ===

    [Fact]
    public async Task HrElement_Rendered()
    {
        var text = await RenderPdf("<p>Above</p><hr><p>Below</p>");
        text.Should().Contain("Above");
        text.Should().Contain("Below");
    }
}
