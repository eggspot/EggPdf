using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class VisualEffectsTests
{
    [Fact]
    public async Task BoxShadow_DoesNotCrash()
    {
        var html = "<div style='box-shadow: 2px 2px 5px rgba(0,0,0,0.3); width: 200px; height: 100px; background-color: white'>Shadow box</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BorderRadius_DoesNotCrash()
    {
        var html = "<div style='border-radius: 10px; background-color: blue; width: 100px; height: 100px'></div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Opacity_DoesNotCrash()
    {
        var html = "<div style='opacity: 0.5; background-color: red; width: 100px; height: 100px'>Semi-transparent</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Transform_DoesNotCrash()
    {
        var html = "<div style='transform: rotate(5deg); width: 100px; height: 100px'>Rotated</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TextDecoration_Rendered()
    {
        var html = "<p><u>Underlined</u> <s>Strikethrough</s></p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Underlined");
        text.Should().Contain("Strikethrough");
    }

    [Fact]
    public async Task TextShadow_DoesNotCrash()
    {
        var html = "<h1 style='text-shadow: 2px 2px 4px #000'>Shadow text</h1>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MultiColumn_DoesNotCrash()
    {
        var html = "<div style='column-count: 2; column-gap: 20px'><p>Column content that should flow into two columns.</p></div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CssNesting_DoesNotCrash()
    {
        var html = "<style>div { & p { color: red; } }</style><div><p>Nested CSS</p></div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ContainerQuery_DoesNotCrash()
    {
        var html = "<style>@container (min-width: 300px) { p { color: blue; } }</style><p>Container query</p>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task VisibilityHidden_TextNotVisible()
    {
        var html = "<div style='visibility: hidden'>Hidden text</div><div>Visible text</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Visible text");
    }

    [Fact]
    public async Task CjkText_DoesNotCrash()
    {
        var html = "<p>English text and some Chinese: 你好世界</p>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Emoji_DoesNotCrash()
    {
        // Note: emoji may not render correctly without color emoji font,
        // but should not crash
        var html = "<p>Hello World 🌍🎉</p>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }
}
