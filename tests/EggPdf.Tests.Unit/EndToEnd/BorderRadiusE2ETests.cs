using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class BorderRadiusE2ETests
{
    private const string CurveTo = @"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c";

    [Fact]
    public async Task UniformBorderRadius_ProducesFill()
    {
        var html = "<div style='background-color: #eee; border: 1px solid #ccc; border-radius: 10px; width: 200px; height: 100px'>Rounded</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Rounded");
        text.Should().Contain("h f", "the background is a closed-path fill, which PDF.js draws reliably");
        text.Should().MatchRegex(CurveTo, "the border stroke draws its rounded corners with Bézier curves");
    }

    [Fact]
    public async Task BorderRadiusWithBorder_BothRounded()
    {
        var html = "<div style='background-color: #f0f0f0; border: 2px solid black; border-radius: 15px; width: 200px; height: 100px'>Both</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Both");
        text.Should().Contain("h f", "background should be filled with rounded path");
        text.Should().Contain("h S", "border should be stroked with rounded path");
    }

    [Fact]
    public async Task PerCornerRadius_ProducesValidPdf()
    {
        var html = @"<div style='background-color: #ddd; border: 1px solid #999;
            border-top-left-radius: 20px;
            border-top-right-radius: 10px;
            border-bottom-right-radius: 5px;
            border-bottom-left-radius: 0px;
            width: 200px; height: 100px'>Mixed corners</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Mixed corners");
        text.Should().MatchRegex(CurveTo, "the border stroke curves at each corner with a non-zero radius");
    }

    [Fact]
    public async Task ZeroBorderRadius_NoCurves()
    {
        var html = "<div style='background-color: #eee; border-radius: 0px; width: 200px; height: 100px'>No radius</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("No radius");
        text.Should().Contain("re f", "with zero radius the background is a plain rectangle, not a curved path");
        text.Should().NotMatchRegex(CurveTo, "nothing is rounded");
    }

    [Fact]
    public async Task CardWithRoundedCorners_ComplexLayout()
    {
        var html = @"
            <div style='background-color: white; border: 1px solid #ddd; border-radius: 8px;
                        width: 300px; padding: 20px'>
                <h2>Card Title</h2>
                <p>Card content with rounded corners looks professional</p>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Card Title");
        text.Should().Contain("Card content");
        text.Should().MatchRegex(CurveTo, "the card's 8px corners are rounded");
    }

    [Fact]
    public async Task PillShape_LargeRadius()
    {
        var html = "<div style='background-color: #007bff; border: 1px solid #0056b3; border-radius: 25px; width: 200px; height: 50px; color: white'>Button</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Button");
        text.Should().MatchRegex(CurveTo, "a 25px radius on a 50px-tall box makes pill-shaped ends");
    }

    [Fact]
    public async Task CircleShape_Equal50Percent()
    {
        var html = "<div style='background-color: red; border: 1px solid darkred; border-radius: 50px; width: 100px; height: 100px'>O</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        int curveCount = Regex.Matches(text, CurveTo).Count;
        curveCount.Should().BeGreaterOrEqualTo(4, "a 50px radius on a 100px box is a circle, which needs at least 4 corner curves");
    }
}
