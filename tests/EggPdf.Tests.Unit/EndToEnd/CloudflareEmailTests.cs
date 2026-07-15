using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Cloudflare email obfuscation (data-cfemail) is normally decoded by a
/// browser-side script. A PDF engine runs no JavaScript, so the DOM pass must
/// decode it directly — otherwise "[email protected]" ends up in the PDF.
/// </summary>
public class CloudflareEmailTests
{
    [Fact]
    public async Task DataCfEmail_IsDecodedToRealAddress()
    {
        // XOR key 0xAF -> "contact@vcrrm.org"
        var html = "<html><body><p>Email: " +
            "<a href=\"/cdn-cgi/l/email-protection\" class=\"__cf_email__\" " +
            "data-cfemail=\"afccc0c1dbceccdbefd9ccddddc281c0ddc8\">[email&#160;protected]</a>" +
            "</p></body></html>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

        text.Should().Contain("contact@vcrrm.org");
        text.Should().NotContain("email protected");
    }

    [Fact]
    public async Task InvalidCfEmail_DoesNotCrash()
    {
        var html = "<html><body><a data-cfemail=\"zz\">[email protected]</a>" +
                   "<a data-cfemail=\"af\">x</a></body></html>";

        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }
}
