using System.Collections.Generic;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class MultiColumnLayoutTests
{
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

    [Fact]
    public void ResolveColumns_Count3_DividesWidthEqually()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='column-count: 3; width: 600px'></div>", 600, 800);
        var div = root.FindByTag("div");

        var (count, width, gap) = MultiColumnLayout.ResolveColumns(div!.Style, 600, 16);

        count.Should().Be(3);
        width.Should().BeApproximately((600 - 2 * 16) / 3f, 1f, "three columns share 600px less two default 16px gaps");
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
        var grandchild = new LayoutBox { X = 5, Y = 5, Width = 100, Height = 20 };
        var child = new LayoutBox { X = 0, Y = 0, Width = 200, Height = 50 };
        child.Children.Add(grandchild);

        var fillerOfTheFirstColumn = new LayoutBox { X = 0, Y = 0, Width = 200, Height = 50 };

        float columnWidth = 190;
        float gap = 20;
        var columns = MultiColumnLayout.DistributeIntoColumns(
            new List<LayoutBox> { fillerOfTheFirstColumn, child }, 2, columnWidth, gap, containerX: 0, containerY: 0);

        columns.Should().HaveCount(2);
        var column2Child = columns[1].Children.Single();
        column2Child.Should().BeSameAs(child);

        const float childXBeforeRedistribution = 0f;
        float expectedDeltaX = column2Child.X - childXBeforeRedistribution;
        grandchild.X.Should().BeApproximately(5 + expectedDeltaX, 0.1f,
            "descendants of a box moved into column 2 move by the same offset, or they paint over column 1");
        grandchild.X.Should().BeGreaterThan(columnWidth,
            "grandchild should now fall within column 2's X range, not still overlap column 1");

        grandchild.Y.Should().Be(5f,
            "descendant Y is parent-relative (the post-layout pass adds each ancestor's Y), so it is not shifted");
        columns[1].Y.Should().Be(0f, "the child is relative to its column box, whose Y is the container offset");
        column2Child.Y.Should().Be(0f, "first child of a column sits at the column's top, relative to the column box");
    }

    [Fact]
    public void Distribute_AbsoluteDescendantOfPositionedAncestor_MovesWithAncestor()
    {
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
            "the badge's containing block is its position:relative ancestor, which moved into column 2");
    }

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
        var root = LayoutTestHelper.Layout(
            "<div style='column-width: 200px; width: 600px'><p>A</p><p>B</p></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        MultiColumnLayout.IsMultiColumn(div!.Style).Should().BeTrue();
    }

    [Fact]
    public void Distribute_SecondColumnChild_AbsolutelyPositionedDescendantNotShifted()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div class='tc' style='column-count:2; column-gap:20px; width:400px'>" +
            "<div style='height:60px'><p>First</p></div>" +
            "<div style='height:60px'><span style='position:absolute; top:10px; left:10px'>Badge</span></div>" +
            "</div></body>", 500, 800);

        var badge = root.FindByTag("span");
        badge.Should().NotBeNull();
        badge!.IsAbsolutelyPositioned.Should().BeTrue();

        badge.X.Should().BeApproximately(10f, 1f,
            "nothing between the badge and the page is positioned, so it stays at left:10px of the page instead of " +
            "moving again with its static parent's column");
        badge.Y.Should().BeApproximately(10f, 1f);
    }

    [Fact]
    public void ColumnSpanAll_SpanningElementWidthEqualsContainerWidth()
    {
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
            "the first page's multi-column content overflows its min-height, and the second page starts after " +
            "that true bottom instead of painting over it");
    }

    [Fact]
    public void Layout_ColumnCount2_SecondColumnParagraphIsShiftedRight()
    {
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

        paragraphs[1].Y.Should().BeApproximately(paragraphs[0].Y, 0.5f,
            "both are the first paragraph of their column; shifting parent-relative Y as well would pull the " +
            "column-2 paragraph up into earlier content");
    }

    [Fact]
    public void Layout_FlexColumnPage_MultiColumnSecondItem_ContentStaysBelowPrecedingItem()
    {
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
                $"paragraph {i} belongs to the multi-column block that follows the header, including article B in column 2");
        }
    }

    [Fact]
    public void Layout_ColumnCount2_PaddingTop_ColumnChildrenNotDoubleOffset()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div class='tc' style='column-count:2; column-gap:20px; width:400px; padding-top:20px'>" +
            "<div style='height:60px'><p style='margin:0'>First</p></div>" +
            "<div style='height:60px'><p style='margin:0'>Second</p></div>" +
            "</div></body>", 500, 800);

        var tc = root.FindByTag("div");
        var paragraphs = root.FindAllByTag("p");
        paragraphs[0].Y.Should().BeApproximately(tc!.Y + 20f, 1f,
            "column children are relative to their column box, which already sits at the padding offset, so the " +
            "padding is not added twice");
    }

    [Fact]
    public void ColumnSpanAll_DeeplyNestedContentIsShiftedWithSpanningElement()
    {
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
            "the spanning element moved up when the paragraphs before it split into two columns, and its " +
            "parent-relative paragraph moves with it");
    }

    [Fact]
    public void ColumnSpanAll_SpanningElementBelowFirstColumnSection()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<div style='column-count:2; width:400px; column-gap:0'>" +
            "<p style='height:30px'>Before</p>" +
            "<h2 style='column-span:all; height:20px'>Heading</h2>" +
            "<p style='height:30px'>After</p>" +
            "</div></body>", 500, 800);

        var h2 = root.FindByTag("h2");
        h2.Should().NotBeNull();
        var before = root.FindAllByTag("p")[0];
        h2!.Y.Should().BeGreaterOrEqualTo(before.Y,
            "column-span:all element should appear below preceding content");
    }
}
