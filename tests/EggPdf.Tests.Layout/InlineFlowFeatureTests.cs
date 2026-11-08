using System;
using System.Collections.Generic;
using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class InlineFlowFeatureTests
{
    private static LayoutBox Layout(string css, string body) =>
        LayoutTestHelper.Layout("<html><head><style>body{margin:0} p{margin:0}" + css + "</style></head><body>" + body + "</body></html>", 600, 800);

    private static List<LayoutBox> Words(LayoutBox paragraph) =>
        paragraph.FindAll(b => !string.IsNullOrEmpty(b.Text) && b.Children.Count == 0)
            .OrderBy(b => b.Y).ThenBy(b => b.X).ToList();

    private static List<List<LayoutBox>> Lines(LayoutBox paragraph)
    {
        var lines = new List<List<LayoutBox>>();
        foreach (var word in Words(paragraph))
        {
            if (lines.Count == 0 || Math.Abs(lines[lines.Count - 1][0].Y - word.Y) > 0.5f)
                lines.Add(new List<LayoutBox>());
            lines[lines.Count - 1].Add(word);
        }
        return lines;
    }

    private const string LongMixedText =
        "alpha <b>beta</b> gamma delta <i>epsilon</i> zeta eta theta iota <b>kappa</b> lambda mu nu xi omicron pi rho";

    [Fact]
    public void JustifiedMixedParagraph_WrappedLinesFillTheWidth()
    {
        var root = Layout("p{text-align:justify;width:200px}", "<p id='p'>" + LongMixedText + "</p>");

        var lines = Lines(root.FindById("p")!);
        lines.Count.Should().BeGreaterThan(2);
        foreach (var line in lines.Take(lines.Count - 1))
            line.Max(w => w.X + w.Width).Should().BeApproximately(200, 0.5f);
    }

    [Fact]
    public void JustifiedMixedParagraph_LastLineIsNotStretched()
    {
        var root = Layout("p{text-align:justify;width:200px}", "<p id='p'>" + LongMixedText + "</p>");

        var lastLine = Lines(root.FindById("p")!).Last();
        lastLine.Max(w => w.X + w.Width).Should().BeLessThan(199);
    }

    [Fact]
    public void JustifiedMixedParagraph_LineEndedByBrIsNotStretched()
    {
        var root = Layout("p{text-align:justify;width:400px}", "<p id='p'>short <b>line</b><br>alpha beta gamma delta epsilon zeta eta theta iota kappa lambda</p>");

        var firstLine = Lines(root.FindById("p")!).First();
        firstLine.Max(w => w.X + w.Width).Should().BeLessThan(200);
    }

    [Fact]
    public void IndentedMixedParagraph_FirstLineStartsAtTheIndent()
    {
        var root = Layout("p{text-indent:30px;width:200px}", "<p id='p'>\n   " + LongMixedText + "</p>");

        var lines = Lines(root.FindById("p")!);
        lines[0][0].X.Should().BeApproximately(30, 0.5f);
        lines[0][0].Text!.Should().NotStartWith(" ", "leading whitespace at the start of the block collapses");
        lines[1][0].X.Should().BeApproximately(0, 0.5f);
    }

    [Fact]
    public void IndentedMixedParagraphBesideLeftFloat_IndentStartsAfterTheFloat()
    {
        var root = Layout("p{text-indent:20px}", "<div><img style='float:left;width:100px;height:100px' src='x.png'><p id='p'>Text <b>beside</b> the float</p></div>");

        Words(root.FindById("p")!).First().X.Should().BeApproximately(120, 0.5f);
    }

    [Fact]
    public void JustifiedMixedLineBeforeBlockChild_IsNotStretched()
    {
        var root = Layout("div{text-align:justify;width:400px}", "<div id='d'>Intro <b>x</b><div>Block</div>Tail</div>");

        var intro = Words(root.FindById("d")!).Where(w => w.Text!.Trim() == "x").Single();
        (intro.X + intro.Width).Should().BeLessThan(100, "the line before a block child is the last line of an anonymous block");
    }

    [Fact]
    public void IndentWithBlockBefore_TextIsNotIndented()
    {
        var root = Layout("p{text-indent:30px} p::before{content:'Title';display:block} p::after{content:'.'}", "<p id='p'>Body text</p>");

        Words(root.FindById("p")!).First(w => w.Text!.Contains("Body")).X.Should().BeApproximately(0, 0.5f);
    }

    [Fact]
    public void IndentedBlockWithLeadingBlockChild_LaterTextIsNotIndentedOrGlued()
    {
        var root = Layout("div{text-indent:30px}", "<div id='d'><div style='height:0'></div>one <b>two</b> three</div>");

        var words = Words(root.FindById("d")!);
        words.Should().Contain(w => w.Text!.StartsWith(" two") || w.Text!.StartsWith(" three"), "words after the first keep their separating spaces");
    }

    [Fact]
    public void MixedParagraphBesideFloat_TextAvoidsTheFloat()
    {
        var root = Layout("", "<div><img style='float:left;width:100px;height:100px' src='x.png'><p id='p'>Text <b>beside</b> the float</p></div>");

        Words(root.FindById("p")!).First().X.Should().BeGreaterThanOrEqualTo(99.5f);
    }

    [Fact]
    public void MixedParagraphWithFloatInside_TextAvoidsTheFloat()
    {
        var root = Layout("", "<p id='p'><img style='float:left;width:100px;height:100px' src='x.png'>Text <b>beside</b> the float</p>");

        Words(root.FindById("p")!).Where(w => w.Text!.Contains("Text")).First().X.Should().BeGreaterThanOrEqualTo(99.5f);
    }

    [Fact]
    public void MixedParagraphLongWord_BreakWordSplitsIt()
    {
        var root = Layout("p{width:100px;overflow-wrap:break-word}", "<p id='p'>x <b>y</b> Supercalifragilisticexpialidocious_and_more</p>");

        Words(root.FindById("p")!).Max(w => w.X + w.Width).Should().BeLessThanOrEqualTo(100.5f);
    }
}
