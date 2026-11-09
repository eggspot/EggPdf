using System.Linq;
using EggPdf.Css;
using EggPdf.Css.Cascade;
using EggPdf.Css.Parser;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CascadeOrderTests
{
    private static (CascadeResolver Resolver, HtmlDocument Document) Setup(string html, string css) =>
        (new CascadeResolver(new[] { CssStyleSheetParser.Parse(css) }), HtmlParser.Parse(html));

    private static HtmlElement Find(HtmlNode node, string tag) =>
        Descendants(node).First(e => e.TagName == tag);

    private static System.Collections.Generic.IEnumerable<HtmlElement> Descendants(HtmlNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is not HtmlElement element) continue;
            yield return element;
            foreach (var nested in Descendants(element)) yield return nested;
        }
    }

    private static ComputedStyle StyleOf(string html, string css, string tag)
    {
        var (resolver, doc) = Setup(html, css);
        return resolver.Resolve(Find(doc, tag), null);
    }

    [Fact]
    public void LayeredRule_LosesToAnUnlayeredRuleOfLowerSpecificity()
    {
        StyleOf("<p id='x'>t</p>", "@layer base { #x { color: red } } p { color: blue }", "p").Color.Should().Be("blue");
    }

    [Fact]
    public void LaterLayer_BeatsAnEarlierLayerOfHigherSpecificity()
    {
        StyleOf("<p id='x'>t</p>", "@layer a { #x { color: red } } @layer b { p { color: green } }", "p").Color.Should().Be("green");
    }

    [Fact]
    public void ImportantInTheEarliestLayer_BeatsLaterLayersAndUnlayeredImportant()
    {
        var css = "@layer a, b; @layer b { p { color: green !important } } @layer a { p { color: red !important } } p { color: blue !important }";

        StyleOf("<p>t</p>", css, "p").Color.Should().Be("red");
    }

    [Fact]
    public void MediaRuleBeforeAPlainRule_LosesToTheLaterPlainRule()
    {
        StyleOf("<p>t</p>", "@media print { p { color: red } } p { color: blue }", "p").Color.Should().Be("blue");
    }

    [Fact]
    public void SelectorList_UsesTheSpecificityOfTheSelectorThatMatched()
    {
        StyleOf("<div class='x'><h1>t</h1></div>", "h1, #title { color: red } .x h1 { color: blue }", "h1").Color.Should().Be("blue");
    }

    [Fact]
    public void PseudoElementSelectorList_StylesEveryListedElement()
    {
        var (resolver, doc) = Setup("<p>t</p><h1>u</h1>", "p::before, h1::before { content: 'x' }");

        resolver.ResolvePseudoElement(Find(doc, "p"), "before", null).Should().NotBeNull();
        resolver.ResolvePseudoElement(Find(doc, "h1"), "before", null).Should().NotBeNull();
    }

    [Fact]
    public void ImportantShorthand_BeatsALaterNormalLonghand()
    {
        StyleOf("<p>t</p>", "p { margin: 0 !important } p { margin-top: 5px }", "p").MarginTop.Should().Be("0");
    }

    [Fact]
    public void InlineShorthand_DoesNotOverrideAnAuthorImportantLonghand()
    {
        var style = StyleOf("<p style='margin: 3px'>t</p>", "p { margin-top: 5px !important }", "p");

        style.MarginTop.Should().Be("5px");
        style.MarginBottom.Should().Be("3px");
    }

    [Fact]
    public void Revert_OnANonInheritedProperty_RestoresTheUserAgentValue()
    {
        StyleOf("<div>t</div>", "div { display: revert }", "div").Display.Should().Be("block");
    }

    [Fact]
    public void PseudoElement_InheritsTextIndentFromItsElement()
    {
        var (resolver, doc) = Setup("<p>t</p>", "p { text-indent: 2em } p::before { content: 'x' }");
        var p = Find(doc, "p");
        var pStyle = resolver.Resolve(p, null);

        resolver.ResolvePseudoElement(p, "before", pStyle)!.Get("text-indent").Should().Be("2em");
    }
}
