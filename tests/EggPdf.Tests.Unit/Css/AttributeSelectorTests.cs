using System.Linq;
using EggPdf.Css;
using EggPdf.Css.Cascade;
using EggPdf.Css.Parser;
using EggPdf.Css.Selectors;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class AttributeSelectorTests
{
    private static HtmlElement First(string bodyHtml) =>
        HtmlParser.Parse("<html><body>" + bodyHtml + "</body></html>").Body!.ChildNodes.OfType<HtmlElement>().First();

    [Theory]
    [InlineData("<a title='a]b'>x</a>", "[title='a]b']")]
    [InlineData("<a title='a]b'>x</a>", "a[title=\"a]b\"]")]
    [InlineData("<a title='x y'>x</a>", "[title='x y']")]
    [InlineData("<a title='a,b'>x</a>", "[title='a,b']")]
    [InlineData("<a title='x'>x</a>", ":not([title='a)b']):not(p)")]
    [InlineData("<a title='a::before'>x</a>", "[title='a::before']")]
    [InlineData("<a title='it&apos;s'>x</a>", "[title='it\\'s']")]
    [InlineData("<a data-x='a b'>x</a>", "[data-x=a\\ b]")]
    public void AttributeSelector_QuotedOrEscapedSpecialCharacters_Matches(string html, string selector)
    {
        SelectorMatcher.Matches(selector, First(html)).Should().BeTrue();
    }

    [Fact]
    public void AttributeSelector_QuotedBracketBeforeDescendantCombinator_MatchesDescendant()
    {
        var div = First("<div title='a]b'><span>x</span></div>");
        var span = div.ChildNodes.OfType<HtmlElement>().First();

        SelectorMatcher.Matches("[title='a]b'] span", span).Should().BeTrue();
        SelectorMatcher.Matches("[title='a]c'] span", span).Should().BeFalse();
    }

    [Fact]
    public void NotPseudoClass_ParenInsideQuotedAttribute_DoesNotEndTheArgument()
    {
        var a = First("<a title='x'>x</a>");

        SelectorMatcher.Matches(":not([title='a)b']):not(a)", a).Should().BeFalse();
        SelectorMatcher.Matches(":not([title='x)']):not(p)", a).Should().BeTrue();
    }

    [Fact]
    public void AttributeSelectorList_CommaInsideQuotes_DoesNotSplitTheList()
    {
        var a = First("<a title='x'>x</a>");

        SelectorMatcher.Matches("[title='a,b'], [title='x']", a).Should().BeTrue();
        SelectorMatcher.Matches("[title='x,y']", a).Should().BeFalse();
    }

    [Theory]
    [InlineData("<a data-state='Open'>x</a>", "[data-state='open']", false)]
    [InlineData("<a data-state='Open'>x</a>", "[data-state='Open']", true)]
    [InlineData("<a data-state='Open'>x</a>", "[data-state='open' i]", true)]
    [InlineData("<a data-state='Open'>x</a>", "[data-state='open'i]", true)]
    [InlineData("<a data-state='Open'>x</a>", "[data-state=open I]", true)]
    [InlineData("<input type='TEXT'>", "[type='text']", true)]
    [InlineData("<input type='TEXT'>", "[type='text' s]", false)]
    [InlineData("<a lang='EN-us'>x</a>", "[lang|='en']", true)]
    [InlineData("<a href='File.PDF'>x</a>", "[href$='.pdf']", false)]
    [InlineData("<a href='File.PDF'>x</a>", "[href$='.pdf' i]", true)]
    public void AttributeSelector_ValueCase_FollowsFlagsAndHtmlRules(string html, string selector, bool expected)
    {
        SelectorMatcher.Matches(selector, First(html)).Should().Be(expected);
    }

    [Theory]
    [InlineData("[title^='']")]
    [InlineData("[title$='']")]
    [InlineData("[title*='']")]
    [InlineData("[title~='']")]
    [InlineData("[title~='a b']")]
    public void AttributeSelector_EmptyOrWhitespaceOperand_MatchesNothing(string selector)
    {
        SelectorMatcher.Matches(selector, First("<a title='a b'>x</a>")).Should().BeFalse();
    }

    [Fact]
    public void AttributeSelector_EmptyEquals_MatchesEmptyValue()
    {
        SelectorMatcher.Matches("[title='']", First("<a title=''>x</a>")).Should().BeTrue();
    }

    [Fact]
    public void AttributeSelector_WordMatchAcrossTabsAndNewlines_Matches()
    {
        SelectorMatcher.Matches("[rel~='b']", First("<a rel='a\tb\nc'>x</a>")).Should().BeTrue();
    }

    [Theory]
    [InlineData("[title='x' q]")]
    [InlineData("[title='x' i i]")]
    [InlineData("[title=='x']")]
    [InlineData("[='x']")]
    public void AttributeSelector_Invalid_MatchesNothing(string selector)
    {
        SelectorMatcher.Matches(selector, First("<a title='x'>x</a>")).Should().BeFalse();
    }

    [Theory]
    [InlineData("[title='a]b'")]
    [InlineData("[title='unterminated")]
    [InlineData("[")]
    [InlineData("[title=")]
    [InlineData(":not([title='a)b'")]
    public void AttributeSelector_Unterminated_DoesNotThrow(string selector)
    {
        var a = First("<a title='x'>x</a>");

        var act = () => SelectorMatcher.Matches(selector, a);

        act.Should().NotThrow();
    }

    [Fact]
    public void AttributeSelector_QuotedBracket_SpecificityCountsOneAttribute()
    {
        SelectorMatcher.CalculateSpecificity("a[title='x]y'].c").Should().Be((0, 2, 1));
    }

    [Fact]
    public void Cascade_QuotedAttributeSelectorRule_Applies()
    {
        var doc = HtmlParser.Parse("<html><body><a title='a]b'>x</a></body></html>");
        var a = doc.Body!.ChildNodes.OfType<HtmlElement>().First();
        var sheet = CssStyleSheetParser.Parse("a[title='a]b'] { color: red; }");

        var style = new CascadeResolver(new[] { sheet }).Resolve(a, null);

        style.Color.Should().Be("red");
    }
}
