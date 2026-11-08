using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class MarginCollapseTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<html><body style='margin:0'>" + body + "</body></html>", 600, 800);

    [Fact]
    public void FirstChildTopMargin_CollapsesThroughAParentWithoutPaddingOrBorder()
    {
        var root = Layout("<div id='a' style='height:10px'></div><div id='p' style='margin-top:10px'><div id='c' style='margin-top:30px;height:5px'></div></div>");
        var a = root.FindById("a")!;
        var p = root.FindById("p")!;
        var c = root.FindById("c")!;

        c.Y.Should().BeApproximately(a.Y + 10 + 30, 0.5f, "the 10px and 30px margins collapse to 30px");
        p.Y.Should().BeApproximately(c.Y, 0.5f, "the parent's top edge moves down with the collapsed margin");
    }

    [Fact]
    public void PaddingOnTheParent_StopsTheCollapse()
    {
        var root = Layout("<div id='p' style='padding-top:1px'><div id='c' style='margin-top:30px;height:5px'></div></div>");

        root.FindById("c")!.Y.Should().BeApproximately(root.FindById("p")!.Y + 31, 0.5f);
    }

    [Fact]
    public void LastChildBottomMargin_StaysInsideAParentWithPadding()
    {
        var root = Layout("<div id='p' style='padding-bottom:1px'><div style='height:10px;margin-bottom:20px'></div></div>");

        root.FindById("p")!.Height.Should().BeApproximately(31, 0.5f);
    }

    [Fact]
    public void LastChildBottomMargin_CollapsesWithTheParentsFollowingSibling()
    {
        var root = Layout("<div id='p'><div style='height:10px;margin-bottom:20px'></div></div><div id='n' style='margin-top:5px'></div>");

        root.FindById("n")!.Y.Should().BeApproximately(root.FindById("p")!.Y + 10 + 20, 0.5f);
    }

    [Fact]
    public void NegativeMargin_ReducesTheGap()
    {
        var root = Layout("<div id='a' style='height:10px;margin-bottom:20px'></div><div id='b' style='margin-top:-5px'></div>");

        root.FindById("b")!.Y.Should().BeApproximately(root.FindById("a")!.Y + 10 + 15, 0.5f);
    }
}
