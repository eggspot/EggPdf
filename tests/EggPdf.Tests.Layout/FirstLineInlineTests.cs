using System.Collections.Generic;
using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class FirstLineInlineTests
{
    private const string Paragraph =
        "<p style='width:300px'>The first line of this paragraph has a <b>bold</b> word and should be red, " +
        "while the following lines of the paragraph keep the colour that the paragraph itself declares.</p>";

    private static List<LayoutBox> TextBoxes(string css, string body)
    {
        var root = LayoutTestHelper.Layout("<html><head><style>body{margin:0}" + css + "</style></head><body>" + body + "</body></html>", 600, 800);
        var boxes = new List<LayoutBox>();
        void Collect(LayoutBox box)
        {
            if (!string.IsNullOrWhiteSpace(box.Text)) boxes.Add(box);
            foreach (var child in box.Children) Collect(child);
        }
        Collect(root.FindByTag("p")!);
        return boxes;
    }

    [Fact]
    public void FirstLineBesideAnInlineElement_IsStyled_AndLaterLinesAreNot()
    {
        var boxes = TextBoxes("p::first-line { color: red }", Paragraph);

        float firstLineTop = boxes.Min(b => b.InlineLineTop);
        var firstLine = boxes.Where(b => b.InlineLineTop == firstLineTop).ToList();
        var laterLines = boxes.Where(b => b.InlineLineTop > firstLineTop).ToList();

        firstLine.Should().NotBeEmpty();
        laterLines.Should().NotBeEmpty();
        firstLine.Should().OnlyContain(b => b.Style!.Get("color") == "red");
        laterLines.Should().OnlyContain(b => b.Style!.Get("color") != "red");
    }

    [Fact]
    public void InlineElementOnTheFirstLine_KeepsWhatItDeclares_AndInheritsTheRest()
    {
        var boxes = TextBoxes("p::first-line { color: red; font-style: italic } b { font-style: normal }", Paragraph);

        var bold = boxes.Single(b => b.Text!.Trim() == "bold");
        bold.Style!.Get("color").Should().Be("red", "color is inherited from the first line");
        bold.Style.Get("font-style").Should().Be("normal", "<b> declares its own font-style");
        bold.Style.FontWeight.Should().Be("bold");
    }

    [Fact]
    public void LargerFirstLineFont_WrapsTheFirstLineSooner()
    {
        int WordsOnFirstLine(string css)
        {
            var boxes = TextBoxes(css, Paragraph);
            float top = boxes.Min(b => b.InlineLineTop);
            return boxes.Count(b => b.InlineLineTop == top);
        }

        WordsOnFirstLine("p::first-line { font-size: 28px }").Should().BeLessThan(WordsOnFirstLine(""));
    }

    [Fact]
    public void FirstLineTextTransform_AppliesOnlyToTheFirstLine()
    {
        var boxes = TextBoxes("p::first-line { text-transform: uppercase }", Paragraph);

        float top = boxes.Min(b => b.InlineLineTop);
        boxes.Where(b => b.InlineLineTop == top).Should().OnlyContain(b => b.Text == b.Text!.ToUpperInvariant());
        boxes.Where(b => b.InlineLineTop > top).Should().Contain(b => b.Text != b.Text!.ToUpperInvariant());
    }
}
