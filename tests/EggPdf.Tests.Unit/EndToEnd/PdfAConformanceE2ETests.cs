using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class PdfAConformanceE2ETests
{
    [Fact]
    public void PdfA2b_EmbedsStandardFontInsteadOfReferencingIt()
    {
        var html = "<html><body><p style=\"font-family:Arial\">Hello World</p></body></html>";

        var plain = HtmlToPdf.Render(html);
        var plainText = PdfAssert.ValidPdf(plain, "Hello World");
        plainText.Should().Contain("/Subtype /Type1");
        plainText.Should().NotContain("/Subtype /CIDFontType2");

        // Embedded CIDFont text is painted as glyph-ID hex, not the literal string, so
        // only the structural checks (no expected visible text) apply here.
        var pdfA = HtmlToPdf.Render(html, PdfAConformance.PdfA2b);
        var pdfAText = PdfAssert.ValidPdf(pdfA);
        pdfAText.Should().Contain("/Subtype /CIDFontType2");
        pdfAText.Should().Contain("/OutputIntents [");
    }

    [Fact]
    public void PdfA3b_ProducesValidPdfWithConformanceMetadata()
    {
        var html = "<html><body><h1>Invoice</h1></body></html>";

        var pdf = HtmlToPdf.Render(html, PdfAConformance.PdfA3b);
        var text = PdfAssert.ValidPdf(pdf);
        text.Should().Contain("/Subtype /CIDFontType2");
        text.Should().Contain("<pdfaid:part>3</pdfaid:part>");
    }

    [Fact]
    public async System.Threading.Tasks.Task PdfA_RenderAsync_Works()
    {
        var pdf = await HtmlToPdf.RenderAsync("<h1>Async</h1>", PdfAConformance.PdfA2b);
        PdfAssert.ValidPdf(pdf).Should().Contain("/OutputIntents [");
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA2u, "2")]
    [InlineData(PdfAConformance.PdfA3u, "3")]
    public void PdfAu_EmbedsFontsWithToUnicodeCMap(PdfAConformance conformance, string expectedPart)
    {
        var html = "<html><body><p style=\"font-family:Arial\">Hello World</p></body></html>";

        var pdf = HtmlToPdf.Render(html, conformance);
        var text = PdfAssert.ValidPdf(pdf);

        // u-level's guarantee (correct Unicode text extraction) rides on the same forced
        // CIDFont embedding as b-level -- every embedded font always carries a ToUnicode CMap.
        text.Should().Contain("/Subtype /CIDFontType2");
        text.Should().Contain("/ToUnicode");
        text.Should().Contain($"<pdfaid:part>{expectedPart}</pdfaid:part>");
        text.Should().Contain("<pdfaid:conformance>U</pdfaid:conformance>");
    }
}
