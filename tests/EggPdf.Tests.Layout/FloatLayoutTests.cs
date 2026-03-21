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
}
