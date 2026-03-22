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

        // Cells should be side-by-side (different X, same Y)
        tds[1].X.Should().BeGreaterThan(tds[0].X, "Cell B should be to the right of Cell A");
    }

    [Fact]
    public void ThreeCells_EqualWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<table style='width: 600px'><tbody><tr><td>A</td><td>B</td><td>C</td></tr></tbody></table>", 600, 800);

        var tds = root.FindAllByTag("td");
        tds.Should().HaveCount(3);

        // Each cell should get roughly 1/3 of the table width
        float expectedWidth = 600f / 3f;
        tds[0].Width.Should().BeApproximately(expectedWidth, 10f);
        tds[1].Width.Should().BeApproximately(expectedWidth, 10f);
        tds[2].Width.Should().BeApproximately(expectedWidth, 10f);
    }

    [Fact]
    public void TwoRows_StackVertically()
    {
        var root = LayoutTestHelper.Layout(
            "<table><tbody><tr><td>Row 1</td></tr><tr><td>Row 2</td></tr></tbody></table>", 600, 800);

        var trs = root.FindAllByTag("tr");
        trs.Should().HaveCount(2);

        // Rows should stack vertically
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

        // Find text boxes inside cells
        var leftText = FindTextInSubtree(tds[0], "Left");
        var rightText = FindTextInSubtree(tds[1], "Right");

        leftText.Should().NotBeNull("'Left' text should be in first cell");
        rightText.Should().NotBeNull("'Right' text should be in second cell");

        if (leftText != null && rightText != null)
        {
            rightText.X.Should().BeGreaterThan(leftText.X, "Right text should be positioned after Left text");
        }
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
        trs.Should().HaveCountGreaterOrEqualTo(2);

        // First row: 2 cells (one spanning 2 columns)
        var row1Cells = GetCellsInRow(trs[0]);
        row1Cells.Should().HaveCount(2);

        // The spanning cell should be roughly 2/3 of the table width
        float expectedSpanWidth = 600f / 3f * 2f;
        row1Cells[0].Width.Should().BeApproximately(expectedSpanWidth, 20f,
            "colspan=2 cell should be twice the width of a single column");

        // The narrow cell should be roughly 1/3
        row1Cells[1].Width.Should().BeApproximately(600f / 3f, 20f);
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

        // First cell at column 0, second cell at column 1 (spanning 2 columns)
        tds[1].X.Should().BeGreaterThan(tds[0].X);
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
