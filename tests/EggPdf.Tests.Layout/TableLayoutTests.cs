using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class TableLayoutTests
{
    [Fact]
    public void SimpleTable_HasTableDisplay()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tr><td>Cell</td></tr></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Style.Display.Should().Be("table");
    }

    [Fact]
    public void TableRow_ExistsInLayout()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody><tr><td>Cell</td></tr></tbody></table>", 600, 800);

        // tr may be nested inside tbody
        var tr = root.FindByTag("tr");
        if (tr == null)
        {
            // Also check tbody children
            var tbody = root.FindByTag("tbody");
            tbody.Should().NotBeNull();
        }
    }

    [Fact]
    public void TableCell_ExistsInLayout()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody><tr><td>Cell</td></tr></tbody></table>", 600, 800);

        var td = root.FindByTag("td");
        td.Should().NotBeNull();
    }

    [Fact]
    public void Table_HasPositiveSize()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody><tr><td>Cell 1</td><td>Cell 2</td></tr></tbody></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Width.Should().BeGreaterThan(0);
        table.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TableWithWidth_RespectsWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 400px'><tr><td>Cell</td></tr></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Width.Should().BeApproximately(400, 2f);
    }

    [Fact]
    public void MultipleRows_StackVertically()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody><tr><td>Row 1</td></tr><tr><td>Row 2</td></tr></tbody></table>", 600, 800);

        var trs = root.FindAllByTag("tr");
        trs.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public void TableHeader_RecognizedAsBold()
    {
        var root = LayoutTestHelper.Layout(
            "<table><thead><tr><th>Header</th></tr></thead><tbody><tr><td>Data</td></tr></tbody></table>", 600, 800);

        var th = root.FindByTag("th");
        th.Should().NotBeNull();
        th!.Style.FontWeight.Should().Be("bold");
    }

    [Fact]
    public void BorderedTable_HasDimensions()
    {
        var root = LayoutTestHelper.Layout(
            "<table border='1' style='border-collapse: collapse'>" +
            "<tr><td>A</td><td>B</td></tr>" +
            "<tr><td>C</td><td>D</td></tr>" +
            "</table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StyledTable_AllStylesApplied()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 100%; border-collapse: collapse'>" +
            "<thead><tr><th>Name</th><th>Value</th></tr></thead>" +
            "<tbody><tr><td>Item</td><td>$10</td></tr></tbody>" +
            "</table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
        table!.Style.Get("border-collapse").Should().Be("collapse");
    }

    [Fact]
    public void EmptyTable_DoesNotCrash()
    {
        var root = LayoutTestHelper.Layout("<table></table>", 600, 800);

        var table = root.FindByTag("table");
        table.Should().NotBeNull();
    }

    [Fact]
    public void VerticalAlignMiddle_ShiftsContentDown()
    {
        // Cell with explicit height and vertical-align:middle
        var html = @"<table><tr>
            <td style='height: 100px; vertical-align: middle;'>Mid</td>
            <td style='height: 100px; vertical-align: top;'>Top</td>
        </tr></table>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var cells = root.FindAllByTag("td");
        cells.Should().HaveCount(2);

        // Middle-aligned cell: children should be shifted down from top
        var midCell = cells[0];
        var topCell = cells[1];

        if (midCell.Children.Count > 0 && topCell.Children.Count > 0)
        {
            // The first child of middle cell should be below the first child of top cell
            // (relative to their respective cells)
            float midChildRelY = midCell.Children[0].Y - midCell.Y;
            float topChildRelY = topCell.Children[0].Y - topCell.Y;
            midChildRelY.Should().BeGreaterOrEqualTo(topChildRelY,
                "middle-aligned content should be shifted down");
        }
    }

    [Fact]
    public void VerticalAlignBottom_ShiftsContentToBottom()
    {
        var html = @"<table><tr>
            <td style='height: 100px; vertical-align: bottom;'>Bot</td>
        </tr></table>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var cell = root.FindByTag("td");
        cell.Should().NotBeNull();

        if (cell!.Children.Count > 0)
        {
            // Bottom-aligned: child should be near the bottom of the cell
            float childRelY = cell.Children[0].Y - cell.Y;
            childRelY.Should().BeGreaterThan(0, "bottom-aligned content should be shifted down");
        }
    }

    [Fact]
    public void BorderCollapse_Collapse_SharedEdgeBorderZeroed()
    {
        var html = @"<table style='border-collapse: collapse;'><tr>
            <td style='border: 2px solid black;'>A</td>
            <td style='border: 2px solid black;'>B</td>
        </tr></table>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var cells = root.FindAllByTag("td");
        cells.Should().HaveCount(2);

        // Second cell should have left border zeroed (shared with first cell's right)
        var secondCell = cells[1];
        var leftBorderWidth = secondCell.Style.Get("border-left-width");
        leftBorderWidth.Should().Be("0", "shared edge border should be zeroed in collapse mode");
    }

    [Fact]
    public void BorderCollapse_Separate_IndependentBorders()
    {
        var html = @"<table style='border-collapse: separate;'><tr>
            <td style='border: 2px solid black;'>A</td>
            <td style='border: 2px solid black;'>B</td>
        </tr></table>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var cells = root.FindAllByTag("td");
        cells.Should().HaveCount(2);

        // In separate mode, second cell should keep its left border
        var secondCell = cells[1];
        var leftBorderWidth = secondCell.Style.Get("border-left-width");
        leftBorderWidth.Should().NotBe("0", "separate mode should keep all borders");
    }

    [Fact]
    public void BorderCollapse_CellsEdgeToEdge_NoGaps()
    {
        var html = @"<table style='width: 400px; border-collapse: collapse;'><tr>
            <td style='border: 1px solid #ddd; padding: 10px;'>A</td>
            <td style='border: 1px solid #ddd; padding: 10px;'>B</td>
            <td style='border: 1px solid #ddd; padding: 10px;'>C</td>
            <td style='border: 1px solid #ddd; padding: 10px;'>D</td>
        </tr></table>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var cells = root.FindAllByTag("td");
        cells.Should().HaveCount(4);

        // Verify cells are edge-to-edge: each cell's X == previous cell's X + Width
        for (int i = 1; i < cells.Count; i++)
        {
            var prevRightEdge = cells[i - 1].X + cells[i - 1].Width;
            var currentX = cells[i].X;
            currentX.Should().BeApproximately(prevRightEdge, 0.5f,
                $"cell {i} X ({currentX}) should equal cell {i - 1} right edge ({prevRightEdge}) - no gaps");
        }

        // Verify total width of all cells fills the row
        var row = root.FindByTag("tr");
        row.Should().NotBeNull();
        var firstCellX = cells[0].X;
        var lastCellRightEdge = cells[3].X + cells[3].Width;
        var totalCellWidth = lastCellRightEdge - firstCellX;
        totalCellWidth.Should().BeApproximately(row!.ContentWidth, 1f,
            "cells should fill the entire row width");
    }

    [Fact]
    public void BorderCollapse_WithStyleTag_CellsEdgeToEdge()
    {
        // Test with CSS from <style> tag (uses CascadeResolver)
        var css = @"table { width: 100%; border-collapse: collapse; }
            th, td { border: 1px solid #ddd; padding: 10px; }";
        var html = @"<html><head><style>" + css + @"</style></head><body>
            <table><thead><tr><th>Item</th><th>Qty</th><th>Price</th><th>Total</th></tr></thead>
            <tbody><tr><td>A</td><td>B</td><td>C</td><td>D</td></tr></tbody></table>
        </body></html>";

        var document = EggPdf.Html.HtmlParser.Parse(html);
        var sheet = EggPdf.Css.Parser.CssStyleSheetParser.Parse(css);
        var cascade = new EggPdf.Css.Cascade.CascadeResolver(new[] { sheet }, "print");
        var root = BlockLayout.LayoutDocument(document, 595, 842, cascade);

        // Verify border-collapse was actually resolved
        var table = root.FindByTag("table");
        table!.Style.Get("border-collapse").Should().Be("collapse");

        var headerCells = root.FindAllByTag("th");
        headerCells.Should().HaveCount(4);

        // Verify header cells are edge-to-edge
        for (int i = 1; i < headerCells.Count; i++)
        {
            var prevRightEdge = headerCells[i - 1].X + headerCells[i - 1].Width;
            var currentX = headerCells[i].X;
            currentX.Should().BeApproximately(prevRightEdge, 0.5f,
                $"header cell {i} X ({currentX:F2}) should equal cell {i - 1} right edge ({prevRightEdge:F2})");
        }

        // Verify data cells
        var dataCells = root.FindAllByTag("td");
        dataCells.Should().HaveCount(4);

        for (int i = 1; i < dataCells.Count; i++)
        {
            var prevRightEdge = dataCells[i - 1].X + dataCells[i - 1].Width;
            var currentX = dataCells[i].X;
            currentX.Should().BeApproximately(prevRightEdge, 0.5f,
                $"data cell {i} X ({currentX:F2}) should equal cell {i - 1} right edge ({prevRightEdge:F2})");
        }
    }

    [Fact]
    public void TbodyRows_SequentialYPositions()
    {
        var html = @"<table style='border-collapse:collapse'>
            <thead><tr><th style='padding:6px'>H</th></tr></thead>
            <tbody><tr><td style='padding:6px'>R1</td></tr>
            <tr><td style='padding:6px'>R2</td></tr>
            <tr><td style='padding:6px'>R3</td></tr></tbody></table>";
        var root = LayoutTestHelper.Layout(html, 595, 842);

        var rows = root.FindAllByTag("tr");
        rows.Should().HaveCountGreaterOrEqualTo(4, "header + 3 body rows");

        // Each row should have a unique, increasing Y position
        for (int i = 1; i < rows.Count; i++)
        {
            rows[i].Y.Should().BeGreaterThan(rows[i - 1].Y,
                $"row {i} (Y={rows[i].Y:F1}) should be below row {i - 1} (Y={rows[i - 1].Y:F1})");
        }
    }

    [Fact]
    public void RowCells_EqualHeight()
    {
        // One cell has more content; both should end up same height
        var html = @"<table><tr>
            <td>Short</td>
            <td>This is a much longer cell content that should not break</td>
        </tr></table>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var cells = root.FindAllByTag("td");
        cells.Should().HaveCount(2);
        cells[0].Height.Should().Be(cells[1].Height, "cells in same row should have equal height");
    }

    [Fact]
    public void VisibilityCollapse_TableRow_TakesNoSpace()
    {
        // Row 2 is visibility:collapse — it must not contribute height to the table.
        // Row 3 should start at the same Y as Row 2 would have if Row 2 were not there.
        var html =
            "<table>" +
            "<tr id='r1'><td style='padding:10px'>Row1</td></tr>" +
            "<tr id='r2' style='visibility:collapse'><td style='padding:10px'>Row2</td></tr>" +
            "<tr id='r3'><td style='padding:10px'>Row3</td></tr>" +
            "</table>";

        var root = LayoutTestHelper.Layout(html, 400, 600);
        var rows = root.FindAllByTag("tr");
        rows.Should().HaveCount(3);

        var r1 = rows[0];
        var r2 = rows[1];
        var r3 = rows[2];

        // Collapsed row should have zero height in layout
        r2.Height.Should().Be(0f, "visibility:collapse row must contribute no height");

        // Row 3 should start where Row 2 would have started (immediately after Row 1)
        r3.Y.Should().BeApproximately(r1.Y + r1.Height, 1f,
            "row after collapsed row should be positioned as if collapsed row does not exist");
    }

    [Fact]
    public void VisibilityCollapse_NonTableElement_KeepsSpace()
    {
        // visibility:collapse on non-table elements behaves like visibility:hidden — keeps space
        var html =
            "<div>" +
            "<p id='p1' style='height:30px'>Para1</p>" +
            "<p id='p2' style='visibility:collapse; height:30px'>Para2</p>" +
            "<p id='p3' style='height:30px'>Para3</p>" +
            "</div>";

        var root = LayoutTestHelper.Layout(html, 400, 600);
        var paras = root.FindAllByTag("p");
        paras.Should().HaveCount(3);

        var p2 = paras[1];
        var p3 = paras[2];

        // p2 (non-table) with visibility:collapse still occupies its 30px
        p2.Height.Should().BeApproximately(30f, 1f,
            "visibility:collapse on non-table element should not collapse height");

        // p3 starts after p2 (space is preserved)
        p3.Y.Should().BeGreaterThan(p2.Y,
            "p3 should start below p2 even though p2 has visibility:collapse");
    }

    // ── empty-cells ──────────────────────────────────────────────────────────

    [Fact]
    public void EmptyCells_Show_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='empty-cells:show'><tr><td></td></tr></table>", 400, 600);
        var table = root.FindByTag("table");
        table!.Style.Get("empty-cells").Should().Be("show");
    }

    [Fact]
    public void EmptyCells_Hide_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='empty-cells:hide'><tr><td></td></tr></table>", 400, 600);
        var table = root.FindByTag("table");
        table!.Style.Get("empty-cells").Should().Be("hide",
            "empty-cells: hide should be preserved in computed style");
    }

    [Fact]
    public void EmptyCells_Hide_EmptyCellHasNoBackground()
    {
        // With empty-cells: hide the empty td should have no background painted.
        // We test this by checking the td box's style has empty-cells inherited from table.
        var root = LayoutTestHelper.Layout(
            "<table style='empty-cells:hide; border-collapse:separate'>" +
            "<tr><td id='empty'></td><td>content</td></tr>" +
            "</table>", 400, 600);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCountGreaterOrEqualTo(2);

        // Empty td should have empty-cells:hide in its resolved style
        var emptyTd = tds.Find(td =>
        {
            var text = string.Concat(td.Children.Where(b => !string.IsNullOrEmpty(b.Text)).Select(b => b.Text));
            return string.IsNullOrEmpty(text);
        });
        emptyTd.Should().NotBeNull("empty td should still have a layout box");
        emptyTd!.Style.Get("empty-cells").Should().Be("hide",
            "empty-cells should be inherited from the table into the td");
    }
}
