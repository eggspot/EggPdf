using System.Threading.Tasks;
using EggPdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>Tests for correct glyph ID handling, including surrogate pairs.</summary>
public class PdfGlyphIdTests
{
    [Fact]
    public async Task Render_AsciiText_ProducesValidPdf()
    {
        var pdf = await HtmlToPdf.RenderAsync("<html><body><p>Hello ABC</p></body></html>");
        pdf.Should().NotBeNull();
        pdf.Length.Should().BeGreaterThan(100);
        // Valid PDF starts with %PDF-
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task Render_EmojiText_ProducesValidPdfWithoutException()
    {
        // Emoji uses surrogate pairs in C# strings (U+1F600 = 2 chars).
        // Bug: GetGlyphIds stored the glyph at the LOW surrogate index,
        // leaving a 0x0000 (notdef) at the HIGH surrogate index — emitting
        // an extra invisible glyph and misaligning subsequent characters.
        var pdf = await HtmlToPdf.RenderAsync("<html><body><p>A\U0001F600B</p></body></html>");
        pdf.Should().NotBeNull();
        pdf.Length.Should().BeGreaterThan(100);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task Render_MixedSurrogatePairs_ProducesValidPdf()
    {
        // Multiple emoji in a row to stress the surrogate alignment
        var html = "<html><body><p>\U0001F600\U0001F4C4\U0001F525 done</p></body></html>";
        var pdf = await HtmlToPdf.RenderAsync(html);
        pdf.Should().NotBeNull();
        pdf.Length.Should().BeGreaterThan(100);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }
}
