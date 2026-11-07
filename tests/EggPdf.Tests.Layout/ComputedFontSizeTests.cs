using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class ComputedFontSizeTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<body style='margin:0;font-size:12px'>" + body + "</body>", 600, 800);

    private static float FontSizePx(LayoutBox root, string id) =>
        BlockLayout.ResolveLength(root.FindById(id)!.Style.FontSize, 0f, 0f);

    [Theory]
    [InlineData("80%", 9.6f)]
    [InlineData("150%", 18f)]
    [InlineData("x-large", 24f)]
    [InlineData("xx-small", 9f)]
    [InlineData("medium", 16f)]
    [InlineData("smaller", 10f)]
    [InlineData("larger", 14.4f)]
    [InlineData("1.5em", 18f)]
    [InlineData("2rem", 32f)]
    [InlineData("12pt", 16f)]
    public void FontSize_RelativeOrKeyword_ComputesToPx(string fontSize, float expectedPx)
    {
        var root = Layout("<div id='d' style='font-size:" + fontSize + "'>Text</div>");

        root.FindById("d")!.Style.FontSize.Should().EndWith("px");
        FontSizePx(root, "d").Should().BeApproximately(expectedPx, 0.01f);
    }

    [Theory]
    [InlineData("2vw")]
    [InlineData("calc(10px + 1vw)")]
    [InlineData("clamp(12px, 2vw, 30px)")]
    public void ParentFontSizeNotComputable_ChildEmStaysRelativeToResolvedParent(string parentFontSize)
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0;font-size:" + parentFontSize + "'>" +
            "<div id='d' style='font-size:2em;width:10em'>A</div><h1 id='h'>H</h1></body>", 600, 800);

        root.FindById("d")!.Style.FontSize.Should().Be("2em");
        root.FindById("h")!.Style.FontSize.Should().Be("2em");
        float bodyPx = BlockLayout.ResolveLength(parentFontSize, 0f, 16f);
        root.FindById("d")!.Width.Should().BeApproximately(bodyPx * 2f * 10f, 0.5f);
    }

    [Fact]
    public void RootFontSizeNotComputable_RemStaysForLayout()
    {
        var root = LayoutTestHelper.Layout(
            "<html style='font-size:calc(10px + 2px)'><body style='margin:0'><div id='d' style='font-size:2rem'>A</div></body></html>",
            600, 800);

        root.FindById("d")!.Style.FontSize.Should().Be("2rem");
    }

    [Fact]
    public void ParentFontSizeNotComputable_AbsoluteChildStillComputes()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0;font-size:2vw'><div id='d' style='font-size:12pt'>A</div></body>", 600, 800);

        root.FindById("d")!.Style.FontSize.Should().Be("16px");
    }

    [Fact]
    public void FontSizePercent_TextHasNonZeroLineBox()
    {
        var root = Layout("<div id='a' style='font-size:80%'>Small</div><div id='b'>Next</div>");

        var a = root.FindById("a")!;
        a.Height.Should().BeGreaterThan(5f);
        root.FindById("b")!.Y.Should().BeGreaterThanOrEqualTo(a.Y + a.Height - 0.01f);
    }

    [Fact]
    public void NestedEm_ChildInheritsComputedSizeNotEmAgain()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0;font-size:16px'><h1 id='h' style='font-size:2em'>H <span id='s'>x</span></h1></body>", 600, 800);

        FontSizePx(root, "h").Should().BeApproximately(32f, 0.01f);
        FontSizePx(root, "s").Should().BeApproximately(32f, 0.01f);
    }

    [Fact]
    public void SmallInsideScaledText_IsRelativeToParent()
    {
        var root = Layout("<p style='font-size:9px'>Body <small id='s'>fine print</small></p>");

        FontSizePx(root, "s").Should().BeLessThan(9f);
    }

    [Theory]
    [InlineData("30px", 30f)]
    [InlineData("200%", 28f)]
    [InlineData("2em", 28f)]
    [InlineData("2", 28f)]
    public void LineHeight_WithUnits_SetsLineBoxHeight(string lineHeight, float expectedPx)
    {
        var root = Layout("<div id='d' style='font-size:14px;line-height:" + lineHeight + "'>a</div>");

        root.FindById("d")!.Height.Should().BeApproximately(expectedPx, 0.5f);
    }

    [Fact]
    public void LineHeightEm_InheritsComputedLength()
    {
        var root = Layout("<div style='font-size:10px;line-height:3em'><span id='s' style='font-size:20px'>a</span></div>");

        root.FindById("s")!.Style.Get("line-height").Should().Be("30px");
    }

    [Fact]
    public void LineHeightNumber_InheritsAsNumber()
    {
        var root = Layout("<div style='line-height:1.5'><p id='p' style='font-size:20px;margin:0'>a</p></div>");

        root.FindById("p")!.Style.Get("line-height").Should().Be("1.5");
        root.FindById("p")!.Height.Should().BeApproximately(30f, 0.5f);
    }
}
