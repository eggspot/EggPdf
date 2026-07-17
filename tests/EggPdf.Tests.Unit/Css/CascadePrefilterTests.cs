using System.Linq;
using EggPdf.Css;
using EggPdf.Css.Cascade;
using EggPdf.Css.Parser;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

/// <summary>
/// The cascade uses a per-rule fast-reject prefilter (rightmost compound's
/// tag/id/class) before running the full selector matcher. These tests pin
/// the tricky selector shapes where a wrong prefilter would silently drop
/// matching rules.
/// </summary>
public class CascadePrefilterTests
{
    private static HtmlElement Find(HtmlElement root, string tagName, int index = 0)
    {
        int seen = 0;
        HtmlElement? found = null;
        void Walk(HtmlElement el)
        {
            if (found != null) return;
            if (el.TagName == tagName && seen++ == index) { found = el; return; }
            foreach (var child in el.ChildNodes.OfType<HtmlElement>()) Walk(child);
        }
        Walk(root);
        found.Should().NotBeNull($"<{tagName}>[{index}] must exist in the test document");
        return found!;
    }

    private static ComputedStyle Resolve(string html, string css, string tag, int index = 0)
    {
        var doc = HtmlParser.Parse(html);
        var sheet = CssStyleSheetParser.Parse(css);
        var resolver = new CascadeResolver(new[] { sheet });
        return resolver.Resolve(Find(doc.Body!, tag, index), null);
    }

    [Fact]
    public void AttributeOnlySelector_StillMatches()
    {
        Resolve("<div data-x='1'>t</div>", "[data-x] { color: red; }", "div")
            .Color.Should().Be("red");
    }

    [Fact]
    public void PseudoClassOnlySelector_StillMatches()
    {
        Resolve("<div><p>first</p></div>", ":first-child { color: red; }", "p")
            .Color.Should().Be("red");
    }

    [Fact]
    public void UniversalSelector_StillMatches()
    {
        Resolve("<span>t</span>", "* { color: red; }", "span")
            .Color.Should().Be("red");
    }

    [Fact]
    public void TagWithPseudoClass_MatchesTagAndRejectsOthers()
    {
        var html = "<div><p>a</p><span>b</span></div>";
        var css = "p:first-child { color: red; }";
        Resolve(html, css, "p").Color.Should().Be("red");
        Resolve(html, css, "span").Color.Should().NotBe("red");
    }

    [Fact]
    public void ClassSelector_IsCaseInsensitive_LikeTheMatcher()
    {
        Resolve("<p class='highlight'>t</p>", ".Highlight { color: red; }", "p")
            .Color.Should().Be("red");
    }

    [Fact]
    public void IdSelector_MatchesAndRejects()
    {
        var css = "#main { color: red; }";
        Resolve("<p id='main'>t</p>", css, "p").Color.Should().Be("red");
        Resolve("<p id='other'>t</p>", css, "p").Color.Should().NotBe("red");
    }

    [Fact]
    public void DescendantSelector_RightmostClassGates()
    {
        var html = "<div><span class='inner'>a</span><span>b</span></div>";
        var css = "div .inner { color: red; }";
        Resolve(html, css, "span", 0).Color.Should().Be("red");
        Resolve(html, css, "span", 1).Color.Should().NotBe("red");
    }

    [Fact]
    public void CompoundTagClassId_StillMatches()
    {
        Resolve("<p class='note' id='x1'>t</p>", "p.note#x1 { color: red; }", "p")
            .Color.Should().Be("red");
    }

    [Fact]
    public void ChildCombinatorWithUniversalRightmost_StillMatches()
    {
        Resolve("<div><em>t</em></div>", "div > * { color: red; }", "em")
            .Color.Should().Be("red");
    }

    [Fact]
    public void NotSelector_KeepsTagRequirement()
    {
        var html = "<div class='skip'>a</div><div>b</div>";
        var css = "div:not(.skip) { color: red; }";
        Resolve(html, css, "div", 1).Color.Should().Be("red");
        Resolve(html, css, "div", 0).Color.Should().NotBe("red");
    }

    [Fact]
    public void AttributeValueWithSpace_DoesNotConfuseCompoundDetection()
    {
        Resolve("<p title='a b'>t</p>", "[title='a b'] { color: red; }", "p")
            .Color.Should().Be("red");
    }

    [Fact]
    public void SelectorList_BothPartsMatch()
    {
        var css = "h1, .x { color: red; }";
        Resolve("<h1>t</h1>", css, "h1").Color.Should().Be("red");
        Resolve("<p class='x'>t</p>", css, "p").Color.Should().Be("red");
    }

    [Fact]
    public void GeneralSiblingCombinator_StillMatches()
    {
        Resolve("<div><p class='a'>a</p><p class='b'>b</p></div>",
                ".a ~ .b { color: red; }", "p", 1)
            .Color.Should().Be("red");
    }

    [Fact]
    public void NthChildWithPlusInsideParens_IsNotTreatedAsCombinator()
    {
        var html = "<ul><li>1</li><li>2</li><li>3</li></ul>";
        var css = "li:nth-child(2n+1) { color: red; }";
        Resolve(html, css, "li", 0).Color.Should().Be("red");
        Resolve(html, css, "li", 1).Color.Should().NotBe("red");
        Resolve(html, css, "li", 2).Color.Should().Be("red");
    }

    [Fact]
    public void PseudoElement_LegacySingleColon_StillResolves()
    {
        var doc = HtmlParser.Parse("<p>t</p>");
        var sheet = CssStyleSheetParser.Parse("p:before { content: 'x'; color: red; }");
        var resolver = new CascadeResolver(new[] { sheet });
        var p = Find(doc.Body!, "p");

        var style = resolver.ResolvePseudoElement(p, "before", null);
        style.Should().NotBeNull();
        style!.Color.Should().Be("red");
    }

    [Fact]
    public void PseudoElement_DoubleColon_StillResolves_AndMainResolveIgnoresIt()
    {
        var doc = HtmlParser.Parse("<p>t</p>");
        var sheet = CssStyleSheetParser.Parse("p::after { content: 'x'; color: red; }");
        var resolver = new CascadeResolver(new[] { sheet });
        var p = Find(doc.Body!, "p");

        resolver.ResolvePseudoElement(p, "after", null)!.Color.Should().Be("red");
        resolver.Resolve(p, null).Color.Should().NotBe("red",
            "a pseudo-element rule must not style the element itself");
    }
}
