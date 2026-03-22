using EggPdf.Css;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CssShorthandExpanderTests
{
    [Fact]
    public void Margin_SingleValue_AllFourSides()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("margin", "10px", style).Should().BeTrue();

        style.MarginTop.Should().Be("10px");
        style.MarginRight.Should().Be("10px");
        style.MarginBottom.Should().Be("10px");
        style.MarginLeft.Should().Be("10px");
    }

    [Fact]
    public void Margin_TwoValues_VerticalHorizontal()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("margin", "10px 20px", style).Should().BeTrue();

        style.MarginTop.Should().Be("10px");
        style.MarginRight.Should().Be("20px");
        style.MarginBottom.Should().Be("10px");
        style.MarginLeft.Should().Be("20px");
    }

    [Fact]
    public void Margin_ThreeValues_TopHorizontalBottom()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("margin", "10px 20px 30px", style).Should().BeTrue();

        style.MarginTop.Should().Be("10px");
        style.MarginRight.Should().Be("20px");
        style.MarginBottom.Should().Be("30px");
        style.MarginLeft.Should().Be("20px");
    }

    [Fact]
    public void Margin_FourValues_TopRightBottomLeft()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("margin", "10px 20px 30px 40px", style).Should().BeTrue();

        style.MarginTop.Should().Be("10px");
        style.MarginRight.Should().Be("20px");
        style.MarginBottom.Should().Be("30px");
        style.MarginLeft.Should().Be("40px");
    }

    [Fact]
    public void Padding_TwoValues_VerticalHorizontal()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("padding", "5px 15px", style).Should().BeTrue();

        style.PaddingTop.Should().Be("5px");
        style.PaddingRight.Should().Be("15px");
        style.PaddingBottom.Should().Be("5px");
        style.PaddingLeft.Should().Be("15px");
    }

    [Fact]
    public void Border_Shorthand_ExpandsAllSides()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("border", "2px solid red", style).Should().BeTrue();

        style.Get("border-top-width").Should().Be("2px");
        style.Get("border-right-width").Should().Be("2px");
        style.Get("border-top-style").Should().Be("solid");
        style.Get("border-top-color").Should().Be("red");
    }

    [Fact]
    public void Border_ThinKeyword_NormalizesTo1px()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("border", "thin solid black", style).Should().BeTrue();

        style.Get("border-top-width").Should().Be("1px");
    }

    [Fact]
    public void Background_Shorthand_SetsBackgroundColor()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("background", "#ff0000", style).Should().BeTrue();

        style.BackgroundColor.Should().Be("#ff0000");
    }

    [Fact]
    public void NonShorthand_ReturnsFalse()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("color", "red", style).Should().BeFalse();
    }

    [Fact]
    public void Margin_WithEmUnits_PreservedCorrectly()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("margin", "1em 2em", style).Should().BeTrue();

        style.MarginTop.Should().Be("1em");
        style.MarginRight.Should().Be("2em");
    }

    [Fact]
    public void Margin_Auto_PreservedCorrectly()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("margin", "0 auto", style).Should().BeTrue();

        style.MarginTop.Should().Be("0");
        style.MarginRight.Should().Be("auto");
        style.MarginBottom.Should().Be("0");
        style.MarginLeft.Should().Be("auto");
    }

    [Fact]
    public void Font_Shorthand_BoldSizeFamily()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("font", "bold 14px Arial", style).Should().BeTrue();

        style.FontWeight.Should().Be("bold");
        style.FontSize.Should().Be("14px");
        style.FontFamily.Should().Be("Arial");
    }

    [Fact]
    public void Font_Shorthand_SizeLineHeightFamily()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("font", "16px/1.5 Helvetica", style).Should().BeTrue();

        style.FontSize.Should().Be("16px");
        style.Get("line-height").Should().Be("1.5");
        style.FontFamily.Should().Be("Helvetica");
    }

    [Fact]
    public void Font_Shorthand_ItalicBoldSizeFamily()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("font", "italic bold 12px serif", style).Should().BeTrue();

        style.Get("font-style").Should().Be("italic");
        style.FontWeight.Should().Be("bold");
        style.FontSize.Should().Be("12px");
        style.FontFamily.Should().Be("serif");
    }

    [Fact]
    public void ListStyle_Shorthand_TypeAndPosition()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("list-style", "disc outside", style).Should().BeTrue();

        style.Get("list-style-type").Should().Be("disc");
        style.Get("list-style-position").Should().Be("outside");
    }

    [Fact]
    public void BorderTop_Shorthand()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("border-top", "2px solid red", style).Should().BeTrue();

        style.Get("border-top-width").Should().Be("2px");
        style.Get("border-top-style").Should().Be("solid");
        style.Get("border-top-color").Should().Be("red");
    }

    [Fact]
    public void Flex_Shorthand()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("flex", "1 0 auto", style).Should().BeTrue();

        style.Get("flex-grow").Should().Be("1");
        style.Get("flex-shrink").Should().Be("0");
        style.Get("flex-basis").Should().Be("auto");
    }

    [Fact]
    public void Outline_Shorthand_WidthStyleColor()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("outline", "2px solid blue", style).Should().BeTrue();

        style.Get("outline-width").Should().Be("2px");
        style.Get("outline-style").Should().Be("solid");
        style.Get("outline-color").Should().Be("blue");
    }

    [Fact]
    public void Outline_Shorthand_None()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("outline", "none", style).Should().BeTrue();

        style.Get("outline-style").Should().Be("none");
    }
}
