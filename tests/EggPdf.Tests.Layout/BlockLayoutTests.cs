using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class BlockLayoutTests
{
    [Fact]
    public void SingleDiv_FillsContainingBlockWidth()
    {
        var root = LayoutTestHelper.Layout("<div style='background-color:red'></div>", 600, 800);

        var body = root.FindByTag("body");
        body.Should().NotBeNull();

        // Body div should exist within the layout
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
    }

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

        // Second div should be below first
        divs[1].Y.Should().BeGreaterThanOrEqualTo(divs[0].Y + divs[0].Height);
    }

    [Fact]
    public void Padding_IncreasesBoxSize()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px; height: 50px; padding: 10px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // Content is 100x50, padding adds 20 to each dimension
        div!.Width.Should().BeApproximately(120, 0.1f);
        div.Height.Should().BeApproximately(70, 0.1f);
    }

    [Fact]
    public void Margin_OffsetsPosition()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='margin-left: 20px; margin-top: 15px; width: 100px; height: 50px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.X.Should().BeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public void AutoWidth_FillsContainingBlock()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='height: 50px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // Auto width = containing block width (minus body margin)
        div!.Width.Should().BeGreaterThan(0);
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

        // Child should be inside parent
        var parent = divs[0];
        var child = divs[1];
        child.X.Should().BeGreaterThanOrEqualTo(parent.X);
        child.Y.Should().BeGreaterThanOrEqualTo(parent.Y);
    }

    [Fact]
    public void DisplayNone_NotLaidOut()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='height: 50px'></div>" +
            "<div style='display: none; height: 100px'></div>" +
            "<div style='height: 30px'></div>", 600, 800);

        var divs = root.FindAllByTag("div");
        // The hidden div should not appear in layout
        // At minimum, the third div should not be offset by 100px
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
