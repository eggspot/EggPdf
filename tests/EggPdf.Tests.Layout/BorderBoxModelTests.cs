using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class BorderBoxModelTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<html><body style='margin:0'>" + body + "</body></html>", 600, 800);

    [Fact]
    public void ContentBox_BorderAddsToTheSizeAndInsetsTheContent()
    {
        var root = Layout("<div id='d' style='border:5px solid red;padding:10px;width:100px'><p id='c' style='margin:0'>x</p></div>");
        var d = root.FindById("d")!;
        var c = root.FindById("c")!;

        d.Width.Should().BeApproximately(130, 0.01f);
        c.X.Should().BeApproximately(d.X + 15, 0.01f);
        c.Y.Should().BeApproximately(d.Y + 15, 0.01f);
        d.Height.Should().BeApproximately(c.Height + 30, 0.01f);
    }

    [Fact]
    public void BorderBox_BorderIsInsideTheSpecifiedWidth()
    {
        var root = Layout("<div id='d' style='box-sizing:border-box;border:5px solid red;padding:10px;width:100px'><p style='margin:0'>x</p></div>");
        var d = root.FindById("d")!;

        d.Width.Should().BeApproximately(100, 0.01f);
        d.ContentWidth.Should().BeApproximately(70, 0.01f);
    }

    [Fact]
    public void BorderStyleNone_TakesNoSpace()
    {
        var root = Layout("<div id='d' style='border-width:5px;border-style:none;width:100px'></div>");

        root.FindById("d")!.Width.Should().BeApproximately(100, 0.01f);
    }

    [Fact]
    public void BorderWithoutWidth_IsMediumThreePixels()
    {
        var root = Layout("<div id='d' style='border-style:solid;width:100px'></div>");

        root.FindById("d")!.Width.Should().BeApproximately(106, 0.01f);
    }
}
