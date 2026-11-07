using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class LengthUnitTests
{
    [Theory]
    [InlineData("2rem", 32f)]
    [InlineData("0.75rem", 12f)]
    [InlineData("2em", 20f)]
    [InlineData("2ex", 10f)]
    [InlineData("2ch", 10f)]
    [InlineData("10px", 10f)]
    [InlineData("10PX", 10f)]
    [InlineData(" 10px ", 10f)]
    [InlineData("12pt", 16f)]
    [InlineData("1pc", 16f)]
    [InlineData("1in", 96f)]
    [InlineData("2.54cm", 96f)]
    [InlineData("25.4mm", 96f)]
    [InlineData("101.6q", 96f)]
    [InlineData("50%", 100f)]
    [InlineData("1e1", 10f)]
    [InlineData("12", 12f)]
    [InlineData("10furlongs", 0f)]
    public void ResolveLength_Unit_ConvertsToPx(string value, float expectedPx)
    {
        BlockLayout.ResolveLength(value, 200f, 10f).Should().BeApproximately(expectedPx, 0.01f);
    }

    [Theory]
    [InlineData("calc(2rem + 1px)", 33f)]
    [InlineData("calc(2ex + 0px)", 10f)]
    [InlineData("calc(1in - 6px)", 90f)]
    public void ResolveLength_CalcWithUnits_ConvertsToPx(string value, float expectedPx)
    {
        BlockLayout.ResolveLength(value, 200f, 10f).Should().BeApproximately(expectedPx, 0.01f);
    }

    [Fact]
    public void Rem_RootFontSizeSet_ResolvesAgainstRoot()
    {
        var root = LayoutTestHelper.Layout(
            "<html style='font-size:20px'><body style='margin:0'><div id='d' style='width:10rem;padding-left:1rem'>A</div></body></html>",
            600, 800);

        var div = root.FindById("d")!;

        div.Width.Should().BeApproximately(220f, 0.01f);
        div.PaddingLeft.Should().BeApproximately(20f, 0.01f);
    }

    [Fact]
    public void Rem_OnRootElementItself_ResolvesAgainstInitialFontSize()
    {
        var root = LayoutTestHelper.Layout(
            "<html style='font-size:2rem'><body style='margin:0'><div id='d' style='width:10rem'>A</div></body></html>",
            600, 800);

        root.FindById("d")!.Width.Should().BeApproximately(320f, 0.01f);
    }

    [Fact]
    public void RemOnRootElement_DescendantInheritsComputedSizeNotRemAgain()
    {
        var root = LayoutTestHelper.Layout(
            "<html style='font-size:2rem'><body style='margin:0'><div id='d' style='width:10em'>A</div></body></html>",
            600, 800);

        root.FindById("d")!.Width.Should().BeApproximately(320f, 0.01f);
    }

    [Fact]
    public void PercentRootFontSize_RemUsesComputedRoot()
    {
        var root = LayoutTestHelper.Layout(
            "<html style='font-size:62.5%'><body style='margin:0'><div id='d' style='width:10rem;padding-left:1.6rem'>A</div></body></html>",
            600, 800);

        var div = root.FindById("d")!;
        div.PaddingLeft.Should().BeApproximately(16f, 0.01f);
        div.Width.Should().BeApproximately(116f, 0.01f);
    }

    [Theory]
    [InlineData("150%", 24f)]
    [InlineData("80%", 12.8f)]
    [InlineData("1.5em", 24f)]
    public void FontSize_RelativeToParent_ResolvesAgainstParentSize(string fontSize, float expectedPx)
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0;font-size:16px'><div id='d' style='font-size:" + fontSize + ";width:10em'>A</div></body>",
            600, 800);

        root.FindById("d")!.Width.Should().BeApproximately(expectedPx * 10f, 0.05f);
    }

    [Fact]
    public void ResetRootFontSize_AfterLayout_RemFallsBackToInitialSize()
    {
        LayoutTestHelper.Layout("<html style='font-size:20px'><body>A</body></html>", 600, 800);

        BlockLayout.ResetRootFontSize();

        BlockLayout.ResolveLength("1rem", 0f, 10f).Should().BeApproximately(16f, 0.01f);
    }

    [Fact]
    public void Vmin_OnPortraitPage_UsesPageWidth()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><div id='d' style='width:10vmin'>A</div></body>", 600, 800);

        root.FindById("d")!.Width.Should().BeApproximately(60f, 0.01f);
    }

    [Fact]
    public void RemFontSize_SmallerText_GetsItsOwnLine()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><div id='a' style='font-size:.75rem'>B</div><div id='b'>C</div></body>", 600, 800);

        var a = root.FindById("a")!;
        var b = root.FindById("b")!;

        a.Height.Should().BeGreaterThan(10f);
        b.Y.Should().BeGreaterThanOrEqualTo(a.Y + a.Height - 0.01f);
    }
}
