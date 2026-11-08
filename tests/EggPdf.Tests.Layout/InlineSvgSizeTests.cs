using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class InlineSvgSizeTests
{
    private static LayoutBox Svg(string svgAttributes, string containerStyle = "")
    {
        var root = LayoutTestHelper.Layout("<html><body style='margin:0'><div style='" + containerStyle + "'><svg id='s' " + svgAttributes + "></svg></div></body></html>", 600, 800);
        return root.FindById("s")!;
    }

    [Theory]
    [InlineData("viewBox='0 0 100 50' width='200'", 200, 100)]
    [InlineData("viewBox='0 0 100 50' height='40'", 80, 40)]
    [InlineData("viewBox='0 0 100 50'", 300, 150)]
    [InlineData("viewBox='0 0 10 20'", 75, 150)]
    [InlineData("width='64' height='32'", 64, 32)]
    [InlineData("", 300, 150)]
    public void InlineSvg_ViewBoxAndSizeAttributes_DetermineTheBox(string attributes, float width, float height)
    {
        var svg = Svg(attributes);

        svg.Width.Should().BeApproximately(width, 0.5f);
        svg.Height.Should().BeApproximately(height, 0.5f);
    }

    [Fact]
    public void InlineSvgAsFlexItem_IsSizedFromItsViewBox()
    {
        var svg = Svg("viewBox='0 0 24 24' width='48'", "display:flex");

        svg.Width.Should().BeApproximately(48, 0.5f);
        svg.Height.Should().BeApproximately(48, 0.5f);
    }
}
