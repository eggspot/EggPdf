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

        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }
}
