using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// letter-spacing is painted via the PDF Tc operator but must also be part of
/// text MEASUREMENT, otherwise word boxes are too narrow and consecutive words
/// overlap (the "QUÉ ĐỂXÁCTHỰC" bug in letter-spaced captions).
/// </summary>
public class LetterSpacingMeasureTests
{
    [Fact]
    public void MeasureWidth_WithLetterSpacing_AddsPerGlyphSpacing()
    {
        float baseWidth = TextMeasurer.MeasureWidth("ABCD", 16, "Arial", null, null);
        float spaced = TextMeasurer.MeasureWidth("ABCD", 16, "Arial", null, null, 2f);

        // PDF Tc applies after every glyph, including the last: 4 glyphs × 2px
        spaced.Should().BeApproximately(baseWidth + 8f, 0.01f);
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
        // The <b> sibling forces the mixed-inline path, which lays out per-word boxes
        var plain = await HtmlToPdf.RenderAsync(
            "<html><body><p>HELLO WORLD <b>X</b></p></body></html>");
        var spaced = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"letter-spacing:4px\">HELLO WORLD <b>X</b></p></body></html>");

        float plainGap = FindTextX(plain, " WORLD") - FindTextX(plain, "HELLO");
        float spacedGap = FindTextX(spaced, " WORLD") - FindTextX(spaced, "HELLO");

        // "HELLO" is 5 glyphs (the boundary space belongs to the next word's box);
        // 5 × 4px = 20px = 15pt wider gap (±2pt tolerance)
        (spacedGap - plainGap).Should().BeApproximately(15f, 2f,
            "the second word must start after the letter-spaced width of the first");
    }
}
