using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class TableGridTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<html><head><style>body{margin:0} table{border-spacing:0} td{padding:0}</style></head><body>" + body + "</body></html>", 600, 800);

    [Fact]
    public void Rowspan_CellInTheNextRowStartsInTheNextFreeColumn()
    {
        var root = Layout("<table><tr><td id='a' rowspan='2'>A</td><td id='b'>B</td></tr><tr><td id='c'>C</td></tr></table>");
        var b = root.FindById("b")!;
        var c = root.FindById("c")!;

        c.X.Should().BeApproximately(b.X, 0.5f, "the rowspan of A still occupies the first column in row 2");
    }

    [Fact]
    public void Rowspan_CellCoversTheHeightOfTheRowsItSpans()
    {
        var root = Layout("<table><tr><td id='a' rowspan='2'>A</td><td id='b'>B</td></tr><tr><td id='c'>C</td></tr></table>");
        var a = root.FindById("a")!;
        var c = root.FindById("c")!;

        (a.Y + a.Height).Should().BeApproximately(c.Y + c.Height, 0.5f);
    }

    [Fact]
    public void TallRowspanCell_GrowsTheLastSpannedRow()
    {
        var root = Layout("<table><tr><td id='a' rowspan='2'><div style='height:200px'></div></td><td id='b'>B</td></tr>" +
                          "<tr><td id='c'>C</td></tr><tr><td id='d'>D</td></tr></table>");
        var a = root.FindById("a")!;
        var d = root.FindById("d")!;

        d.Y.Should().BeGreaterThanOrEqualTo(a.Y + 200 - 0.5f, "rows after the span start below the tall cell");
    }

    [Fact]
    public void AutoTableWithoutWidth_IsAsWideAsItsContent()
    {
        var root = Layout("<table id='t'><tr><td>Short</td><td>Text</td></tr></table>");

        root.FindById("t")!.Width.Should().BeLessThan(300, "a table without a width shrinks to its content");
    }

    [Fact]
    public void LongWord_KeepsItsColumnFromBeingSqueezedBelowIt()
    {
        var root = Layout("<table style='width:200px'><tr><td id='w'>Supercalifragilisticexpialidocious</td>" +
                          "<td>lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt</td></tr></table>");

        float word = TextMeasurer.MeasureWidth("Supercalifragilisticexpialidocious", 16, null);
        root.FindById("w")!.Width.Should().BeGreaterThanOrEqualTo(word - 0.5f);
    }

    [Fact]
    public void ColumnWidths_UseTheCellsOwnFontSize()
    {
        var root = Layout("<table><tr><td id='big' style='font-size:32px'>Wide</td><td id='small'>Wide</td></tr></table>");

        root.FindById("big")!.Width.Should().BeGreaterThan(root.FindById("small")!.Width * 1.5f);
    }
}
