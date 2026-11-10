using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class FontWeightVariantTests
{
    [Fact]
    public async Task IntermediateWeight_EmbedsDistinctFontFace()
    {
        var html =
            "<html><head><style>" +
            "@font-face { font-family: T; font-weight: 400; src: local('Arial'); }" +
            "@font-face { font-family: T; font-weight: 600; src: local('Arial Bold'); }" +
            "p { font-family: T; }" +
            "</style></head><body>" +
            "<p>regular text</p>" +
            "<p style=\"font-weight:600\">semibold text</p>" +
            "</body></html>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

        text.Should().Contain("-W600",
            "with @font-face faces at 400 and 600, weight 600 embeds its own matching face instead of being bucketed " +
            "into regular or bold, or semibold labels render thinner than in the browser");
        text.Should().Contain("/FontFile2");
    }

    [Fact]
    public async Task IntermediateWeight_WithoutWebfonts_KeepsStandardNames()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"font-weight:600\">plain semibold</p></body></html>");

        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
        text.Should().NotContain("-W600", "without @font-face, weight 600 stays in the built-in bold bucket");
    }
}
