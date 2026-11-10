using System.Collections.Generic;
using System.Threading.Tasks;
using EggPdf;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfGlyphIdTests
{
    private const int GrinningFace = 0x1F600;

    [Fact]
    public void GetGlyphIds_SurrogatePair_YieldsOneGlyphInPlace()
    {
        var doc = new PdfDocument();
        doc.AddEmbeddedFont("F", new byte[] { 0x00 },
            new Dictionary<int, ushort> { ['A'] = 1, [GrinningFace] = 2, ['B'] = 3 },
            new ushort[] { 0, 500, 1000, 500 },
            unitsPerEm: 1000, ascent: 800, descent: -200);

        doc.GetGlyphIds("F", "A" + char.ConvertFromUtf32(GrinningFace) + "B").Should().Equal(new ushort[] { 1, 2, 3 },
            "a surrogate pair is one glyph; storing it at the low surrogate's index left a .notdef for the high " +
            "surrogate, an extra invisible glyph that misaligned every later character");
    }

    [Fact]
    public async Task Render_AsciiText_ProducesValidPdf()
    {
        var pdf = await HtmlToPdf.RenderAsync("<html><body><p>Hello ABC</p></body></html>");
        pdf.Length.Should().BeGreaterThan(100);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task Render_EmojiText_ProducesValidPdfWithoutException()
    {
        var pdf = await HtmlToPdf.RenderAsync("<html><body><p>A\U0001F600B</p></body></html>");
        pdf.Length.Should().BeGreaterThan(100);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task Render_MixedSurrogatePairs_ProducesValidPdf()
    {
        var html = "<html><body><p>\U0001F600\U0001F4C4\U0001F525 done</p></body></html>";
        var pdf = await HtmlToPdf.RenderAsync(html);
        pdf.Length.Should().BeGreaterThan(100, "several emoji in a row must not throw off the surrogate alignment");
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }
}
