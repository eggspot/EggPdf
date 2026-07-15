using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// When @font-face declares multiple weights, intermediate font-weight values
/// (500, 600, 800...) must select and embed the matching face instead of
/// bucketing everything into regular/bold — otherwise semibold labels render
/// visibly thinner than the browser.
/// </summary>
public class FontWeightVariantTests
{
    [Fact]
    public async Task IntermediateWeight_EmbedsDistinctFontFace()
    {
        // Two local() faces under one family; weight 600 must get its own
        // embedded font (name carries the weight), separate from weight 400.
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

        text.Should().Contain("-W600", "weight 600 must produce its own font resource");
        text.Should().Contain("/FontFile2");
    }

    [Fact]
    public async Task IntermediateWeight_WithoutWebfonts_KeepsStandardNames()
    {
        // No @font-face: weight 600 stays in the classic bold bucket with
        // built-in fonts — no behavior change for plain documents.
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"font-weight:600\">plain semibold</p></body></html>");

        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
        text.Should().NotContain("-W600");
    }
}
