using System.Linq;
using EggPdf.Css.Parser;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CssStyleSheetParserAtRuleTests
{
    [Fact]
    public void Supports_NestedMediaRule_IsKept()
    {
        var sheet = CssStyleSheetParser.Parse("@supports (display: grid) { @media print { p { color: red } } } div { color: blue }");

        sheet.MediaRules.Should().ContainSingle(m => m.MediaQuery == "print");
        sheet.MediaRules[0].Rules.Single().SelectorText.Should().Be("p");
        sheet.Rules.Single().SelectorText.Should().Be("div", "the nested block must not swallow the rules after it");
    }

    [Fact]
    public void Layer_NestedMediaRules_KeepTheLayerOrder()
    {
        var sheet = CssStyleSheetParser.Parse("@layer base { @media print { p { color: red } } }");

        sheet.MediaRules.Single().Rules.Single().LayerOrder.Should().Be(0);
    }

    [Fact]
    public void Media_NestedSupportsAndLayer_RulesBelongToTheMedia()
    {
        var sheet = CssStyleSheetParser.Parse("@media print { @supports (display: grid) { p { color: red } } @layer x { a { color: blue } } }");

        var media = sheet.MediaRules.Single();
        media.Rules.Select(r => r.SelectorText).Should().Equal("p", "a");
        sheet.Rules.Should().BeEmpty();
    }

    [Fact]
    public void Media_NestedMedia_CombinesTheQueries()
    {
        var sheet = CssStyleSheetParser.Parse("@media print { @media (min-width: 10px) { p { color: red } } }");

        sheet.MediaRules.Should().Contain(m => m.MediaQuery == "print and (min-width: 10px)" && m.Rules.Count == 1);
    }

    [Fact]
    public void PageDeclaration_HexColour_KeepsTheHash()
    {
        var sheet = CssStyleSheetParser.Parse("@page { background: #eee; }");

        sheet.PageRules.Single().Declarations.Single().Value.Should().Be("#eee");
    }

    [Theory]
    [InlineData("p { color: red ! important }")]
    [InlineData("p { color: red !IMPORTANT; }")]
    [InlineData("p { color: red!important }")]
    public void Important_WithOrWithoutSpaces_IsDetected(string css)
    {
        var declaration = CssStyleSheetParser.Parse(css).Rules.Single().Declarations.Single();

        declaration.Important.Should().BeTrue();
        declaration.Value.Should().Be("red");
    }

    [Fact]
    public void Important_InsideAString_IsNotAFlag()
    {
        var declaration = CssStyleSheetParser.Parse("p::before { content: \"Hi! important\" }").Rules.Single().Declarations.Single();

        declaration.Important.Should().BeFalse();
    }

    [Fact]
    public void Import_WithLayer_IsKeptWithoutAMediaQuery()
    {
        var sheet = CssStyleSheetParser.Parse("@import url(a.css) layer; @import url(b.css) layer(base) print;");

        sheet.ImportRules.Select(i => (i.Url, i.MediaQuery)).Should().Equal(("a.css", (string?)null), ("b.css", "print"));
    }

    [Fact]
    public void Import_WithSupportsCondition_IsKeptOnlyWhenSupported()
    {
        var sheet = CssStyleSheetParser.Parse("@import url(a.css) supports(-webkit-foo: bar); @import url(b.css) supports(display: grid);");

        sheet.ImportRules.Select(i => i.Url).Should().Equal("b.css");
    }

    [Fact]
    public void LayerStatement_SetsTheOrderByFirstAppearance()
    {
        var sheet = CssStyleSheetParser.Parse("@layer base, components; @layer components { a { color: red } } @layer base { b { color: blue } }");

        sheet.Rules.Single(r => r.SelectorText == "a").LayerOrder.Should().Be(1);
        sheet.Rules.Single(r => r.SelectorText == "b").LayerOrder.Should().Be(0);
    }

    [Fact]
    public void Layer_RepeatedName_SharesItsOrder()
    {
        var sheet = CssStyleSheetParser.Parse("@layer x { a { color: red } } @layer y { b { color: red } } @layer x { c { color: red } }");

        sheet.Rules.Select(r => r.LayerOrder).Should().Equal(0, 1, 0);
    }

    [Fact]
    public void Supports_DoublyParenthesizedAnd_EvaluatesBothSides()
    {
        var sheet = CssStyleSheetParser.Parse("@supports ((-webkit-foo: 1) and (display: grid)) { p { color: red } }");

        sheet.Rules.Should().BeEmpty();
    }

    [Fact]
    public void Rules_RecordTheirSourceOrderAcrossMediaBlocks()
    {
        var sheet = CssStyleSheetParser.Parse("a { color: red } @media print { b { color: red } } c { color: red }");

        var a = sheet.Rules.Single(r => r.SelectorText == "a");
        var b = sheet.MediaRules.Single().Rules.Single();
        var c = sheet.Rules.Single(r => r.SelectorText == "c");
        a.SourceIndex.Should().BeLessThan(b.SourceIndex);
        b.SourceIndex.Should().BeLessThan(c.SourceIndex);
    }
}
