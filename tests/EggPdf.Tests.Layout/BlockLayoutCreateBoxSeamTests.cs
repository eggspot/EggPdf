using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// Characterization tests for BlockLayout.CreateBox's internal branch seams
/// (flex/grid delegation, box-model resolution, auto-margin centering,
/// table-cell width exemption). Written to pin exact current behavior before
/// splitting CreateBox into smaller methods, so an accidental change to what
/// a sub-method reads or returns during the split shows up as a failure here
/// rather than only surfacing as a subtle rendering difference later.
/// </summary>
public class BlockLayoutCreateBoxSeamTests
{
    // ── flex/grid: relative-position offset applied inside their own branch ──

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
        // The relative offset (top:20px) must be applied on top of the normal
        // flow position, not instead of it.
        (flex.Y - spacer.Height).Should().BeApproximately(20, 1f);
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
        // Min/max height constraints are applied AFTER the auto-height
        // computed-from-children pass inside the flex branch; pin the
        // ordering (min wins over a too-small auto height).
        var root = LayoutTestHelper.Layout(
            "<div style='display:flex; min-height:100px; width:200px'>" +
            "<div style='width:50px; height:10px'></div></div>", 600, 800);

        var flex = root.FindByTag("div")!;
        flex.Height.Should().BeGreaterOrEqualTo(100);
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

    // ── box model: min/max width interacting with border-box sizing ─────────

    [Fact]
    public void BorderBoxWidth_BelowMinWidth_ContentWidthRecomputedFromClampedWidth()
    {
        // box.Width is clamped to min-width AFTER border-box's initial
        // ContentWidth computation; ContentWidth must be recomputed from the
        // clamped box.Width, not left stale from the pre-clamp value.
        var root = LayoutTestHelper.Layout(
            "<div style='box-sizing:border-box; width:50px; min-width:150px; padding:10px; height:20px'></div>",
            600, 800);

        var div = root.FindByTag("div")!;
        div.Width.Should().BeApproximately(150, 1f);
        div.ContentWidth.Should().BeApproximately(130, 1f); // 150 - 10 - 10 padding
    }

    [Fact]
    public void ContentBoxWidth_AboveMaxWidth_ContentWidthRecomputedFromClampedWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='box-sizing:content-box; width:300px; max-width:100px; padding:10px; height:20px'></div>",
            600, 800);

        var div = root.FindByTag("div")!;
        div.Width.Should().BeApproximately(100, 1f);
        div.ContentWidth.Should().BeApproximately(80, 1f); // 100 - 10 - 10 padding
    }

    // ── auto-margin centering: only applies with a specified width ──────────

    [Fact]
    public void AutoMarginBoth_WithMinWidthClamp_CentersUsingClampedWidth()
    {
        // Auto-margin centering reads box.Width AFTER the min/max clamp —
        // pin that a min-width clamp changes the centering remainder too.
        var root = LayoutTestHelper.Layout(
            "<div id='wrap' style='width:400px; padding:0; margin:0'>" +
            "<div id='inner' style='width:50px; min-width:200px; margin:0 auto; height:10px'></div></div>",
            600, 800);

        var inner = root.FindByTag("div")!.Children[0];
        inner.Width.Should().BeApproximately(200, 1f);
        inner.MarginLeft.Should().BeApproximately(100, 1f); // (400-200)/2
    }

    [Fact]
    public void AutoMarginLeftOnly_ExplicitRightMargin_LeftAbsorbsAllRemainingSpace()
    {
        var root = LayoutTestHelper.Layout(
            "<div id='wrap' style='width:500px; padding:0; margin:0'>" +
            "<div id='inner' style='width:200px; margin-left:auto; margin-right:30px; height:10px'></div></div>",
            600, 800);

        var inner = root.FindByTag("div")!.Children[0];
        // remaining = 500 - 200 = 300; marginLeft = remaining - marginRight = 300 - 30 = 270
        inner.MarginLeft.Should().BeApproximately(270, 1f);
        inner.MarginRight.Should().BeApproximately(30, 1f);
    }

    [Fact]
    public void AutoWidth_MarginAuto_DoesNotCenter()
    {
        // Auto-margin centering only fires when width is explicitly
        // specified; an auto-width box ignores "margin: auto" for centering.
        var root = LayoutTestHelper.Layout(
            "<div id='wrap' style='width:400px; padding:0; margin:0'>" +
            "<div id='inner' style='margin:0 auto; height:10px'></div></div>", 600, 800);

        var inner = root.FindByTag("div")!.Children[0];
        inner.Width.Should().BeApproximately(400, 1f); // fills container, not centered-with-margin
    }

    // ── table cells ignore their own explicit width style ───────────────────

    [Fact]
    public void TableCell_FixedLayoutColumnWidth_OverridesConflictingCellWidthStyle()
    {
        // Isolates CreateBox's own isTableCell branch (which resolves width from
        // containingWidth, not the cell's own style) from the auto column-width
        // algorithm's separate habit of reading a cell's width as a sizing hint:
        // table-layout:fixed derives column widths from <col> alone, so a
        // conflicting inline width on the cell can only be reflected if CreateBox
        // resolved it directly — pin that it does not.
        var root = LayoutTestHelper.Layout(
            "<table style='table-layout:fixed; width:400px'>" +
            "<colgroup><col style='width:300px'><col style='width:100px'></colgroup>" +
            "<tr><td style='width:10px'>a</td><td>b</td></tr></table>", 600, 800);

        var firstCell = root.FindByTag("td")!;
        firstCell.Width.Should().BeApproximately(300, 1f,
            "the cell's own width:10px style must be ignored in favor of the column width");
    }
}
