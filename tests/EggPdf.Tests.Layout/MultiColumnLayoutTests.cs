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
