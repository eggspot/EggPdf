using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class FlexLayoutTests
{
    // Body margin is 8px by default, so flex container at X=8, Width=600-16=584
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

        // Container is the first div
        var container = divs[0];
        container.Style.Display.Should().Be("flex");

        // Find the two flex items (children of the flex container)
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        // Items should be side by side horizontally
        item1.Width.Should().Be(100f);
        item2.Width.Should().Be(100f);

        // Item2 should be to the right of item1
        item2.X.Should().BeApproximately(item1.X + 100f, 1f);

        // Both should have the same Y position
        item1.Y.Should().BeApproximately(item2.Y, 1f);
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

        // Items should be stacked vertically
        item1.Height.Should().Be(50f);
        item2.Height.Should().Be(60f);

        // Item2 should be below item1
        item2.Y.Should().BeApproximately(item1.Y + 50f, 1f);

        // Both should have the same X position
        item1.X.Should().BeApproximately(item2.X, 1f);
    }

    [Fact]
    public void FlexGrow_DistributesSpace()
    {
        // Container width = 584px (600 - 2*8 body margin)
        // Two items with flex-grow:1 each should share equally
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='flex-grow: 1; height: 50px'></div>" +
            "<div style='flex-grow: 1; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float containerWidth = container.ContentWidth;

        // Each item should get half the space
        item1.Width.Should().BeApproximately(containerWidth / 2, 1f);
        item2.Width.Should().BeApproximately(containerWidth / 2, 1f);
    }

    [Fact]
    public void FlexGrow_Proportional()
    {
        // flex-grow:1 vs flex-grow:2 -> 1/3 and 2/3 of free space
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='flex-grow: 1; height: 50px'></div>" +
            "<div style='flex-grow: 2; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float containerWidth = container.ContentWidth;

        // Item1 should get 1/3, item2 should get 2/3
        item1.Width.Should().BeApproximately(containerWidth / 3, 1f);
        item2.Width.Should().BeApproximately(containerWidth * 2 / 3, 1f);
    }

    [Fact]
    public void FlexShrink_AbsorbsNegativeSpace()
    {
        // Container is 584px but items total 700px -> shrink needed
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='width: 400px; height: 50px'></div>" +
            "<div style='width: 300px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        float containerWidth = container.ContentWidth;

        // Total of items should equal container width (they shrink to fit)
        (item1.Width + item2.Width).Should().BeApproximately(containerWidth, 1f);

        // Items should shrink proportionally to their sizes
        // flex-shrink is 1 by default for both; shrink factor = shrink * baseSize
        // item1: 1 * 400 = 400, item2: 1 * 300 = 300, total = 700
        // Overflow = 700 - 584 = 116
        // item1 shrinks by 116 * (400/700) = 66.3, item2 shrinks by 116 * (300/700) = 49.7
        item1.Width.Should().BeGreaterThan(item2.Width);
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

        // Items should be centered: offset = freeSpace/2
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

        // First item at start
        item1.X.Should().BeApproximately(container.X + container.PaddingLeft, 1f);

        // Last item at end
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
        float perItem = freeSpace / 2; // space-around: each item gets equal share

        // First item offset by perItem/2
        float expectedX1 = container.X + container.PaddingLeft + perItem / 2;
        item1.X.Should().BeApproximately(expectedX1, 1f);

        // Second item after first item + perItem gap
        float expectedX2 = expectedX1 + 100f + perItem;
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
        float gap = freeSpace / 3; // space-evenly: n+1 gaps

        float expectedX1 = container.X + container.PaddingLeft + gap;
        item1.X.Should().BeApproximately(expectedX1, 1f);

        float expectedX2 = expectedX1 + 100f + gap;
        item2.X.Should().BeApproximately(expectedX2, 1f);
    }

    [Fact]
    public void AlignItems_Center()
    {
        // Container with explicit height, items centered on cross axis
        var root = LayoutFlex(
            "<div style='display: flex; align-items: center; height: 200px'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item = container.Children[0];

        // Item should be vertically centered
        // Line cross size is the explicit container height (200px)
        float expectedY = container.Y + container.PaddingTop + (200f - 50f) / 2;
        item.Y.Should().BeApproximately(expectedY, 1f);
    }

    [Fact]
    public void AlignItems_Stretch()
    {
        // Default: items stretch to fill cross axis
        var root = LayoutFlex(
            "<div style='display: flex; height: 200px'>" +
            "<div style='width: 100px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item = container.Children[0];

        // Item should stretch to container height (200px)
        item.Height.Should().BeApproximately(200f, 1f);
    }

    [Fact]
    public void FlexWrap_ItemsWrapToNextLine()
    {
        // Container is 584px, 200+200=400 fits, third 200 wraps (400+200=600 > 584)
        var root = LayoutFlex(
            "<div style='display: flex; flex-wrap: wrap'>" +
            "<div style='width: 200px; height: 50px'></div>" +
            "<div style='width: 200px; height: 50px'></div>" +
            "<div style='width: 200px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];

        // Should have 3 children
        container.Children.Count.Should().Be(3);

        var item1 = container.Children[0];
        var item2 = container.Children[1];
        var item3 = container.Children[2];

        // First two items should be on the same line (side by side)
        item1.Y.Should().BeApproximately(item2.Y, 1f);

        // Third item should be on a new line (below)
        item3.Y.Should().BeGreaterThan(item1.Y);
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

        // Gap of 20px between items
        float expectedX2 = item1.X + 100f + 20f;
        item2.X.Should().BeApproximately(expectedX2, 1f);
    }

    [Fact]
    public void FlexBasis_OverridesWidth()
    {
        // flex-basis: 200px should override width: 100px
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

        // Inner flex container
        var innerContainer = outerContainer.Children[0];
        innerContainer.Style.Display.Should().Be("flex");
        innerContainer.Children.Count.Should().Be(2);

        // Inner items should be stacked vertically
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
        var item1 = container.Children[0]; // first in DOM
        var item2 = container.Children[1]; // second in DOM

        // In row-reverse, last DOM item should be positioned first (leftmost)
        // item2 (second in DOM) should be at left, item1 at right
        item2.X.Should().BeLessThan(item1.X);
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

        // Container height should be determined by tallest child (80px)
        // because align-items:stretch makes children stretch to max height
        container.Height.Should().BeApproximately(80f, 1f);
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
        var item1 = container.Children[0]; // first in DOM
        var item2 = container.Children[1]; // second in DOM

        // In column-reverse, last DOM item should be positioned first (top)
        item2.Y.Should().BeLessThan(item1.Y);
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
        var item1 = container.Children[0]; // align-self: flex-end
        var item2 = container.Children[1]; // inherits flex-start

        // item1 should be at the bottom, item2 at the top
        item1.Y.Should().BeGreaterThan(item2.Y);
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

        // Item should be at the right edge
        float expectedX = container.X + container.PaddingLeft + containerWidth - 100f;
        item.X.Should().BeApproximately(expectedX, 1f);
    }

    [Fact]
    public void FlexGrow_WithFixedItems()
    {
        // One fixed-width item, one growing
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='width: 100px; height: 50px'></div>" +
            "<div style='flex-grow: 1; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var fixedItem = container.Children[0];
        var growItem = container.Children[1];

        fixedItem.Width.Should().BeApproximately(100f, 1f);

        // Growing item should fill remaining space
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

        // Only 2 visible children
        container.Children.Count.Should().Be(2);

        // Second visible item should be right after first (no gap from hidden)
        var item1 = container.Children[0];
        var item2 = container.Children[1];
        item2.X.Should().BeApproximately(item1.X + 100f, 1f);
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

        // Item should respect container padding
        item.X.Should().BeApproximately(container.X + 20f, 1f);
        item.Y.Should().BeApproximately(container.Y + 20f, 1f);
    }

    [Fact]
    public void FlexShorthand_SingleNumber_ProportionalSizing()
    {
        // flex: 2 + flex: 1 + flex: 1 + flex: 1 → widths in 2:1:1:1 ratio
        // CSS spec: flex: <n> means flex-grow:<n>, flex-shrink:1, flex-basis:0
        // With flex-basis:0, ALL container space is distributed by grow ratio
        // Using explicit width on items to prove flex-basis:0 overrides it
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

        // flex-basis:0 means full container width distributed 2:1:1:1,
        // ignoring the width:50px (flex-basis:0 takes precedence over width)
        item1.Width.Should().BeApproximately(w * 2f / 5f, 1f);
        item2.Width.Should().BeApproximately(w * 1f / 5f, 1f);
        item3.Width.Should().BeApproximately(w * 1f / 5f, 1f);
        item4.Width.Should().BeApproximately(w * 1f / 5f, 1f);
    }

    [Fact]
    public void FlexShorthand_None_ZeroGrow()
    {
        // flex: none → flex-grow:0, flex-shrink:0, flex-basis:auto → item keeps natural size
        var root = LayoutFlex(
            "<div style='display: flex'>" +
            "<div style='flex: none; width: 100px; height: 50px'></div>" +
            "<div style='flex: none; width: 80px; height: 50px'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        // flex: none → items keep their specified widths, no grow or shrink
        item1.Width.Should().BeApproximately(100f, 1f);
        item2.Width.Should().BeApproximately(80f, 1f);
    }

    [Fact]
    public void FlexRow_SpaceBetween_BlockChildItems_SizedToTextNotContainerWidth()
    {
        // Two items each wrapping block <p> children (no explicit widths, no flex properties).
        // Without the fix: each item's baseSize = container width (inflated by auto-width <p> blocks).
        // With the fix: each item's baseSize = max leaf text width, much smaller than container.
        // Result: neither item should be anywhere near the container width (400px).
        var root = LayoutFlex(
            "<div style='display:flex; justify-content:space-between; width:400px; margin:0; padding:0'>" +
            "<div style='margin:0; padding:0'><p style='margin:0'>Left item</p></div>" +
            "<div style='margin:0; padding:0'><p style='margin:0'>Right</p></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        // If content-based sizing is wrong, items would each be ~200px (shrunk from 400px).
        // With correct sizing, items should be their text widths (~50-80px each).
        item1.Width.Should().BeLessThan(200f, "item should be sized to text, not container");
        item2.Width.Should().BeLessThan(200f, "item should be sized to text, not container");

        // Items at opposite ends (space-between) — item2 should start well past item1
        item2.X.Should().BeGreaterThan(item1.X + item1.Width + 50f,
            "space-between should place items far apart when they are narrower than container");
    }

    [Fact]
    public void FlexGrow_ProportionalRatio_FlexShorthand_2and1()
    {
        // flex:2 + flex:1 + flex:1 → widths in ratio 2:1:1 → 200:100:100 in 400px
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
        // Two flex:1 items each wrapping a <p>. After layout the <p> must fit
        // within its flex-parent, not overflow at the initial full-container width.
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

        // Inner <p> tags must not exceed their parent's width
        foreach (var child in item1.Children)
            child.Width.Should().BeLessOrEqualTo(item1.Width + 0.5f, "inner content must fit within flex item");
        foreach (var child in item2.Children)
            child.Width.Should().BeLessOrEqualTo(item2.Width + 0.5f, "inner content must fit within flex item");
    }

    [Fact]
    public void FlexRow_SpaceBetween_AutoWidth_InnerChildrenDoNotOverflow()
    {
        // justify-content: space-between with two auto-width items.
        // Each item's inner block must not overflow into the other item's space.
        var root = LayoutFlex(
            "<div style='display:flex; justify-content:space-between; width:400px; margin:0; padding:0'>" +
            "<div style='margin:0; padding:0'><p style='margin:0'>Left text</p></div>" +
            "<div style='margin:0; padding:0'><p style='margin:0'>Right text</p></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        var item1 = container.Children[0];
        var item2 = container.Children[1];

        // Items must not overlap
        item2.X.Should().BeGreaterOrEqualTo(item1.X + item1.Width - 0.5f, "items must not overlap");

        // Inner children must fit within their parent
        foreach (var child in item1.Children)
            child.Width.Should().BeLessOrEqualTo(item1.Width + 0.5f);
        foreach (var child in item2.Children)
            child.Width.Should().BeLessOrEqualTo(item2.Width + 0.5f);
    }

    [Fact]
    public void Order_ReordersItems()
    {
        // DOM order: A(order=2), B(order=0), C(order=1)
        // Visual/paint order (sorted by `order`): B(0), C(1), A(2)
        // container.Children are in visual order after layout.
        var root = LayoutFlex(
            "<div style='display:flex; margin:0; padding:0; width:300px'>" +
            "<div style='order:2; width:80px; height:50px; margin:0'>A</div>" +
            "<div style='order:0; width:80px; height:50px; margin:0'>B</div>" +
            "<div style='order:1; width:80px; height:50px; margin:0'>C</div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        container.Children.Count.Should().Be(3);

        // Children in container are in visual order (sorted by `order` property):
        // [0] = B(order=0), [1] = C(order=1), [2] = A(order=2)
        var visual1 = container.Children[0]; // should be B (order=0)
        var visual2 = container.Children[1]; // should be C (order=1)
        var visual3 = container.Children[2]; // should be A (order=2)

        // Verify sorted by `order` value
        visual1.Style.Get("order").Should().Be("0", "first visual item should be B (order=0)");
        visual2.Style.Get("order").Should().Be("1", "second visual item should be C (order=1)");
        visual3.Style.Get("order").Should().Be("2", "third visual item should be A (order=2)");

        // And they should be physically side-by-side left-to-right
        visual1.X.Should().BeLessThan(visual2.X);
        visual2.X.Should().BeLessThan(visual3.X);
    }

    [Fact]
    public void FlexShorthand_ThreeValues_GrowShrinkBasis()
    {
        // flex: 1 0 100px → grow=1, shrink=0, basis=100px
        // Two items: flex: 1 0 100px → each starts at 100px basis, then grows equally
        var root = LayoutFlex(
            "<div style='display:flex; width:400px; margin:0; padding:0'>" +
            "<div style='flex: 1 0 100px; margin:0'></div>" +
            "<div style='flex: 1 0 100px; margin:0'></div>" +
            "</div>");

        var container = FindInnerDivs(root)[0];
        container.Children.Count.Should().Be(2);

        var item1 = container.Children[0];
        var item2 = container.Children[1];

        // Each starts at 100px basis + equal share of remaining 200px = 200px each
        item1.Width.Should().BeApproximately(200f, 2f);
        item2.Width.Should().BeApproximately(200f, 2f);
    }
}
