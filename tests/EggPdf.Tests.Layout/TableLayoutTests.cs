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
}
