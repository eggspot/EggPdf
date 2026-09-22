using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class ArabicShapingE2ETests
{
    [Fact]
    public async Task ArabicParagraph_RendersValidPdf()
    {
        var html = "<html><body dir='rtl' style='font-family: Arial'>" +
            "<p>كتاب بلا لا مرحبا بالعالم</p>" +
            "</body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("/Type /Page");
    }

    [Fact]
    public async Task MixedArabicAndLatin_RendersValidPdf()
    {
        var html = "<html><body><p>Invoice فاتورة #123 مدفوعة</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf);

        // The mixed-script line is emitted as glyph-id text (a literal "Invoice" string never appears).
        text.Should().MatchRegex(@"BT [^\n]*(> Tj|\] TJ)[^\n]* ET", "the mixed line is painted as glyph-id text");
        PdfAssert.PageCount(text).Should().Be(1);
        if (System.IO.File.Exists(System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts), "arial.ttf")))
            text.Should().Contain("-CXA", "Arabic runs use the shaped-script font key when a covering font is installed");
    }
}
