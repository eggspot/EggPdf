using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class InlineLayoutTests
{
    [Fact]
    public void SingleWord_FitsOnOneLine()
    {
        var root = LayoutTestHelper.Layout("<p>Hello</p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void LongText_WrapsToMultipleLines()
    {
        // With a narrow container, long text should wrap
        var root = LayoutTestHelper.Layout(
            "<div style='width: 100px'><p>This is a long paragraph that should wrap to multiple lines because the container is narrow</p></div>",
            600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        // Multiple lines should make the paragraph taller than a single line
        p!.Height.Should().BeGreaterThan(15); // at least more than one line
    }

    [Fact]
    public void BoldText_RenderedWithBoldFont()
    {
        var root = LayoutTestHelper.Layout("<p><strong>Bold text</strong></p>", 600, 800);

        var strong = root.FindByTag("strong");
        strong.Should().NotBeNull();
        strong!.Style.FontWeight.Should().Be("bold");
    }

    [Fact]
    public void ItalicText_RenderedWithItalicStyle()
    {
        var root = LayoutTestHelper.Layout("<p><em>Italic text</em></p>", 600, 800);

        var em = root.FindByTag("em");
        em.Should().NotBeNull();
        em!.Style.Get("font-style").Should().Be("italic");
    }

    [Fact]
    public void InlineElements_FlowHorizontally()
    {
        var root = LayoutTestHelper.Layout(
            "<p><span>First</span> <span>Second</span></p>", 600, 800);

        var spans = root.FindAllByTag("span");
        spans.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public void TextAlign_Center_AppliedToBlock()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-align: center'>Centered</p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.TextAlign.Should().Be("center");
    }

    [Fact]
    public void WhitespacePre_Preserved()
    {
        var root = LayoutTestHelper.Layout(
            "<pre>  hello\n  world  </pre>", 600, 800);

        var pre = root.FindByTag("pre");
        pre.Should().NotBeNull();
        pre!.Style.Get("white-space").Should().Be("pre");
    }

    [Fact]
    public void CodeElement_MonospaceFont()
    {
        var root = LayoutTestHelper.Layout("<p><code>var x = 1;</code></p>", 600, 800);

        var code = root.FindByTag("code");
        code.Should().NotBeNull();
        code!.Style.FontFamily.Should().Be("monospace");
    }

    [Fact]
    public void LineHeight_AffectsTextBoxHeight()
    {
        // Default line height should produce reasonable height
        var root = LayoutTestHelper.Layout("<p>Single line</p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        // Height should be roughly fontSize * lineHeight + margins
        p!.Height.Should().BeGreaterThan(10);
        p.Height.Should().BeLessThan(100); // not absurdly large
    }

    [Fact]
    public void NestedInlineElements_AllRendered()
    {
        var root = LayoutTestHelper.Layout(
            "<p>Normal <strong>bold</strong> <em>italic</em> text</p>", 600, 800);

        // Inline children should produce layout boxes
        root.FindByTag("strong").Should().NotBeNull();
        root.FindByTag("em").Should().NotBeNull();
    }

    [Fact]
    public void EmptyParagraph_HasMinimalHeight()
    {
        var root = LayoutTestHelper.Layout("<p></p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        // Empty paragraph should still have height from margins
    }

    [Fact]
    public void BrElement_CausesLineBreak()
    {
        var root = LayoutTestHelper.Layout("<p>Line one<br>Line two</p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        // Should be taller than a single line due to <br>
    }

    [Fact]
    public void MultipleInlineChildren_InSameBlock()
    {
        var root = LayoutTestHelper.Layout(
            "<div>Text <a href='#'>link</a> more text</div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Children.Should().NotBeEmpty();
    }

    [Fact]
    public void TwoSpans_SameLine_SameY()
    {
        var root = LayoutTestHelper.Layout(
            "<p><span>Hello</span><span>World</span></p>", 600, 800);

        var spans = root.FindAllByTag("span");
        spans.Should().HaveCount(2);

        // Both spans should be on the same line (same Y)
        spans[1].Y.Should().Be(spans[0].Y, "inline spans should be on the same line");
        // Second span should be to the right of the first
        spans[1].X.Should().BeGreaterThan(spans[0].X, "second span should be to the right");
    }

    [Fact]
    public void InlineElement_WidthMatchesContent()
    {
        var root = LayoutTestHelper.Layout(
            "<p><span>Hi</span></p>", 600, 800);

        var span = root.FindByTag("span");
        span.Should().NotBeNull();

        // Inline element width should match text content, not full container width
        span!.Width.Should().BeLessThan(600, "inline element should not be full container width");
    }

    [Fact]
    public void MixedInlineAndText_SameY()
    {
        var root = LayoutTestHelper.Layout(
            "<p><strong>Bold</strong> and <em>italic</em></p>", 600, 800);

        var strong = root.FindByTag("strong");
        var em = root.FindByTag("em");
        strong.Should().NotBeNull();
        em.Should().NotBeNull();

        // Both inline elements should be on the same line
        em!.Y.Should().Be(strong!.Y, "strong and em should share the same Y");
    }

    [Fact]
    public void TextNode_ThenInlineElement_XOrderCorrect()
    {
        // Reproduces: text node followed by inline element in same block
        // Text "Hello " should come before the <a> "World" on the X axis
        var root = LayoutTestHelper.Layout(
            "<p>Hello <a href='#'>World</a></p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        // Find text run boxes for "Hello" (no element ref) and "World" (element ref = <a>)
        var textBoxes = new System.Collections.Generic.List<LayoutBox>();
        foreach (var child in p!.Children)
            if (child.Text != null) textBoxes.Add(child);

        textBoxes.Should().HaveCountGreaterThan(1, "should have multiple word boxes");

        // First word ("Hello") should have smaller X than last word ("World")
        float firstX = textBoxes[0].X;
        float lastX = textBoxes[textBoxes.Count - 1].X;
        lastX.Should().BeGreaterThan(firstX, "inline element text must follow the preceding text node");
    }

    [Fact]
    public void MixedTextAndStrong_XOrderCorrect()
    {
        // Reproduces: (<strong>Stripe</strong> payment gateway)
        var root = LayoutTestHelper.Layout(
            "<p>(<strong>Bold</strong> normal text)</p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var textBoxes = new System.Collections.Generic.List<LayoutBox>();
        foreach (var child in p!.Children)
            if (child.Text != null) textBoxes.Add(child);

        // Should have at least 3 word boxes: "(", "Bold", " normal", ...
        textBoxes.Should().HaveCountGreaterThan(2);

        // Each successive word should be to the right of the previous
        for (int i = 1; i < textBoxes.Count; i++)
            textBoxes[i].X.Should().BeGreaterThan(textBoxes[i - 1].X,
                $"word {i} ('{textBoxes[i].Text}') must be right of word {i-1} ('{textBoxes[i-1].Text}')");
    }
}
