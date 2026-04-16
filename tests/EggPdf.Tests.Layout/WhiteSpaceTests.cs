using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class WhiteSpaceTests
{
    [Fact]
    public void WhiteSpacePre_PreservesNewlines()
    {
        var root = LayoutTestHelper.Layout(
            "<pre>Line 1\nLine 2\nLine 3</pre>", 600, 800);

        var pre = root.FindByTag("pre");
        pre.Should().NotBeNull();

        // Should have multiple text boxes for each line
        var textBoxes = pre!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Count.Should().BeGreaterOrEqualTo(3, "pre should preserve newlines as separate lines");
    }

    [Fact]
    public void WhiteSpacePre_PreservesSpaces()
    {
        var root = LayoutTestHelper.Layout(
            "<pre>  hello   world  </pre>", 600, 800);

        var pre = root.FindByTag("pre");
        pre.Should().NotBeNull();

        // Text should contain preserved spaces
        var textBoxes = pre!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Should().NotBeEmpty();
        var allText = string.Join("", textBoxes.Select(t => t.Text));
        allText.Should().Contain("  hello   world");
    }

    [Fact]
    public void WhiteSpaceNowrap_SingleLine()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='white-space: nowrap'>This is a very long line that should not wrap even if it exceeds the container width because nowrap is set</p>",
            200, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var textBoxes = p!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Should().HaveCount(1, "nowrap text should not wrap to multiple lines");
    }

    [Fact]
    public void WhiteSpaceNormal_CollapsesAndWraps()
    {
        var root = LayoutTestHelper.Layout(
            "<p>The   quick   brown   fox</p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var textBoxes = p!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Should().NotBeEmpty();
        // Collapsed text should not have multiple consecutive spaces
        foreach (var tb in textBoxes)
        {
            tb.Text.Should().NotContain("  ", "normal white-space should collapse multiple spaces");
        }
    }

    [Fact]
    public void WhiteSpacePreWrap_PreservesAndWraps()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='white-space: pre-wrap'>Line 1\nLine 2</div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();

        var textBoxes = div!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Count.Should().BeGreaterOrEqualTo(2, "pre-wrap should preserve newlines");
    }

    [Fact]
    public void WhiteSpacePreLine_CollapsesSpacesButPreservesNewlines()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='white-space: pre-line'>Line  1\nLine  2</div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();

        var textBoxes = div!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Count.Should().BeGreaterOrEqualTo(2, "pre-line should preserve newlines");

        // Should collapse spaces within lines
        foreach (var tb in textBoxes)
        {
            if (!string.IsNullOrEmpty(tb.Text))
                tb.Text.Should().NotContain("  ", "pre-line should collapse multiple spaces");
        }
    }

    [Fact]
    public void TextIndent_FirstLineIndented()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='text-indent: 30px'>First line of a paragraph that should be indented</p>", 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var textBoxes = p!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Should().NotBeEmpty();

        // First text box X should be offset by text-indent
        float baseX = p.X + p.PaddingLeft;
        textBoxes[0].X.Should().BeGreaterThan(baseX, "first line should be indented");

        // If there are subsequent lines, they should not be indented
        if (textBoxes.Count > 1)
        {
            textBoxes[1].X.Should().BeLessOrEqualTo(textBoxes[0].X,
                "subsequent lines should not have text-indent");
        }
    }

    [Fact]
    public void PreElement_DirectText_MultiLine()
    {
        // <pre> has white-space:pre in UA defaults
        // Direct text content (not wrapped in inline child)
        var root = LayoutTestHelper.Layout(
            "<pre>function hello() {\n  return 'world';\n}</pre>", 600, 800);

        var pre = root.FindByTag("pre");
        pre.Should().NotBeNull();

        var textBoxes = pre!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Count.Should().BeGreaterOrEqualTo(3, "pre with newlines should produce multiple text lines");
    }

    [Fact]
    public void OverflowWrap_BreakWord_BreaksLongWord()
    {
        // A very long word in a narrow container with overflow-wrap:break-word
        var html = @"<div style='width: 60px; overflow-wrap: break-word;'>ABCDEFGHIJKLMNOPQRSTUVWXYZ</div>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();

        // The long word should produce multiple text lines
        var textBoxes = div!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Count.Should().BeGreaterThan(1, "long word should break across multiple lines");
    }

    [Fact]
    public void WordBreak_BreakAll_BreaksAtCharacter()
    {
        var html = @"<div style='width: 50px; word-break: break-all;'>LONGWORDHERE</div>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();

        var textBoxes = div!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Count.Should().BeGreaterThan(1, "word should break at character boundary");
    }

    [Fact]
    public void NoBreakWord_LongWordOnSingleLine()
    {
        // Without overflow-wrap, long word stays on one line
        var html = @"<div style='width: 60px;'>ABCDEFGHIJKLMNOPQRSTUVWXYZ</div>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();

        // Without break-word, the text is on a single line (overflows)
        var textBoxes = div!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Count.Should().Be(1, "without break-word, long word stays as single line");
    }

    // ── &nbsp; / \u00A0 non-breaking space ──────────────────────────────────

    [Fact]
    public void Nbsp_InlineText_IsRendered()
    {
        // Text node with &nbsp; between two words should produce a visible gap.
        // The \u00A0 must NOT be trimmed or collapsed.
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><p>PAYMENT LINK:&nbsp;&nbsp;<a>Pay Now</a></p></body>",
            400, 600);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        // All text boxes concatenated should contain the original \u00A0 characters
        var allText = string.Concat(p!.Children
            .Where(b => !string.IsNullOrEmpty(b.Text))
            .Select(b => b.Text));

        allText.Should().Contain("\u00A0",
            "non-breaking spaces from &nbsp; must survive trimming and appear in layout");
    }

    [Fact]
    public void Nbsp_OnlyNode_IsNotSkipped()
    {
        // A text node that contains ONLY &nbsp; entities should produce a layout box.
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><p><span>A</span>&nbsp;&nbsp;<span>B</span></p></body>",
            400, 600);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var allText = string.Concat(p!.Children
            .SelectMany(b => new[] { b }.Concat(b.Children))
            .Where(b => !string.IsNullOrEmpty(b.Text))
            .Select(b => b.Text));

        allText.Should().Contain("\u00A0",
            "a text node with only &nbsp; must not be skipped");
    }

    [Fact]
    public void Nbsp_Width_EqualsSpaceWidth()
    {
        // A paragraph with one &nbsp; should have the same width as one normal space
        // when measured (within a small tolerance for font metric rounding).
        float spaceWidth = EggPdf.Layout.TextMeasurer.MeasureWidth(" ", 12f, "Arial");
        float nbspWidth  = EggPdf.Layout.TextMeasurer.MeasureWidth("\u00A0", 12f, "Arial");

        nbspWidth.Should().BeApproximately(spaceWidth, 0.5f,
            "non-breaking space should have the same width as a regular space");
    }

    // ── tab-size ─────────────────────────────────────────────────────────────

    [Fact]
    public void TabSize_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<pre style='tab-size: 8'>text</pre>", 400, 600);
        var pre = root.FindByTag("pre");
        pre!.Style.Get("tab-size").Should().Be("8",
            "tab-size should be preserved in computed style");
    }

    [Fact]
    public void TabSize_4_WiderThan_2()
    {
        // In a pre block a tab with tab-size:4 is expanded to 4 spaces, while tab-size:2
        // expands to 2 spaces.  The text box that contains the tab + "X" should therefore
        // be wider when tab-size is larger.
        var root2 = LayoutTestHelper.Layout("<pre style='tab-size:2'>\tX</pre>", 400, 600);
        var root4 = LayoutTestHelper.Layout("<pre style='tab-size:4'>\tX</pre>", 400, 600);

        var box2 = root2.FindByTag("pre")!.Children.Find(b => !string.IsNullOrEmpty(b.Text) && b.Text.Contains("X"));
        var box4 = root4.FindByTag("pre")!.Children.Find(b => !string.IsNullOrEmpty(b.Text) && b.Text.Contains("X"));

        box2.Should().NotBeNull();
        box4.Should().NotBeNull();

        box4!.ContentWidth.Should().BeGreaterThan(box2!.ContentWidth,
            "tab-size:4 expands to 4 spaces so the line should be wider than tab-size:2 (2 spaces)");
    }
}
