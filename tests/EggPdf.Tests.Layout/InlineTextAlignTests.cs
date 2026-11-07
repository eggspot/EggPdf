using System;
using System.Collections.Generic;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class InlineTextAlignTests
{
    private const float ContainerWidth = 400f;

    private static LayoutBox Layout(string html) =>
        LayoutTestHelper.Layout("<body style='margin:0'>" + html + "</body>", 600, 800);

    private static List<LayoutBox> WordBoxes(LayoutBox root) =>
        root.FindAll(b => !string.IsNullOrEmpty(b.Text) && b.Children.Count == 0);

    private static (float Left, float Right) Extent(List<LayoutBox> boxes)
    {
        float left = float.MaxValue, right = float.MinValue;
        foreach (var b in boxes)
        {
            left = Math.Min(left, b.X);
            right = Math.Max(right, b.X + b.Width);
        }
        return (left, right);
    }

    private static List<List<LayoutBox>> Lines(List<LayoutBox> words)
    {
        var lines = new List<List<LayoutBox>>();
        foreach (var w in words)
        {
            var line = lines.Find(l => Math.Abs(l[0].Y - w.Y) < 3f);
            if (line == null) lines.Add(line = new List<LayoutBox>());
            line.Add(w);
        }
        return lines;
    }

    [Fact]
    public void TextAlignCenter_TextWithStrong_CentersLine()
    {
        var root = Layout("<div style='width:400px'><div style='text-align:center'>No: <strong>ABC-2026-123</strong></div></div>");

        var (left, right) = Extent(WordBoxes(root));

        left.Should().BeGreaterThan(50f);
        left.Should().BeApproximately(ContainerWidth - right, 1f);
    }

    [Fact]
    public void TextAlignRight_TextWithSpan_EndsAtRightEdge()
    {
        var root = Layout("<div style='width:400px;text-align:right'>Total: <span>42 EUR</span></div>");

        var (left, right) = Extent(WordBoxes(root));

        right.Should().BeApproximately(ContainerWidth, 1f);
        left.Should().BeGreaterThan(200f);
    }

    [Fact]
    public void TextAlignCenter_InheritedFromAncestor_CentersMixedLine()
    {
        var root = Layout("<div style='width:400px;text-align:center'><p style='margin:0'>Invoice <em>No. 7</em> issued</p></div>");

        var (left, right) = Extent(WordBoxes(root));

        left.Should().BeGreaterThan(50f);
        left.Should().BeApproximately(ContainerWidth - right, 1f);
    }

    [Fact]
    public void TextAlignCenter_WrappedMixedText_CentersEachLine()
    {
        var root = Layout("<div style='width:200px;text-align:center'>one two three four five six " +
                          "<b>seven eight nine ten</b> eleven twelve thirteen</div>");

        var lines = Lines(WordBoxes(root));

        lines.Count.Should().BeGreaterThan(1);
        foreach (var line in lines)
        {
            var (left, right) = Extent(line);
            left.Should().BeApproximately(200f - right, 1f);
        }
    }

    [Fact]
    public void TextAlignLeft_MixedLine_StartsAtLeftEdge()
    {
        var root = Layout("<div style='width:400px'>No: <strong>ABC-2026-123</strong></div>");

        var (left, _) = Extent(WordBoxes(root));

        left.Should().BeApproximately(0f, 0.01f);
    }

    [Fact]
    public void TextAlignCenter_AutoWidthInlineBlockWithMixedContent_ShrinksToContent()
    {
        var root = Layout("<div style='width:400px;text-align:center'>" +
                          "<span style='display:inline-block'>Paid <b>in full</b></span></div>");

        var badge = root.FindByTag("span")!;
        var (left, right) = Extent(WordBoxes(badge));

        badge.Width.Should().BeApproximately(right - left, 1f);
        badge.X.Should().BeApproximately((ContainerWidth - badge.Width) / 2, 1f);
        left.Should().BeApproximately(badge.X, 1f);
    }

    [Fact]
    public void TextAlignCenter_InlineImage_Centered()
    {
        var root = Layout("<div style='width:400px;text-align:center'><img src='logo.png' width='100' height='50'></div>");

        var img = root.FindByTag("img")!;

        img.X.Should().BeApproximately(150f, 1f);
    }

    [Fact]
    public void TextAlignCenter_LinkInMixedLine_SpanMovesWithWords()
    {
        var root = Layout("<div style='width:400px;text-align:center'>Go to <a href='https://example.com'>the site</a></div>");

        var linkWords = root.FindAll(b => b.InlineSpan != null && b.Children.Count == 0);
        linkWords.Should().NotBeEmpty();
        var (left, right) = Extent(linkWords);
        var span = linkWords[0].InlineSpan!;

        span.X.Should().BeApproximately(left, 0.5f);
        (span.X + span.Width).Should().BeApproximately(right, 0.5f);
        left.Should().BeGreaterThan(100f);
    }

    [Fact]
    public void TextAlignCenter_InlineBlockFollowedByText_CentersWholeLine()
    {
        var root = Layout("<div style='width:400px;text-align:center'>" +
                          "<span style='display:inline-block;width:50px;height:10px'></span> Text <b>here</b></div>");

        var block = root.FindByTag("span")!;
        var (_, right) = Extent(WordBoxes(root));

        block.X.Should().BeGreaterThan(50f);
        block.X.Should().BeApproximately(ContainerWidth - right, 1f);
    }

    [Fact]
    public void TextAlignCenter_NestedAutoWidthInlineBlocks_DoNotInflateShrinkWidth()
    {
        var root = Layout("<div style='width:400px;text-align:center'>" +
                          "<span id='outer' style='display:inline-block'>" +
                          "<span id='inner' style='display:inline-block'>Inner <b>x</b></span></span></div>");

        var outer = root.FindById("outer")!;
        var inner = root.FindById("inner")!;

        inner.Width.Should().BeLessThan(150f);
        outer.Width.Should().BeApproximately(inner.Width, 1f);
        outer.X.Should().BeApproximately((ContainerWidth - outer.Width) / 2, 1f);
    }

    [Fact]
    public void TextAlignCenter_MultiColumnBlock_CentersWithinColumn()
    {
        var root = Layout("<div style='width:400px;column-count:2;column-gap:0;text-align:center'>" +
                          "<p style='margin:0'>No: <b>X1</b></p></div>");

        var (left, right) = Extent(WordBoxes(root));

        right.Should().BeLessThan(200.5f);
        left.Should().BeApproximately(200f - right, 1f);
    }

    [Fact]
    public void TextAlignCenter_FloatClearedAbove_CentersInFullWidth()
    {
        var root = Layout("<div style='width:400px'>" +
                          "<div style='float:left;width:50px;height:20px'></div><div style='clear:both'></div>" +
                          "<div style='text-align:center'>No: <strong>ABC-2026-123</strong></div></div>");

        var (left, right) = Extent(WordBoxes(root));

        left.Should().BeApproximately(ContainerWidth - right, 1f);
    }

    [Fact]
    public void TextAlignCenter_LineBesideFloat_CentersInFreeBand()
    {
        var root = Layout("<div style='width:400px'>" +
                          "<div style='float:left;width:100px;height:100px'></div>" +
                          "<div style='text-align:center'>No: <strong>ABC-2026-123</strong></div></div>");

        var (left, right) = Extent(WordBoxes(root));

        left.Should().BeGreaterThan(100f);
        (left - 100f).Should().BeApproximately(ContainerWidth - right, 1f);
    }

    [Fact]
    public void TextAlignCenter_PlainTextBesideFloat_CentersInFreeBand()
    {
        var root = Layout("<div style='width:400px'>" +
                          "<div style='float:left;width:100px;height:100px'></div>" +
                          "<div style='text-align:center'>Only plain text</div></div>");

        var (left, right) = Extent(WordBoxes(root));

        left.Should().BeGreaterThan(100f);
        (left - 100f).Should().BeApproximately(ContainerWidth - right, 1f);
    }

    [Fact]
    public void TextAlignCenter_RubyLine_CentersBaseAndAnnotation()
    {
        var root = Layout("<div style='width:400px;text-align:center'>Read <ruby>base<rt>note</rt></ruby> here</div>");

        var (left, right) = Extent(WordBoxes(root));

        left.Should().BeGreaterThan(50f);
        left.Should().BeApproximately(ContainerWidth - right, 1f);
    }

    [Fact]
    public void TextAlignCenter_LinesSplitByBr_CentersEachLine()
    {
        var root = Layout("<div style='width:400px;text-align:center'>First <b>line</b><br>Second <i>one, a longer line</i></div>");

        var lines = Lines(WordBoxes(root));

        lines.Count.Should().Be(2);
        foreach (var line in lines)
        {
            var (left, right) = Extent(line);
            left.Should().BeApproximately(ContainerWidth - right, 1f);
        }
    }

    [Fact]
    public void TextAlignCenter_TableCell_CentersMixedLineInCell()
    {
        var root = Layout("<table style='width:400px;border-collapse:collapse'><tr>" +
                          "<td style='padding:0;text-align:center'>No: <strong>X1</strong></td></tr></table>");

        var td = root.FindByTag("td")!;
        float cellLeft = td.X + td.PaddingLeft;
        float cellRight = cellLeft + td.ContentWidth;
        var (left, right) = Extent(WordBoxes(td));

        (left - cellLeft).Should().BeGreaterThan(20f);
        (left - cellLeft).Should().BeApproximately(cellRight - right, 1.5f);
    }

    [Fact]
    public void TextAlignCenter_FlexItem_CentersMixedLineInItem()
    {
        var root = Layout("<div style='display:flex;width:400px'>" +
                          "<div id='item' style='width:200px;text-align:center'>No: <strong>X1</strong></div></div>");

        var item = root.FindById("item")!;
        var (left, right) = Extent(WordBoxes(item));

        (left - item.X).Should().BeGreaterThan(20f);
        (left - item.X).Should().BeApproximately(item.X + item.Width - right, 1f);
    }

    [Fact]
    public void TextAlignJustify_MixedLine_StartsAtLeftEdge()
    {
        var root = Layout("<div style='width:400px;text-align:justify'>No: <strong>ABC-2026-123</strong></div>");

        var (left, _) = Extent(WordBoxes(root));

        left.Should().BeApproximately(0f, 0.01f);
    }

    [Fact]
    public void DirectionRtl_DefaultStartAlign_MixedLineEndsAtRightEdge()
    {
        var root = Layout("<div style='width:400px;direction:rtl'>No: <strong>ABC-2026-123</strong></div>");

        var (_, right) = Extent(WordBoxes(root));

        right.Should().BeApproximately(ContainerWidth, 1f);
    }

    [Fact]
    public void TextAlignCenter_OverflowingWord_StaysStartAligned()
    {
        var root = Layout("<div style='width:100px;text-align:center'>A " +
                          "<b>Supercalifragilisticexpialidocious</b></div>");

        var longWord = root.FindAll(b => b.Text != null && b.Text.IndexOf("Supercali", StringComparison.Ordinal) >= 0)[0];

        longWord.Width.Should().BeGreaterThan(100f);
        longWord.X.Should().BeApproximately(0f, 0.01f);
    }
}
