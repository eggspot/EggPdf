using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Checkbox/radio/select are painted as real vector shapes (bordered square, circle,
/// dropdown arrow) rather than injected Unicode glyph text -- see
/// BoxPainter.PaintCheckboxOrRadio / PaintSelectDropdownArrow.
/// </summary>
public class FormControlsTests
{
    [Fact]
    public async Task Checkbox_Unchecked_SizedAsThirteenPixelSquare()
    {
        var html = "<input type='checkbox'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        // 13px * 0.75 (px->pt) = 9.75pt, used for both the stroked border rect's width and height
        text.Should().Contain("9.75", "an unchecked checkbox must be a fixed 13x13px native-style square, not sized by glyph text");
    }

    [Fact]
    public async Task Checkbox_Checked_FillsWithAccentColor()
    {
        var html = "<input type='checkbox' checked style='accent-color: purple'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        // purple = #800080 = (128,0,128) -> 128/255 = 0.50
        text.Should().Contain("0.50 0.00 0.50 rg", "a checked checkbox must fill its box with the accent color");
    }

    [Fact]
    public async Task Checkbox_Checked_DrawsWhiteCheckmark()
    {
        var html = "<input type='checkbox' checked>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("1.00 1.00 1.00 RG", "the checkmark drawn over a checked checkbox must stroke in white");
    }

    [Fact]
    public async Task Checkbox_Unchecked_DoesNotFillAccentColor()
    {
        var html = "<input type='checkbox' style='accent-color: purple'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().NotContain("0.50 0.00 0.50 rg", "an unchecked checkbox must not be filled with the accent color");
    }

    [Fact]
    public async Task Checkbox_AppearanceNone_SuppressesNativeRendering()
    {
        var html = "<input type='checkbox' checked style='appearance: none; accent-color: purple'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().NotContain("0.50 0.00 0.50 rg", "appearance:none must suppress the native checked-fill rendering");
    }

    [Fact]
    public async Task Radio_Checked_DrawsInnerAccentDot()
    {
        var html = "<input type='radio' checked style='accent-color: #00aa00'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        // #00aa00 -> (0, 170, 0) -> 170/255 = 0.67
        text.Should().Contain("0.00 0.67 0.00 rg", "a checked radio button must draw an accent-colored inner dot");
    }

    [Fact]
    public async Task Radio_Unchecked_DoesNotDrawInnerDot()
    {
        var html = "<input type='radio' style='accent-color: #00aa00'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().NotContain("0.00 0.67 0.00 rg", "an unchecked radio button must not draw the accent-colored dot");
    }

    [Fact]
    public async Task Radio_HasCircularBorder_ViaFiftyPercentRadius()
    {
        var html = "<input type='radio'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        // A circular border is drawn as a rounded-rect stroke with Bezier "c" curve operators,
        // not a plain "re" rectangle.
        text.Should().Contain(" c\n", "a radio button's border must be rendered as a circle (border-radius: 50%), not a square");
    }

    [Fact]
    public async Task Select_RendersDropdownArrow()
    {
        var html = "<select style='width: 120px; height: 24px'><option selected>A</option></select>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("0.35 0.35 0.35 rg", "a <select> box must paint a dropdown arrow indicator");
    }

    [Fact]
    public async Task Select_AppearanceNone_SuppressesDropdownArrow()
    {
        var html = "<select style='width: 120px; height: 24px; appearance: none'><option selected>A</option></select>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().NotContain("0.35 0.35 0.35 rg", "appearance:none must suppress the native dropdown arrow");
    }
}
