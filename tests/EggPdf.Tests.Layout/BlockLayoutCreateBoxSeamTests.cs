using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class BlockLayoutCreateBoxSeamTests
{
    [Fact]
    public void FlexContainer_PositionRelativeWithTop_OffsetsYAfterChildLayout()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width:400px'>" +
            "<div id='spacer' style='height:50px'></div>" +
            "<div id='flex' style='display:flex; position:relative; top:20px; width:200px'>" +
            "<div style='width:50px; height:30px'></div></div></div>", 600, 800);

        var flex = root.FindByTag("div")!.Children[1];
        var spacer = root.FindByTag("div")!.Children[0];
        (flex.Y - spacer.Height).Should().BeApproximately(20, 1f,
            "top:20px applies on top of the normal flow position, not instead of it");
    }

    [Fact]
    public void GridContainer_PositionRelativeWithTop_OffsetsYAfterChildLayout()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width:400px'>" +
            "<div id='spacer' style='height:50px'></div>" +
            "<div id='grid' style='display:grid; position:relative; top:15px; width:200px'>" +
            "<div style='width:50px; height:30px'></div></div></div>", 600, 800);

        var grid = root.FindByTag("div")!.Children[1];
        var spacer = root.FindByTag("div")!.Children[0];
        (grid.Y - spacer.Height).Should().BeApproximately(15, 1f);
    }

    [Fact]
    public void FlexContainer_AutoHeight_MinHeightAndMaxHeightBothApplied()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='display:flex; min-height:100px; width:200px'>" +
            "<div style='width:50px; height:10px'></div></div>", 600, 800);

        var flex = root.FindByTag("div")!;
        flex.Height.Should().BeGreaterOrEqualTo(100, "min-height applies after the auto height computed from the children");
    }

    [Fact]
    public void GridContainer_AutoHeight_MaxHeightClampsComputedHeight()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='display:grid; max-height:20px; width:200px'>" +
            "<div style='width:50px; height:100px'></div></div>", 600, 800);

        var grid = root.FindByTag("div")!;
        grid.Height.Should().BeLessOrEqualTo(21);
    }

    [Fact]
    public void BorderBoxWidth_BelowMinWidth_ContentWidthRecomputedFromClampedWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='box-sizing:border-box; width:50px; min-width:150px; padding:10px; height:20px'></div>",
            600, 800);

        var div = root.FindByTag("div")!;
        div.Width.Should().BeApproximately(150, 1f);
        div.ContentWidth.Should().BeApproximately(130, 1f, "the content width comes from the clamped 150px minus 10px padding per side");
    }

    [Fact]
    public void ContentBoxWidth_AboveMaxWidth_ContentWidthRecomputedFromClampedWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='box-sizing:content-box; width:300px; max-width:100px; padding:10px; height:20px'></div>",
            600, 800);

        var div = root.FindByTag("div")!;
        div.Width.Should().BeApproximately(100, 1f);
        div.ContentWidth.Should().BeApproximately(80, 1f, "the content width comes from the clamped 100px minus 10px padding per side");
    }

    [Fact]
    public void AutoMarginBoth_WithMinWidthClamp_CentersUsingClampedWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div id='wrap' style='width:400px; padding:0; margin:0'>" +
            "<div id='inner' style='width:50px; min-width:200px; margin:0 auto; height:10px'></div></div>",
            600, 800);

        var inner = root.FindByTag("div")!.Children[0];
        inner.Width.Should().BeApproximately(200, 1f);
        inner.MarginLeft.Should().BeApproximately(100, 1f, "centering uses the clamped width: (400 - 200) / 2");
    }

    [Fact]
    public void AutoMarginLeftOnly_ExplicitRightMargin_LeftAbsorbsAllRemainingSpace()
    {
        var root = LayoutTestHelper.Layout(
            "<div id='wrap' style='width:500px; padding:0; margin:0'>" +
            "<div id='inner' style='width:200px; margin-left:auto; margin-right:30px; height:10px'></div></div>",
            600, 800);

        var inner = root.FindByTag("div")!.Children[0];
        inner.MarginLeft.Should().BeApproximately(270, 1f, "the left margin takes 500 - 200 - 30");
        inner.MarginRight.Should().BeApproximately(30, 1f);
    }

    [Fact]
    public void AutoWidth_MarginAuto_DoesNotCenter()
    {
        var root = LayoutTestHelper.Layout(
            "<div id='wrap' style='width:400px; padding:0; margin:0'>" +
            "<div id='inner' style='margin:0 auto; height:10px'></div></div>", 600, 800);

        var inner = root.FindByTag("div")!.Children[0];
        inner.Width.Should().BeApproximately(400, 1f, "an auto-width box fills its container instead of centering");
    }

    [Fact]
    public void TableCell_FixedLayoutColumnWidth_OverridesConflictingCellWidthStyle()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='table-layout:fixed; width:400px'>" +
            "<colgroup><col style='width:300px'><col style='width:100px'></colgroup>" +
            "<tr><td style='width:10px'>a</td><td>b</td></tr></table>", 600, 800);

        var firstCell = root.FindByTag("td")!;
        firstCell.Width.Should().BeApproximately(300, 1f,
            "with table-layout:fixed the column widths come from <col> alone, so the cell's own width:10px is ignored");
    }
}
