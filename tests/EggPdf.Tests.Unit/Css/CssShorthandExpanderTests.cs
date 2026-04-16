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
    public void Background_Shorthand_ColorAndImage_ExtractsBoth()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("background", "url('img.png') no-repeat center #fff", style).Should().BeTrue();

        style.BackgroundColor.Should().Be("#fff");
        style.Get("background-image").Should().Be("url('img.png')");
        style.Get("background-repeat").Should().Be("no-repeat");
        style.Get("background-position").Should().Be("center");
    }

    [Fact]
    public void Background_Shorthand_GradientNoColor_SetsImage()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("background", "linear-gradient(to right, red, blue)", style).Should().BeTrue();

        style.Get("background-image").Should().Contain("linear-gradient");
    }

    [Fact]
    public void Background_Shorthand_NoneKeyword_ClearsImage()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("background", "none", style).Should().BeTrue();

        style.Get("background-image").Should().Be("none");
    }

    [Fact]
    public void Background_Shorthand_SizeAfterSlash_ExtractsSize()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("background", "url('bg.png') center/cover no-repeat", style).Should().BeTrue();

        style.Get("background-image").Should().Be("url('bg.png')");
        style.Get("background-position").Should().Be("center");
        style.Get("background-size").Should().Be("cover");
        style.Get("background-repeat").Should().Be("no-repeat");
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
    public void Flex_SingleNumber_SetsBasisToZero()
    {
        // CSS spec: flex: <number> → flex-grow:<n>, flex-shrink:1, flex-basis:0
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("flex", "2", style).Should().BeTrue();

        style.Get("flex-grow").Should().Be("2");
        style.Get("flex-shrink").Should().Be("1");
        style.Get("flex-basis").Should().Be("0");
    }

    [Fact]
    public void Flex_SingleOne_SetsBasisToZero()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("flex", "1", style).Should().BeTrue();

        style.Get("flex-grow").Should().Be("1");
        style.Get("flex-shrink").Should().Be("1");
        style.Get("flex-basis").Should().Be("0");
    }

    [Fact]
    public void Flex_None_SetsZeroZeroAuto()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("flex", "none", style).Should().BeTrue();

        style.Get("flex-grow").Should().Be("0");
        style.Get("flex-shrink").Should().Be("0");
        style.Get("flex-basis").Should().Be("auto");
    }

    [Fact]
    public void Flex_Auto_SetsOneOneAuto()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("flex", "auto", style).Should().BeTrue();

        style.Get("flex-grow").Should().Be("1");
        style.Get("flex-shrink").Should().Be("1");
        style.Get("flex-basis").Should().Be("auto");
    }

    [Fact]
    public void Flex_TwoNumbers_SetsBasisToZero()
    {
        // CSS spec: flex: <grow> <shrink> → flex-basis:0
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("flex", "2 1", style).Should().BeTrue();

        style.Get("flex-grow").Should().Be("2");
        style.Get("flex-shrink").Should().Be("1");
        style.Get("flex-basis").Should().Be("0");
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

    [Fact]
    public void FlexFlow_RowWrap_ExpandsDirectionAndWrap()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("flex-flow", "row wrap", style).Should().BeTrue();

        style.Get("flex-direction").Should().Be("row");
        style.Get("flex-wrap").Should().Be("wrap");
    }

    [Fact]
    public void FlexFlow_ColumnNowrap_ExpandsDirectionAndWrap()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("flex-flow", "column nowrap", style).Should().BeTrue();

        style.Get("flex-direction").Should().Be("column");
        style.Get("flex-wrap").Should().Be("nowrap");
    }

    [Fact]
    public void FlexFlow_DirectionOnly_DefaultsWrapToNowrap()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("flex-flow", "row-reverse", style).Should().BeTrue();

        style.Get("flex-direction").Should().Be("row-reverse");
        style.Get("flex-wrap").Should().Be("nowrap");
    }

    [Fact]
    public void FlexFlow_WrapOnly_DefaultsDirectionToRow()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("flex-flow", "wrap-reverse", style).Should().BeTrue();

        style.Get("flex-direction").Should().Be("row");
        style.Get("flex-wrap").Should().Be("wrap-reverse");
    }

    // === border-radius shorthand ===

    [Fact]
    public void BorderRadius_SingleValue_AllFourCorners()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("border-radius", "8px", style).Should().BeTrue();

        style.Get("border-top-left-radius").Should().Be("8px");
        style.Get("border-top-right-radius").Should().Be("8px");
        style.Get("border-bottom-right-radius").Should().Be("8px");
        style.Get("border-bottom-left-radius").Should().Be("8px");
    }

    [Fact]
    public void BorderRadius_TwoValues_TopLeftBottomRight_TopRightBottomLeft()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("border-radius", "4px 8px", style).Should().BeTrue();

        style.Get("border-top-left-radius").Should().Be("4px");
        style.Get("border-top-right-radius").Should().Be("8px");
        style.Get("border-bottom-right-radius").Should().Be("4px");
        style.Get("border-bottom-left-radius").Should().Be("8px");
    }

    [Fact]
    public void BorderRadius_ThreeValues_TopLeft_TopRightBottomLeft_BottomRight()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("border-radius", "4px 8px 12px", style).Should().BeTrue();

        style.Get("border-top-left-radius").Should().Be("4px");
        style.Get("border-top-right-radius").Should().Be("8px");
        style.Get("border-bottom-right-radius").Should().Be("12px");
        style.Get("border-bottom-left-radius").Should().Be("8px");
    }

    [Fact]
    public void BorderRadius_FourValues_EachCorner()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("border-radius", "2px 4px 6px 8px", style).Should().BeTrue();

        style.Get("border-top-left-radius").Should().Be("2px");
        style.Get("border-top-right-radius").Should().Be("4px");
        style.Get("border-bottom-right-radius").Should().Be("6px");
        style.Get("border-bottom-left-radius").Should().Be("8px");
    }

    [Fact]
    public void BorderRadius_Percentage_Preserved()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("border-radius", "50%", style).Should().BeTrue();

        style.Get("border-top-left-radius").Should().Be("50%");
        style.Get("border-top-right-radius").Should().Be("50%");
        style.Get("border-bottom-right-radius").Should().Be("50%");
        style.Get("border-bottom-left-radius").Should().Be("50%");
    }

    [Fact]
    public void BorderRadius_SlashSyntax_StoresHorizontalAndVerticalRadii()
    {
        // border-radius: 10px / 5px → each corner has "10px 5px" (h v)
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("border-radius", "10px / 5px", style).Should().BeTrue();

        style.Get("border-top-left-radius").Should().Be("10px 5px",
            "slash syntax sets horizontal and vertical radius as two-value shorthand");
        style.Get("border-top-right-radius").Should().Be("10px 5px");
        style.Get("border-bottom-right-radius").Should().Be("10px 5px");
        style.Get("border-bottom-left-radius").Should().Be("10px 5px");
    }

    [Fact]
    public void BorderRadius_SlashSyntax_DifferentPerSide()
    {
        // border-radius: 20px 10px / 8px 4px
        // TL=20px 8px, TR=10px 4px, BR=20px 8px, BL=10px 4px
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("border-radius", "20px 10px / 8px 4px", style).Should().BeTrue();

        style.Get("border-top-left-radius").Should().Be("20px 8px");
        style.Get("border-top-right-radius").Should().Be("10px 4px");
        style.Get("border-bottom-right-radius").Should().Be("20px 8px");
        style.Get("border-bottom-left-radius").Should().Be("10px 4px");
    }

    // === text-decoration shorthand ===

    [Fact]
    public void TextDecoration_Underline_SetsLine()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("text-decoration", "underline", style).Should().BeTrue();
        style.Get("text-decoration-line").Should().Be("underline");
    }

    [Fact]
    public void TextDecoration_UnderlineDashedRed_SetsAllLonghands()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("text-decoration", "underline dashed red", style).Should().BeTrue();
        style.Get("text-decoration-line").Should().Be("underline");
        style.Get("text-decoration-style").Should().Be("dashed");
        style.Get("text-decoration-color").Should().Be("red");
    }

    [Fact]
    public void TextDecoration_None_SetsLineNone()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("text-decoration", "none", style).Should().BeTrue();
        style.Get("text-decoration-line").Should().Be("none");
    }

    // === place-items / place-self / place-content ===

    [Fact]
    public void PlaceItems_SingleValue_SetsAlignAndJustify()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("place-items", "center", style).Should().BeTrue();

        style.Get("align-items").Should().Be("center");
        style.Get("justify-items").Should().Be("center");
    }

    [Fact]
    public void PlaceItems_TwoValues_SetsAlignAndJustify()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("place-items", "start end", style).Should().BeTrue();

        style.Get("align-items").Should().Be("start");
        style.Get("justify-items").Should().Be("end");
    }

    [Fact]
    public void PlaceSelf_SingleValue_SetsAlignAndJustify()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("place-self", "center", style).Should().BeTrue();

        style.Get("align-self").Should().Be("center");
        style.Get("justify-self").Should().Be("center");
    }

    [Fact]
    public void PlaceContent_TwoValues_SetsAlignAndJustify()
    {
        var style = new ComputedStyle();
        CssShorthandExpander.TryExpand("place-content", "space-between end", style).Should().BeTrue();

        style.Get("align-content").Should().Be("space-between");
        style.Get("justify-content").Should().Be("end");
    }
}
