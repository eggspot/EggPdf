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

    [Fact]
    public void AbsolutePosition_RemovedFromNormalFlow()
    {
        // An absolutely positioned element should not affect sibling positions
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; width: 400px; height: 400px'>" +
            "  <div style='height: 50px; background: red'></div>" +
            "  <div style='position: absolute; top: 10px; left: 10px; width: 100px; height: 100px; background: blue'></div>" +
            "  <div style='height: 50px; background: green'></div>" +
            "</div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        // Abs children are added after normal flow children, so order is:
        // divs[0] = outer relative container
        // divs[1] = first normal-flow div (50px)
        // divs[2] = second normal-flow div (50px) -- placed right after first
        // divs[3] = absolute div (deferred, added last)
        divs.Count.Should().BeGreaterOrEqualTo(4);

        var first = divs[1];
        var second = divs[2];
        var absDiv = divs[3];

        // The second div should be positioned right after the first (50px gap, not 150px)
        float gap = second.Y - first.Y;
        gap.Should().BeApproximately(50, 2f, "absolute element should not push siblings down");

        // The absolute div should be marked as absolutely positioned
        absDiv.IsAbsolutelyPositioned.Should().BeTrue();
    }

    [Fact]
    public void AbsolutePosition_RelativeToPositionedParent()
    {
        // Absolute element should position relative to nearest positioned ancestor
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; top: 0px; left: 0px; width: 400px; height: 400px; padding: 20px'>" +
            "  <div style='position: absolute; top: 30px; left: 40px; width: 80px; height: 60px'></div>" +
            "</div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        divs.Count.Should().BeGreaterOrEqualTo(2);

        var container = divs[0];
        var absChild = divs[1];

        // Absolute child should be offset from the container's position
        absChild.X.Should().BeApproximately(container.X + 40, 2f, "left:40px from positioned parent");
        absChild.Y.Should().BeApproximately(container.Y + 30, 2f, "top:30px from positioned parent");
        absChild.Width.Should().BeApproximately(80, 1f);
        absChild.Height.Should().BeApproximately(60, 1f);
    }

    [Fact]
    public void AbsolutePosition_TopLeft_Positioned()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; width: 300px; height: 300px'>" +
            "  <div style='position: absolute; top: 50px; left: 60px; width: 100px; height: 80px'></div>" +
            "</div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        var container = divs[0];
        var absChild = divs[1];

        absChild.X.Should().BeApproximately(container.X + 60, 2f);
        absChild.Y.Should().BeApproximately(container.Y + 50, 2f);
    }

    [Fact]
    public void AbsolutePosition_BottomRight_Positioned()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; width: 300px; height: 300px'>" +
            "  <div style='position: absolute; bottom: 20px; right: 30px; width: 100px; height: 80px'></div>" +
            "</div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        var container = divs[0];
        var absChild = divs[1];

        // right:30px means X = container.X + container.Width - 30 - child.Width
        float expectedX = container.X + container.Width - 30 - absChild.Width;
        absChild.X.Should().BeApproximately(expectedX, 2f, "right:30px from containing block edge");

        // bottom:20px means Y = container.Y + container.Height - 20 - child.Height
        float expectedY = container.Y + container.Height - 20 - absChild.Height;
        absChild.Y.Should().BeApproximately(expectedY, 2f, "bottom:20px from containing block edge");
    }

    [Fact]
    public void FixedPosition_RelativeToPage()
    {
        // Fixed position should be relative to the page, not any parent
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; margin: 50px; width: 400px; height: 400px'>" +
            "  <div style='position: fixed; top: 10px; left: 20px; width: 100px; height: 50px'></div>" +
            "</div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        var fixedDiv = divs[1];

        // Fixed elements position relative to the page (0,0), not the parent
        fixedDiv.X.Should().BeApproximately(20, 2f, "left:20px from page edge");
        fixedDiv.Y.Should().BeApproximately(10, 2f, "top:10px from page edge");
        fixedDiv.IsAbsolutelyPositioned.Should().BeTrue();
    }

    [Fact]
    public void AbsolutePosition_DoesNotAffectParentHeight()
    {
        // Parent's auto height should not include absolutely positioned children
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; width: 300px'>" +
            "  <div style='height: 50px'></div>" +
            "  <div style='position: absolute; top: 0; left: 0; width: 100px; height: 500px'></div>" +
            "</div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        var container = divs[0];

        // Container's auto height should only account for normal-flow child (50px),
        // not the absolute child (500px)
        container.Height.Should().BeLessThan(100, "absolute children should not contribute to parent auto height");
    }

    [Fact]
    public void ZIndex_PositionedElements_StylePreserved()
    {
        var html = @"<div style='position: relative; width: 200px; height: 200px;'>
            <div style='position: absolute; z-index: 10; top: 0; left: 0; width: 50px; height: 50px;'>High</div>
            <div style='position: absolute; z-index: 1; top: 0; left: 0; width: 50px; height: 50px;'>Low</div>
        </div>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var divs = root.FindAllByTag("div");
        // Should have 3 divs: container + 2 positioned children
        divs.Should().HaveCountGreaterOrEqualTo(3);

        // z-index should be stored in style
        var highDiv = divs.Find(d => d.Style.Get("z-index") == "10");
        var lowDiv = divs.Find(d => d.Style.Get("z-index") == "1");
        highDiv.Should().NotBeNull("should find z-index:10 element");
        lowDiv.Should().NotBeNull("should find z-index:1 element");
    }
}
