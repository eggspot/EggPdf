using System.Threading.Tasks;
using EggPdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class UnicodeFontEmbeddingTests
{
    private static string Latin1(byte[] pdf)
    {
#if NETFRAMEWORK || NETSTANDARD2_0
        return System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
#else
        return System.Text.Encoding.Latin1.GetString(pdf);
#endif
    }

    [Fact]
    public async Task Render_VietnameseText_DefaultFont_EmbedsTrueTypeFont()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>Hiệp hội Công nghiệp Ghi âm Việt Nam</p></body></html>");

        var text = Latin1(pdf);
        text.Should().Contain("/FontFile2",
            "Vietnamese text cannot be encoded in WinAnsi, so a TrueType subset must be embedded");
        text.Should().Contain("/Type0",
            "embedded Unicode fonts must use the CIDFont Type 2 (Type0) machinery");
    }

    [Fact]
    public async Task Render_VietnameseText_ArialFamily_EmbedsTrueTypeFont()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"font-family: 'Be Vietnam Pro', 'Segoe UI', Arial, sans-serif\">Giấy Phép Sử Dụng Âm Nhạc</p></body></html>");

        Latin1(pdf).Should().Contain("/FontFile2",
            "Arial resolves to a standard PDF font name, but the code points exceed WinAnsi, so a font is still embedded");
    }

    [Fact]
    public async Task Render_VietnameseText_SerifFamily_EmbedsTrueTypeFont()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"font-family: 'Cormorant Garamond', serif\">Phụ lục Giấy phép</p></body></html>");

        Latin1(pdf).Should().Contain("/FontFile2", "the Times-Roman (serif) path also embeds when the code points exceed WinAnsi");
    }

    [Fact]
    public async Task Render_VietnameseText_CourierFamily_EmbedsTrueTypeFont()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"font-family: 'Courier New', monospace\">Phiên bản 6</p></body></html>");

        Latin1(pdf).Should().Contain("/FontFile2",
            "Vietnamese text cannot be encoded in WinAnsi, so a TrueType subset must be embedded even for the Courier " +
            "family used by monospace fields next to Vietnamese labels, falling back to some embedded font on machines " +
            "without Courier New, Liberation Mono, DejaVu Sans Mono or Noto Sans Mono, never to '?'");
    }

    [Fact]
    public async Task Render_AsciiOnlyText_KeepsBuiltinType1Font()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"font-family: Arial\">Plain ASCII text only</p></body></html>");

        Latin1(pdf).Should().NotContain("/FontFile2",
            "WinAnsi-encodable text keeps the lean non-embedded built-in font, so the output doesn't grow");
    }

    [Fact]
    public async Task Render_VietnameseText_NoQuestionMarksInContentStream()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>Đơn vị được cấp phép</p></body></html>");

        var text = Latin1(pdf);
        text.Should().NotContain("n v? ???c",
            "with embedding the content stream uses glyph ids, so the diacritics no longer degrade to '?' as with the WinAnsi fallback");
    }
}
