using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// The PDF gs operator persists until changed: after painting semi-transparent
/// content, opacity must be explicitly reset to 1, otherwise the alpha leaks
/// into every subsequent element (washed-out text bug).
/// </summary>
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

        // The reset must come AFTER the 50% state
        text.IndexOf("/GS100 gs").Should().BeGreaterThan(text.IndexOf("/GS50 gs"));
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
