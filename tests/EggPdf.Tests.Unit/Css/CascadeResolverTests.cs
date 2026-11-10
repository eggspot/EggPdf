using EggPdf.Css;
using EggPdf.Css.Cascade;
using EggPdf.Css.Parser;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CascadeResolverTests
{
    [Fact]
    public void StylesheetRule_AppliedToMatchingElement()
    {
        var doc = HtmlParser.Parse("<p>Hello</p>");
        var sheet = CssStyleSheetParser.Parse("p { color: red; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);

        style.Color.Should().Be("red");
    }

    [Fact]
    public void InlineStyle_OverridesStylesheet()
    {
        var doc = HtmlParser.Parse("<p style='color: blue'>Hello</p>");
        var sheet = CssStyleSheetParser.Parse("p { color: red; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);

        style.Color.Should().Be("blue");
    }

    [Fact]
    public void HigherSpecificity_Wins()
    {
        var doc = HtmlParser.Parse("<p class='highlight'>Hello</p>");
        var sheet = CssStyleSheetParser.Parse("p { color: red; } .highlight { color: green; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);

        style.Color.Should().Be("green", ".highlight (0,1,0) beats p (0,0,1)");
    }

    [Fact]
    public void Important_BeatsHigherSpecificity()
    {
        var doc = HtmlParser.Parse("<p class='highlight'>Hello</p>");
        var sheet = CssStyleSheetParser.Parse("p { color: red !important; } .highlight { color: green; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);

        style.Color.Should().Be("red");
    }

    [Fact]
    public void LaterRule_WinsAtSameSpecificity()
    {
        var doc = HtmlParser.Parse("<p>Hello</p>");
        var sheet = CssStyleSheetParser.Parse("p { color: red; } p { color: blue; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);

        style.Color.Should().Be("blue");
    }

    [Fact]
    public void UaDefaults_StillApplied()
    {
        var doc = HtmlParser.Parse("<h1>Title</h1>");
        var sheet = CssStyleSheetParser.Parse("h1 { color: navy; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var h1 = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "h1");
        var style = resolver.Resolve(h1, null);

        style.Display.Should().Be("block", "from the UA stylesheet");
        style.Color.Should().Be("navy", "from the author stylesheet");
        style.FontWeight.Should().Be("bold", "from the UA stylesheet");
    }

    [Fact]
    public void Inheritance_WorksFromParent()
    {
        var doc = HtmlParser.Parse("<div style='color: purple'><span>text</span></div>");
        var resolver = new CascadeResolver(Array.Empty<CssStyleSheet>());

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var divStyle = resolver.Resolve(div, null);

        var span = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "span");
        var spanStyle = resolver.Resolve(span, divStyle);

        spanStyle.Color.Should().Be("purple", "color is inherited");
    }

    [Fact]
    public void NonMatchingRule_NotApplied()
    {
        var doc = HtmlParser.Parse("<p>Hello</p>");
        var sheet = CssStyleSheetParser.Parse("h1 { color: red; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);

        style.Color.Should().NotBe("red");
    }

    [Fact]
    public void MediaPrint_Applied()
    {
        var doc = HtmlParser.Parse("<p>Hello</p>");
        var sheet = CssStyleSheetParser.Parse("@media print { p { color: black; } }");
        var resolver = new CascadeResolver(new[] { sheet }, mediaType: "print");

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);

        style.Color.Should().Be("black");
    }

    [Fact]
    public void MediaScreen_SkippedInPrintMode()
    {
        var doc = HtmlParser.Parse("<p>Hello</p>");
        var sheet = CssStyleSheetParser.Parse("@media screen { p { color: green; } }");
        var resolver = new CascadeResolver(new[] { sheet }, mediaType: "print");

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);

        style.Color.Should().NotBe("green");
    }

    [Fact]
    public void HiddenAttribute_DisplayNone()
    {
        var doc = HtmlParser.Parse("<div hidden>secret</div>");
        var resolver = new CascadeResolver(Array.Empty<CssStyleSheet>());

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First();
        var style = resolver.Resolve(div, null);

        style.Display.Should().Be("none");
    }

    [Fact]
    public void MultipleStylesheets_AllApplied()
    {
        var doc = HtmlParser.Parse("<p class='bold'>Hello</p>");
        var sheet1 = CssStyleSheetParser.Parse("p { color: red; }");
        var sheet2 = CssStyleSheetParser.Parse(".bold { font-weight: bold; }");
        var resolver = new CascadeResolver(new[] { sheet1, sheet2 });

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);

        style.Color.Should().Be("red");
        style.FontWeight.Should().Be("bold");
    }

    [Fact]
    public void Inherit_Keyword_CopiesParentValue()
    {
        var doc = HtmlParser.Parse("<div><p>Hello</p></div>");
        var sheet = CssStyleSheetParser.Parse("div { color: purple; } p { color: inherit; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var divStyle = resolver.Resolve(div, null);
        var pStyle = resolver.Resolve(p, divStyle);

        pStyle.Color.Should().Be("purple");
    }

    [Fact]
    public void Inherit_Keyword_NoParent_FallsBackToInitial()
    {
        var doc = HtmlParser.Parse("<p>Hello</p>");
        var sheet = CssStyleSheetParser.Parse("p { color: inherit; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var pStyle = resolver.Resolve(p, null);

        pStyle.Color.Should().NotBe("inherit", "with no parent the color is the initial value, not the literal keyword");
    }

    [Fact]
    public void Initial_Keyword_ResetsToInitialValue()
    {
        var doc = HtmlParser.Parse("<div><p>Hello</p></div>");
        var sheet = CssStyleSheetParser.Parse("div { color: purple; } p { color: initial; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var divStyle = resolver.Resolve(div, null);
        var pStyle = resolver.Resolve(p, divStyle);

        pStyle.Color.Should().NotBe("purple", "the initial color is canvastext or unset, not the parent's purple");
        pStyle.Color.Should().NotBe("initial");
    }

    [Fact]
    public void Unset_Keyword_InheritedProperty_ActsAsInherit()
    {
        var doc = HtmlParser.Parse("<div><p>Hello</p></div>");
        var sheet = CssStyleSheetParser.Parse("div { color: purple; } p { color: unset; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var divStyle = resolver.Resolve(div, null);
        var pStyle = resolver.Resolve(p, divStyle);

        pStyle.Color.Should().Be("purple", "color is inherited, so unset acts as inherit and takes the parent's purple");
    }

    [Fact]
    public void Unset_Keyword_NonInheritedProperty_ActsAsInitial()
    {
        var doc = HtmlParser.Parse("<div><p>Hello</p></div>");
        var sheet = CssStyleSheetParser.Parse("div { margin-top: 50px; } p { margin-top: unset; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var divStyle = resolver.Resolve(div, null);
        var pStyle = resolver.Resolve(p, divStyle);

        pStyle.Get("margin-top").Should().NotBe("50px", "margin-top is not inherited, so unset acts as initial");
        pStyle.Get("margin-top").Should().NotBe("unset");
    }

    [Fact]
    public void Revert_Keyword_DoesNotLeaveKeywordLiteral()
    {
        var doc = HtmlParser.Parse("<div><p>Hello</p></div>");
        var sheet = CssStyleSheetParser.Parse("p { color: revert; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var p = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var divStyle = resolver.Resolve(div, null);
        var pStyle = resolver.Resolve(p, divStyle);

        pStyle.Color.Should().NotBe("revert",
            "revert on an inherited property falls back to the UA cascade, which inherits; the keyword never becomes a computed value");
    }

    [Fact]
    public void Revert_Keyword_NonInheritedProperty_RemovesAuthorValue()
    {
        var doc = HtmlParser.Parse("<p>Hello</p>");
        var sheet = CssStyleSheetParser.Parse("p { margin-top: revert; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var pStyle = resolver.Resolve(p, null);

        pStyle.Get("margin-top").Should().NotBe("revert",
            "revert on a non-inherited property falls back to the UA default; the keyword never becomes a computed value");
    }

    [Fact]
    public void SpecificityCache_MultipleElements_EachGetsCorrectStyle()
    {
        var doc = HtmlParser.Parse("<p id='hero' class='highlight'>A</p><p class='highlight'>B</p>");
        var sheet = CssStyleSheetParser.Parse(
            "#hero { color: gold; } .highlight { color: green; } p { color: red; }");
        var resolver = new CascadeResolver(new[] { sheet });

        var elements = doc.Body!.ChildNodes.OfType<HtmlElement>()
            .Where(e => e.TagName == "p").ToList();
        var heroStyle  = resolver.Resolve(elements[0], null);
        var otherStyle = resolver.Resolve(elements[1], null);

        heroStyle.Color.Should().Be("gold", "on the element with id=hero, #hero (1,0,0) beats .highlight (0,1,0)");
        otherStyle.Color.Should().Be("green",
            "the class-only element gets .highlight (0,1,0) over p; the cached specificity doesn't bleed between elements");
    }
}
