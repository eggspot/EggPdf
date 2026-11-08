using System;
using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class InlineNoWrapTests
{
    private static LayoutBox Layout(string body) =>
        LayoutTestHelper.Layout("<body style='margin:0'>" + body + "</body>", 600, 800);

    private static int LineCount(LayoutBox box) =>
        box.FindAll(b => !string.IsNullOrEmpty(b.Text) && b.Children.Count == 0)
            .Select(b => (int)Math.Round(b.Y))
            .Distinct()
            .Count();

    [Fact]
    public void NoWrapBlockWithMixedInlineContent_StaysOnOneLine()
    {
        var root = Layout("<div id='d' style='width:150px;white-space:nowrap'>Total: <b>1.234.567 VND</b> including tax</div>");

        LineCount(root.FindById("d")!).Should().Be(1);
    }

    [Fact]
    public void NoWrapSpanInsideWrappingText_KeepsItsWordsTogether()
    {
        var root = Layout("<div id='d' style='width:140px'>Ship to <span id='s' style='white-space:nowrap'>Nguyen Van A, 123 Le Loi</span></div>");

        var spanWords = root.FindById("d")!.FindAll(b => b.InlineSpan?.Element.Id == "s" || (b.Text != null && b.Text.Contains("Loi")));
        spanWords.Select(b => (int)Math.Round(b.Y)).Distinct().Should().ContainSingle("the nowrap span must not break internally");
    }

    [Fact]
    public void NoWrapTableCellWithMixedContent_StaysOnOneLine()
    {
        var root = Layout("<table style='width:120px'><tr><td id='c' style='white-space:nowrap'>Ngày <b>01/01/2024</b> ký</td></tr></table>");

        LineCount(root.FindById("c")!).Should().Be(1);
    }

    [Fact]
    public void TextWrapModeNoWrap_StaysOnOneLine()
    {
        var root = Layout("<div id='d' style='width:120px;text-wrap-mode:nowrap'>One two <b>three four</b> five six</div>");

        LineCount(root.FindById("d")!).Should().Be(1);
    }

    [Theory]
    [InlineData("text-wrap-mode", "nowrap")]
    [InlineData("text-wrap", "balance")]
    public void TextWrapProperties_AreInherited(string property, string value)
    {
        var root = Layout("<div style='" + property + ":" + value + "'><p id='p'>Child text</p></div>");

        root.FindById("p")!.Style.Get(property).Should().Be(value);
    }

    [Fact]
    public void NormalWhiteSpace_StillWraps()
    {
        var root = Layout("<div id='d' style='width:150px'>Total: <b>1.234.567 VND</b> including tax</div>");

        LineCount(root.FindById("d")!).Should().BeGreaterThan(1);
    }
}
