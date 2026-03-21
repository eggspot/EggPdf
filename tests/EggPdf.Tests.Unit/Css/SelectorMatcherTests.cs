using EggPdf.Css.Selectors;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class SelectorMatcherTests
{
    private static HtmlDocument Doc(string html) => HtmlParser.Parse(html);

    [Fact]
    public void TypeSelector_MatchesByTagName()
    {
        var doc = Doc("<p>text</p>");
        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");

        SelectorMatcher.Matches("p", p).Should().BeTrue();
        SelectorMatcher.Matches("div", p).Should().BeFalse();
    }

    [Fact]
    public void UniversalSelector_MatchesAll()
    {
        var doc = Doc("<div></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches("*", div).Should().BeTrue();
    }

    [Fact]
    public void ClassSelector_MatchesByClass()
    {
        var doc = Doc("<div class='foo bar'></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches(".foo", div).Should().BeTrue();
        SelectorMatcher.Matches(".bar", div).Should().BeTrue();
        SelectorMatcher.Matches(".baz", div).Should().BeFalse();
    }

    [Fact]
    public void IdSelector_MatchesById()
    {
        var doc = Doc("<div id='main'></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches("#main", div).Should().BeTrue();
        SelectorMatcher.Matches("#other", div).Should().BeFalse();
    }

    [Fact]
    public void CompoundSelector_AllMustMatch()
    {
        var doc = Doc("<div id='x' class='foo'></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches("div.foo", div).Should().BeTrue();
        SelectorMatcher.Matches("div#x", div).Should().BeTrue();
        SelectorMatcher.Matches("div.foo#x", div).Should().BeTrue();
        SelectorMatcher.Matches("span.foo", div).Should().BeFalse();
    }

    [Fact]
    public void DescendantCombinator_MatchesNested()
    {
        var doc = Doc("<div><p><span>text</span></p></div>");
        var span = doc.Body!.FindByTag("span");
        span.Should().NotBeNull();

        // span is a descendant of div
        SelectorMatcher.Matches("div span", span!).Should().BeTrue();
        SelectorMatcher.Matches("p span", span).Should().BeTrue();
        SelectorMatcher.Matches("body span", span).Should().BeTrue();
        SelectorMatcher.Matches("h1 span", span).Should().BeFalse();
    }

    [Fact]
    public void ChildCombinator_MatchesDirectChild()
    {
        var doc = Doc("<div><span>text</span></div>");
        var span = doc.Body!.FindByTag("span");
        span.Should().NotBeNull();

        SelectorMatcher.Matches("div > span", span!).Should().BeTrue();
        SelectorMatcher.Matches("body > span", span).Should().BeFalse(); // span's parent is div, not body
    }

    [Fact]
    public void AttributeSelector_Exists()
    {
        var doc = Doc("<input type='text' disabled>");
        var input = doc.Body!.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches("[disabled]", input).Should().BeTrue();
        SelectorMatcher.Matches("[required]", input).Should().BeFalse();
    }

    [Fact]
    public void AttributeSelector_ValueEquals()
    {
        var doc = Doc("<input type='text'>");
        var input = doc.Body!.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches("[type='text']", input).Should().BeTrue();
        SelectorMatcher.Matches("[type=\"text\"]", input).Should().BeTrue();
        SelectorMatcher.Matches("[type='password']", input).Should().BeFalse();
    }

    [Fact]
    public void PseudoClass_FirstChild()
    {
        var doc = Doc("<div><p>first</p><p>second</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var firstP = div.ChildNodes.OfType<HtmlElement>().First();
        var secondP = div.ChildNodes.OfType<HtmlElement>().Last();

        SelectorMatcher.Matches("p:first-child", firstP).Should().BeTrue();
        SelectorMatcher.Matches("p:first-child", secondP).Should().BeFalse();
    }

    [Fact]
    public void PseudoClass_LastChild()
    {
        var doc = Doc("<div><p>first</p><p>second</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var firstP = div.ChildNodes.OfType<HtmlElement>().First();
        var lastP = div.ChildNodes.OfType<HtmlElement>().Last();

        SelectorMatcher.Matches("p:last-child", lastP).Should().BeTrue();
        SelectorMatcher.Matches("p:last-child", firstP).Should().BeFalse();
    }

    [Fact]
    public void Specificity_Calculated()
    {
        SelectorMatcher.CalculateSpecificity("#id").Should().Be((1, 0, 0));
        SelectorMatcher.CalculateSpecificity(".class").Should().Be((0, 1, 0));
        SelectorMatcher.CalculateSpecificity("div").Should().Be((0, 0, 1));
        SelectorMatcher.CalculateSpecificity("div.foo#bar").Should().Be((1, 1, 1));
        SelectorMatcher.CalculateSpecificity("div p").Should().Be((0, 0, 2));
        SelectorMatcher.CalculateSpecificity("*").Should().Be((0, 0, 0));
    }

    [Fact]
    public void InvalidSelector_ReturnsFalse()
    {
        var doc = Doc("<div></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First();

        // Invalid selectors should return false, not throw
        SelectorMatcher.Matches("", div).Should().BeFalse();
        SelectorMatcher.Matches("   ", div).Should().BeFalse();
    }
}

// Helper extension for tests
internal static class HtmlElementExtensions
{
    public static HtmlElement? FindByTag(this HtmlNode node, string tagName)
    {
        if (node is HtmlElement elem && elem.TagName == tagName) return elem;
        foreach (var child in node.ChildNodes)
        {
            var found = FindByTag(child, tagName);
            if (found != null) return found;
        }
        return null;
    }
}
