using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class OpacityLeakTests
{
    [Fact]
    public async Task SemiTransparentBackground_ResetsOpacityAfterPainting()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"background:rgba(0,0,0,0.5);width:50px;height:20px\"></div>" +
            "<p>after text</p></body></html>");

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/GS50 gs", "the rgba(…,0.5) background sets 50% alpha");
        text.Should().Contain("/GS100 gs", "opacity must be reset to 1 after the transparent fill");
        text.IndexOf("/GS100 gs").Should().BeGreaterThan(text.IndexOf("/GS50 gs"),
            "the gs state persists until changed, so the reset must follow the 50% fill or later text is washed out");
    }

    [Fact]
    public async Task SemiTransparentText_ResetsOpacityAfterPainting()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"color:rgba(0,0,0,0.4)\">faded</p><p>solid</p></body></html>");

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/GS40 gs");
        text.Should().Contain("/GS100 gs", "text alpha must not leak into the next paragraph");
    }
}
