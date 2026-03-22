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
}
