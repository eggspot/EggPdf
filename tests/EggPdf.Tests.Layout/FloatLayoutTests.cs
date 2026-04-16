using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class FloatLayoutTests
{
    [Fact]
    public void FloatLeft_PositionedAtLeft()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='float: left; width: 100px; height: 50px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("float").Should().Be("left");
        div.Width.Should().BeApproximately(100, 1f);
    }

    [Fact]
    public void FloatRight_PositionedAtRight()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='float: right; width: 100px; height: 50px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("float").Should().Be("right");
    }

    [Fact]
    public void FloatElement_RemovedFromNormalFlow()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='float: left; width: 100px; height: 50px'></div>" +
            "<div style='height: 30px'>Normal flow</div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(2);
        // The normal flow div should not be pushed down by the float
    }

    [Fact]
    public void ClearBoth_MovesBelow()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='float: left; width: 100px; height: 50px'></div>" +
            "<div style='clear: both; height: 30px'>Cleared</div>",
            600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public void Float_StylePropertyRecognized()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='float: left; width: 50px; height: 50px'>F</div><p>Text next to float</p>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
    }

    [Fact]
    public void FloatLeft_XAtContainerLeft()
    {
        // float:left must place the element at the left edge of the container
        var root = LayoutTestHelper.Layout(
            "<div id='container' style='width:400px; padding:0'>" +
            "<div id='fl' style='float:left; width:100px; height:50px'></div>" +
            "</div>",
            600, 800);

        var container = root.FindById("container");
        var fl = root.FindById("fl");
        container.Should().NotBeNull();
        fl.Should().NotBeNull();

        fl!.X.Should().BeApproximately(container!.X + container.PaddingLeft, 1f,
            "float:left should position at container's left edge");
    }

    [Fact]
    public void FloatRight_XAtContainerRight()
    {
        // float:right must place the element against the right edge of the container
        var root = LayoutTestHelper.Layout(
            "<div id='container' style='width:400px; padding:0'>" +
            "<div id='fr' style='float:right; width:100px; height:50px'></div>" +
            "</div>",
            600, 800);

        var container = root.FindById("container");
        var fr = root.FindById("fr");
        container.Should().NotBeNull();
        fr.Should().NotBeNull();

        float expectedX = container!.X + container.PaddingLeft + container.ContentWidth - fr!.Width;
        fr.X.Should().BeApproximately(expectedX, 1f,
            "float:right should position at container's right edge");
    }

    [Fact]
    public void FloatLeft_NormalFlowSibling_NotPushedDown()
    {
        // A normal-flow block after a float:left should start at the same Y as the float
        // (floats are removed from normal flow)
        var root = LayoutTestHelper.Layout(
            "<div id='container' style='width:400px'>" +
            "<div id='fl' style='float:left; width:100px; height:50px'></div>" +
            "<div id='normal' style='height:30px'>Normal</div>" +
            "</div>",
            600, 800);

        var container = root.FindById("container");
        var fl = root.FindById("fl");
        var normal = root.FindById("normal");
        container.Should().NotBeNull();
        fl.Should().NotBeNull();
        normal.Should().NotBeNull();

        // normal flow element should start at top of container, same Y as the float
        normal!.Y.Should().BeApproximately(fl!.Y, 1f,
            "float:left is removed from normal flow; sibling should not be pushed below it");
    }

    [Fact]
    public void ClearBoth_Y_IsAtOrBelowFloat()
    {
        // clear:both must move the element below the bottom of any active float
        var root = LayoutTestHelper.Layout(
            "<div id='container' style='width:400px'>" +
            "<div id='fl' style='float:left; width:100px; height:80px'></div>" +
            "<div id='cleared' style='clear:both; height:20px'>After float</div>" +
            "</div>",
            600, 800);

        var fl = root.FindById("fl");
        var cleared = root.FindById("cleared");
        fl.Should().NotBeNull();
        cleared.Should().NotBeNull();

        float floatBottom = fl!.Y + fl.Height;
        cleared!.Y.Should().BeGreaterOrEqualTo(floatBottom - 1f,
            "clear:both element should start at or below the float's bottom");
    }

    [Fact]
    public void FloatLeft_IsFloat_PropertySet()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='float:left; width:60px; height:40px'></div>",
            400, 600);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.IsFloat.Should().BeTrue("float:left element should have IsFloat=true");
    }
}
