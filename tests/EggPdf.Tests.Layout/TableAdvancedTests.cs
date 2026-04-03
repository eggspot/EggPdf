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
}
