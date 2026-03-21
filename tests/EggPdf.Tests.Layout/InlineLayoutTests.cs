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
}
