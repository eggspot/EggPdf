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

    // ── margin: auto centering ────────────────────────────────────────────────

    [Fact]
    public void MarginAuto_BothSides_CentersBlock()
    {
        // Use a zero-padding wrapper so the container edge is exactly known.
        var root = LayoutTestHelper.Layout(
            "<div id='wrap' style='width: 600px; padding: 0; margin: 0'>" +
            "<div id='inner' style='width: 200px; margin: 0 auto'></div>" +
            "</div>", 800, 800);

        var wrap  = root.FindById("wrap");
        var inner = root.FindById("inner");
        wrap.Should().NotBeNull();
        inner.Should().NotBeNull();

        // Container is 600px wide, inner is 200px — should center at wrap.X + 200
        float expectedX = wrap!.X + (600f - 200f) / 2f;
        inner!.X.Should().BeApproximately(expectedX, 1f,
            "margin: 0 auto should center a block within its container");
    }

    [Fact]
    public void MarginAutoLeft_PushesBlockToRight()
    {
        var root = LayoutTestHelper.Layout(
            "<div id='wrap' style='width: 600px; padding: 0; margin: 0'>" +
            "<div id='inner' style='width: 200px; margin-left: auto; margin-right: 0'></div>" +
            "</div>", 800, 800);

        var wrap  = root.FindById("wrap");
        var inner = root.FindById("inner");
        wrap.Should().NotBeNull();
        inner.Should().NotBeNull();

        // margin-left absorbs all remaining space → inner pushed to right edge of wrap
        float expectedX = wrap!.X + 600f - 200f;
        inner!.X.Should().BeApproximately(expectedX, 1f,
            "margin-left: auto should push block to the right edge");
    }

    [Fact]
    public void MarginAutoRight_KeepsBlockAtLeft()
    {
        var root = LayoutTestHelper.Layout(
            "<div id='wrap' style='width: 600px; padding: 0; margin: 0'>" +
            "<div id='inner' style='width: 200px; margin-left: 0; margin-right: auto'></div>" +
            "</div>", 800, 800);

        var wrap  = root.FindById("wrap");
        var inner = root.FindById("inner");
        wrap.Should().NotBeNull();
        inner.Should().NotBeNull();

        // margin-right: auto — left margin is 0, block stays at left edge of wrap
        inner!.X.Should().BeApproximately(wrap!.X, 1f,
            "margin-right: auto with margin-left: 0 should keep block at left");
    }

    [Fact]
    public void MarginAuto_ShorthandZeroAuto_Centering()
    {
        // Common pattern: margin: 0 auto via shorthand expander
        var root = LayoutTestHelper.Layout(
            "<div id='wrap' style='width: 900px; padding: 0; margin: 0'>" +
            "<div id='inner' style='width: 300px; margin: 0 auto'></div>" +
            "</div>", 1000, 800);

        var wrap  = root.FindById("wrap");
        var inner = root.FindById("inner");
        wrap.Should().NotBeNull();
        inner.Should().NotBeNull();

        float expectedX = wrap!.X + (900f - 300f) / 2f; // 300 from wrap edge
        inner!.X.Should().BeApproximately(expectedX, 1f,
            "margin: 0 auto should center within the 900px container");
    }

    // ── outline-offset ───────────────────────────────────────────────────────

    [Fact]
    public void OutlineOffset_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='outline: 2px solid black; outline-offset: 5px'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div!.Style.Get("outline-offset").Should().Be("5px",
            "outline-offset should be preserved in computed style");
    }

    [Fact]
    public void OutlineOffset_NegativeValue_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='outline: 2px solid black; outline-offset: -3px'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div!.Style.Get("outline-offset").Should().Be("-3px");
    }

    // ── print-color-adjust ───────────────────────────────────────────────────

    [Fact]
    public void PrintColorAdjust_Economy_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='print-color-adjust: economy'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("print-color-adjust").Should().Be("economy");
    }

    [Fact]
    public void PrintColorAdjust_Exact_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='print-color-adjust: exact'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("print-color-adjust").Should().Be("exact");
    }

    [Fact]
    public void PrintColorAdjust_Inherited_FromParent()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='print-color-adjust: exact'><span>child</span></div>", 400, 600);
        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Style.Get("print-color-adjust").Should().Be("exact",
            "print-color-adjust is an inherited property");
    }

    // ── container-type / container-name ──────────────────────────────────────

    [Fact]
    public void ContainerType_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='container-type: inline-size'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("container-type").Should().Be("inline-size");
    }

    [Fact]
    public void ContainerName_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='container-name: card'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("container-name").Should().Be("card");
    }

    // ── mix-blend-mode / background-blend-mode ───────────────────────────────

    [Fact]
    public void MixBlendMode_Multiply_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='mix-blend-mode: multiply'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("mix-blend-mode").Should().Be("multiply");
    }

    [Fact]
    public void MixBlendMode_Screen_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='mix-blend-mode: screen'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("mix-blend-mode").Should().Be("screen");
    }

    [Fact]
    public void BackgroundBlendMode_Multiply_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='background-blend-mode: multiply'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("background-blend-mode").Should().Be("multiply");
    }

    // ── CSS masks ────────────────────────────────────────────────────────────

    [Fact]
    public void MaskImage_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='mask-image: url(mask.svg)'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("mask-image").Should().Be("url(mask.svg)");
    }

    [Fact]
    public void MaskSize_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='mask-size: cover'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("mask-size").Should().Be("cover");
    }

    [Fact]
    public void MaskComposite_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='mask-composite: intersect'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("mask-composite").Should().Be("intersect");
    }

    [Fact]
    public void Mask_Shorthand_Expanded()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='mask: url(mask.png) no-repeat center'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        // The shorthand should expand — at minimum mask-image should be set
        div!.Style.Get("mask-image").Should().Be("url(mask.png)",
            "mask shorthand should expand mask-image longhand");
    }

    // ── backdrop-filter ──────────────────────────────────────────────────────

    [Fact]
    public void BackdropFilter_Blur_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='backdrop-filter: blur(10px)'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("backdrop-filter").Should().Be("blur(10px)");
    }

    [Fact]
    public void BackdropFilter_Multiple_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='backdrop-filter: blur(4px) brightness(0.8)'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("backdrop-filter").Should().Be("blur(4px) brightness(0.8)");
    }

    [Fact]
    public void BackdropFilter_DoesNotInherit()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='backdrop-filter: blur(10px)'><span>child</span></div>", 400, 600);
        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        // backdrop-filter is NOT inherited — child should not have it
        span!.Style.Get("backdrop-filter").Should().BeNullOrEmpty();
    }

    // ── border-image ─────────────────────────────────────────────────────────

    [Fact]
    public void BorderImage_Shorthand_ExpandsSource()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='border-image: url(border.png) 30 round'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("border-image-source").Should().Be("url(border.png)",
            "border-image shorthand should expand border-image-source");
    }

    [Fact]
    public void BorderImage_Shorthand_ExpandsSlice()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='border-image: url(b.png) 27 / 4px stretch'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("border-image-slice").Should().Be("27",
            "border-image shorthand should expand border-image-slice");
    }

    [Fact]
    public void BorderImage_Source_Longhand_Stored()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='border-image-source: url(frame.png)'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("border-image-source").Should().Be("url(frame.png)");
    }

    [Fact]
    public void BorderImage_Slice_Longhand_Stored()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='border-image-slice: 30%'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("border-image-slice").Should().Be("30%");
    }

    [Fact]
    public void BorderImage_Width_Longhand_Stored()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='border-image-width: 4px'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("border-image-width").Should().Be("4px");
    }

    [Fact]
    public void BorderImage_Repeat_Longhand_Stored()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='border-image-repeat: round'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("border-image-repeat").Should().Be("round");
    }

    // ── contain ──────────────────────────────────────────────────────────────

    [Fact]
    public void Contain_Strict_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='contain: strict'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("contain").Should().Be("strict");
    }

    [Fact]
    public void Contain_Layout_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='contain: layout'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("contain").Should().Be("layout");
    }

    [Fact]
    public void Contain_DoesNotInherit()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='contain: strict'><span>child</span></div>", 400, 600);
        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Style.Get("contain").Should().BeNullOrEmpty("contain is not inherited");
    }

    // ── isolation ────────────────────────────────────────────────────────────

    [Fact]
    public void Isolation_Isolate_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='isolation: isolate'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("isolation").Should().Be("isolate");
    }

    [Fact]
    public void Isolation_Auto_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='isolation: auto'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("isolation").Should().Be("auto");
    }

    [Fact]
    public void Isolation_DoesNotInherit()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='isolation: isolate'><span>child</span></div>", 400, 600);
        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Style.Get("isolation").Should().BeNullOrEmpty("isolation is not inherited");
    }

    // ── background-clip ──────────────────────────────────────────────────────

    [Fact]
    public void BackgroundClip_Text_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='background-clip: text; -webkit-background-clip: text'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("background-clip").Should().Be("text");
    }

    [Fact]
    public void BackgroundClip_BorderBox_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='background-clip: border-box'>X</div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Style.Get("background-clip").Should().Be("border-box");
    }

    [Fact]
    public void BackgroundClip_DoesNotInherit()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='background-clip: text'><span>child</span></div>", 400, 600);
        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Style.Get("background-clip").Should().BeNullOrEmpty();
    }

    // ── aspect-ratio ─────────────────────────────────────────────────────────

    [Fact]
    public void AspectRatio_WidthGiven_HeightComputed()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 200px; aspect-ratio: 2 / 1'></div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(100f, 1f,
            "height = width(200) / ratio(2) = 100");
    }

    [Fact]
    public void AspectRatio_Slash_Parsed()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 300px; aspect-ratio: 16 / 9'></div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(300f * 9f / 16f, 2f,
            "height = 300 * 9/16 ≈ 168.75");
    }

    [Fact]
    public void AspectRatio_NoSlash_Parsed()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px; aspect-ratio: 1'></div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(100f, 1f, "1:1 ratio → height = width");
    }

    [Fact]
    public void AspectRatio_DoesNotOverrideExplicitHeight()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 200px; height: 50px; aspect-ratio: 2 / 1'></div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(50f, 1f,
            "explicit height wins over aspect-ratio");
    }

    // ── details/summary collapsed state ────────────────────────────────────

    [Fact]
    public void Details_Closed_HidesContent()
    {
        // Without 'open', only <summary> should be visible; body content should be hidden
        var root = LayoutTestHelper.Layout(
            "<details><summary>Title</summary><p id='body'>Body content</p></details>", 400, 600);
        var bodyBox = root.FindById("body");
        // body content must be hidden (display:none → no box, or zero-size box)
        if (bodyBox != null)
        {
            (bodyBox.Width == 0 && bodyBox.Height == 0).Should().BeTrue(
                "closed <details> body content must have zero size (hidden)");
        }
        // summary should still be present
        var summaryBoxes = root.FindAll(b => b.Text?.Contains("Title") == true);
        summaryBoxes.Should().NotBeEmpty("summary text should always be visible");
    }

    [Fact]
    public void Details_Open_ShowsContent()
    {
        // With 'open' attribute, body content should be visible
        var root = LayoutTestHelper.Layout(
            "<details open><summary>Title</summary><p id='body'>Body content</p></details>", 400, 600);
        var bodyBoxes = root.FindAll(b => b.Text?.Contains("Body content") == true);
        bodyBoxes.Should().NotBeEmpty("open <details> must show body content");
    }

    [Fact]
    public void Details_Summary_AlwaysVisible()
    {
        var root = LayoutTestHelper.Layout(
            "<details><summary>Always shown</summary><p>Hidden</p></details>", 400, 600);
        var summaryBoxes = root.FindAll(b => b.Text?.Contains("Always shown") == true);
        summaryBoxes.Should().NotBeEmpty("summary should always render");
    }

    // ── fit-content / min-content / max-content ──────────────────────────────

    [Fact]
    public void FitContent_Width_DoesNotCrash()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: fit-content(200px); height: 50px'>Hello</div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FitContent_Width_DoesNotExceedArgument()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: fit-content(150px); height: 50px'>Hello</div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeLessOrEqualTo(150);
    }

    [Fact]
    public void MaxContent_Width_UsesAvailableWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: max-content; height: 50px'>Hello</div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MinContent_Width_IsPositive()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: min-content; height: 50px'>Hello</div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeGreaterThan(0);
    }

    // ── viewport + miscellaneous units ───────────────────────────────────────

    [Fact]
    public void ViewportWidth_50vw_IsHalfPageWidth()
    {
        // Page width = 600px → 50vw should resolve to ~300px (minus body margins ~8px each = 284)
        var root = LayoutTestHelper.Layout(
            "<div style='width: 50vw; height: 50px'>vw</div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(300f, 20f, "50vw of 600px page should be ~300px");
    }

    [Fact]
    public void ViewportHeight_50vh_IsHalfPageHeight()
    {
        // Page height = 800px → 50vh should resolve to ~400px
        var root = LayoutTestHelper.Layout(
            "<div style='height: 50vh; width: 200px'>vh</div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(400f, 20f, "50vh of 800px page should be ~400px");
    }

    [Fact]
    public void Unit_Ch_IsPositive()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 10ch; height: 50px'>Hello</div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeGreaterThan(0, "10ch should resolve to positive width");
    }

    [Fact]
    public void Unit_Pc_Resolves()
    {
        // 1pc = 12pt = 16px; 6pc = 96px
        var root = LayoutTestHelper.Layout(
            "<div style='width: 6pc; height: 50px'>pc</div>", 600, 800);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Width.Should().BeApproximately(96f, 5f, "6pc should be ~96px (1pc = 12pt = 16px)");
    }
}
