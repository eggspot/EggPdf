using System.Diagnostics;
using System.Linq;
using EggPdf.Css.Selectors;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class SelectorPseudoClassTests
{
    private static HtmlElement First(string html, string tag)
    {
        var doc = HtmlParser.Parse(html);
        return Descendants(doc).First(e => e.TagName == tag);
    }

    private static System.Collections.Generic.IEnumerable<HtmlElement> Descendants(HtmlNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is not HtmlElement element) continue;
            yield return element;
            foreach (var nested in Descendants(element)) yield return nested;
        }
    }

    [Theory]
    [InlineData("button:-moz-focusring")]
    [InlineData("button:unknown-state")]
    [InlineData("button:unknown-function(x)")]
    [InlineData("button:before")]
    public void UnknownPseudoClassOrLegacyPseudoElement_DoesNotMatchTheElement(string selector)
    {
        SelectorMatcher.Matches(selector, First("<button>b</button>", "button")).Should().BeFalse();
    }

    [Fact]
    public void AnyLink_MatchesAnchorsWithAnHref()
    {
        SelectorMatcher.Matches("a:any-link", First("<a href='x'>l</a>", "a")).Should().BeTrue();
        SelectorMatcher.Matches("a:any-link", First("<a>l</a>", "a")).Should().BeFalse();
    }

    [Fact]
    public void ReadOnly_MatchesNonEditableElements()
    {
        SelectorMatcher.Matches("p:read-only", First("<p>t</p>", "p")).Should().BeTrue();
        SelectorMatcher.Matches("input:read-only", First("<input type='text'>", "input")).Should().BeFalse();
        SelectorMatcher.Matches("div:read-write", First("<div contenteditable>t</div>", "div")).Should().BeTrue();
    }

    [Fact]
    public void EnabledDisabledAndOptional_OnlyMatchFormControls()
    {
        SelectorMatcher.Matches("div:enabled", First("<div>t</div>", "div")).Should().BeFalse();
        SelectorMatcher.Matches("div:optional", First("<div>t</div>", "div")).Should().BeFalse();
        SelectorMatcher.Matches("input:enabled", First("<input>", "input")).Should().BeTrue();
    }

    [Fact]
    public void Disabled_IncludesControlsInsideADisabledFieldset()
    {
        SelectorMatcher.Matches("input:disabled", First("<fieldset disabled><input></fieldset>", "input")).Should().BeTrue();
    }

    [Fact]
    public void Empty_IgnoresCommentsButNotWhitespace()
    {
        SelectorMatcher.Matches("p:empty", First("<p><!-- note --></p>", "p")).Should().BeTrue();
        SelectorMatcher.Matches("p:empty", First("<p> </p>", "p")).Should().BeFalse();
    }

    [Fact]
    public void Dir_FollowsTheNearestDirAttribute()
    {
        SelectorMatcher.Matches("p:dir(rtl)", First("<div dir='rtl'><p>t</p></div>", "p")).Should().BeTrue();
        SelectorMatcher.Matches("p:dir(ltr)", First("<div dir='rtl'><p>t</p></div>", "p")).Should().BeFalse();
    }

    [Fact]
    public void Required_InvalidAndValid_FollowTheValue()
    {
        SelectorMatcher.Matches("input:invalid", First("<input required>", "input")).Should().BeTrue();
        SelectorMatcher.Matches("input:valid", First("<input required value='x'>", "input")).Should().BeTrue();
    }

    [Fact]
    public void SiblingSelectors_OnOneHundredThousandSiblings_RunInLinearTime()
    {
        const int count = 100_000;
        var parent = new HtmlElement("tbody");
        for (int i = 0; i < count; i++) parent.AppendChild(new HtmlElement("tr"));
        var rows = parent.ChildNodes.OfType<HtmlElement>().ToArray();

        var stopwatch = Stopwatch.StartNew();
        int even = 0, adjacent = 0, lastOfType = 0;
        foreach (var row in rows)
        {
            if (SelectorMatcher.Matches("tr:nth-child(even)", row)) even++;
            if (SelectorMatcher.Matches("tr + tr", row)) adjacent++;
            if (SelectorMatcher.Matches("tr:nth-last-of-type(1)", row)) lastOfType++;
        }
        stopwatch.Stop();

        even.Should().Be(count / 2);
        adjacent.Should().Be(count - 1);
        lastOfType.Should().Be(1);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "sibling positions come from a cached index, not a scan per element");
    }
}
