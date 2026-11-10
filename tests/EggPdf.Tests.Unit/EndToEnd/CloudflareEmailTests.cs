using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class CloudflareEmailTests
{
    private static readonly string NoBreakSpace = ((char)0xA0).ToString();

    [Fact]
    public async Task DataCfEmail_IsDecodedToRealAddress()
    {
        var html = "<html><body><p>Email: " +
            "<a href=\"/cdn-cgi/l/email-protection\" class=\"__cf_email__\" " +
            "data-cfemail=\"afccc0c1dbceccdbefd9ccddddc281c0ddc8\">[email&#160;protected]</a>" +
            "</p></body></html>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

        text.Should().Contain("contact@vcrrm.org",
            "a PDF engine runs no script, so the DOM pass decodes data-cfemail (XOR key 0xAF) itself");
        text.Should().NotContain("email" + NoBreakSpace + "protected", "the obfuscated placeholder text is replaced");
    }

    [Fact]
    public async Task InvalidCfEmail_LeavesOriginalLinkTextUnchanged()
    {
        var html = "<html><body><a data-cfemail=\"zz\">[email protected]</a>" +
                   "<a data-cfemail=\"af\">x</a></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        PdfAssert.ValidPdf(pdf, "([email) Tj", "protected]) Tj", "(x) Tj");
    }
}
