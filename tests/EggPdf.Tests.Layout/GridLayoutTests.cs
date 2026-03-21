using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class GridLayoutTests
{
    [Fact]
    public void GridContainer_HasGridDisplay()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='display: grid'><div>Item</div></div>", 600, 800);

        var grid = root.FindAllByTag("div")[0];
        grid.Should().NotBeNull();
        grid.Style.Display.Should().Be("grid");
    }

    [Fact]
    public void GridContainer_ChildrenLaidOut()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='display: grid; grid-template-columns: 1fr 1fr'>" +
            "<div>Item 1</div><div>Item 2</div>" +
            "</div>", 600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(3);
    }

    [Fact]
    public void GridContainer_DoesNotCrash()
    {
        var act = () => LayoutTestHelper.Layout(
            "<div style='display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px'>" +
            "<div>1</div><div>2</div><div>3</div>" +
            "<div>4</div><div>5</div><div>6</div>" +
            "</div>", 600, 800);

        act.Should().NotThrow();
    }

    [Fact]
    public void GridTemplateColumns_RecognizedAsProperty()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='display: grid; grid-template-columns: 200px 1fr'>" +
            "<div>A</div><div>B</div>" +
            "</div>", 600, 800);

        var grid = root.FindAllByTag("div")[0];
        grid.Style.Get("grid-template-columns").Should().Be("200px 1fr");
    }

    [Fact]
    public void InlineGrid_Recognized()
    {
        var root = LayoutTestHelper.Layout(
            "<span style='display: inline-grid'><span>A</span></span>", 600, 800);

        var span = root.FindAllByTag("span")[0];
        span.Should().NotBeNull();
    }
}
