using System;
using System.Collections.Generic;
using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class InlineFlowTextFeatureTests
{
    private static readonly string Ellipsis = ((char)0x2026).ToString();

    private static LayoutBox Layout(string css, string body) =>
        LayoutTestHelper.Layout("<html><head><style>body{margin:0} p{margin:0;font-family:Helvetica;font-size:16px}" + css +
            "</style></head><body>" + body + "</body></html>", 600, 800);

    private static List<LayoutBox> Words(LayoutBox paragraph) =>
        paragraph.FindAll(b => !string.IsNullOrEmpty(b.Text) && b.Children.Count == 0)
            .OrderBy(b => b.Y).ThenBy(b => b.X).ToList();

    private static List<List<LayoutBox>> Lines(LayoutBox paragraph)
    {
        var lines = new List<List<LayoutBox>>();
        foreach (var word in paragraph.FindAll(b => !string.IsNullOrEmpty(b.Text) && b.Children.Count == 0)
                     .OrderBy(b => b.InlineLineTop).ThenBy(b => b.X))
        {
            if (lines.Count == 0 || Math.Abs(lines[lines.Count - 1][0].InlineLineTop - word.InlineLineTop) > 0.5f)
                lines.Add(new List<LayoutBox>());
            lines[lines.Count - 1].Add(word);
        }
        return lines;
    }

    private static LayoutBox Fragment(LayoutBox paragraph, string word) =>
        Words(paragraph).Single(b => b.Text!.Trim() == word);

    [Fact]
    public void InlineFlow_RtlParagraph_PlacesFirstWordRightmost()
    {
        var root = Layout("p{direction:rtl;width:400px}", "<p id='p'>مرحبا بكم في <b>الموقع</b> الجديد</p>");
        var p = root.FindById("p")!;

        var words = Words(p);
        Fragment(p, "مرحبا").X.Should().BeApproximately(words.Max(w => w.X), 0.01f);
        Fragment(p, "الجديد").X.Should().BeApproximately(words.Min(w => w.X), 0.01f);
        Fragment(p, "بكم").X.Should().BeLessThan(Fragment(p, "مرحبا").X);
        Fragment(p, "الموقع").X.Should().BeLessThan(Fragment(p, "في").X);
    }

    [Fact]
    public void InlineFlow_RtlParagraph_KeepsLtrWordsInSourceOrder()
    {
        var root = Layout("p{direction:rtl;width:400px}", "<p id='p'>مرحبا <b>Hello world</b> بكم</p>");
        var p = root.FindById("p")!;

        Fragment(p, "Hello").X.Should().BeLessThan(Fragment(p, "world").X);
        Fragment(p, "world").X.Should().BeLessThan(Fragment(p, "مرحبا").X);
        Fragment(p, "بكم").X.Should().BeLessThan(Fragment(p, "Hello").X);
    }

    [Fact]
    public void InlineFlow_LtrParagraphWithArabicWords_ReversesOnlyTheArabicRun()
    {
        var root = Layout("p{width:400px}", "<p id='p'>Hello مرحبا بكم <b>world</b></p>");
        var p = root.FindById("p")!;

        Fragment(p, "Hello").X.Should().BeLessThan(Fragment(p, "بكم").X);
        Fragment(p, "بكم").X.Should().BeLessThan(Fragment(p, "مرحبا").X);
        Fragment(p, "مرحبا").X.Should().BeLessThan(Fragment(p, "world").X);
    }

    [Fact]
    public void InlineFlow_LtrParagraphWithoutRtlText_KeepsSourceOrder()
    {
        var root = Layout("p{width:400px}", "<p id='p'>one <b>two</b> three</p>");
        var p = root.FindById("p")!;

        Fragment(p, "one").X.Should().BeLessThan(Fragment(p, "two").X);
        Fragment(p, "two").X.Should().BeLessThan(Fragment(p, "three").X);
    }

    [Fact]
    public void InlineFlow_FirstLetter_SplitsFirstLetterWithItsStyle()
    {
        var root = Layout("p{width:400px} p::first-letter{font-size:40px}", "<p id='p'>Hello <b>bold</b> world</p>");
        var words = Words(root.FindById("p")!);

        var letter = words.First(w => w.Text == "H");
        var rest = words.First(w => w.Text == "ello");
        letter.Height.Should().BeGreaterThan(rest.Height);
        rest.X.Should().BeApproximately(letter.X + letter.Width, 0.5f);
    }

    [Fact]
    public void InlineFlow_FirstLetterInsideBold_KeepsBoldWeight()
    {
        var root = Layout("p{width:400px} p::first-letter{color:red}", "<p id='p'><b>Bold</b> start</p>");
        var letter = Words(root.FindById("p")!).First();

        letter.Text.Should().Be("B");
        letter.Style.Color.Should().Be("red");
        letter.Style.FontWeight.Should().BeOneOf("bold", "700");
    }

    [Fact]
    public void InlineFlow_FirstLetterWithLeadingQuote_KeepsThePunctuationWithTheLetter()
    {
        var root = Layout("p{width:400px} p::first-letter{font-size:40px}", "<p id='p'>\"Quoted\" <b>text</b></p>");

        Words(root.FindById("p")!).First().Text.Should().Be("\"Q");
    }

    [Fact]
    public void InlineFlow_LineClamp_KeepsTwoLinesAndEndsWithEllipsis()
    {
        var root = Layout("p{width:120px;-webkit-line-clamp:2;overflow:hidden}",
            "<p id='p'>one two three four five six seven <b>eight</b> nine ten eleven twelve thirteen</p>");
        var p = root.FindById("p")!;

        var lines = Lines(p);
        lines.Should().HaveCount(2);
        lines[1].Last().Text.Should().EndWith(Ellipsis);
        lines[1].Max(w => w.X + w.Width).Should().BeLessThanOrEqualTo(120.5f);
        p.Height.Should().BeLessThan(lines[0][0].Height * 2.5f);
    }

    [Fact]
    public void InlineFlow_LineClampNotReached_LeavesTextAlone()
    {
        var root = Layout("p{width:400px;line-clamp:3}", "<p id='p'>short <b>text</b></p>");

        Words(root.FindById("p")!).Should().NotContain(w => w.Text!.EndsWith(Ellipsis));
    }

    [Fact]
    public void InlineFlow_HyphensAuto_BreaksLongWordWithHyphen()
    {
        var root = Layout("p{width:90px;hyphens:auto}", "<p id='p'>Extraordinarily international <b>communication</b> responsibilities</p>");
        var words = Words(root.FindById("p")!);

        words.Should().Contain(w => w.Text!.EndsWith("-"));
        words.Max(w => w.X + w.Width).Should().BeLessThanOrEqualTo(90.5f);
    }

    [Fact]
    public void InlineFlow_HyphensAutoWordThatFits_StaysWhole()
    {
        var root = Layout("p{width:400px;hyphens:auto}", "<p id='p'>Extraordinarily <b>bold</b></p>");

        Words(root.FindById("p")!).Should().Contain(w => w.Text == "Extraordinarily");
    }

    [Fact]
    public void InlineFlow_HyphensAutoWordThatFitsTheNextLine_MovesWhole()
    {
        var root = Layout("p{width:90px;hyphens:auto}", "<p id='p'>Extraordinarily international <b>bold</b></p>");

        Words(root.FindById("p")!).Should().Contain(w => w.Text!.Trim() == "international");
    }

    [Fact]
    public void InlineFlow_LargerInlineText_StaysInsideItsLine()
    {
        var root = Layout("p{width:400px}", "<div style='height:30px'></div><p id='p'>Hello <span style='font-size:40px'>Big</span> world</p>");
        var p = root.FindById("p")!;

        var big = Fragment(p, "Big");
        var hello = Fragment(p, "Hello");
        big.Y.Should().BeGreaterThanOrEqualTo(p.Y - 0.01f);
        hello.Y.Should().BeGreaterThan(big.Y, "the smaller text is lowered so its baseline lines up with the larger text");
    }

    [Fact]
    public void InlineFlow_HyphensManual_DoesNotHyphenate()
    {
        var root = Layout("p{width:90px}", "<p id='p'>Extraordinarily international <b>communication</b></p>");

        Words(root.FindById("p")!).Should().NotContain(w => w.Text!.EndsWith("-"));
    }

    [Fact]
    public void InlineFlow_TextWrapBalance_MakesLinesSimilarWidth()
    {
        var root = Layout("p{width:300px;text-wrap:balance}", "<p id='p'>A heading that wraps onto two lines with <b>bold</b> word</p>");

        var lines = Lines(root.FindById("p")!);
        lines.Should().HaveCount(2);
        float first = lines[0].Max(w => w.X + w.Width) - lines[0].Min(w => w.X);
        float second = lines[1].Max(w => w.X + w.Width) - lines[1].Min(w => w.X);
        Math.Abs(first - second).Should().BeLessThan(Math.Max(first, second) * 0.35f);
    }

    [Fact]
    public void InlineFlow_TextWrapBalanceCentred_CentresAgainstTheContainer()
    {
        var root = Layout("p{width:300px;text-wrap:balance;text-align:center}", "<p id='p'>A heading that wraps onto two lines with <b>bold</b> word</p>");

        foreach (var line in Lines(root.FindById("p")!))
        {
            float centre = (line.Min(w => w.X) + line.Max(w => w.X + w.Width)) / 2f;
            centre.Should().BeApproximately(150f, 1f);
        }
    }

    [Fact]
    public void TextIndent_NegativeOnTextOnlyBlock_ShiftsFirstLineLeft()
    {
        var root = Layout("p{width:200px;padding-left:40px;text-indent:-20px}",
            "<p id='p'>alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu</p>");

        var p = root.FindById("p")!;
        var lines = p.FindAll(b => !string.IsNullOrEmpty(b.Text) && b.Children.Count == 0).OrderBy(b => b.Y).ToList();
        lines[0].X.Should().BeApproximately(20f, 0.5f);
        lines[1].X.Should().BeApproximately(40f, 0.5f);
    }

    [Fact]
    public void InlineFlow_NegativeTextIndent_ShiftsFirstLineLeft()
    {
        var root = Layout("p{width:200px;padding-left:40px;text-indent:-20px}",
            "<p id='p'>alpha <b>beta</b> gamma delta epsilon zeta eta theta iota kappa lambda mu</p>");

        var lines = Lines(root.FindById("p")!);
        lines[0][0].X.Should().BeApproximately(20f, 0.5f);
        lines[1][0].X.Should().BeApproximately(40f, 0.5f);
    }

    [Fact]
    public void InlineFlow_BreakWordPlainRun_BreaksLongUrl()
    {
        var root = Layout("p{width:100px;overflow-wrap:break-word}",
            "<p id='p'>See https://example.com/a/very/long/path/that/never/ends <b>here</b></p>");

        Words(root.FindById("p")!).Max(w => w.X + w.Width).Should().BeLessThanOrEqualTo(100.5f);
    }

    [Fact]
    public void TextOnlyJustify_OnlyTheLastLineIsNotJustifiable()
    {
        var root = Layout("p{width:200px;text-align:justify}",
            "<p id='p'>word word word word word word word word word word word</p>");

        var lines = root.FindById("p")!.FindAll(b => !string.IsNullOrEmpty(b.Text) && b.Children.Count == 0).OrderBy(b => b.Y).ToList();
        lines.Should().HaveCountGreaterThan(1);
        lines.Last().Justification.Should().Be(LineJustification.NotJustifiable);
        lines.Take(lines.Count - 1).Should().OnlyContain(l => l.Justification == LineJustification.Justifiable);
    }
}
