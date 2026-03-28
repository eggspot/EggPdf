using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// End-to-end tests for border-radius: HTML-to-PDF pipeline.
/// Verifies that rounded corners produce Bézier curve operators in PDF.
/// </summary>
public class BorderRadiusE2ETests
{
    [Fact]
    public async Task UniformBorderRadius_ProducesCurves()
    {
        var html = "<div style='background-color: #eee; border-radius: 10px; width: 200px; height: 100px'>Rounded</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Rounded");
        // Bézier curve operator should be present for rounded corners
        text.Should().MatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c",
            "border-radius should produce Bézier curve operators");
        text.Should().Contain("h f", "rounded rect path should be closed and filled");
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
        var html = @"<div style='background-color: #ddd;
            border-top-left-radius: 20px;
            border-top-right-radius: 10px;
            border-bottom-right-radius: 5px;
            border-bottom-left-radius: 0px;
            width: 200px; height: 100px'>Mixed corners</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Mixed corners");
        // Should have curves for corners with non-zero radii
        text.Should().MatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c");
    }

    [Fact]
    public async Task ZeroBorderRadius_NoCurves()
    {
        var html = "<div style='background-color: #eee; border-radius: 0px; width: 200px; height: 100px'>No radius</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("No radius");
        // With zero radius, should use regular rectangle (re operator) not curves
        text.Should().Contain("re f", "zero radius should use regular rectangle");
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
    }

    [Fact]
    public async Task PillShape_LargeRadius()
    {
        // Pill shape: radius is half the height
        var html = "<div style='background-color: #007bff; border-radius: 25px; width: 200px; height: 50px; color: white'>Button</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Button");
        text.Should().MatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c");
    }

    [Fact]
    public async Task CircleShape_Equal50Percent()
    {
        // Circle: equal width/height with 50% radius
        var html = "<div style='background-color: red; border-radius: 50px; width: 100px; height: 100px'>O</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        // Should produce curves (radius clamped to 50 = half of 100)
        int curveCount = Regex.Matches(text, @"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c").Count;
        curveCount.Should().BeGreaterOrEqualTo(4, "circle needs at least 4 corner curves");
    }
}
