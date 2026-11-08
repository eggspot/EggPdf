using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class TableGroupOrderTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<html><body style='margin:0'>" + body + "</body></html>", 600, 800);

    [Fact]
    public void TfootBeforeTbody_FooterRowsRenderAfterTheBody()
    {
        var root = Layout("<table><tfoot><tr><td id='f'>Total</td></tr></tfoot><tbody><tr><td id='b1'>A</td></tr><tr><td id='b2'>B</td></tr></tbody></table>");

        root.FindById("f")!.Y.Should().BeGreaterThan(root.FindById("b2")!.Y);
        root.FindById("b1")!.Y.Should().BeLessThan(root.FindById("b2")!.Y);
    }

    [Fact]
    public void TheadAfterTbody_HeaderRowsRenderFirst()
    {
        var root = Layout("<table><tbody><tr><td id='b'>A</td></tr></tbody><thead><tr><th id='h'>Head</th></tr></thead></table>");

        root.FindById("h")!.Y.Should().BeLessThan(root.FindById("b")!.Y);
    }

    [Fact]
    public void TfootBeforeTbody_ColumnWidthsComeFromTheFirstRenderedRow()
    {
        var root = Layout("<table style='table-layout:fixed;width:400px;border-spacing:0'><tfoot><tr><td style='width:300px'>T</td><td>U</td></tr></tfoot>" +
                          "<tbody><tr><td id='b' style='width:120px'>A</td><td>B</td></tr></tbody></table>");

        root.FindById("b")!.Width.Should().BeApproximately(120, 1f);
    }

    [Fact]
    public void GroupsInSourceOrder_KeepTheirOrder()
    {
        var root = Layout("<table><thead><tr><th id='h'>H</th></tr></thead><tbody><tr><td id='b'>B</td></tr></tbody><tfoot><tr><td id='f'>F</td></tr></tfoot></table>");

        root.FindById("h")!.Y.Should().BeLessThan(root.FindById("b")!.Y);
        root.FindById("b")!.Y.Should().BeLessThan(root.FindById("f")!.Y);
    }
}
