using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>Tests for CSS Logical Properties Level 1.</summary>
public class LogicalPropertyTests
{
    // ===== margin-inline-start / margin-inline-end =====

    [Fact]
    public void MarginInlineStart_LTR_MapsToMarginLeft()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><div style='margin-inline-start:30px; width:100px; height:20px'>A</div></body>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // In LTR, margin-inline-start = margin-left → div.X should be 30
        div!.X.Should().BeApproximately(30f, 1f,
            "margin-inline-start:30px in LTR should produce 30px left margin");
    }

    [Fact]
    public void MarginInlineEnd_LTR_MapsToMarginRight()
    {
        // Two siblings: first has margin-inline-end:20px, second should be pushed right
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><div style='display:flex'>" +
            "<span style='margin-inline-end:20px; width:50px'>A</span>" +
            "<span style='width:50px'>B</span>" +
            "</div></body>", 400, 600);
        var spans = root.FindAllByTag("span");
        spans.Should().HaveCount(2);
        // Second span should start at 50 + 20 = 70
        spans[1].X.Should().BeApproximately(70f, 2f,
            "margin-inline-end:20px in LTR should add 20px right margin to first span");
    }

    [Fact]
    public void MarginBlockStart_MapsToMarginTop()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='margin-block-start:25px; width:100px; height:20px'>B</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Y.Should().BeApproximately(25f, 1f,
            "margin-block-start:25px should produce 25px top margin");
    }

    [Fact]
    public void MarginBlockEnd_MapsToMarginBottom()
    {
        // Two block siblings: first has margin-block-end:15px
        var root = LayoutTestHelper.Layout(
            "<div style='height:20px; margin-block-end:15px'>A</div>" +
            "<div style='height:20px'>B</div>", 400, 600);
        var divs = root.FindAllByTag("div");
        divs.Should().HaveCount(2);
        divs[1].Y.Should().BeApproximately(35f, 1f,
            "margin-block-end:15px should add 15px below first div");
    }

    // ===== padding-inline / padding-block =====

    [Fact]
    public void PaddingInlineStart_LTR_MapsToLeftPadding()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='padding-inline-start:12px; width:100px'>text</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.PaddingLeft.Should().BeApproximately(12f, 1f,
            "padding-inline-start:12px in LTR should set padding-left");
    }

    [Fact]
    public void PaddingBlockStart_MapsToTopPadding()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='padding-block-start:8px; width:100px'>text</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.PaddingTop.Should().BeApproximately(8f, 1f,
            "padding-block-start:8px should set padding-top");
    }

    // ===== inline-size / block-size =====

    [Fact]
    public void InlineSize_HorizontalWritingMode_MapsToWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='inline-size:150px; height:20px'>C</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(150f, 2f,
            "inline-size:150px in horizontal writing mode should set width");
    }

    [Fact]
    public void BlockSize_HorizontalWritingMode_MapsToHeight()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width:100px; block-size:45px'>D</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(45f, 2f,
            "block-size:45px in horizontal writing mode should set height");
    }

    // ===== text-align: start / end =====

    [Fact]
    public void TextAlign_Start_LTR_MapsToLeft()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-align:start; width:200px'>Hello</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("text-align").Should().Be("left",
            "text-align:start in LTR should resolve to left");
    }

    [Fact]
    public void TextAlign_End_LTR_MapsToRight()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-align:end; width:200px'>Hello</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("text-align").Should().Be("right",
            "text-align:end in LTR should resolve to right");
    }
}
