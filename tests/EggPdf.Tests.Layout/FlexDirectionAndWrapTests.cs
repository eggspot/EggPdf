using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class FlexDirectionAndWrapTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<html><head><style>body{margin:0} div{font-family:Helvetica;font-size:16px}</style></head><body>" +
            body + "</body></html>", 800, 1000);

    [Fact]
    public void FlexWrap_ItemMargins_CountTowardWrapping()
    {
        var root = Layout("<div style='display:flex;flex-wrap:wrap;width:200px'>" +
            "<div id='a' style='width:90px;margin:0 10px'>a</div><div id='b' style='width:90px;margin:0 10px'>b</div></div>");

        root.FindById("b")!.Y.Should().BeGreaterThan(root.FindById("a")!.Y);
    }

    [Fact]
    public void FlexGrow_ItemClampedByMaxWidth_LeavesItsShareToTheOthers()
    {
        var root = Layout("<div style='display:flex;width:400px'>" +
            "<div id='a' style='flex:1;max-width:100px'>a</div><div id='b' style='flex:1'>b</div></div>");

        root.FindById("a")!.Width.Should().BeApproximately(100f, 0.5f);
        root.FindById("b")!.Width.Should().BeApproximately(300f, 0.5f);
    }

    [Fact]
    public void AlignContent_SpaceBetween_KeepsTheRowGap()
    {
        var root = Layout("<div id='c' style='display:flex;flex-wrap:wrap;width:100px;height:300px;row-gap:20px;align-content:space-between'>" +
            "<div id='a' style='width:100px;height:40px'>a</div><div id='b' style='width:100px;height:40px'>b</div></div>");

        var c = root.FindById("c")!;
        var b = root.FindById("b")!;
        (b.Y + b.Height).Should().BeApproximately(c.Y + 300f, 0.5f);
        root.FindById("a")!.Y.Should().BeApproximately(c.Y, 0.5f);
    }

    [Fact]
    public void FlexRowReverse_JustifyStart_PacksAtTheRight()
    {
        var root = Layout("<div style='display:flex;flex-direction:row-reverse;width:400px'>" +
            "<div id='a' style='width:50px'>A</div><div id='b' style='width:50px'>B</div></div>");

        root.FindById("a")!.X.Should().BeApproximately(350f, 0.5f);
        root.FindById("b")!.X.Should().BeApproximately(300f, 0.5f);
    }

    [Fact]
    public void FlexRowReverse_JustifyEnd_PacksAtTheLeft()
    {
        var root = Layout("<div style='display:flex;flex-direction:row-reverse;justify-content:flex-end;width:400px'>" +
            "<div id='a' style='width:50px'>A</div><div id='b' style='width:50px'>B</div></div>");

        root.FindById("a")!.X.Should().BeApproximately(50f, 0.5f);
        root.FindById("b")!.X.Should().BeApproximately(0f, 0.5f);
    }

    [Fact]
    public void AlignSelfAuto_UsesTheContainersAlignItems()
    {
        var root = Layout("<div id='c' style='display:flex;align-items:center;height:100px'>" +
            "<div id='a' style='align-self:auto;height:20px'>a</div></div>");

        (root.FindById("a")!.Y - root.FindById("c")!.Y).Should().BeApproximately(40f, 0.5f);
    }
}
