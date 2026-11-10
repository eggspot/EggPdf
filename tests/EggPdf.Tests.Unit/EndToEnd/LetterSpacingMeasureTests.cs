using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class LetterSpacingMeasureTests
{
    [Fact]
    public void MeasureWidth_WithLetterSpacing_AddsPerGlyphSpacing()
    {
        float baseWidth = TextMeasurer.MeasureWidth("ABCD", 16, "Arial", null, null);
        float spaced = TextMeasurer.MeasureWidth("ABCD", 16, "Arial", null, null, 2f);

        spaced.Should().BeApproximately(baseWidth + 8f, 0.01f,
            "measurement must include the letter-spacing the PDF Tc operator paints after every glyph, the last " +
            "included: 4 glyphs x 2px");
    }

    private static float FindTextX(byte[] pdf, string text)
    {
        var content = Encoding.ASCII.GetString(pdf);
        var m = Regex.Match(content, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(" + Regex.Escape(text) + @"\) Tj");
        m.Success.Should().BeTrue($"'{text}' should be in the content stream");
        return float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task LetterSpacedWords_SecondWordShiftsBySpacing()
    {
        var plain = await HtmlToPdf.RenderAsync(
            "<html><body><p>HELLO WORLD <b>X</b></p></body></html>");
        var spaced = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"letter-spacing:4px\">HELLO WORLD <b>X</b></p></body></html>");

        float plainGap = FindTextX(plain, " WORLD") - FindTextX(plain, "HELLO");
        float spacedGap = FindTextX(spaced, " WORLD") - FindTextX(spaced, "HELLO");

        (spacedGap - plainGap).Should().BeApproximately(15f, 2f,
            "with the <b> sibling forcing per-word boxes, the second word starts after the letter-spaced first one: " +
            "'HELLO' is 5 glyphs x 4px = 20px = 15pt wider, or the words overlap");
    }
}
