using System.Linq;
using EggPdf.Css;
using EggPdf.Css.Cascade;
using EggPdf.Css.Parser;
using EggPdf.Css.Tokenizer;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CssIdentifierEscapeTests
{
    private static HtmlElement Find(HtmlElement root, string tag)
    {
        foreach (var child in root.ChildNodes.OfType<HtmlElement>())
        {
            if (child.TagName == tag) return child;
            var found = FindOrNull(child, tag);
            if (found != null) return found;
        }
        return null!;
    }

    private static HtmlElement? FindOrNull(HtmlElement root, string tag)
    {
        foreach (var child in root.ChildNodes.OfType<HtmlElement>())
        {
            if (child.TagName == tag) return child;
            var found = FindOrNull(child, tag);
            if (found != null) return found;
        }
        return null;
    }

    private static string? ColorOf(string bodyHtml, string css, string tag)
    {
        var doc = HtmlParser.Parse("<html><body>" + bodyHtml + "</body></html>");
        var sheet = CssStyleSheetParser.Parse(css);
        return new CascadeResolver(new[] { sheet }).Resolve(Find(doc.Body!, tag), null).Color;
    }

    [Theory]
    [InlineData("\\5FAE\\8F6F\\96C5\\9ED1", "微软雅黑")]
    [InlineData("md\\:flex", "md:flex")]
    [InlineData("\\31 23", "123")]
    [InlineData("a\\ b", "a b")]
    public void Tokenizer_IdentWithEscapes_IsDecoded(string css, string expected)
    {
        var token = new CssTokenizer(css).NextToken();

        token.Type.Should().Be(CssTokenType.Ident);
        token.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("p { margin-top: 5px; } p { margin-top: 1px \\9; }", "margin-top", "5px")]
    [InlineData("p { width: 50px; } p { width: 100px\\9; }", "width", "50px")]
    public void Declaration_IeBackslashNineHack_IsIgnoredLikeChrome(string css, string property, string expected)
    {
        var doc = HtmlParser.Parse("<html><body><p>t</p></body></html>");
        var sheet = CssStyleSheetParser.Parse(css);

        new CascadeResolver(new[] { sheet }).Resolve(Find(doc.Body!, "p"), null).Get(property).Should().Be(expected);
    }

    [Fact]
    public void Selector_HexEscapeFollowedByDescendantCombinator_KeepsTheCombinator()
    {
        ColorOf("<div class='1'><p>t</p></div>", ".\\31  p { color: red; }", "p").Should().Be("red");
    }

    [Fact]
    public void Selector_EscapedAttributeName_MatchesTheDecodedName()
    {
        ColorOf("<p data:x='1'>t</p>", "[data\\:x='1'] { color: red; }", "p").Should().Be("red");
    }

    [Fact]
    public void FontFamily_EscapedName_DecodesToTheRealFamilyName()
    {
        var doc = HtmlParser.Parse("<html><body><p>t</p></body></html>");
        var sheet = CssStyleSheetParser.Parse("p { font-family: \\5FAE\\8F6F\\96C5\\9ED1, sans-serif; }");

        var style = new CascadeResolver(new[] { sheet }).Resolve(Find(doc.Body!, "p"), null);

        style.Get("font-family").Should().StartWith("微软雅黑");
    }

    [Theory]
    [InlineData("suffix: \"\\29 \";", ")")]
    [InlineData("suffix: \"\\29  \";", ") ")]
    [InlineData("suffix: '\\'';", "'")]
    [InlineData("prefix: \"(\";", "(")]
    public void CounterStyle_EscapedPrefixOrSuffix_IsDecoded(string descriptor, string expected)
    {
        var sheet = CssStyleSheetParser.Parse("@counter-style x { system: numeric; symbols: '0' '1'; " + descriptor + " }");

        var rule = sheet.CounterStyleRules.Single();
        (descriptor.StartsWith("suffix") ? rule.Suffix : rule.Prefix).Should().Be(expected);
    }
}
