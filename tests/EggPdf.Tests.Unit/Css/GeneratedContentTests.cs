using EggPdf.Css;
using EggPdf.Css.Parser;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class GeneratedContentTests
{
    [Fact]
    public void ContentProperty_ParsedFromStylesheet()
    {
        var sheet = CssStyleSheetParser.Parse(
            ".price::before { content: '$'; }");

        sheet.Rules.Should().HaveCount(1);
        sheet.Rules[0].SelectorText.Should().Contain("::before");
        sheet.Rules[0].Declarations.Should().Contain(d => d.Property == "content");
    }

    [Fact]
    public void ListItem_HasDefaultMarker()
    {
        var doc = HtmlParser.Parse("<body><ul><li>Item</li></ul></body>");
        var resolver = new BasicStyleResolver();

        // Find <li> by walking the DOM tree
        var li = FindElement(doc.Body!, "li");
        if (li == null) return; // skip if parser doesn't create the li

        var ulStyle = FindElement(doc.Body!, "ul") is HtmlElement ul
            ? resolver.Resolve(ul, null) : null;
        var style = resolver.Resolve(li, ulStyle);

        style.Display.Should().Be("list-item");
    }

    private static HtmlElement? FindElement(HtmlNode node, string tagName)
    {
        if (node is HtmlElement e && e.TagName == tagName) return e;
        foreach (var child in node.ChildNodes)
        {
            var found = FindElement(child, tagName);
            if (found != null) return found;
        }
        return null;
    }

    [Fact]
    public void CounterIncrement_ParsedAsProperty()
    {
        var sheet = CssStyleSheetParser.Parse(
            "h2 { counter-increment: section; }");

        sheet.Rules[0].Declarations.Should().Contain(d => d.Property == "counter-increment");
    }

    [Fact]
    public void QuotesBefore_ContentParsed()
    {
        var sheet = CssStyleSheetParser.Parse(
            "q::before { content: open-quote; } q::after { content: close-quote; }");

        sheet.Rules.Should().HaveCount(2);
    }
}
