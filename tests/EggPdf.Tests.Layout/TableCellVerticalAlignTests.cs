using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class TableCellVerticalAlignTests
{
    private static (LayoutBox Cell, LayoutBox Text) Cell(string cellStyle, string rowExtra = "")
    {
        var root = LayoutTestHelper.Layout(
            "<table style='border-collapse:collapse'><tr>" +
            $"<td id='cell' style='padding:0; {cellStyle}'>Text</td>{rowExtra}" +
            "</tr></table>", 600, 800);
        var cell = root.FindById("cell")!;
        cell.Children.Should().NotBeEmpty();
        return (cell, cell.Children[0]);
    }

    [Fact]
    public void Middle_InACellWithAnExplicitHeight_CentersTheContent()
    {
        var (cell, text) = Cell("height:100px; vertical-align:middle");

        (text.Y - cell.Y).Should().BeApproximately((cell.Height - text.Height) / 2, 1.5f,
            "a specified height makes the cell taller than its content, and middle centers the content in it");
    }

    [Fact]
    public void Bottom_InACellWithAnExplicitHeight_PutsTheContentAtTheBottom()
    {
        var (cell, text) = Cell("height:100px; vertical-align:bottom");

        (text.Y - cell.Y).Should().BeApproximately(cell.Height - text.Height, 1.5f);
    }

    [Fact]
    public void Top_InACellWithAnExplicitHeight_KeepsTheContentAtTheTop()
    {
        var (cell, text) = Cell("height:100px; vertical-align:top");

        (text.Y - cell.Y).Should().BeApproximately(0f, 1.5f);
    }

    [Fact]
    public void Middle_InACellWithMinHeight_CentersTheContent()
    {
        var (cell, text) = Cell("min-height:100px; height:100px; vertical-align:middle");

        (text.Y - cell.Y).Should().BeGreaterThan(30f, "the content is centered in the 100px cell, not left at its top");
    }

    [Fact]
    public void Middle_InARowStretchedByATallerNeighbour_CentersTheContent()
    {
        var (cell, text) = Cell("vertical-align:middle", "<td style='height:120px'>Tall</td>");

        (text.Y - cell.Y).Should().BeApproximately((cell.Height - text.Height) / 2, 1.5f);
    }
}
