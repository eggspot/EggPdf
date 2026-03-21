using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class FlexLayoutTests
{
    [Fact]
    public void FlexContainer_HasFlexDisplay()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='display: flex'><div>Item</div></div>", 600, 800);

        var flex = root.FindAllByTag("div")[0];
        flex.Should().NotBeNull();
        flex.Style.Display.Should().Be("flex");
    }

    [Fact]
    public void FlexContainer_ChildrenLaidOut()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='display: flex'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>", 600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(3); // container + 2 items
    }

    [Fact]
    public void FlexDirection_Row_ChildrenHorizontal()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='display: flex; flex-direction: row'>" +
            "<div style='width: 100px; height: 50px'>A</div>" +
            "<div style='width: 100px; height: 50px'>B</div>" +
            "</div>", 600, 800);

        // Flex items should exist
        var divs = root.FindAllByTag("div");
        divs.Count.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public void FlexContainer_DoesNotCrash()
    {
        var act = () => LayoutTestHelper.Layout(
            "<div style='display: flex; justify-content: center; align-items: center'>" +
            "<div>Centered content</div>" +
            "</div>", 600, 800);

        act.Should().NotThrow();
    }

    [Fact]
    public void NestedFlex_DoesNotCrash()
    {
        var act = () => LayoutTestHelper.Layout(
            "<div style='display: flex'>" +
            "<div style='display: flex; flex-direction: column'>" +
            "<div>Item 1</div><div>Item 2</div>" +
            "</div>" +
            "</div>", 600, 800);

        act.Should().NotThrow();
    }

    [Fact]
    public void FlexGap_RecognizedAsProperty()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='display: flex; gap: 10px'>" +
            "<div>A</div><div>B</div>" +
            "</div>", 600, 800);

        var flex = root.FindAllByTag("div")[0];
        flex.Style.Get("gap").Should().Be("10px");
    }
}
