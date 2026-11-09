using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class GridLineAndTrackTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<html><head><style>body{margin:0} div{font-family:Helvetica;font-size:16px}</style></head><body>" +
            body + "</body></html>", 800, 1000);

    [Fact]
    public void GridColumn_OneToMinusOne_SpansAllExplicitColumns()
    {
        var root = Layout("<div style='display:grid;grid-template-columns:100px 100px 100px'>" +
            "<div id='full' style='grid-column:1 / -1'>full</div><div>a</div></div>");

        root.FindById("full")!.Width.Should().BeApproximately(300f, 0.5f);
    }

    [Fact]
    public void GridColumn_SpanToMinusOne_EndsAtTheLastLine()
    {
        var root = Layout("<div style='display:grid;grid-template-columns:100px 100px 100px'>" +
            "<div id='tail' style='grid-column:span 2 / -1'>tail</div></div>");

        var tail = root.FindById("tail")!;
        tail.X.Should().BeApproximately(100f, 0.5f);
        tail.Width.Should().BeApproximately(200f, 0.5f);
    }

    [Fact]
    public void GridAutoFill_WithGap_CountsTheGapWhenFittingColumns()
    {
        var root = Layout("<div style='display:grid;width:620px;gap:20px;grid-template-columns:repeat(auto-fill, minmax(200px, 1fr))'>" +
            "<div id='a'>a</div><div id='b'>b</div><div id='c'>c</div></div>");

        root.FindById("b")!.Y.Should().BeApproximately(root.FindById("a")!.Y, 0.5f);
        root.FindById("c")!.Y.Should().BeGreaterThan(root.FindById("a")!.Y, "only two 200px columns fit in 620px with a 20px gap");
        root.FindById("a")!.Width.Should().BeApproximately(300f, 0.5f);
    }

    [Fact]
    public void GridMinmax_FractionBelowTheMinimum_KeepsTheMinimum()
    {
        var root = Layout("<div style='display:grid;width:300px;grid-template-columns:minmax(150px, 1fr) 1fr 1fr'>" +
            "<div id='a'>a</div><div id='b'>b</div><div id='c'>c</div></div>");

        root.FindById("a")!.Width.Should().BeApproximately(150f, 0.5f);
        root.FindById("b")!.Width.Should().BeApproximately(75f, 0.5f);
    }

    [Fact]
    public void GridContainer_BareText_BecomesAGridItem()
    {
        var root = Layout("<div id='g' style='display:grid;grid-template-columns:100px 100px'>Bare text<span id='s'>span</span></div>");

        var text = root.FindById("g")!.FindAll(b => b.Text != null && b.Text.Contains("Bare")).Single();
        text.X.Should().BeApproximately(0f, 0.5f);
        root.FindById("s")!.X.Should().BeApproximately(100f, 0.5f);
    }

    [Fact]
    public void GridContainer_LongBareText_WrapsInsideItsCell()
    {
        var root = Layout("<div id='g' style='display:grid;grid-template-columns:100px 100px'>one two three four five six seven<span>x</span></div>");

        var lines = root.FindById("g")!.FindAll(b => b.Text != null && b.Children.Count == 0 && b.Text != "x");
        lines.Should().HaveCountGreaterThan(1);
        lines.Should().OnlyContain(l => l.X + l.ContentWidth <= 100.5f);
    }
}
