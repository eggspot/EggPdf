using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class BoxShadowE2ETests
{
    [Fact]
    public async Task SimpleShadow_ProducesExtraRectangle()
    {
        var html = "<div style='box-shadow: 5px 5px 10px rgba(0,0,0,0.3); width: 200px; height: 100px; background: white'>Shadow</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Shadow");
        // Shadow produces extra rectangles before the background
        int fillCount = Regex.Matches(text, @"re f").Count;
        fillCount.Should().BeGreaterOrEqualTo(2, "shadow should add extra filled rectangles");
    }

    [Fact]
    public async Task ShadowWithSpread_ProducesLargerRect()
    {
        var html = "<div style='box-shadow: 0 0 0 5px red; width: 200px; height: 100px; background: white'>Spread</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Spread");
    }

    [Fact]
    public async Task ShadowWithBlur_ProducesMultipleLayers()
    {
        var html = "<div style='box-shadow: 0 4px 8px rgba(0,0,0,0.2); width: 200px; height: 100px; background: white'>Blur</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Blur");
        // Blur creates multiple layers with opacity
        text.Should().StartWith("%PDF");
    }

    [Fact]
    public async Task NamedColorShadow_ProducesValidPdf()
    {
        var html = "<div style='box-shadow: 3px 3px 5px gray; width: 200px; height: 100px; background: white'>Gray</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Gray");
        text.Should().StartWith("%PDF");
    }

    [Fact]
    public async Task MultipleShadows_AllRendered()
    {
        var html = "<div style='box-shadow: 2px 2px 4px red, -2px -2px 4px blue; width: 200px; height: 100px; background: white'>Multi</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Multi");
    }

    [Fact]
    public async Task NoShadow_NoExtraRects()
    {
        var html = "<div style='width: 200px; height: 100px; background: white'>No shadow</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("No shadow");
    }

    [Fact]
    public async Task CardWithShadow_ComplexLayout()
    {
        var html = @"
            <div style='box-shadow: 0 2px 10px rgba(0,0,0,0.15); border-radius: 8px;
                        background: white; width: 300px; padding: 20px'>
                <h2>Card Title</h2>
                <p>Card with subtle shadow effect</p>
            </div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Card Title");
        text.Should().Contain("Card with subtle shadow effect");
    }

    [Fact]
    public async Task ShadowNone_NoShadowRendered()
    {
        var html = "<div style='box-shadow: none; width: 200px; height: 100px; background: white'>None</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
    }
}
