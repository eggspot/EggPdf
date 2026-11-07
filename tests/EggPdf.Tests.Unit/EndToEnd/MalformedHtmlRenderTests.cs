using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class MalformedHtmlRenderTests
{
    [Theory]
    [InlineData("&#xD800;")]
    [InlineData("&#99999999999;")]
    [InlineData("&#xFFFFFFFFFFFF;")]
    public async Task InvalidNumericReference_RendersPageWithoutThrowing(string reference)
    {
        var pdf = await HtmlToPdf.RenderAsync("<html><body><p>Before" + reference + "After</p><p>Next</p></body></html>");

        var content = Encoding.ASCII.GetString(pdf);
        content.Should().Contain("/Type /Page ");
        content.Should().EndWith("%%EOF\n");
    }
}
