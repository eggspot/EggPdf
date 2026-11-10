using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class TableCellLayoutTests
{
    [Fact]
    public void TwoCells_SideBySide_NotStacked()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody><tr><td>Cell A</td><td>Cell B</td></tr></tbody></table>", 600, 800);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCount(2);

        tds[1].X.Should().BeGreaterThan(tds[0].X, "Cell B should be to the right of Cell A");
    }

    [Fact]
    public void DisplayTableCell_DirectChildOfDisplayTable_NoRowWrapper_SideBySide()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='display:table; width:600px'>" +
            "<div class='cell' style='display:table-cell'>Left</div>" +
            "<div class='cell' style='display:table-cell'>Right</div>" +
            "</div>", 600, 800);

        var cells = root.FindAll(b => b.Element?.GetAttribute("class") == "cell");
        cells.Should().HaveCount(2);

        cells[0].Y.Should().Be(cells[1].Y,
            "the equal-height-columns pattern has no tr; per CSS 2.1 the cells get an anonymous table row, " +
            "so they lay out on one row");
        cells[1].X.Should().BeGreaterThan(cells[0].X, "the second cell must sit to the right of the first, not stacked below it");
    }

    [Fact]
    public void ThreeCells_EqualWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 600px'><tbody><tr><td>A</td><td>B</td><td>C</td></tr></tbody></table>", 600, 800);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCount(3);

        float expectedWidth = 600f / 3f;
        tds[0].Width.Should().BeApproximately(expectedWidth, 10f, "each cell gets about a third of the table");
        tds[1].Width.Should().BeApproximately(expectedWidth, 10f, "each cell gets about a third of the table");
        tds[2].Width.Should().BeApproximately(expectedWidth, 10f, "each cell gets about a third of the table");
    }

    [Fact]
    public void TwoRows_StackVertically()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody><tr><td>Row 1</td></tr><tr><td>Row 2</td></tr></tbody></table>", 600, 800);

        var trs = root.FindAllByTag("tr");
        trs.Should().HaveCount(2);

        trs[1].Y.Should().BeGreaterThan(trs[0].Y, "Row 2 should be below Row 1");
    }

    [Fact]
    public void HeaderAndDataCells_SideBySide()
    {
        var root = LayoutTestHelper.Layout(
            "<table><thead><tr><th>Name</th><th>Value</th></tr></thead>" +
            "<tbody><tr><td>Alpha</td><td>100</td></tr></tbody></table>", 600, 800);

        var ths = root.FindAllByTag("th");
        ths.Should().HaveCount(2);
        ths[1].X.Should().BeGreaterThan(ths[0].X, "Header cells should be side-by-side");

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCount(2);
        tds[1].X.Should().BeGreaterThan(tds[0].X, "Data cells should be side-by-side");
    }

    [Fact]
    public void CellText_PositionedInsideCell()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 400px'><tbody><tr><td>Left</td><td>Right</td></tr></tbody></table>", 600, 800);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCount(2);

        var leftText = FindTextInSubtree(tds[0], "Left");
        var rightText = FindTextInSubtree(tds[1], "Right");

        leftText.Should().NotBeNull("'Left' text should be in first cell");
        rightText.Should().NotBeNull("'Right' text should be in second cell");
        rightText!.X.Should().BeGreaterThan(leftText!.X, "Right text should be positioned after Left text");
    }

    [Fact]
    public void Colspan_CellSpansMultipleColumns()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 600px'><tbody>" +
            "<tr><td colspan='2'>Wide</td><td>Narrow</td></tr>" +
            "<tr><td>A</td><td>B</td><td>C</td></tr>" +
            "</tbody></table>", 600, 800);

        var trs = root.FindAllByTag("tr");
        trs.Should().HaveCount(2);

        var row1Cells = GetCellsInRow(trs[0]);
        row1Cells.Should().HaveCount(2, "the first row has a colspan=2 cell and a single-column cell");

        row1Cells[0].Width.Should().BeGreaterThan(50f,
            "colspan=2 cell should have substantial width");
        row1Cells[1].Width.Should().BeGreaterThan(50f,
            "single column cell should have substantial width");

        float totalCellWidth = row1Cells[0].Width + row1Cells[1].Width;
        totalCellWidth.Should().BeApproximately(600f, 30f,
            "cell widths should sum to approximately the table width");
    }

    [Fact]
    public void Colspan_CellPositionedCorrectly()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 300px'><tbody>" +
            "<tr><td>A</td><td colspan='2'>BC</td></tr>" +
            "</tbody></table>", 600, 800);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCount(2);

        tds[1].X.Should().BeGreaterThan(tds[0].X, "the colspan=2 cell starts at the second column");
    }

    [Fact]
    public void ThHeader_PercentageWidth_ContentWidthReflectsColumnWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 500px; border-collapse: collapse'><thead><tr>" +
            "<th style='width: 50%'>Description</th>" +
            "<th style='width: 30%'>Rate</th>" +
            "<th style='width: 20%'>Line Total</th>" +
            "</tr></thead></table>", 600, 800);

        var ths = root.FindAllByTag("th");
        ths.Should().HaveCount(3);

        ths[2].Width.Should().BeApproximately(100f, 5f,
            "th with width:20% should get 20% of the 500px TABLE (100px), not 20% of itself (20px)");

        var lineBox = FindTextInSubtree(ths[2], "Line Total");
        lineBox.Should().NotBeNull("'Line Total' fits on one line, so it stays a single text box instead of wrapping");
    }

    [Fact]
    public void TdCell_PercentageWidth_ContentWidthReflectsColumnWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 500px; border-collapse: collapse'><tbody><tr>" +
            "<td style='width: 80%'>Long description text here</td>" +
            "<td style='width: 20%'>Value</td>" +
            "</tr></tbody></table>", 600, 800);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCount(2);

        tds[1].Width.Should().BeApproximately(100f, 5f,
            "td with width:20% should get 20% of the 500px TABLE (100px), not 20% of itself");
    }

    private static System.Collections.Generic.List<LayoutBox> GetCellsInRow(LayoutBox row)
    {
        var cells = new System.Collections.Generic.List<LayoutBox>();
        foreach (var child in row.Children)
        {
            if (child.Element?.TagName == "td" || child.Element?.TagName == "th")
                cells.Add(child);
        }
        return cells;
    }

    private static LayoutBox? FindTextInSubtree(LayoutBox box, string text)
    {
        if (box.Text == text) return box;
        foreach (var child in box.Children)
        {
            var found = FindTextInSubtree(child, text);
            if (found != null) return found;
        }
        return null;
    }
}
