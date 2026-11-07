using System;
using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class FloatShrinkToFitTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<body style='margin:0'><div style='width:400px'>" + body + "</div></body>", 600, 800);

    private static LayoutBox Word(LayoutBox root, string word) =>
        root.FindAll(b => b.Text != null && b.Text.IndexOf(word, StringComparison.Ordinal) >= 0).First();

    [Fact]
    public void FloatRightFixedWidth_ContentMovesWithTheFloat()
    {
        var root = Layout("<div id='f' style='float:right;width:200px'>R</div>");

        var f = root.FindById("f")!;
        f.X.Should().BeApproximately(200f, 0.5f);
        Word(root, "R").X.Should().BeGreaterThanOrEqualTo(f.X - 0.5f);
    }

    [Fact]
    public void FloatRightAutoWidth_ShrinksToContentAndSitsAtRightEdge()
    {
        var root = Layout("<div id='f' style='float:right'>INVOICE #123</div><div>ACME Corp</div>");

        var f = root.FindById("f")!;
        f.Width.Should().BeLessThan(150f);
        (f.X + f.Width).Should().BeApproximately(400f, 0.5f);
        Word(root, "INVOICE").X.Should().BeGreaterThanOrEqualTo(f.X - 0.5f);
        var acme = root.FindAll(b => b.Text != null && b.Text.Contains("ACME")).First();
        acme.Text!.Should().Contain("Corp", "the text beside the shrunk float must still fit on one line");
    }

    [Fact]
    public void FloatLeftAutoWidth_ShrinksToContent()
    {
        var root = Layout("<div id='f' style='float:left;padding:0 5px'>Logo text</div>");

        var f = root.FindById("f")!;
        f.X.Should().BeApproximately(0f, 0.5f);
        f.Width.Should().BeLessThan(120f);
        f.Width.Should().BeGreaterThan(40f);
    }

    [Fact]
    public void FloatAutoWidthWithLongText_FillsAvailableWidth()
    {
        var root = Layout("<div id='f' style='float:left'>" + string.Join(" ", Enumerable.Repeat("word", 60)) + "</div>");

        root.FindById("f")!.Width.Should().BeApproximately(400f, 0.5f);
    }

    [Fact]
    public void FloatRightWithMarginRight_LeavesTheMargin()
    {
        var root = Layout("<div id='f' style='float:right;width:100px;margin-right:10px'>R</div>");

        var f = root.FindById("f")!;
        (f.X + f.Width).Should().BeApproximately(390f, 0.5f);
    }

    [Fact]
    public void FloatRightWithNestedBlock_NestedContentMovesToo()
    {
        var root = Layout("<div id='f' style='float:right;width:150px'><div id='inner'><span>Inside</span></div></div>");

        var f = root.FindById("f")!;
        var inner = root.FindById("inner")!;
        inner.X.Should().BeApproximately(f.X, 0.5f);
        Word(root, "Inside").X.Should().BeGreaterThanOrEqualTo(f.X - 0.5f);
    }
}
