using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class FlexLayoutTests
{
    private const float PageWidth = 600f;
    private const float PageHeight = 800f;
    private const float BodyMargin = 8f;

    private LayoutBox LayoutFlex(string html)
        => LayoutTestHelper.Layout(html, PageWidth, PageHeight);

    /// <summary>Helper to find all divs except the outermost body wrapper.</summary>
    private static System.Collections.Generic.List<LayoutBox> FindInnerDivs(LayoutBox root)
        => root.FindAllByTag("div");

    [Fact]
    public void FlexRow_ChildrenLaidOutHorizontally()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var divs = FindInnerDivs(root);
        divs.Count.Should().BeGreaterOrEqualTo(3);

        var container = divs[0];
        container.Style.Display.Should().Be("flex");

        var item1 = container.Children[0];
        var item2 = container.Children[1];

        item1.Width.Should().Be(100f);
        item2.Width.Should().Be(100f);
        item2.X.Should().BeApproximately(item1.X + 100f, 1f, "the second item sits right after the first");
        item1.Y.Should().BeApproximately(item2.Y, 1f, "both items are on one row");
    }

    [Fact]
    public void FlexColumn_ContentOverflowsMinHeight_ItemNotShrunkBelowContent()
    {
        string longText = string.Concat(System.Linq.Enumerable.Repeat(
            "Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt. ", 20));

        var root = LayoutFlex(
            "<div style='display: flex; flex-direction: column; min-height: 40px; width: 200px'>" +
            $"<p style='margin:0'>{longText}</p>" +
            "<div style='width: 50px; height: 20px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var paragraph = root.FindByTag("p");
        paragraph.Should().NotBeNull();

        paragraph!.Height.Should().BeGreaterThan(40f,
            "text does not reflow when squeezed vertically, so flex-shrink must not compress the paragraph below the " +
            "height its wrapped lines need, far more than the container's 40px min-height");

        var secondItem = container.Children[1];
        secondItem.Y.Should().BeGreaterOrEqualTo(paragraph.Y + paragraph.Height - 0.5f,
            "the second flex item must be positioned after the paragraph's true content height, not overlapping it");
    }

    [Fact]
    public void FlexColumn_OverflowHidden_ItemShrinksToFitContainer()
    {
        string longText = string.Concat(System.Linq.Enumerable.Repeat(
            "Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt. ", 20));

        var root = LayoutFlex(
            "<div style='display: flex; flex-direction: column; height: 80px; width: 200px'>" +
            $"<p style='margin:0; overflow:hidden'>{longText}</p>" +
            "</div>");

        var paragraph = root.FindByTag("p");
        paragraph.Should().NotBeNull();
        paragraph!.Height.Should().BeLessOrEqualTo(80f + 0.5f,
            "the automatic minimum size (css-flexbox §4.5) applies only to overflow:visible, so a clipped item shrinks to the container");
    }

    [Fact]
    public void FlexColumn_ExplicitHeightLargerThanContent_StillShrinksToFitContainer()
    {
        var root = LayoutFlex(
            "<div style='display: flex; flex-direction: column; height: 60px; width: 200px'>" +
            "<p style='margin: 0; height: 500px; overflow: visible'>short</p>" +
            "</div>");

        var paragraph = root.FindByTag("p");
        paragraph.Should().NotBeNull();
        paragraph!.Height.Should().BeApproximately(60f, 0.5f,
            "the automatic minimum (§4.5) is a content-based floor, so an explicit height taller than the container still shrinks");
    }

    [Fact]
    public void FlexColumn_MaxHeightBelowContent_AutoMinimumClampedToMaxHeight()
    {
        string longText = string.Concat(System.Linq.Enumerable.Repeat(
            "Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt. ", 20));

        var root = LayoutFlex(
            "<div style='display: flex; flex-direction: column; min-height: 40px; width: 200px'>" +
            $"<p style='margin:0; max-height:30px'>{longText}</p>" +
            "<div style='width: 50px; height: 20px'></div>" +
            "</div>");

        var paragraph = root.FindByTag("p");
        paragraph.Should().NotBeNull();
        paragraph!.Height.Should().BeApproximately(30f, 0.5f,
            "max-height caps the automatic minimum size");
    }

    [Fact]
    public void FlexColumn_ChildrenLaidOutVertically()
    {
        var root = LayoutFlex(
            "<div style='display: flex; flex-direction: column'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='width: 100px; height: 60px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        item1.Height.Should().Be(50f);
        item2.Height.Should().Be(60f);
        item2.Y.Should().BeApproximately(item1.Y + 50f, 1f, "a column stacks the second item below the first");
        item1.X.Should().BeApproximately(item2.X, 1f);
    }

    [Fact]
    public void FlexGrow_DistributesSpace()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='flex-grow: 1; height: 50px'></div>" +
            "<div style='flex-grow: 1; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float containerWidth = container.ContentWidth;

        item1.Width.Should().BeApproximately(containerWidth / 2, 1f);
        item2.Width.Should().BeApproximately(containerWidth / 2, 1f);
    }

    [Fact]
    public void FlexGrow_Proportional()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='flex-grow: 1; height: 50px'></div>" +
            "<div style='flex-grow: 2; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float containerWidth = container.ContentWidth;

        item1.Width.Should().BeApproximately(containerWidth / 3, 1f, "flex-grow 1 against 2 takes a third of the free space");
        item2.Width.Should().BeApproximately(containerWidth * 2 / 3, 1f);
    }

    [Fact]
    public void FlexShrink_AbsorbsNegativeSpace()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='width: 400px; height: 50px'></div>" +
            "<div style='width: 300px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float containerWidth = container.ContentWidth;

        (item1.Width + item2.Width).Should().BeApproximately(containerWidth, 1f,
            "the 700px of items shrink to fit the 584px container");
        item1.Width.Should().BeGreaterThan(item2.Width,
            "the 116px overflow is taken in proportion to flex-shrink times base size: 66.3px from 400px, 49.7px from 300px");
    }

    [Fact]
    public void JustifyContent_Center()
    {
        var root = LayoutFlex(
            "<div style='display: flex; justify-content: center'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float containerWidth = container.ContentWidth;
        float totalItems = 200f;
        float freeSpace = containerWidth - totalItems;

        float expectedStart = container.X + container.PaddingLeft + freeSpace / 2;
        item1.X.Should().BeApproximately(expectedStart, 1f);
        item2.X.Should().BeApproximately(expectedStart + 100f, 1f);
    }

    [Fact]
    public void JustifyContent_SpaceBetween()
    {
        var root = LayoutFlex(
            "<div style='display: flex; justify-content: space-between'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float containerWidth = container.ContentWidth;

        item1.X.Should().BeApproximately(container.X + container.PaddingLeft, 1f);
        item2.X.Should().BeApproximately(container.X + container.PaddingLeft + containerWidth - 100f, 1f);
    }

    [Fact]
    public void JustifyContent_SpaceAround()
    {
        var root = LayoutFlex(
            "<div style='display: flex; justify-content: space-around'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float containerWidth = container.ContentWidth;
        float freeSpace = containerWidth - 200f;
        float spaceAroundEachItem = freeSpace / 2;

        float expectedX1 = container.X + container.PaddingLeft + spaceAroundEachItem / 2;
        item1.X.Should().BeApproximately(expectedX1, 1f);

        float expectedX2 = expectedX1 + 100f + spaceAroundEachItem;
        item2.X.Should().BeApproximately(expectedX2, 1f);
    }

    [Fact]
    public void JustifyContent_SpaceEvenly()
    {
        var root = LayoutFlex(
            "<div style='display: flex; justify-content: space-evenly'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float containerWidth = container.ContentWidth;
        float freeSpace = containerWidth - 200f;
        float evenGap = freeSpace / 3;

        float expectedX1 = container.X + container.PaddingLeft + evenGap;
        item1.X.Should().BeApproximately(expectedX1, 1f, "space-evenly splits the free space into one more gap than items");

        float expectedX2 = expectedX1 + 100f + evenGap;
        item2.X.Should().BeApproximately(expectedX2, 1f);
    }

    [Fact]
    public void AlignItems_Center()
    {
        var root = LayoutFlex(
            "<div style='display: flex; align-items: center; height: 200px'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item = container.Children[0];

        float expectedY = container.Y + container.PaddingTop + (200f - 50f) / 2;
        item.Y.Should().BeApproximately(expectedY, 1f, "the line's cross size is the container's explicit 200px height");
    }

    [Fact]
    public void AlignItems_Stretch()
    {
        var root = LayoutFlex(
            "<div style='display: flex; height: 200px'>" +
            "<div style='width: 100px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item = container.Children[0];

        item.Height.Should().BeApproximately(200f, 1f, "align-items defaults to stretch");
    }

    [Fact]
    public void FlexWrap_ItemsWrapToNextLine()
    {
        var root = LayoutFlex(
            "<div style='display: flex; flex-wrap: wrap'>" +
            "<div style='width: 200px; height: 50px'></div>" +
            "<div style='width: 200px; height: 50px'></div>" +
            "<div style='width: 200px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        container.Children.Count.Should().Be(3);

        var item1 = container.Children[0];
        var item2 = container.Children[1];
        var item3 = container.Children[2];

        item1.Y.Should().BeApproximately(item2.Y, 1f, "two 200px items fit the 584px container");
        item3.Y.Should().BeGreaterThan(item1.Y, "a third would need 600px, so it wraps");
    }

    [Fact]
    public void FlexGap_AddsSpaceBetweenItems()
    {
        var root = LayoutFlex(
            "<div style='display: flex; gap: 20px'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float expectedX2 = item1.X + 100f + 20f;
        item2.X.Should().BeApproximately(expectedX2, 1f);
    }

    [Fact]
    public void FlexBasis_OverridesWidth()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='width: 100px; flex-basis: 200px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item = container.Children[0];

        item.Width.Should().BeApproximately(200f, 1f);
    }

    [Fact]
    public void NestedFlex_WorksCorrectly()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='display: flex; flex-direction: column; width: 200px'>" +
            "<div style='height: 30px'></div>" +
            "<div style='height: 40px'></div>" +
            "</div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var outerContainer = FindInnerDivs(root)[0];
        outerContainer.Children.Count.Should().Be(2);

        var innerContainer = outerContainer.Children[0];
        innerContainer.Style.Display.Should().Be("flex");
        innerContainer.Children.Count.Should().Be(2);

        var innerItem1 = innerContainer.Children[0];
        var innerItem2 = innerContainer.Children[1];
        innerItem2.Y.Should().BeGreaterThan(innerItem1.Y);
        innerItem1.Height.Should().Be(30f);
        innerItem2.Height.Should().Be(40f);
    }

    [Fact]
    public void FlexRowReverse_ReverseOrder()
    {
        var root = LayoutFlex(
            "<div style='display: flex; flex-direction: row-reverse'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='width: 150px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var firstInDom = container.Children[0];
        var secondInDom = container.Children[1];

        secondInDom.X.Should().BeLessThan(firstInDom.X, "row-reverse puts the last DOM item leftmost");
    }

    [Fact]
    public void FlexContainer_HasFlexDisplay()
    {
        var root = LayoutFlex(
            "<div style='display: flex'><div>Item</div></div>");

        var flex = FindInnerDivs(root)[0];
        flex.Should().NotBeNull();
        flex.Style.Display.Should().Be("flex");
    }

    [Fact]
    public void FlexContainer_ChildrenLaidOut()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        container.Children.Count.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public void FlexContainer_AutoHeight_FitsContent()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='width: 100px; height: 80px'></div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];

        container.Height.Should().BeApproximately(80f, 1f, "an auto-height row is as tall as its tallest item");
    }

    [Fact]
    public void FlexGap_RecognizedAsProperty()
    {
        var root = LayoutFlex(
            "<div style='display: flex; gap: 10px'>" +
            "<div>A</div><div>B</div>" +
            "</div>");

        var flex = FindInnerDivs(root)[0];
        flex.Style.Get("gap").Should().Be("10px");
    }

    [Fact]
    public void FlexColumnReverse_ReverseVerticalOrder()
    {
        var root = LayoutFlex(
            "<div style='display: flex; flex-direction: column-reverse'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='width: 100px; height: 60px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var firstInDom = container.Children[0];
        var secondInDom = container.Children[1];

        secondInDom.Y.Should().BeLessThan(firstInDom.Y, "column-reverse puts the last DOM item on top");
    }

    [Fact]
    public void AlignSelf_OverridesAlignItems()
    {
        var root = LayoutFlex(
            "<div style='display: flex; align-items: flex-start; height: 200px'>" +
            "<div style='width: 100px; height: 50px; align-self: flex-end'></div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var alignedToEnd = container.Children[0];
        var alignedToStart = container.Children[1];

        alignedToEnd.Y.Should().BeGreaterThan(alignedToStart.Y, "align-self: flex-end overrides align-items: flex-start");
    }

    [Fact]
    public void JustifyContent_FlexEnd()
    {
        var root = LayoutFlex(
            "<div style='display: flex; justify-content: flex-end'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item = container.Children[0];

        float containerWidth = container.ContentWidth;

        float rightEdgeX = container.X + container.PaddingLeft + containerWidth - 100f;
        item.X.Should().BeApproximately(rightEdgeX, 1f);
    }

    [Fact]
    public void FlexGrow_WithFixedItems()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='flex-grow: 1; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var fixedItem = container.Children[0];
        var growItem = container.Children[1];

        fixedItem.Width.Should().BeApproximately(100f, 1f);

        float containerWidth = container.ContentWidth;
        growItem.Width.Should().BeApproximately(containerWidth - 100f, 1f);
    }

    [Fact]
    public void FlexDisplayNone_ChildSkipped()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='display: none; width: 100px; height: 50px'></div>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];

        container.Children.Count.Should().Be(2);

        var item1 = container.Children[0];
        var item2 = container.Children[1];
        item2.X.Should().BeApproximately(item1.X + 100f, 1f, "the display:none item leaves no gap");
    }

    [Fact]
    public void FlexRow_ContainerWithPadding()
    {
        var root = LayoutFlex(
            "<div style='display: flex; padding: 20px'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item = container.Children[0];

        container.PaddingLeft.Should().Be(20f);
        container.PaddingTop.Should().Be(20f);

        item.X.Should().BeApproximately(container.X + 20f, 1f);
        item.Y.Should().BeApproximately(container.Y + 20f, 1f);
    }

    [Fact]
    public void FlexShorthand_SingleNumber_ProportionalSizing()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='flex: 2; width: 50px; height: 50px'></div>" +
            "<div style='flex: 1; width: 50px; height: 50px'></div>" +
            "<div style='flex: 1; width: 50px; height: 50px'></div>" +
            "<div style='flex: 1; width: 50px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        float w = container.ContentWidth;

        var item1 = container.Children[0];
        var item2 = container.Children[1];
        var item3 = container.Children[2];
        var item4 = container.Children[3];

        item1.Width.Should().BeApproximately(w * 2f / 5f, 1f,
            "flex: <n> means flex-basis 0, so the whole width is shared 2:1:1:1 and width:50px is ignored");
        item2.Width.Should().BeApproximately(w * 1f / 5f, 1f);
        item3.Width.Should().BeApproximately(w * 1f / 5f, 1f);
        item4.Width.Should().BeApproximately(w * 1f / 5f, 1f);
    }

    [Fact]
    public void FlexShorthand_None_ZeroGrow()
    {
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='flex: none; width: 100px; height: 50px'></div>" +
            "<div style='flex: none; width: 80px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        item1.Width.Should().BeApproximately(100f, 1f, "flex: none neither grows nor shrinks");
        item2.Width.Should().BeApproximately(80f, 1f);
    }

    [Fact]
    public void FlexRow_SpaceBetween_BlockChildItems_SizedToTextNotContainerWidth()
    {
        var root = LayoutFlex(
            "<div style='display:flex; justify-content:space-between; width:400px; margin:0; padding:0'>" +
            "<div style='margin:0; padding:0'><p style='margin:0'>Left item</p></div>" +
            "<div style='margin:0; padding:0'><p style='margin:0'>Right</p></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        item1.Width.Should().BeLessThan(200f,
            "an item wrapping an auto-width <p> is sized to its text, not to the 400px container shrunk in half");
        item2.Width.Should().BeLessThan(200f, "item should be sized to text, not container");
        item2.X.Should().BeGreaterThan(item1.X + item1.Width + 50f,
            "space-between should place items far apart when they are narrower than container");
    }

    [Fact]
    public void FlexGrow_ProportionalRatio_FlexShorthand_2and1()
    {
        var root = LayoutFlex(
            "<div style='display:flex; width:400px; margin:0; padding:0'>" +
            "<div style='flex:2; margin:0; padding:0'></div>" +
            "<div style='flex:1; margin:0; padding:0'></div>" +
            "<div style='flex:1; margin:0; padding:0'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        container.Children.Count.Should().Be(3);

        container.Children[0].Width.Should().BeApproximately(200f, 1f);
        container.Children[1].Width.Should().BeApproximately(100f, 1f);
        container.Children[2].Width.Should().BeApproximately(100f, 1f);
    }

    [Fact]
    public void FlexRow_AutoWidthItems_InnerBlockChildrenFitFinalWidth()
    {
        var root = LayoutFlex(
            "<div style='display:flex; width:400px; margin:0; padding:0'>" +
            "<div style='flex:1; margin:0; padding:0'>" +
            "<p style='margin:0'>Content A</p>" +
            "</div>" +
            "<div style='flex:1; margin:0; padding:0'>" +
            "<p style='margin:0'>Content B</p>" +
            "</div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        container.Children.Count.Should().BeGreaterOrEqualTo(2);

        var item1 = container.Children[0];
        var item2 = container.Children[1];

        item1.Width.Should().BeApproximately(200f, 1f);
        item2.Width.Should().BeApproximately(200f, 1f);

        foreach (var child in item1.Children)
            child.Width.Should().BeLessOrEqualTo(item1.Width + 0.5f,
                "the <p> is laid out at the item's final width, not the initial full-container width");
        foreach (var child in item2.Children)
            child.Width.Should().BeLessOrEqualTo(item2.Width + 0.5f, "inner content must fit within flex item");
    }

    [Fact]
    public void FlexRow_SpaceBetween_AutoWidth_InnerChildrenDoNotOverflow()
    {
        var root = LayoutFlex(
            "<div style='display:flex; justify-content:space-between; width:400px; margin:0; padding:0'>" +
            "<div style='margin:0; padding:0'><p style='margin:0'>Left text</p></div>" +
            "<div style='margin:0; padding:0'><p style='margin:0'>Right text</p></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        item2.X.Should().BeGreaterOrEqualTo(item1.X + item1.Width - 0.5f, "items must not overlap");

        foreach (var child in item1.Children)
            child.Width.Should().BeLessOrEqualTo(item1.Width + 0.5f);
        foreach (var child in item2.Children)
            child.Width.Should().BeLessOrEqualTo(item2.Width + 0.5f);
    }

    [Fact]
    public void Order_ReordersItems()
    {
        var root = LayoutFlex(
            "<div style='display:flex; margin:0; padding:0; width:300px'>" +
            "<div style='order:2; width:80px; height:50px; margin:0'>A</div>" +
            "<div style='order:0; width:80px; height:50px; margin:0'>B</div>" +
            "<div style='order:1; width:80px; height:50px; margin:0'>C</div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        container.Children.Count.Should().Be(3);

        var visual1 = container.Children[0];
        var visual2 = container.Children[1];
        var visual3 = container.Children[2];

        visual1.Style.Get("order").Should().Be("0", "first visual item should be B (order=0)");
        visual2.Style.Get("order").Should().Be("1", "second visual item should be C (order=1)");
        visual3.Style.Get("order").Should().Be("2", "third visual item should be A (order=2)");

        visual1.X.Should().BeLessThan(visual2.X, "layout children come in order-property order, placed left to right");
        visual2.X.Should().BeLessThan(visual3.X);
    }

    [Fact]
    public void FlexShorthand_ThreeValues_GrowShrinkBasis()
    {
        var root = LayoutFlex(
            "<div style='display:flex; width:400px; margin:0; padding:0'>" +
            "<div style='flex: 1 0 100px; margin:0'></div>" +
            "<div style='flex: 1 0 100px; margin:0'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        container.Children.Count.Should().Be(2);

        var item1 = container.Children[0];
        var item2 = container.Children[1];

        item1.Width.Should().BeApproximately(200f, 2f,
            "flex: 1 0 100px starts each item at its 100px basis and shares the remaining 200px equally");
        item2.Width.Should().BeApproximately(200f, 2f);
    }
}
