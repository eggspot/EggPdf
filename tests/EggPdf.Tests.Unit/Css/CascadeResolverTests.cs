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

        // .highlight (0,1,0) beats p (0,0,1)
        style.Color.Should().Be("green");
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

        style.Display.Should().Be("block"); // from UA
        style.Color.Should().Be("navy"); // from author stylesheet
        style.FontWeight.Should().Be("bold"); // from UA
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

        spanStyle.Color.Should().Be("purple"); // inherited
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
}
