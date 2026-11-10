using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class PositionedLayoutTests
{
    private const float RoundingTolerance = 1f;

    [Fact]
    public void Relative_KeepsItsOwnSize()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; top: 10px; left: 20px; width: 100px; height: 50px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(100, 1f, "a relative offset moves the box without resizing it");
        div.Height.Should().BeApproximately(50, 1f, "a relative offset moves the box without resizing it");
    }

    [Fact]
    public void MinWidth_EnforcesMinimum()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 50px; min-width: 100px; height: 30px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeGreaterOrEqualTo(100, "min-width overrides the smaller specified width");
    }

    [Fact]
    public void MaxWidth_EnforcesMaximum()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 500px; max-width: 200px; height: 30px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeLessOrEqualTo(200 + RoundingTolerance, "max-width constrains the larger specified width");
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
        outerDiv.Width.Should().BeApproximately(100, 1f, "an overflow:hidden parent keeps its specified width");
        outerDiv.Height.Should().BeApproximately(50, 1f, "an overflow:hidden parent keeps its specified height");
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
        div!.Width.Should().BeApproximately(100, 1f, "with border-box the 100px width already includes the padding");
    }

    [Fact]
    public void DisplayNone_ProducesNoBoxAndTakesNoSpace()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='height: 50px'></div>" +
            "<div style='display: none; height: 100px'></div>" +
            "<div style='height: 50px'></div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCount(2, "a display:none element produces no layout box");
        (divs[1].Y - divs[0].Y).Should().BeApproximately(50, 1f,
            "the third div sits right after the first, as if the hidden one were not there");
    }

    [Fact]
    public void AbsolutePosition_RemovedFromNormalFlow()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; width: 400px; height: 400px'>" +
            "  <div id='first' style='height: 50px; background: red'></div>" +
            "  <div id='abs' style='position: absolute; top: 10px; left: 10px; width: 100px; height: 100px; background: blue'></div>" +
            "  <div id='second' style='height: 50px; background: green'></div>" +
            "</div>",
            600, 800);

        var first = root.FindById("first")!;
        var second = root.FindById("second")!;
        var absDiv = root.FindById("abs")!;

        (second.Y - first.Y).Should().BeApproximately(50, 2f, "an absolute element must not push its siblings down");
        absDiv.IsAbsolutelyPositioned.Should().BeTrue();
    }

    [Fact]
    public void AbsolutePosition_RelativeToPositionedParent()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; top: 0px; left: 0px; width: 400px; height: 400px; padding: 20px'>" +
            "  <div style='position: absolute; top: 30px; left: 40px; width: 80px; height: 60px'></div>" +
            "</div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCount(2);

        var container = divs[0];
        var absChild = divs[1];

        absChild.X.Should().BeApproximately(container.X + 40, 2f, "left:40px from the nearest positioned ancestor");
        absChild.Y.Should().BeApproximately(container.Y + 30, 2f, "top:30px from the nearest positioned ancestor");
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

        float expectedX = container.X + container.Width - 30 - absChild.Width;
        absChild.X.Should().BeApproximately(expectedX, 2f, "right:30px from the containing block's right edge");

        float expectedY = container.Y + container.Height - 20 - absChild.Height;
        absChild.Y.Should().BeApproximately(expectedY, 2f, "bottom:20px from the containing block's bottom edge");
    }

    [Fact]
    public void FixedPosition_RelativeToPage()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; margin: 50px; width: 400px; height: 400px'>" +
            "  <div style='position: fixed; top: 10px; left: 20px; width: 100px; height: 50px'></div>" +
            "</div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        var fixedDiv = divs[1];

        fixedDiv.X.Should().BeApproximately(20, 2f, "a fixed element's left:20px is from the page edge, not its parent");
        fixedDiv.Y.Should().BeApproximately(10, 2f, "a fixed element's top:10px is from the page edge, not its parent");
        fixedDiv.IsAbsolutelyPositioned.Should().BeTrue();
    }

    [Fact]
    public void AbsolutePosition_DoesNotAffectParentHeight()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='position: relative; width: 300px'>" +
            "  <div style='height: 50px'></div>" +
            "  <div style='position: absolute; top: 0; left: 0; width: 100px; height: 500px'></div>" +
            "</div>",
            600, 800);

        var container = root.FindAllByTag("div")[0];

        container.Height.Should().BeLessThan(100,
            "the auto height counts only the 50px normal-flow child, not the 500px absolute one");
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
        divs.Should().HaveCount(3, "the container and its two positioned children");

        var highDiv = divs.Find(d => d.Style.Get("z-index") == "10");
        var lowDiv = divs.Find(d => d.Style.Get("z-index") == "1");
        highDiv.Should().NotBeNull("the z-index:10 element keeps its z-index in the computed style");
        lowDiv.Should().NotBeNull("the z-index:1 element keeps its z-index in the computed style");
    }
}
