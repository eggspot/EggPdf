using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class ReplacedElementLimitTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<html><body style='margin:0'>" + body + "</body></html>", 600, 800);

    [Fact]
    public void MaxWidthAndMinHeight_AreResolvedTogether()
    {
        var root = Layout("<svg id='s' viewBox='0 0 400 200' style='width:400px;max-width:100px;min-height:80px'></svg>");
        var svg = root.FindById("s")!;

        svg.Width.Should().BeApproximately(100, 0.5f, "max-width still holds when min-height grows the box");
        svg.Height.Should().BeApproximately(80, 0.5f);
    }

    [Fact]
    public void MaxWidthAndMaxHeight_UseTheTighterLimit()
    {
        var root = Layout("<svg id='s' viewBox='0 0 400 200' style='width:400px;max-width:200px;max-height:50px'></svg>");
        var svg = root.FindById("s")!;

        svg.Width.Should().BeApproximately(100, 0.5f);
        svg.Height.Should().BeApproximately(50, 0.5f);
    }

    [Fact]
    public void ReplacedFlexItem_KeepsItsPadding()
    {
        var root = Layout("<div style='display:flex'><svg id='s' viewBox='0 0 10 10' style='width:40px;height:40px;padding:5px'></svg></div>");

        root.FindById("s")!.Width.Should().BeApproximately(50, 0.5f);
    }

    [Fact]
    public void ReplacedElementWithBorderBoxSizing_KeepsItsOuterWidth()
    {
        var root = Layout("<svg id='s' viewBox='0 0 10 10' style='display:block;box-sizing:border-box;width:40px;height:40px;padding:5px'></svg>");

        var svg = root.FindById("s")!;
        svg.Width.Should().BeApproximately(40, 0.5f);
        svg.ContentWidth.Should().BeApproximately(30, 0.5f);
    }
}
