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

    // --- Sibling combinators (NEW) ---

    [Fact]
    public void AdjacentSiblingCombinator_MatchesImmediatelyAfter()
    {
        var doc = Doc("<div><h1>Title</h1><p>Text</p><p>More</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p1 = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var p2 = div.ChildNodes.OfType<HtmlElement>().Last(e => e.TagName == "p");

        // p immediately after h1
        SelectorMatcher.Matches("h1 + p", p1).Should().BeTrue();
        // second p is after first p, not h1
        SelectorMatcher.Matches("h1 + p", p2).Should().BeFalse();
        // second p is after first p
        SelectorMatcher.Matches("p + p", p2).Should().BeTrue();
    }

    [Fact]
    public void GeneralSiblingCombinator_MatchesAnySibling()
    {
        var doc = Doc("<div><h1>Title</h1><p>Text</p><p>More</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p2 = div.ChildNodes.OfType<HtmlElement>().Last(e => e.TagName == "p");

        // second p is a general sibling of h1 (not adjacent, but after)
        SelectorMatcher.Matches("h1 ~ p", p2).Should().BeTrue();
    }

    [Fact]
    public void GeneralSiblingCombinator_DoesNotMatchBefore()
    {
        var doc = Doc("<div><p>Text</p><h1>Title</h1></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");

        // p is BEFORE h1, not after
        SelectorMatcher.Matches("h1 ~ p", p).Should().BeFalse();
    }

    [Fact]
    public void AdjacentSibling_WithClass()
    {
        var doc = Doc("<div><h2 class='sub'>Sub</h2><p>text</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");

        SelectorMatcher.Matches("h2.sub + p", p).Should().BeTrue();
        SelectorMatcher.Matches("h2.other + p", p).Should().BeFalse();
    }

    // --- :not() pseudo-class ---

    [Fact]
    public void Not_ExcludesMatchingElements()
    {
        var doc = Doc("<div><p class='skip'>A</p><p>B</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var pSkip = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var pNormal = div.ChildNodes.OfType<HtmlElement>().Last(e => e.TagName == "p");

        SelectorMatcher.Matches("p:not(.skip)", pSkip).Should().BeFalse();
        SelectorMatcher.Matches("p:not(.skip)", pNormal).Should().BeTrue();
    }

    [Fact]
    public void Not_WithTypeSelector()
    {
        var doc = Doc("<div><span>A</span><p>B</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var span = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "span");
        var p = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");

        SelectorMatcher.Matches(":not(p)", span).Should().BeTrue();
        SelectorMatcher.Matches(":not(p)", p).Should().BeFalse();
    }

    // --- :nth-child() ---

    [Fact]
    public void NthChild_Number()
    {
        var doc = Doc("<ul><li>1</li><li>2</li><li>3</li></ul>");
        var ul = doc.Body!.FindByTag("ul");
        var items = ul!.ChildNodes.OfType<HtmlElement>().ToList();

        SelectorMatcher.Matches("li:nth-child(1)", items[0]).Should().BeTrue();
        SelectorMatcher.Matches("li:nth-child(2)", items[1]).Should().BeTrue();
        SelectorMatcher.Matches("li:nth-child(3)", items[2]).Should().BeTrue();
        SelectorMatcher.Matches("li:nth-child(4)", items[2]).Should().BeFalse();
    }

    [Fact]
    public void NthChild_OddEven()
    {
        var doc = Doc("<ul><li>1</li><li>2</li><li>3</li><li>4</li></ul>");
        var ul = doc.Body!.FindByTag("ul");
        var items = ul!.ChildNodes.OfType<HtmlElement>().ToList();

        SelectorMatcher.Matches("li:nth-child(odd)", items[0]).Should().BeTrue();  // 1st
        SelectorMatcher.Matches("li:nth-child(odd)", items[1]).Should().BeFalse(); // 2nd
        SelectorMatcher.Matches("li:nth-child(even)", items[1]).Should().BeTrue(); // 2nd
        SelectorMatcher.Matches("li:nth-child(even)", items[2]).Should().BeFalse(); // 3rd
    }

    [Fact]
    public void NthChild_Formula_2n()
    {
        var doc = Doc("<ul><li>1</li><li>2</li><li>3</li><li>4</li></ul>");
        var ul = doc.Body!.FindByTag("ul");
        var items = ul!.ChildNodes.OfType<HtmlElement>().ToList();

        // 2n matches 2, 4 (even)
        SelectorMatcher.Matches("li:nth-child(2n)", items[0]).Should().BeFalse();
        SelectorMatcher.Matches("li:nth-child(2n)", items[1]).Should().BeTrue();
        SelectorMatcher.Matches("li:nth-child(2n)", items[3]).Should().BeTrue();
    }

    [Fact]
    public void NthChild_Formula_2nPlus1()
    {
        var doc = Doc("<ul><li>1</li><li>2</li><li>3</li></ul>");
        var ul = doc.Body!.FindByTag("ul");
        var items = ul!.ChildNodes.OfType<HtmlElement>().ToList();

        // 2n+1 matches 1, 3 (odd)
        SelectorMatcher.Matches("li:nth-child(2n+1)", items[0]).Should().BeTrue();
        SelectorMatcher.Matches("li:nth-child(2n+1)", items[1]).Should().BeFalse();
        SelectorMatcher.Matches("li:nth-child(2n+1)", items[2]).Should().BeTrue();
    }

    // --- :nth-last-child() ---

    [Fact]
    public void NthLastChild_MatchesFromEnd()
    {
        var doc = Doc("<ul><li>1</li><li>2</li><li>3</li></ul>");
        var ul = doc.Body!.FindByTag("ul");
        var items = ul!.ChildNodes.OfType<HtmlElement>().ToList();

        SelectorMatcher.Matches("li:nth-last-child(1)", items[2]).Should().BeTrue(); // last
        SelectorMatcher.Matches("li:nth-last-child(2)", items[1]).Should().BeTrue();
        SelectorMatcher.Matches("li:nth-last-child(3)", items[0]).Should().BeTrue(); // first
    }

    // --- :nth-of-type() ---

    [Fact]
    public void NthOfType_MatchesByType()
    {
        var doc = Doc("<div><span>A</span><p>1</p><span>B</span><p>2</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var spans = div.ChildNodes.OfType<HtmlElement>().Where(e => e.TagName == "span").ToList();
        var ps = div.ChildNodes.OfType<HtmlElement>().Where(e => e.TagName == "p").ToList();

        SelectorMatcher.Matches("span:nth-of-type(1)", spans[0]).Should().BeTrue();
        SelectorMatcher.Matches("span:nth-of-type(2)", spans[1]).Should().BeTrue();
        SelectorMatcher.Matches("p:nth-of-type(1)", ps[0]).Should().BeTrue();
        SelectorMatcher.Matches("p:nth-of-type(2)", ps[1]).Should().BeTrue();
    }

    // --- :first-of-type, :last-of-type ---

    [Fact]
    public void FirstOfType_MatchesFirstOfTag()
    {
        var doc = Doc("<div><span>A</span><p>1</p><span>B</span><p>2</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var spans = div.ChildNodes.OfType<HtmlElement>().Where(e => e.TagName == "span").ToList();

        SelectorMatcher.Matches("span:first-of-type", spans[0]).Should().BeTrue();
        SelectorMatcher.Matches("span:first-of-type", spans[1]).Should().BeFalse();
    }

    [Fact]
    public void LastOfType_MatchesLastOfTag()
    {
        var doc = Doc("<div><span>A</span><p>1</p><span>B</span><p>2</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var spans = div.ChildNodes.OfType<HtmlElement>().Where(e => e.TagName == "span").ToList();

        SelectorMatcher.Matches("span:last-of-type", spans[1]).Should().BeTrue();
        SelectorMatcher.Matches("span:last-of-type", spans[0]).Should().BeFalse();
    }

    // --- :only-child, :only-of-type ---

    [Fact]
    public void OnlyChild_MatchesWhenSoleChild()
    {
        var doc = Doc("<div><p>Only</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p = div.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches("p:only-child", p).Should().BeTrue();
    }

    [Fact]
    public void OnlyChild_FailsWithSiblings()
    {
        var doc = Doc("<div><p>First</p><p>Second</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p = div.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches("p:only-child", p).Should().BeFalse();
    }

    [Fact]
    public void OnlyOfType_MatchesWhenSoleType()
    {
        var doc = Doc("<div><span>A</span><p>Only p</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");

        SelectorMatcher.Matches("p:only-of-type", p).Should().BeTrue();
    }

    // --- :is() and :where() ---

    [Fact]
    public void Is_MatchesAny()
    {
        var doc = Doc("<div><h1>Title</h1><h2>Sub</h2><p>Text</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var h1 = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "h1");
        var h2 = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "h2");
        var p = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");

        SelectorMatcher.Matches(":is(h1, h2)", h1).Should().BeTrue();
        SelectorMatcher.Matches(":is(h1, h2)", h2).Should().BeTrue();
        SelectorMatcher.Matches(":is(h1, h2)", p).Should().BeFalse();
    }

    // --- Attribute selector operators ---

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
    public void AttributeSelector_StartsWith()
    {
        var doc = Doc("<a href='https://example.com'>link</a>");
        var a = doc.Body!.FindByTag("a");

        SelectorMatcher.Matches("[href^='https']", a!).Should().BeTrue();
        SelectorMatcher.Matches("[href^='http']", a).Should().BeTrue();
        SelectorMatcher.Matches("[href^='ftp']", a).Should().BeFalse();
    }

    [Fact]
    public void AttributeSelector_EndsWith()
    {
        var doc = Doc("<a href='file.pdf'>link</a>");
        var a = doc.Body!.FindByTag("a");

        SelectorMatcher.Matches("[href$='.pdf']", a!).Should().BeTrue();
        SelectorMatcher.Matches("[href$='.doc']", a).Should().BeFalse();
    }

    [Fact]
    public void AttributeSelector_Contains()
    {
        var doc = Doc("<a href='https://example.com/page'>link</a>");
        var a = doc.Body!.FindByTag("a");

        SelectorMatcher.Matches("[href*='example']", a!).Should().BeTrue();
        SelectorMatcher.Matches("[href*='other']", a).Should().BeFalse();
    }

    [Fact]
    public void AttributeSelector_WordMatch()
    {
        var doc = Doc("<div class='foo bar baz'></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches("[class~='bar']", div).Should().BeTrue();
        SelectorMatcher.Matches("[class~='qux']", div).Should().BeFalse();
    }

    [Fact]
    public void AttributeSelector_HyphenMatch()
    {
        var doc = Doc("<div lang='en-US'></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches("[lang|='en']", div).Should().BeTrue();
        SelectorMatcher.Matches("[lang|='fr']", div).Should().BeFalse();
    }

    // --- Pseudo-class: existing tests ---

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
    public void PseudoClass_Disabled()
    {
        var doc = Doc("<input disabled>");
        var input = doc.Body!.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches("input:disabled", input).Should().BeTrue();
        SelectorMatcher.Matches("input:enabled", input).Should().BeFalse();
    }

    // --- Specificity ---

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
    public void Specificity_SiblingCombinators()
    {
        // Combinators don't add specificity; only their compound selectors do
        SelectorMatcher.CalculateSpecificity("h1 + p").Should().Be((0, 0, 2));
        SelectorMatcher.CalculateSpecificity("h1 ~ p").Should().Be((0, 0, 2));
        SelectorMatcher.CalculateSpecificity("div > p + span").Should().Be((0, 0, 3));
    }

    // --- Comma-separated selector list ---

    [Fact]
    public void SelectorList_MatchesAny()
    {
        var doc = Doc("<div><h1>Title</h1><p>Text</p></div>");
        var h1 = doc.Body!.FindByTag("h1");
        var p = doc.Body!.FindByTag("p");

        SelectorMatcher.Matches("h1, p", h1!).Should().BeTrue();
        SelectorMatcher.Matches("h1, p", p!).Should().BeTrue();
    }

    // --- Edge cases ---

    [Fact]
    public void InvalidSelector_ReturnsFalse()
    {
        var doc = Doc("<div></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First();

        // Invalid selectors should return false, not throw
        SelectorMatcher.Matches("", div).Should().BeFalse();
        SelectorMatcher.Matches("   ", div).Should().BeFalse();
    }

    [Fact]
    public void ComplexSelector_DescendantPlusSibling()
    {
        // div > h1 + p means: p that is adjacent sibling of h1, where h1 is direct child of div
        var doc = Doc("<div><h1>Title</h1><p>Text</p></div>");
        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");

        SelectorMatcher.Matches("div > h1 + p", p).Should().BeTrue();
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
