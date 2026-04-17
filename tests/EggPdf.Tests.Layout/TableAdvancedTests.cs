using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// Advanced table layout tests: rowspan, colgroup, caption, thead/tfoot,
/// border-spacing, percentage widths, and nested tables.
/// </summary>
public class TableAdvancedTests
{
    // ── rowspan ─────────────────────────────────────────────────────────────

    [Fact]
    public void Rowspan_CellSpansTwoRows_DoesNotCrash()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody>" +
            "<tr><td rowspan='2'>Spans</td><td>R1C2</td></tr>" +
            "<tr><td>R2C2</td></tr>" +
            "</tbody></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
    }

    [Fact]
    public void Rowspan_CellPositiveHeight()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody>" +
            "<tr><td rowspan='2' style='height:80px'>Spans</td><td>A</td></tr>" +
            "<tr><td>B</td></tr>" +
            "</tbody></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Height.Should().BeGreaterThan(0);
    }

    // ── caption ─────────────────────────────────────────────────────────────

    [Fact]
    public void Caption_Exists_DoesNotCrash()
    {
        var root = LayoutTestHelper.Layout(
            "<table><caption>My Table</caption>" +
            "<tr><td>Cell</td></tr></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Height.Should().BeGreaterThan(0);
    }

    // ── thead / tfoot ────────────────────────────────────────────────────────

    [Fact]
    public void TfootAfterTbody_BothPresent()
    {
        var root = LayoutTestHelper.Layout(
            "<table>" +
            "<thead><tr><th>Header</th></tr></thead>" +
            "<tbody><tr><td>Data</td></tr></tbody>" +
            "<tfoot><tr><td>Footer</td></tr></tfoot>" +
            "</table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Height.Should().BeGreaterThan(0);
    }

    // ── colgroup / col ───────────────────────────────────────────────────────

    [Fact]
    public void Colgroup_WithWidths_DoesNotCrash()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 600px'>" +
            "<colgroup><col style='width: 200px'><col style='width: 400px'></colgroup>" +
            "<tr><td>A</td><td>B</td></tr>" +
            "</table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
    }

    [Fact]
    public void Colgroup_ColWidths_AppliedToColumns()
    {
        // col elements with explicit widths should set the column widths.
        // col1 = 200px, col2 = 400px in a 600px table.
        var root = LayoutTestHelper.Layout(
            "<table style='width: 600px'>" +
            "<colgroup><col style='width: 200px'><col style='width: 400px'></colgroup>" +
            "<tbody><tr><td>A</td><td>B</td></tr></tbody>" +
            "</table>", 800, 800);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCount(2);

        tds[0].Width.Should().BeApproximately(200f, 5f,
            "first col with width:200px should produce a ~200px cell");
        tds[1].Width.Should().BeApproximately(400f, 5f,
            "second col with width:400px should produce a ~400px cell");
    }

    [Fact]
    public void Colgroup_ColPercentageWidth_AppliedToColumns()
    {
        // col with percentage widths relative to the table width
        var root = LayoutTestHelper.Layout(
            "<table style='width: 500px'>" +
            "<colgroup><col style='width: 40%'><col style='width: 60%'></colgroup>" +
            "<tbody><tr><td>A</td><td>B</td></tr></tbody>" +
            "</table>", 800, 800);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCount(2);

        tds[0].Width.Should().BeApproximately(200f, 5f,
            "40% col in 500px table should produce a ~200px cell");
        tds[1].Width.Should().BeApproximately(300f, 5f,
            "60% col in 500px table should produce a ~300px cell");
    }

    [Fact]
    public void Colgroup_SpanAttribute_WithWidth_AppliesToMultipleCols()
    {
        // <colgroup span="2" style="width:150px"> applies 150px to first 2 columns
        var root = LayoutTestHelper.Layout(
            "<table style='width: 600px; table-layout: fixed'>" +
            "<colgroup span='2' style='width: 150px'><col><col>" +
            "<col style='width: 300px'></colgroup>" +
            "<tbody><tr><td>A</td><td>B</td><td>C</td></tr></tbody>" +
            "</table>", 800, 800);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCountGreaterOrEqualTo(3);

        // cols A and B should each be ~150px
        tds[0].Width.Should().BeApproximately(150f, 10f,
            "colgroup span=2 width=150px should give ~150px to first col");
        tds[1].Width.Should().BeApproximately(150f, 10f,
            "colgroup span=2 width=150px should give ~150px to second col");
    }

    [Fact]
    public void Col_SpanAttribute_AppliesWidthToMultipleCols()
    {
        // <col span="2" style="width:200px"> covers 2 columns
        var root = LayoutTestHelper.Layout(
            "<table style='width: 600px; table-layout: fixed'>" +
            "<colgroup><col span='2' style='width: 200px'><col style='width: 200px'></colgroup>" +
            "<tbody><tr><td>A</td><td>B</td><td>C</td></tr></tbody>" +
            "</table>", 800, 800);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCountGreaterOrEqualTo(3);

        tds[0].Width.Should().BeApproximately(200f, 5f, "col span=2 should apply 200px to col 0");
        tds[1].Width.Should().BeApproximately(200f, 5f, "col span=2 should apply 200px to col 1");
    }

    // ── border-spacing ───────────────────────────────────────────────────────

    [Fact]
    public void BorderSpacing_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='border-spacing: 10px'><tr><td>A</td><td>B</td></tr></table>",
            600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Style.Get("border-spacing").Should().Be("10px");
    }

    // ── table width ──────────────────────────────────────────────────────────

    [Fact]
    public void Table_FixedWidth_Respected()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 400px'><tr><td>A</td><td>B</td></tr></table>",
            600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Width.Should().BeApproximately(400, 2f);
    }

    [Fact]
    public void Table_PercentageWidth_RelativeToContainer()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 50%'><tr><td>A</td></tr></table>",
            600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Width.Should().BeApproximately(300, 20f); // 50% of ~600
    }

    // ── nested tables ─────────────────────────────────────────────────────────

    [Fact]
    public void NestedTable_DoesNotCrash()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tr><td>" +
            "  <table><tr><td>Inner</td></tr></table>" +
            "</td></tr></table>", 600, 800);

        var tables = root.FindAllByTag("table");
        tables.Should().HaveCountGreaterOrEqualTo(2);
    }

    // ── empty cells ──────────────────────────────────────────────────────────

    [Fact]
    public void EmptyCells_TableHasPositiveSize()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tr><td></td><td></td></tr></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        // Even empty cells should give the table some size
        table!.Width.Should().BeGreaterThan(0);
    }

    // ── row height equalization ───────────────────────────────────────────────

    [Fact]
    public void Row_AllCellsSameHeight()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tr>" +
            "<td style='height:60px'>Short</td>" +
            "<td>A longer cell that has more content to determine height</td>" +
            "</tr></table>", 600, 800);

        var cells = root.FindAllByTag("td");
        if (cells.Count >= 2)
        {
            // All cells in a row should have the same height (row height equalization)
            cells[0].Height.Should().BeApproximately(cells[1].Height, 2f,
                "cells in the same row should have equal heights");
        }
    }

    // ── table with background ─────────────────────────────────────────────────

    [Fact]
    public void TableCell_BackgroundColor_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tr><td style='background-color: #ff0000'>Red</td></tr></table>",
            600, 800);

        var td = root.FindByTag("td");
        td.Should().NotBeNull();
        td!.Style.Get("background-color").Should().NotBeNullOrEmpty();
    }

    // ── multiple rows stack vertically ────────────────────────────────────────

    [Fact]
    public void MultipleRows_YPositionsIncreasing()
    {
        var root = LayoutTestHelper.Layout(
            "<table>" +
            "<tr><td>Row 1</td></tr>" +
            "<tr><td>Row 2</td></tr>" +
            "<tr><td>Row 3</td></tr>" +
            "</table>", 600, 800);

        var rows = root.FindAllByTag("tr");
        rows.Should().HaveCountGreaterOrEqualTo(3);

        for (int i = 1; i < rows.Count; i++)
            rows[i].Y.Should().BeGreaterThan(rows[i - 1].Y,
                $"row {i} should be below row {i - 1}");
    }

    // ── table-layout: fixed ─────────────────────────────────────────────────

    [Fact]
    public void TableLayoutFixed_EqualWidthCells_WhenNoExplicitWidth()
    {
        // With table-layout:fixed and no explicit widths, columns are equal
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><table style='table-layout:fixed; width:300px; border-spacing:0'>" +
            "<tr><td>A</td><td>B</td><td>C</td></tr>" +
            "<tr><td>very long content here</td><td>B2</td><td>C2</td></tr>" +
            "</table></body>", 400, 600);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCountGreaterOrEqualTo(3);

        // First row cells should all be equal width (300 / 3 = 100)
        tds[0].Width.Should().BeApproximately(100f, 2f, "fixed layout distributes equally");
        tds[1].Width.Should().BeApproximately(100f, 2f, "fixed layout distributes equally");
        tds[2].Width.Should().BeApproximately(100f, 2f, "fixed layout distributes equally");

        // Second row cells should use the same column widths, not content-driven widths
        tds[3].Width.Should().BeApproximately(100f, 2f,
            "fixed layout: second row must use first-row column widths, not content");
    }

    [Fact]
    public void TableLayoutFixed_ExplicitFirstRowWidths_Respected()
    {
        // With table-layout:fixed, first-row explicit widths define all column widths
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><table style='table-layout:fixed; width:300px; border-spacing:0'>" +
            "<tr><td style='width:60px'>A</td><td style='width:120px'>B</td><td>C</td></tr>" +
            "<tr><td>very long content here</td><td>short</td><td>x</td></tr>" +
            "</table></body>", 400, 600);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCountGreaterOrEqualTo(6);

        // First row: explicit widths 60, 120, remaining = 120
        tds[0].Width.Should().BeApproximately(60f, 2f, "first-row explicit width:60px respected");
        tds[1].Width.Should().BeApproximately(120f, 2f, "first-row explicit width:120px respected");

        // Second row must use same column widths as first row
        tds[3].Width.Should().BeApproximately(60f, 2f,
            "second row inherits first-row column widths in fixed layout");
        tds[4].Width.Should().BeApproximately(120f, 2f,
            "second row inherits first-row column widths in fixed layout");
    }
}
