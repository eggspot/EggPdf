using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// Tests for box model edge cases: min/max-height, border-box sizing,
/// overflow, percentage dimensions, and padding/margin shorthands.
/// </summary>
public class BoxModelTests
{
    // ── min-height / max-height ─────────────────────────────────────────────

    [Fact]
    public void MinHeight_EnforcesMinimumWhenContentShorter()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='min-height: 100px; width: 200px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeGreaterOrEqualTo(100);
    }

    [Fact]
    public void MaxHeight_ConstrainsHeightWhenContentTaller()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='max-height: 50px; width: 200px; height: 200px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeLessOrEqualTo(51); // 50 + 1 for rounding
    }

    [Fact]
    public void MinHeight_DoesNotConstrainTallerContent()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='min-height: 20px; width: 200px; height: 80px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(80, 1f);
    }

    [Fact]
    public void MaxHeight_DoesNotConstrainShorterContent()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='max-height: 200px; width: 200px; height: 50px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(50, 1f);
    }

    // ── percentage dimensions ───────────────────────────────────────────────

    [Fact]
    public void Width_50Percent_HalfOfContainer()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 50%; height: 50px'></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // 50% of 600 = 300, but body margin may reduce available width
        div!.Width.Should().BeApproximately(300, 20f);
    }

    [Fact]
    public void Width_100Percent_FillsContainer()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 400px; height: 100px'>" +
            "  <div style='width: 100%; height: 50px'></div>" +
            "</div>", 600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(2);
        // Child width should equal parent content width
        divs[1].Width.Should().BeApproximately(divs[0].ContentWidth, 2f);
    }

    // ── box-sizing ──────────────────────────────────────────────────────────

    [Fact]
    public void BoxSizingBorderBox_PaddingIncludedInWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='box-sizing: border-box; width: 200px; padding: 20px; height: 100px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // Total width stays at 200px (padding is inside)
        div!.Width.Should().BeApproximately(200, 1f);
        // Content width = 200 - 2*20 = 160
        div.ContentWidth.Should().BeApproximately(160, 2f);
    }

    [Fact]
    public void BoxSizingContentBox_PaddingAddsToWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='box-sizing: content-box; width: 200px; padding: 20px; height: 100px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // Total width = 200 + 2*20 = 240px
        div!.Width.Should().BeApproximately(240, 1f);
    }

    // ── padding shorthands ──────────────────────────────────────────────────

    [Fact]
    public void PaddingShorthand_FourValues_AllSides()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px; height: 50px; padding: 10px 20px 30px 40px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.PaddingTop.Should().BeApproximately(10, 0.5f);
        div.PaddingRight.Should().BeApproximately(20, 0.5f);
        div.PaddingBottom.Should().BeApproximately(30, 0.5f);
        div.PaddingLeft.Should().BeApproximately(40, 0.5f);
    }

    [Fact]
    public void PaddingShorthand_TwoValues_TopBottomAndLeftRight()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px; height: 50px; padding: 10px 20px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.PaddingTop.Should().BeApproximately(10, 0.5f);
        div.PaddingBottom.Should().BeApproximately(10, 0.5f);
        div.PaddingLeft.Should().BeApproximately(20, 0.5f);
        div.PaddingRight.Should().BeApproximately(20, 0.5f);
    }

    // ── margin shorthands ───────────────────────────────────────────────────

    [Fact]
    public void MarginShorthand_FourValues_AllSides()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px; height: 50px; margin: 5px 10px 15px 20px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.MarginTop.Should().BeApproximately(5, 0.5f);
        div.MarginRight.Should().BeApproximately(10, 0.5f);
        div.MarginBottom.Should().BeApproximately(15, 0.5f);
        div.MarginLeft.Should().BeApproximately(20, 0.5f);
    }

    [Fact]
    public void MarginAuto_HorizontallyCenters()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 200px; margin: 0 auto; height: 50px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // With auto margins, div should be centered (X > 0)
        div!.X.Should().BeGreaterThan(0, "auto margins should center the block");
    }

    // ── nested padding stacks ───────────────────────────────────────────────

    [Fact]
    public void NestedPadding_ChildOffsetByParentPadding()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='padding: 30px; width: 400px'>" +
            "  <div style='height: 40px'></div>" +
            "</div>", 600, 800);

        var divs = root.FindAllByTag("div");
        divs.Should().HaveCountGreaterOrEqualTo(2);

        var parent = divs[0];
        var child = divs[1];
        // Child's X should be offset by parent's padding-left
        child.X.Should().BeApproximately(parent.X + 30, 2f);
        child.Y.Should().BeApproximately(parent.Y + 30, 2f);
    }

    // ── border affects dimensions ───────────────────────────────────────────

    [Fact]
    public void Border_StyleIsPreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px; height: 50px; border: 5px solid black'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // Border style is stored and the element is laid out
        div!.Width.Should().BeGreaterThan(0);
        div.Style.Get("border-top-width").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Border_BorderBox_TotalWidthUnchanged()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='box-sizing: border-box; width: 100px; height: 50px; border: 5px solid black'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(100, 2f);
    }

    // ── overflow ────────────────────────────────────────────────────────────

    [Fact]
    public void OverflowHidden_StyleIsPreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='overflow: hidden; width: 100px; height: 50px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("overflow").Should().Be("hidden");
    }

    [Fact]
    public void OverflowAuto_StyleIsPreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='overflow: auto; width: 100px; height: 200px'></div>",
            600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("overflow").Should().Be("auto");
    }

    // ── display variants ────────────────────────────────────────────────────

    [Fact]
    public void DisplayBlock_TakesFullWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<span style='display: block; height: 30px'>Block span</span>",
            600, 800);

        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Style.Display.Should().Be("block");
        span.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DisplayInlineBlock_WidthFromContent()
    {
        var root = LayoutTestHelper.Layout(
            "<div><span style='display: inline-block; width: 80px; height: 40px'>IB</span></div>",
            600, 800);

        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Width.Should().BeApproximately(80, 1f);
        span.Height.Should().BeApproximately(40, 1f);
    }
}
