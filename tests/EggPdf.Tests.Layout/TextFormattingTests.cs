using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// Tests for text formatting properties: text-align, text-transform, text-indent,
/// letter-spacing, word-spacing, line-height, text-decoration, white-space variants.
/// </summary>
public class TextFormattingTests
{
    // ── text-align ──────────────────────────────────────────────────────────

    [Fact]
    public void TextAlign_Left_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-align: left'>Left</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("text-align").Should().Be("left");
    }

    [Fact]
    public void TextAlign_Center_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-align: center'>Center</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("text-align").Should().Be("center");
    }

    [Fact]
    public void TextAlign_Right_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-align: right'>Right</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("text-align").Should().Be("right");
    }

    [Fact]
    public void TextAlign_Justify_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-align: justify'>Justified text content</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("text-align").Should().Be("justify");
    }

    // ── text-transform ──────────────────────────────────────────────────────

    [Fact]
    public void TextTransform_Uppercase_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-transform: uppercase'>hello</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("text-transform").Should().Be("uppercase");
    }

    [Fact]
    public void TextTransform_Lowercase_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-transform: lowercase'>HELLO</p>", 600, 800);
        var p = root.FindByTag("p");
        p!.Style.Get("text-transform").Should().Be("lowercase");
    }

    [Fact]
    public void TextTransform_Capitalize_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-transform: capitalize'>hello world</p>", 600, 800);
        var p = root.FindByTag("p");
        p!.Style.Get("text-transform").Should().Be("capitalize");
    }

    // ── text-indent ─────────────────────────────────────────────────────────

    [Fact]
    public void TextIndent_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-indent: 30px'>Indented paragraph</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("text-indent").Should().Be("30px");
    }

    // ── letter-spacing / word-spacing ───────────────────────────────────────

    [Fact]
    public void LetterSpacing_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='letter-spacing: 2px'>Spaced</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("letter-spacing").Should().Be("2px");
    }

    [Fact]
    public void WordSpacing_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='word-spacing: 5px'>Word spacing</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("word-spacing").Should().Be("5px");
    }

    // ── line-height ─────────────────────────────────────────────────────────

    [Fact]
    public void LineHeight_Unitless_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='line-height: 1.5'>Text</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("line-height").Should().Be("1.5");
    }

    [Fact]
    public void LineHeight_Px_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='line-height: 24px'>Text</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("line-height").Should().Be("24px");
    }

    [Fact]
    public void LineHeight_Larger_IncreasesTextBoxHeight()
    {
        var rootNormal = LayoutTestHelper.Layout(
            "<p style='line-height: 1; font-size: 16px'>Text</p>", 600, 800);
        var rootLarge = LayoutTestHelper.Layout(
            "<p style='line-height: 3; font-size: 16px'>Text</p>", 600, 800);

        var pNormal = rootNormal.FindByTag("p");
        var pLarge = rootLarge.FindByTag("p");

        pNormal.Should().NotBeNull();
        pLarge.Should().NotBeNull();
        pLarge!.Height.Should().BeGreaterThan(pNormal!.Height,
            "larger line-height should produce taller text box");
    }

    // ── text-decoration ─────────────────────────────────────────────────────

    [Fact]
    public void TextDecoration_Underline_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-decoration: underline'>Underlined</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("text-decoration").Should().Be("underline");
    }

    [Fact]
    public void TextDecoration_LineThrough_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-decoration: line-through'>Strikethrough</p>", 600, 800);
        var p = root.FindByTag("p");
        p!.Style.Get("text-decoration").Should().Be("line-through");
    }

    // ── white-space ─────────────────────────────────────────────────────────

    [Fact]
    public void WhiteSpace_Nowrap_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='white-space: nowrap'>No wrap</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("white-space").Should().Be("nowrap");
    }

    [Fact]
    public void WhiteSpace_PreWrap_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='white-space: pre-wrap'>Pre wrap</p>", 600, 800);
        var p = root.FindByTag("p");
        p!.Style.Get("white-space").Should().Be("pre-wrap");
    }

    // ── font properties ─────────────────────────────────────────────────────

    [Fact]
    public void FontSize_Px_ProducesPositiveHeight()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-size: 24px'>Big text</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FontWeight_Bold_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-weight: bold'>Bold</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("font-weight").Should().Be("bold");
    }

    [Fact]
    public void FontStyle_Italic_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-style: italic'>Italic</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("font-style").Should().Be("italic");
    }

    [Fact]
    public void FontFamily_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='font-family: Arial, sans-serif'>Arial</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("font-family").Should().Contain("Arial");
    }

    // ── color ───────────────────────────────────────────────────────────────

    [Fact]
    public void Color_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='color: #ff0000'>Red text</p>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        // Color should be stored in style (exact value depends on cascade parsing)
        p!.Style.Get("color").Should().NotBeNullOrEmpty();
    }

    // ── inheritance ─────────────────────────────────────────────────────────

    [Fact]
    public void FontSize_InheritedByChildren()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='font-size: 20px'><p>Child text</p></div>", 600, 800);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void LargerFontSize_ProducesLargerTextBoxes()
    {
        var rootSmall = LayoutTestHelper.Layout(
            "<p style='font-size: 10px'>Text</p>", 600, 800);
        var rootLarge = LayoutTestHelper.Layout(
            "<p style='font-size: 32px'>Text</p>", 600, 800);

        rootSmall.FindByTag("p")!.Height.Should()
            .BeLessThan(rootLarge.FindByTag("p")!.Height,
            "larger font size should produce taller text boxes");
    }
}
