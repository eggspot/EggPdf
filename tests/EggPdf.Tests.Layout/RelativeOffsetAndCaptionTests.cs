using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class RelativeOffsetAndCaptionTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<html><head><style>body{margin:0} div,p,td,caption{font-family:Helvetica;font-size:16px} p{margin:0}</style></head><body>" +
            body + "</body></html>", 800, 1000);

    [Fact]
    public void RelativePosition_Bottom_MovesTheBoxUp()
    {
        var root = Layout("<div style='height:50px'></div><div id='r' style='position:relative;bottom:10px;height:20px'></div>");

        root.FindById("r")!.Y.Should().BeApproximately(40f, 0.5f);
    }

    [Fact]
    public void RelativePosition_Right_MovesTheBoxLeft()
    {
        var root = Layout("<div style='padding-left:50px'><div id='r' style='position:relative;right:10px;height:20px'></div></div>");

        root.FindById("r")!.X.Should().BeApproximately(40f, 0.5f);
    }

    [Fact]
    public void RelativePosition_TopWinsOverBottom()
    {
        var root = Layout("<div style='height:50px'></div><div id='r' style='position:relative;top:5px;bottom:10px;height:20px'></div>");

        root.FindById("r")!.Y.Should().BeApproximately(55f, 0.5f);
    }

    [Fact]
    public void CaptionSideBottom_CaptionContent_StaysInsideTheCaption()
    {
        var root = Layout("<table style='caption-side:bottom'><caption id='cap'><span id='s'>Cap</span></caption><tr><td>cell</td></tr></table>");

        var cap = root.FindById("cap")!;
        var span = root.FindById("s")!;
        span.Y.Should().BeGreaterThanOrEqualTo(cap.Y - 0.5f);
        span.Y.Should().BeLessThan(cap.Y + cap.Height);
    }

    [Fact]
    public void BreakWordBesideFloat_ChunksStayClearOfTheFloat()
    {
        var root = Layout("<div style='width:200px'><div style='float:right;width:100px;height:300px'></div>" +
            "<p id='p' style='overflow-wrap:break-word'>See <b>x</b> https://example.com/a/very/long/path/that/never/ends</p></div>");

        var words = root.FindById("p")!.FindAll(b => b.Text != null && b.Children.Count == 0);
        words.Should().OnlyContain(w => w.X + w.Width <= 100.5f);
    }
}
