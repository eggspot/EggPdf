using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class AbsoluteContainingBlockTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<body style='margin:0'>" + body + "</body>", 600, 1200);

    [Fact]
    public void AbsoluteChildOfRelativeBoxBelowContent_TopCountsFromThatBox()
    {
        var root = Layout("<div style='height:300px'></div>" +
                          "<div id='rel' style='position:relative;height:100px'>" +
                          "<div id='abs' style='position:absolute;top:10px;left:200px;width:50px;height:20px'></div></div>");

        var rel = root.FindById("rel")!;
        var abs = root.FindById("abs")!;

        rel.Y.Should().BeApproximately(300f, 0.5f);
        abs.Y.Should().BeApproximately(310f, 0.5f);
        abs.X.Should().BeApproximately(200f, 0.5f);
    }

    [Fact]
    public void AbsoluteChildWithBottom_CountsFromRelativeBoxBottom()
    {
        var root = Layout("<div style='height:300px'></div>" +
                          "<div style='position:relative;height:100px'>" +
                          "<div id='abs' style='position:absolute;bottom:10px;left:0;width:50px;height:20px'></div></div>");

        root.FindById("abs")!.Y.Should().BeApproximately(370f, 0.5f);
    }

    [Fact]
    public void AbsoluteChildOfNestedRelativeBox_FollowsBothAncestors()
    {
        var root = Layout("<div style='height:200px'></div>" +
                          "<div style='padding-top:50px'><div id='rel' style='position:relative;height:100px'>" +
                          "<div id='abs' style='position:absolute;top:5px;left:0;width:10px;height:10px'></div></div></div>");

        var rel = root.FindById("rel")!;
        root.FindById("abs")!.Y.Should().BeApproximately(rel.Y + 5f, 0.5f);
        rel.Y.Should().BeApproximately(250f, 0.5f);
    }

    [Fact]
    public void BodyTopMarginShift_MovesAbsoluteChildWithItsRelativeParent()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:40px 0 0 0'><div style='height:100px'></div>" +
            "<div id='rel' style='position:relative;height:100px'>" +
            "<div id='abs' style='position:absolute;top:10px;left:0;width:10px;height:10px'></div></div></body>",
            600, 1200);

        var rel = root.FindById("rel")!;
        rel.Y.Should().BeApproximately(140f, 0.5f);
        root.FindById("abs")!.Y.Should().BeApproximately(rel.Y + 10f, 0.5f);
    }

    [Fact]
    public void AbsoluteChildOfRelativeFlexContainer_TopCountsFromThatContainer()
    {
        var root = Layout("<div style='height:300px'></div>" +
                          "<div id='flex' style='display:flex;position:relative;height:100px'>" +
                          "<div style='width:50px'>Item</div>" +
                          "<div id='abs' style='position:absolute;top:20px;right:0;width:10px;height:10px'></div></div>");

        var flex = root.FindById("flex")!;
        flex.Y.Should().BeApproximately(300f, 0.5f);
        root.FindById("abs")!.Y.Should().BeApproximately(320f, 0.5f);
    }

    [Fact]
    public void FixedChild_StaysAgainstThePage()
    {
        var root = Layout("<div style='height:300px'></div>" +
                          "<div style='position:relative;height:100px'>" +
                          "<div id='fixed' style='position:fixed;top:10px;left:0;width:10px;height:10px'></div></div>");

        root.FindById("fixed")!.Y.Should().BeApproximately(10f, 0.5f);
    }

    [Fact]
    public void AbsoluteChildOfStaticBox_StaysAgainstThePage()
    {
        var root = Layout("<div style='height:300px'></div>" +
                          "<div style='height:100px'>" +
                          "<div id='abs' style='position:absolute;top:10px;left:0;width:10px;height:10px'></div></div>");

        root.FindById("abs")!.Y.Should().BeApproximately(10f, 0.5f);
    }
}
