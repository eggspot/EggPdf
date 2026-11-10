using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class BlockLayoutTests
{
    private const float BodyMargin = 8f;

    [Fact]
    public void FixedWidthHeight_CorrectDimensions()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 200px; height: 100px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(200, 0.1f);
        div.Height.Should().BeApproximately(100, 0.1f);
    }

    [Fact]
    public void TwoDivs_StackVertically()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='height: 50px'></div>" +
            "<div style='height: 30px'></div>", 600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(2);

        divs[1].Y.Should().BeGreaterThanOrEqualTo(divs[0].Y + divs[0].Height, "the second block starts below the first");
    }

    [Fact]
    public void Padding_IncreasesBoxSize()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px; height: 50px; padding: 10px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(120, 0.1f, "10px padding on each side adds 20px to the 100px content width");
        div.Height.Should().BeApproximately(70, 0.1f, "10px padding on each side adds 20px to the 50px content height");
    }

    [Fact]
    public void Margin_OffsetsPosition()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='margin-left: 20px; margin-top: 15px; width: 100px; height: 50px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.X.Should().BeApproximately(BodyMargin + 20, 0.5f, "margin-left: 20px inside the body's 8px margin");
    }

    [Fact]
    public void AutoWidth_FillsContainingBlock()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='height: 50px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(600 - 2 * BodyMargin, 0.5f,
            "an auto-width block fills the 600px page less the body's margins");
    }

    [Fact]
    public void NestedDivs_ChildInsideParent()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 300px; padding: 10px'>" +
            "  <div style='height: 50px'></div>" +
            "</div>", 600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(2);

        var parent = divs[0];
        var child = divs[1];
        child.X.Should().BeGreaterThanOrEqualTo(parent.X, "the child is inside its parent");
        child.Y.Should().BeGreaterThanOrEqualTo(parent.Y, "the child is inside its parent");
    }

    [Fact]
    public void TextContent_HasHeight()
    {
        var root = LayoutTestHelper.Layout("<p>Hello World</p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Height.Should().BeGreaterThan(0);
    }
}
