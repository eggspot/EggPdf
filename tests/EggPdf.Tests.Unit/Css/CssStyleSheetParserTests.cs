using System.Linq;
using EggPdf.Css.Parser;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CssStyleSheetParserTests
{
    [Fact]
    public void EmptyStyleSheet_ReturnsNoRules()
    {
        var sheet = CssStyleSheetParser.Parse("");
        sheet.Rules.Should().BeEmpty();
    }

    [Fact]
    public void SingleRule_ParsedCorrectly()
    {
        var sheet = CssStyleSheetParser.Parse("p { color: red; }");

        sheet.Rules.Should().HaveCount(1);
        sheet.Rules[0].SelectorText.Should().Be("p");
        sheet.Rules[0].Declarations.Should().HaveCount(1);
        sheet.Rules[0].Declarations[0].Property.Should().Be("color");
        sheet.Rules[0].Declarations[0].Value.Should().Be("red");
    }

    [Fact]
    public void MultipleRules_AllParsed()
    {
        var css = @"
            h1 { font-size: 2em; font-weight: bold; }
            p { color: #333; margin: 1em 0; }
            .highlight { background-color: yellow; }
        ";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.Rules.Should().HaveCount(3);
        sheet.Rules[0].SelectorText.Should().Be("h1");
        sheet.Rules[1].SelectorText.Should().Be("p");
        sheet.Rules[2].SelectorText.Should().Be(".highlight");
    }

    [Fact]
    public void ComplexSelector_PreservedAsText()
    {
        var sheet = CssStyleSheetParser.Parse("div.container > p.text { color: blue; }");

        sheet.Rules[0].SelectorText.Should().Be("div.container > p.text");
    }

    [Fact]
    public void MultipleSelectors_PreservedAsList()
    {
        var sheet = CssStyleSheetParser.Parse("h1, h2, h3 { font-weight: bold; }");

        sheet.Rules[0].SelectorText.Should().Be("h1, h2, h3");
    }

    [Fact]
    public void Important_Detected()
    {
        var sheet = CssStyleSheetParser.Parse("p { color: red !important; }");

        sheet.Rules[0].Declarations[0].Important.Should().BeTrue();
        sheet.Rules[0].Declarations[0].Value.Should().Be("red");
    }

    [Fact]
    public void MediaRule_Parsed()
    {
        var css = "@media print { body { font-size: 12pt; } }";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.MediaRules.Should().HaveCount(1);
        sheet.MediaRules[0].MediaQuery.Should().Be("print");
        sheet.MediaRules[0].Rules.Should().HaveCount(1);
        sheet.MediaRules[0].Rules[0].SelectorText.Should().Be("body");
    }

    [Fact]
    public void FontFaceRule_Parsed()
    {
        var css = "@font-face { font-family: 'MyFont'; src: url('font.woff2'); }";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.FontFaceRules.Should().HaveCount(1);
        sheet.FontFaceRules[0].Declarations.Should().Contain(d => d.Property == "font-family");
    }

    [Fact]
    public void PageRule_Parsed()
    {
        var css = "@page { margin: 2cm; size: A4; }";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.PageRules.Should().HaveCount(1);
        sheet.PageRules[0].Declarations.Should().Contain(d => d.Property == "margin");
    }

    [Fact]
    public void CommentsBetweenRules_Stripped()
    {
        var css = "/* comment */ p { color: red; } /* another */";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.Rules.Should().HaveCount(1);
    }

    [Fact]
    public void MalformedRule_Skipped()
    {
        var css = "p { color: red; } @@invalid$$ { } h1 { font-size: 2em; }";
        var sheet = CssStyleSheetParser.Parse(css);

        // Should get at least the valid rules
        sheet.Rules.Should().Contain(r => r.SelectorText == "p");
        sheet.Rules.Should().Contain(r => r.SelectorText == "h1");
    }

    [Fact]
    public void ValueWithMultipleWords_PreservedComplete()
    {
        var css = "p { font-family: Arial, Helvetica, sans-serif; border: 1px solid #ddd; }";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.Rules[0].Declarations.Should().Contain(d =>
            d.Property == "font-family" && d.Value == "Arial, Helvetica, sans-serif");
        sheet.Rules[0].Declarations.Should().Contain(d =>
            d.Property == "border" && d.Value == "1px solid #ddd");
    }

    [Fact]
    public void NeverThrows_OnAnyInput()
    {
        var inputs = new[] { "", " ", "{}", "{{", "}}", "@", "p{", "p { color: }", ";;;", "/* unterminated" };
        foreach (var input in inputs)
        {
            var act = () => CssStyleSheetParser.Parse(input);
            act.Should().NotThrow($"input '{input}' should not throw");
        }
    }

    [Fact]
    public void PageRule_InsideMediaPrint_IsAddedToPageRules()
    {
        var sheet = CssStyleSheetParser.Parse("@media print { @page { margin: 5mm; } }");

        sheet.PageRules.Should().HaveCount(1, "nested @page inside @media print should be parsed");
        sheet.PageRules[0].Declarations.Should().Contain(d => d.Property == "margin" && d.Value == "5mm");
    }

    [Fact]
    public void PageRule_InsideMediaPrint_WithStyleRules_BothParsed()
    {
        var css = "@media print { body { margin: 0; } @page { margin: 5mm; } }";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.MediaRules.Should().HaveCount(1);
        sheet.MediaRules[0].Rules.Should().HaveCount(1, "body rule should be in media rules");
        sheet.PageRules.Should().HaveCount(1, "@page should be extracted to top-level PageRules");
    }
}
