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

    [Fact]
    public void TextUnderlineOffset_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-decoration: underline; text-underline-offset: 4px'>Text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("text-underline-offset").Should().Be("4px",
            "text-underline-offset should be preserved in computed style");
    }

    [Fact]
    public void TextDecorationThickness_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-decoration: underline; text-decoration-thickness: 2px'>Text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("text-decoration-thickness").Should().Be("2px",
            "text-decoration-thickness should be preserved in computed style");
    }

    [Fact]
    public void TextDecorationSkipInk_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-decoration: underline; text-decoration-skip-ink: none'>Text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("text-decoration-skip-ink").Should().Be("none",
            "text-decoration-skip-ink should be preserved in computed style");
    }

    [Fact]
    public void TextDecorationColor_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-decoration: underline; text-decoration-color: red'>Text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("text-decoration-color").Should().Be("red");
    }

    [Fact]
    public void TextDecorationStyle_Wavy_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-decoration: underline wavy'>Text</p>", 400, 600);
        var p = root.FindByTag("p");
        // text-decoration shorthand with style=wavy should set text-decoration-style
        p!.Style.Get("text-decoration-style").Should().Be("wavy");
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

    // ── initial-letter (drop cap) ────────────────────────────────────────────

    [Fact]
    public void InitialLetter_StyleStoredOnParagraph()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='initial-letter:3'>Once upon a time</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("initial-letter").Should().Be("3",
            "initial-letter value should be preserved in style");
    }

    [Fact]
    public void InitialLetter_3_FirstLetterTallerThanNormalText()
    {
        // With initial-letter:3, the first letter box should be about 3x taller than normal text
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'>" +
            "<p style='initial-letter:3; font-size:12px; width:300px'>Once upon a time in a land far away</p>" +
            "</body>", 400, 600);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        // Find the first text child — should be the drop-cap letter 'O'
        var textBoxes = p!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));
        textBoxes.Should().NotBeEmpty("paragraph should have text boxes");

        var firstLetterBox = textBoxes[0];
        firstLetterBox.Text.Should().Be("O", "first text box should contain only the first letter");

        // Normal line height at 12px ≈ 14-16px. Drop cap at 3 lines ≈ 42-48px.
        firstLetterBox.Height.Should().BeGreaterThan(30f,
            "initial-letter:3 should make first letter at least 3x taller than base font size");
    }

    [Fact]
    public void InitialLetter_1_SameHeightAsNormalText()
    {
        // initial-letter:1 should behave like normal text (no enlargement)
        var rootNormal = LayoutTestHelper.Layout(
            "<p style='font-size:12px; width:200px'>Hello world</p>", 400, 600);
        var rootDrop = LayoutTestHelper.Layout(
            "<p style='initial-letter:1; font-size:12px; width:200px'>Hello world</p>", 400, 600);

        var normalFirstBox = rootNormal.FindByTag("p")!.Children.Find(b => !string.IsNullOrEmpty(b.Text));
        var dropFirstBox = rootDrop.FindByTag("p")!.Children.Find(b => !string.IsNullOrEmpty(b.Text));

        normalFirstBox.Should().NotBeNull();
        dropFirstBox.Should().NotBeNull();

        // Heights should be similar (within 5px)
        dropFirstBox!.Height.Should().BeApproximately(normalFirstBox!.Height, 5f,
            "initial-letter:1 should not significantly change text height");
    }

    // ── text-align-last ─────────────────────────────────────────────────────

    [Fact]
    public void TextAlignLast_Center_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-align: justify; text-align-last: center'>Some text</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("text-align-last").Should().Be("center",
            "text-align-last: center should be preserved in computed style");
    }

    [Fact]
    public void TextAlignLast_Right_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-align: justify; text-align-last: right'>Some text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("text-align-last").Should().Be("right");
    }

    [Fact]
    public void TextAlignLast_Left_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-align-last: left'>Some text</p>", 400, 600);
        var p = root.FindByTag("p");
        p!.Style.Get("text-align-last").Should().Be("left");
    }

    // ── text-emphasis ────────────────────────────────────────────────────────

    [Fact]
    public void TextEmphasis_Dot_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<span style='text-emphasis: dot'>CJK</span>", 400, 600);
        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Style.Get("text-emphasis-style").Should().Be("dot",
            "text-emphasis: dot shorthand should set text-emphasis-style");
    }

    [Fact]
    public void TextEmphasis_Position_Over_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<span style='text-emphasis-position: over'>CJK</span>", 400, 600);
        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Style.Get("text-emphasis-position").Should().Be("over");
    }

    [Fact]
    public void TextEmphasis_Color_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<span style='text-emphasis-color: red'>CJK</span>", 400, 600);
        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Style.Get("text-emphasis-color").Should().Be("red");
    }

    [Fact]
    public void TextEmphasis_Inherited_FromParent()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='text-emphasis-color: blue'><span>child</span></div>", 400, 600);
        var span = root.FindByTag("span");
        span.Should().NotBeNull();
        span!.Style.Get("text-emphasis-color").Should().Be("blue",
            "text-emphasis-color is an inherited property");
    }

    // ── hanging-punctuation ──────────────────────────────────────────────────

    [Fact]
    public void HangingPunctuation_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='hanging-punctuation: first'>\"Quote text\"</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("hanging-punctuation").Should().Be("first");
    }

    [Fact]
    public void HangingPunctuation_IsInherited()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='hanging-punctuation: first last'><p>text</p></div>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("hanging-punctuation").Should().Be("first last",
            "hanging-punctuation is an inherited property");
    }

    [Fact]
    public void HangingPunctuation_None_IsDefault()
    {
        // Default value is none — no special handling
        var root = LayoutTestHelper.Layout(
            "<p>Normal text</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        var val = p!.Style.Get("hanging-punctuation");
        // Default is null/empty or "none" — either is acceptable
        (val == null || val == "" || val == "none").Should().BeTrue(
            "default hanging-punctuation should be none or unset");
    }

    [Fact]
    public void HangingPunctuation_First_InlineBoxStartsBeforeContentEdge()
    {
        // When hanging-punctuation: first and text starts with an opening quote,
        // the first inline text box X should be less than the paragraph's content edge (p.X + p.PaddingLeft)
        var root = LayoutTestHelper.Layout(
            "<p style='hanging-punctuation: first; margin: 0; padding: 0'>\"Quoted text goes here\"</p>",
            400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Children.Count.Should().BeGreaterThan(0, "paragraph should have child text boxes");
        var firstTextBox = p.Children[0];
        float contentEdge = p.X + p.PaddingLeft;
        firstTextBox.X.Should().BeLessThan(contentEdge,
            "hanging-punctuation: first should shift the first text box left of the content edge");
    }
}
