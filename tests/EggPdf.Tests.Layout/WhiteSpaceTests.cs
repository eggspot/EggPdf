using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class WhiteSpaceTests
{
    private static readonly string NoBreakSpace = ((char)0xA0).ToString();

    [Fact]
    public void WhiteSpacePre_PreservesNewlines()
    {
        var root = LayoutTestHelper.Layout(
            "<pre>Line 1\nLine 2\nLine 3</pre>", 600, 800);

        var pre = root.FindByTag("pre");
        pre.Should().NotBeNull();

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

        var textBoxes = pre!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Should().NotBeEmpty();
        var allText = string.Join("", textBoxes.Select(t => t.Text));
        allText.Should().Contain("  hello   world");
    }

    [Fact]
    public void MixedInlineContent_CrlfAfterInlineSibling_ProducesNoRawControlCharacters()
    {
        var htmlWrappedByATemplate = "<p>Foo <strong>bar</strong> baz)\r\n    qux</p>";
        var root = LayoutTestHelper.Layout(htmlWrappedByATemplate, 600, 800);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var allText = string.Join("", p!.Children.Select(c => c.Text ?? ""));
        allText.Should().NotContain("\r",
            "trimming only touches a text node's edges, so word splitting after an inline sibling must treat CR " +
            "as a delimiter, or it paints as a .notdef box");
        allText.Should().NotContain("\n", "a raw LF must not reach a painted text box");
        allText.Should().Contain("qux");
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

        float baseX = p.X + p.PaddingLeft;
        textBoxes[0].X.Should().BeGreaterThan(baseX, "first line should be indented");

        if (textBoxes.Count > 1)
        {
            textBoxes[1].X.Should().BeLessOrEqualTo(textBoxes[0].X,
                "subsequent lines should not have text-indent");
        }
    }

    [Fact]
    public void PreElement_DirectText_MultiLine()
    {
        var root = LayoutTestHelper.Layout(
            "<pre>function hello() {\n  return 'world';\n}</pre>", 600, 800);

        var pre = root.FindByTag("pre");
        pre.Should().NotBeNull();

        var textBoxes = pre!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Count.Should().BeGreaterOrEqualTo(3,
            "the UA stylesheet gives <pre> white-space: pre, so its direct text keeps its newlines");
    }

    [Fact]
    public void OverflowWrap_BreakWord_BreaksLongWord()
    {
        var html = @"<div style='width: 60px; overflow-wrap: break-word;'>ABCDEFGHIJKLMNOPQRSTUVWXYZ</div>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();

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
        var html = @"<div style='width: 60px;'>ABCDEFGHIJKLMNOPQRSTUVWXYZ</div>";
        var root = LayoutTestHelper.Layout(html, 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();

        var textBoxes = div!.Children.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
        textBoxes.Count.Should().Be(1, "without break-word, long word stays as single line and overflows");
    }

    [Fact]
    public void Nbsp_InlineText_IsRendered()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><p>PAYMENT LINK:&nbsp;&nbsp;<a>Pay Now</a></p></body>",
            400, 600);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var allText = string.Concat(p!.Children
            .Where(b => !string.IsNullOrEmpty(b.Text))
            .Select(b => b.Text));

        allText.Should().Contain(NoBreakSpace,
            "non-breaking spaces from &nbsp; must survive trimming and appear in layout");
    }

    [Fact]
    public void Nbsp_OnlyNode_IsNotSkipped()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><p><span>A</span>&nbsp;&nbsp;<span>B</span></p></body>",
            400, 600);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var allText = string.Concat(p!.Children
            .SelectMany(b => new[] { b }.Concat(b.Children))
            .Where(b => !string.IsNullOrEmpty(b.Text))
            .Select(b => b.Text));

        allText.Should().Contain(NoBreakSpace,
            "a text node with only &nbsp; must not be skipped");
    }

    [Fact]
    public void Nbsp_TrailingInSiblingSpan_IsNotTrimmedAway()
    {
        var root = LayoutTestHelper.Layout(
            "<body style='margin:0'><ul><li><span class='dash'>-&nbsp;</span>" +
            "<span><span class='label'>Label:</span> <span class='value'>VALUE</span></span></li></ul></body>",
            400, 600);

        var li = root.FindByTag("li");
        li.Should().NotBeNull();

        var textBoxes = li!.Children
            .SelectMany(b => new[] { b }.Concat(b.Children).Concat(b.Children.SelectMany(c => c.Children)))
            .Where(b => !string.IsNullOrEmpty(b.Text))
            .OrderBy(b => b.X)
            .ToList();

        var dashBox = textBoxes.FirstOrDefault(b => b.Text!.Contains("-"));
        dashBox.Should().NotBeNull();

        dashBox!.Text.Should().Contain(NoBreakSpace,
            "the dash marker is its own <span>, so it goes through the inline-run whitespace trimming, which must " +
            "keep the trailing &nbsp; or the marker and label glue together");

        float bareDashWidth = EggPdf.Layout.TextMeasurer.MeasureWidth("-", 16f, "Helvetica");
        dashBox.Width.Should().BeGreaterThan(bareDashWidth + 1f,
            "the nbsp's own advance is what separates the dash from the label");
    }

    [Fact]
    public void Nbsp_Width_EqualsSpaceWidth()
    {
        float spaceWidth = EggPdf.Layout.TextMeasurer.MeasureWidth(" ", 12f, "Arial");
        float nbspWidth = EggPdf.Layout.TextMeasurer.MeasureWidth(NoBreakSpace, 12f, "Arial");

        nbspWidth.Should().BeApproximately(spaceWidth, 0.5f,
            "non-breaking space should have the same width as a regular space, within font metric rounding");
    }

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
