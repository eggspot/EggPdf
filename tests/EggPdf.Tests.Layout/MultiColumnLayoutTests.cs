using System.Collections.Generic;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// Tests for MultiColumnLayout: column count/width resolution and content distribution.
/// </summary>
public class MultiColumnLayoutTests
{
    // ── IsMultiColumn ───────────────────────────────────────────────────────

    [Fact]
    public void IsMultiColumn_ColumnCount_ReturnsTrue()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='column-count: 3; width: 600px'><p>A</p><p>B</p><p>C</p></div>",
            600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        MultiColumnLayout.IsMultiColumn(div!.Style).Should().BeTrue();
    }

    [Fact]
    public void IsMultiColumn_NoColumns_ReturnsFalse()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 600px'><p>A</p></div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        MultiColumnLayout.IsMultiColumn(div!.Style).Should().BeFalse();
    }

    [Fact]
    public void IsMultiColumn_ColumnCountAuto_ReturnsFalse()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='column-count: auto'></div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        MultiColumnLayout.IsMultiColumn(div!.Style).Should().BeFalse();
    }

    [Fact]
    public void IsMultiColumn_ColumnWidth_ReturnsTrue()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='column-width: 200px; width: 600px'></div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        MultiColumnLayout.IsMultiColumn(div!.Style).Should().BeTrue();
    }

    // ── ResolveColumns: column-count ────────────────────────────────────────

    [Fact]
    public void ResolveColumns_Count3_DividesWidthEqually()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='column-count: 3; width: 600px'></div>", 600, 800);
        var div = root.FindByTag("div");

        var (count, width, gap) = MultiColumnLayout.ResolveColumns(div!.Style, 600, 16);

        count.Should().Be(3);
        // 3 columns with 16px default gap: (600 - 2*16) / 3 = 189.33px
        width.Should().BeApproximately((600 - 2 * 16) / 3f, 1f);
    }

    [Fact]
    public void ResolveColumns_Count2_TwoEqualColumns()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='column-count: 2; width: 600px'></div>", 600, 800);
        var div = root.FindByTag("div");

        var (count, width, gap) = MultiColumnLayout.ResolveColumns(div!.Style, 600, 16);

        count.Should().Be(2);
        width.Should().BeApproximately((600 - 16) / 2f, 1f);
    }

    [Fact]
    public void ResolveColumns_Count1_SingleColumn()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='column-count: 1; width: 600px'></div>", 600, 800);
        var div = root.FindByTag("div");

        var (count, width, gap) = MultiColumnLayout.ResolveColumns(div!.Style, 600, 16);

        count.Should().Be(1);
        width.Should().BeApproximately(600, 1f);
    }

    // ── ResolveColumns: column-gap ──────────────────────────────────────────

    [Fact]
    public void ResolveColumns_CustomGap_UsedInCalculation()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='column-count: 2; column-gap: 20px; width: 600px'></div>", 600, 800);
        var div = root.FindByTag("div");

        var (count, width, gap) = MultiColumnLayout.ResolveColumns(div!.Style, 600, 16);

        gap.Should().BeApproximately(20, 0.1f);
        width.Should().BeApproximately((600 - 20) / 2f, 1f);
    }

    [Fact]
    public void ResolveColumns_NoGap_DefaultGap16()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='column-count: 2'></div>", 600, 800);
        var div = root.FindByTag("div");

        var (_, _, gap) = MultiColumnLayout.ResolveColumns(div!.Style, 600, 16);
        gap.Should().BeApproximately(16, 0.1f);
    }

    // ── DistributeIntoColumns ───────────────────────────────────────────────

    [Fact]
    public void Distribute_SingleColumn_ReturnsOriginalChildren()
    {
        var children = new List<LayoutBox>
        {
            new() { X = 0, Y = 0, Width = 200, Height = 50 },
            new() { X = 0, Y = 50, Width = 200, Height = 50 },
        };

        var result = MultiColumnLayout.DistributeIntoColumns(children, 1, 200, 16, 0, 0);
        result.Should().HaveCount(2, "single column keeps children as-is");
    }

    [Fact]
    public void Distribute_NoChildren_ReturnsEmpty()
    {
        var result = MultiColumnLayout.DistributeIntoColumns(
            new List<LayoutBox>(), 3, 190, 16, 0, 0);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Distribute_TwoColumns_ProducesTwoColumnBoxes()
    {
        var children = new List<LayoutBox>();
        for (int i = 0; i < 6; i++)
            children.Add(new LayoutBox { X = 0, Y = i * 50, Width = 200, Height = 50 });

        var result = MultiColumnLayout.DistributeIntoColumns(children, 2, 190, 20, 0, 0);
        result.Should().HaveCount(2, "should produce 2 column boxes");
    }

    [Fact]
    public void Distribute_TwoColumns_ColumnsPositionedSideBySide()
    {
        var children = new List<LayoutBox>();
        for (int i = 0; i < 4; i++)
            children.Add(new LayoutBox { X = 0, Y = i * 40, Width = 200, Height = 40 });

        float columnWidth = 190;
        float gap = 20;
        var result = MultiColumnLayout.DistributeIntoColumns(children, 2, columnWidth, gap, 0, 0);

        result.Should().HaveCount(2);
        result[0].X.Should().BeApproximately(0, 0.1f);
        result[1].X.Should().BeApproximately(columnWidth + gap, 0.1f);
    }

    [Fact]
    public void Distribute_ThreeColumns_AllChildrenAccountedFor()
    {
        var children = new List<LayoutBox>();
        for (int i = 0; i < 9; i++)
            children.Add(new LayoutBox { X = 0, Y = i * 30, Width = 180, Height = 30 });

        var columns = MultiColumnLayout.DistributeIntoColumns(children, 3, 180, 16, 0, 0);

        int totalChildren = 0;
        foreach (var col in columns)
            totalChildren += col.Children.Count;

        totalChildren.Should().Be(9, "all children should be distributed");
    }

    [Fact]
    public void Distribute_SecondColumnChild_DescendantsAreShiftedToMatch()
    {
        // A child box moved into column 2 carries a grandchild (e.g. a text run inside
        // a paragraph) that was positioned during the original single-column layout pass.
        var grandchild = new LayoutBox { X = 5, Y = 5, Width = 100, Height = 20 };
        var child = new LayoutBox { X = 0, Y = 0, Width = 200, Height = 50 };
        child.Children.Add(grandchild);

        // First child fills column 1 entirely (targetHeight forces the second child into column 2).
        var filler = new LayoutBox { X = 0, Y = 0, Width = 200, Height = 50 };

        float columnWidth = 190;
        float gap = 20;
        var columns = MultiColumnLayout.DistributeIntoColumns(
            new List<LayoutBox> { filler, child }, 2, columnWidth, gap, containerX: 0, containerY: 0);

        columns.Should().HaveCount(2);
        var column2Child = columns[1].Children.Single();
        column2Child.Should().BeSameAs(child);

        // The grandchild must move by the same X delta as its parent — otherwise it keeps
        // painting at its stale column-1 coordinates, overlapping column 1's real content.
        float expectedDeltaX = column2Child.X - 0; // child's X before redistribution was 0
        grandchild.X.Should().BeApproximately(5 + expectedDeltaX, 0.1f,
            "descendants of a box moved into column 2 must be translated by the same offset as the box itself");
        grandchild.X.Should().BeGreaterThan(columnWidth,
            "grandchild should now fall within column 2's X range, not still overlap column 1");

        // Y is parent-relative in this engine (BlockLayout's post-layout pass adds each
        // ancestor's Y), so the grandchild's Y must NOT be touched — it is already relative
        // to `child`, which is what moved.
        grandchild.Y.Should().Be(5f, "descendant Y is parent-relative and must not be shifted");
        // ...and `child` itself is now relative to its column box, whose Y is the container offset.
        columns[1].Y.Should().Be(0f);
        column2Child.Y.Should().Be(0f, "first child of a column sits at the column's top, relative to the column box");
    }

    [Fact]
    public void Distribute_AbsoluteDescendantOfPositionedAncestor_MovesWithAncestor()
    {
        // The absolutely-positioned badge's containing block is the position:relative div
        // that gets moved into column 2 — so the badge must move with it (its coordinates
        // were resolved against that div). Contrast with the static-parent case below.
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div class='tc' style='column-count:2; column-gap:20px; width:400px'>" +
            "<div style='height:60px'><p>First</p></div>" +
            "<div style='height:60px; position:relative'><span style='position:absolute; top:10px; left:10px'>Badge</span></div>" +
            "</div></body>", 500, 800);

        var badge = root.FindByTag("span");
        badge.Should().NotBeNull();
        badge!.IsAbsolutelyPositioned.Should().BeTrue();
        badge.X.Should().BeGreaterThan(190f,
            "the badge is positioned against its relative ancestor, which now lives in column 2");
    }

    // ── integration: column-count in HTML layout ────────────────────────────

    [Fact]
    public void Layout_ColumnCount2_ContainerDoesNotCrash()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='column-count: 2; width: 600px'>" +
            "<p>Para 1</p><p>Para 2</p><p>Para 3</p><p>Para 4</p>" +
            "</div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(600, 2f);
    }

    [Fact]
    public void Layout_ColumnCount3_ProducesPositiveWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='column-count: 3; width: 300px'><p>A</p><p>B</p><p>C</p></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BePositive();
    }

    [Fact]
    public void Layout_ColumnWidth_ColumnCountDerived()
    {
        // column-width: 200px on 600px container → ~3 columns
        var root = LayoutTestHelper.Layout(
            "<div style='column-width: 200px; width: 600px'><p>A</p><p>B</p></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        MultiColumnLayout.IsMultiColumn(div!.Style).Should().BeTrue();
    }

    // ── column-span: all ────────────────────────────────────────────────────

    [Fact]
    public void Distribute_SecondColumnChild_AbsolutelyPositionedDescendantNotShifted()
    {
        // A static-positioned box moved into column 2 may contain an absolutely
        // positioned descendant. That descendant's coordinates were already resolved
        // against its own containing block (the page root here, since nothing in
        // between is positioned) during the original layout pass — translating it
        // again by the column offset would double-move it.
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div class='tc' style='column-count:2; column-gap:20px; width:400px'>" +
            "<div style='height:60px'><p>First</p></div>" +
            "<div style='height:60px'><span style='position:absolute; top:10px; left:10px'>Badge</span></div>" +
            "</div></body>", 500, 800);

        var badge = root.FindByTag("span");
        badge.Should().NotBeNull();
        badge!.IsAbsolutelyPositioned.Should().BeTrue();

        // top:10px/left:10px against the page-root containing block — must stay there
        // regardless of which column its static parent was distributed into.
        badge.X.Should().BeApproximately(10f, 1f);
        badge.Y.Should().BeApproximately(10f, 1f);
    }

    [Fact]
    public void ColumnSpanAll_SpanningElementWidthEqualsContainerWidth()
    {
        // h2 has column-span:all — should span the full 400px container width
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div style='column-count:2; width:400px; column-gap:0'>" +
            "<p>Before</p>" +
            "<h2 style='column-span:all'>Heading</h2>" +
            "<p>After</p>" +
            "</div></body>", 500, 800);

        var h2 = root.FindByTag("h2");
        h2.Should().NotBeNull("h2 with column-span:all should be laid out");
        h2!.Width.Should().BeApproximately(400f, 5f,
            "column-span:all element should span full container width");
    }

    [Fact]
    public void Layout_StackedFlexColumnPages_SecondPageStartsAfterFirstPageOverflow()
    {
        // Reproduces the VCRRM certificate bug report: two `.page` sections, each
        // `display:flex; flex-direction:column; min-height:...`, stacked in normal flow.
        // The first page's multi-column content overflows its own min-height. The second
        // page must still start at or after the first page's TRUE (overflowed) bottom —
        // not at min-height — or its content paints on top of the first page's overflow.
        string longParagraph = string.Concat(System.Linq.Enumerable.Repeat(
            "<p>Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>",
            12));

        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<section class='page' style='display:flex; flex-direction:column; min-height:300px; width:400px'>" +
            "<div class='tc' style='column-count:2; column-gap:16px'>" + longParagraph + "</div>" +
            "</section>" +
            "<section class='page' style='display:flex; flex-direction:column; min-height:300px; width:400px'>" +
            "<p>Second page marker</p>" +
            "</section>" +
            "</body>", 400, 800);

        var pages = root.FindAllByTag("section");
        pages.Should().HaveCount(2);

        float firstPageTrueBottom = pages[0].Y + pages[0].Height;
        pages[1].Y.Should().BeGreaterOrEqualTo(firstPageTrueBottom - 0.5f,
            "the second page must start after the first page's overflowed content, not overlap it");
    }

    [Fact]
    public void Layout_ColumnCount2_SecondColumnParagraphIsShiftedRight()
    {
        // Two same-height blocks split 1-per-column. The second block's own <p> descendant
        // must be repositioned into column 2, not left overlapping column 1's text.
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div class='tc' style='column-count:2; column-gap:20px; width:400px'>" +
            "<div style='height:60px'><p>First</p></div>" +
            "<div style='height:60px'><p>Second</p></div>" +
            "</div></body>", 500, 800);

        var paragraphs = root.FindAllByTag("p");
        paragraphs.Should().HaveCount(2);

        paragraphs[1].X.Should().BeGreaterThan(paragraphs[0].X + 50,
            "the second block's paragraph was distributed into column 2 and must be painted there, " +
            "not at column 1's original X — otherwise the two columns' text overlaps");

        // Both are the first paragraph of their column, so they sit on the same row. If the
        // redistribution had also shifted descendants in Y (which is parent-relative here),
        // the column-2 paragraph would be pulled up by column 1's height into earlier content.
        paragraphs[1].Y.Should().BeApproximately(paragraphs[0].Y, 0.5f,
            "column-2 content must not be shifted vertically relative to column 1");
    }

    [Fact]
    public void Layout_FlexColumnPage_MultiColumnSecondItem_ContentStaysBelowPrecedingItem()
    {
        // Reproduces the "Phụ lục" bleed: a flex-column page whose second item is a
        // 2-column block. Article B is distributed into column 2; its paragraphs must
        // all render below the page header, never above it / on the previous page.
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<section style='display:flex; flex-direction:column; width:400px'>" +
            "<p style='margin:0'>Header</p>" +
            "<div style='column-count:2; column-gap:16px'>" +
            "<div><h3>A</h3><p>a1</p><p>a2</p><p>a3</p></div>" +
            "<div><h3>B</h3><p>b1</p><p>b2</p><p>b3</p><p>b4</p><p>b5</p></div>" +
            "</div></section></body>", 500, 800);

        var paragraphs = root.FindAllByTag("p");
        paragraphs.Should().HaveCount(9);
        var header = paragraphs[0];
        for (int i = 1; i < paragraphs.Count; i++)
        {
            paragraphs[i].Y.Should().BeGreaterThan(header.Y + 0.5f,
                $"paragraph {i} belongs to the multi-column block that follows the header");
        }
    }

    [Fact]
    public void Layout_ColumnCount2_PaddingTop_ColumnChildrenNotDoubleOffset()
    {
        // Column boxes sit at the container's padding offset; their children must be
        // relative to the column box, or the padding gets added twice by the post-layout pass.
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div class='tc' style='column-count:2; column-gap:20px; width:400px; padding-top:20px'>" +
            "<div style='height:60px'><p style='margin:0'>First</p></div>" +
            "<div style='height:60px'><p style='margin:0'>Second</p></div>" +
            "</div></body>", 500, 800);

        var tc = root.FindByTag("div");
        var paragraphs = root.FindAllByTag("p");
        paragraphs[0].Y.Should().BeApproximately(tc!.Y + 20f, 1f,
            "the first column paragraph starts right after the container's top padding, not 2x padding");
    }

    [Fact]
    public void ColumnSpanAll_DeeplyNestedContentIsShiftedWithSpanningElement()
    {
        // Two paragraphs before the spanning element split across 2 columns, so the
        // spanning element's Y after redistribution differs from its original single-column
        // Y. The paragraph nested inside it is parent-relative and must end up inside the
        // spanning div's resolved bounds — not pulled out of it by a stray Y shift.
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div style='column-count:2; width:400px; column-gap:0'>" +
            "<p style='height:80px'>Before one</p>" +
            "<p style='height:80px'>Before two</p>" +
            "<div style='column-span:all'><p>Spanning paragraph text</p></div>" +
            "<p>After</p>" +
            "</div></body>", 500, 800);

        var paragraphs = root.FindAllByTag("p");
        var spanningParagraph = paragraphs[2];
        var spanningDiv = root.FindAllByTag("div").Find(d => d.Style.Get("column-span") == "all");
        spanningDiv.Should().NotBeNull();

        spanningParagraph.Y.Should().BeGreaterOrEqualTo(spanningDiv!.Y - 0.5f);
        spanningParagraph.Y.Should().BeLessOrEqualTo(spanningDiv.Y + spanningDiv.Height + 0.5f,
            "a paragraph nested inside the column-span:all element must stay inside it");
    }

    [Fact]
    public void ColumnSpanAll_SpanningElementBelowFirstColumnSection()
    {
        // The h2 should appear below the content laid out before it
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div style='column-count:2; width:400px; column-gap:0'>" +
            "<p style='height:30px'>Before</p>" +
            "<h2 style='column-span:all; height:20px'>Heading</h2>" +
            "<p style='height:30px'>After</p>" +
            "</div></body>", 500, 800);

        var h2 = root.FindByTag("h2");
        h2.Should().NotBeNull();
        // The h2 should be below the "before" paragraph
        var before = root.FindAllByTag("p")[0];
        h2!.Y.Should().BeGreaterOrEqualTo(before.Y,
            "column-span:all element should appear below preceding content");
    }
}
