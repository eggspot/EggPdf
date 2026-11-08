using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class PresentationalAttributeLayoutTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<body style='margin:0'>" + body + "</body>", 600, 800);

    [Fact]
    public void TableAlignCenter_CentersTheTable()
    {
        var root = Layout("<table id='t' align='center' width='200'><tr><td>Cell</td></tr></table>");

        var table = root.FindById("t")!;
        table.Width.Should().BeApproximately(200, 0.5f);
        table.X.Should().BeApproximately(200, 0.5f);
    }

    [Fact]
    public void TableAlignCenter_DoesNotCenterCellText()
    {
        var root = Layout("<table align='center' width='300'><tr><td id='c'>Hi</td></tr></table>");

        var cell = root.FindById("c")!;
        var text = cell.FindAll(b => b.Text == "Hi")[0];
        (text.X - cell.X).Should().BeLessThan(10);
    }

    [Fact]
    public void WidthAttributeWithPxSuffix_IsHonoured()
    {
        var root = Layout("<table id='t' width='250px'><tr><td>Cell</td></tr></table>");

        root.FindById("t")!.Width.Should().BeApproximately(250, 0.5f);
    }

    [Fact]
    public void CellSpacingZero_PutsCellsEdgeToEdge()
    {
        var root = Layout("<table cellspacing='0' width='300'><tr><td id='a'>A</td><td id='b'>B</td></tr></table>");

        var a = root.FindById("a")!;
        var b = root.FindById("b")!;
        b.X.Should().BeApproximately(a.X + a.Width, 0.5f);
    }

    [Fact]
    public void OrderedListTypeAttribute_ChangesMarkers()
    {
        var markers = LayoutTestHelper.Layout("<ol type='a'><li>x</li><li>y</li></ol><ol type='I'><li>z</li></ol>", 600, 800)
            .FindAllByTag("li")
            .Select(li => li.Children.First(c => c.IsListMarker).Text!)
            .ToArray();

        markers.Should().Equal("a.", "b.", "I.");
    }

    [Fact]
    public void CellNowrapAttribute_KeepsTextOnOneLine()
    {
        var root = Layout("<table width='120'><tr><td id='c' nowrap>Ngày <b>01/01/2024</b> ký tên đóng dấu</td></tr></table>");

        root.FindById("c")!.FindAll(b => !string.IsNullOrEmpty(b.Text) && b.Children.Count == 0)
            .Select(b => (int)System.Math.Round(b.Y)).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void BodyMarginAttributes_RemoveDefaultBodyMargin()
    {
        var root = LayoutTestHelper.Layout("<body leftmargin='0' topmargin='0' marginwidth='0' marginheight='0'><div id='d'>x</div></body>", 600, 800);

        var div = root.FindById("d")!;
        div.X.Should().BeApproximately(0, 0.01f);
        div.Y.Should().BeApproximately(0, 0.01f);
    }

    [Fact]
    public void CellPadding_InsetsCellContent()
    {
        var root = Layout("<table cellspacing='0' cellpadding='12'><tr><td id='c'>Hi</td></tr></table>");

        var cell = root.FindById("c")!;
        cell.PaddingLeft.Should().BeApproximately(12, 0.01f);
        cell.PaddingTop.Should().BeApproximately(12, 0.01f);
        var text = cell.FindAll(b => b.Text == "Hi")[0];
        (text.X - cell.X).Should().BeApproximately(12, 0.5f);
    }
}
