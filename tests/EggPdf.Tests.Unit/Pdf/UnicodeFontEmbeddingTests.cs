using System.Threading.Tasks;
using EggPdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// Tests that text with codepoints outside WinAnsiEncoding (e.g. Vietnamese)
/// forces TrueType subset embedding instead of falling back to a non-embedded
/// Type1 font that can only encode Latin-1 (which degrades to '?').
/// </summary>
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
        // 'Arial' resolves to a "standard PDF font" name; embedding must still
        // kick in because the codepoints exceed WinAnsi.
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"font-family: 'Be Vietnam Pro', 'Segoe UI', Arial, sans-serif\">Giấy Phép Sử Dụng Âm Nhạc</p></body></html>");

        Latin1(pdf).Should().Contain("/FontFile2");
    }

    [Fact]
    public async Task Render_VietnameseText_SerifFamily_EmbedsTrueTypeFont()
    {
        // Times-Roman path (serif) must also embed when codepoints exceed WinAnsi.
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"font-family: 'Cormorant Garamond', serif\">Phụ lục Giấy phép</p></body></html>");

        Latin1(pdf).Should().Contain("/FontFile2");
    }

    [Fact]
    public async Task Render_AsciiOnlyText_KeepsBuiltinType1Font()
    {
        // Pure WinAnsi-encodable text should keep the lean non-embedded built-in
        // font path (no regression in output size).
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"font-family: Arial\">Plain ASCII text only</p></body></html>");

        Latin1(pdf).Should().NotContain("/FontFile2");
    }

    [Fact]
    public async Task Render_VietnameseText_NoQuestionMarksInContentStream()
    {
        // The WinAnsi fallback replaced every Vietnamese diacritic with '?'.
        // With embedding, content streams use glyph IDs (hex strings), so the
        // literal "(...?...)" degradation must be gone.
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>Đơn vị được cấp phép</p></body></html>");

        var text = Latin1(pdf);
        text.Should().NotContain("n v? ???c",
            "diacritics must not degrade to '?' in the content stream");
    }
}
