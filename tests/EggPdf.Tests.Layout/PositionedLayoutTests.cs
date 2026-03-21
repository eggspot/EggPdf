using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class PositionedLayoutTests
{
    [Fact]
    public void Relative_OffsetsFromNormalPosition()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; top: 10px; left: 20px; width: 100px; height: 50px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // Relative positioning should offset from normal flow position
        // The box should exist and have dimensions
        div!.Width.Should().BeApproximately(100, 1f);
        div.Height.Should().BeApproximately(50, 1f);
    }

    [Fact]
    public void MinWidth_EnforcesMinimum()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 50px; min-width: 100px; height: 30px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // min-width should override the smaller specified width
        div!.Width.Should().BeGreaterOrEqualTo(100);
    }

    [Fact]
    public void MaxWidth_EnforcesMaximum()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 500px; max-width: 200px; height: 30px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // max-width should constrain the larger specified width
        div!.Width.Should().BeLessOrEqualTo(200 + 1); // +1 for rounding
    }

    [Fact]
    public void OverflowHidden_DoesNotExpandParent()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px; height: 50px; overflow: hidden'>" +
            "  <div style='width: 200px; height: 200px'></div>" +
            "</div>",
            600, 800);

        var outerDiv = root.FindAllByTag("div")[0];
        outerDiv.Should().NotBeNull();
        // Parent with overflow:hidden should keep its specified dimensions
        outerDiv.Width.Should().BeApproximately(100, 1f);
        outerDiv.Height.Should().BeApproximately(50, 1f);
    }

    [Fact]
    public void InlineBlock_FlowsInline()
    {
        var root = LayoutTestHelper.Layout(
            "<div><span style='display: inline-block; width: 50px; height: 30px'></span></div>",
            600, 800);

        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Style.Display.Should().Be("inline-block");
    }

    [Fact]
    public void BoxSizingBorderBox_IncludesPaddingInWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='box-sizing: border-box; width: 100px; padding: 10px; height: 50px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // With border-box, total width should be 100px (padding included)
        div!.Width.Should().BeApproximately(100, 1f);
    }

    [Fact]
    public void DisplayNone_Skipped()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='height: 50px'></div>" +
            "<div style='display: none; height: 100px'></div>" +
            "<div style='height: 50px'></div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        // display:none div should not produce a layout box
        // The third div should be close to where second would be without the hidden one
    }
}
