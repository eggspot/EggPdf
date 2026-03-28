using System;
using System.Linq;
using EggPdf.Css;
using EggPdf.Css.Cascade;
using EggPdf.Css.Parser;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CssVariablesTests
{
    [Fact]
    public void CustomProperty_DeclaredAndResolved()
    {
        var doc = HtmlParser.Parse("<div>Hello</div>");
        var sheet = CssStyleSheetParser.Parse(":root { --main-color: red; } div { color: var(--main-color); }");
        var resolver = new CascadeResolver(new[] { sheet });

        // Resolve :root (html element) first, then body, then div
        var html = doc.DocumentElement!;
        var htmlStyle = resolver.Resolve(html, null);

        var body = doc.Body!;
        var bodyStyle = resolver.Resolve(body, htmlStyle);

        var div = body.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var style = resolver.Resolve(div, bodyStyle);

        style.Color.Should().Be("red");
    }

    [Fact]
    public void CustomProperty_WithFallback()
    {
        var doc = HtmlParser.Parse("<div>Hello</div>");
        // --accent is not defined, so fallback "blue" should be used
        var sheet = CssStyleSheetParser.Parse("div { color: var(--accent, blue); }");
        var resolver = new CascadeResolver(new[] { sheet });

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var style = resolver.Resolve(div, null);

        style.Color.Should().Be("blue");
    }

    [Fact]
    public void CustomProperty_Inherited()
    {
        var doc = HtmlParser.Parse("<div><span>text</span></div>");
        var sheet = CssStyleSheetParser.Parse("div { --text-color: green; } span { color: var(--text-color); }");
        var resolver = new CascadeResolver(new[] { sheet });

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var divStyle = resolver.Resolve(div, null);

        // Custom property should be on the div
        divStyle.Get("--text-color").Should().Be("green");

        var span = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "span");
        var spanStyle = resolver.Resolve(span, divStyle);

        // span inherits --text-color from div, resolves var(--text-color) to "green"
        spanStyle.Color.Should().Be("green");
    }

    [Fact]
    public void CustomProperty_NestedVar()
    {
        var doc = HtmlParser.Parse("<div>Hello</div>");
        var sheet = CssStyleSheetParser.Parse(
            ":root { --fallback: navy; --primary: var(--fallback); } div { color: var(--primary); }");
        var resolver = new CascadeResolver(new[] { sheet });

        var html = doc.DocumentElement!;
        var htmlStyle = resolver.Resolve(html, null);

        var body = doc.Body!;
        var bodyStyle = resolver.Resolve(body, htmlStyle);

        var div = body.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var style = resolver.Resolve(div, bodyStyle);

        style.Color.Should().Be("navy");
    }

    [Fact]
    public void CustomProperty_CycleDetection()
    {
        // --a references --b, --b references --a - cycle
        var style = new ComputedStyle();
        style.Set("--a", "var(--b)");
        style.Set("--b", "var(--a)");

        CssVariableResolver.HasCycle("--a", style).Should().BeTrue();
        CssVariableResolver.HasCycle("--b", style).Should().BeTrue();
    }

    [Fact]
    public void CustomProperty_UndefinedWithFallback()
    {
        var doc = HtmlParser.Parse("<p>text</p>");
        // --undefined is never declared, fallback is "12px"
        var sheet = CssStyleSheetParser.Parse("p { font-size: var(--undefined, 12px); }");
        var resolver = new CascadeResolver(new[] { sheet });

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);

        style.FontSize.Should().Be("12px");
    }

    [Fact]
    public void CustomProperty_DirectResolve_SimpleValue()
    {
        var style = new ComputedStyle();
        style.Set("--my-color", "purple");

        var result = CssVariableResolver.ResolveVariables("var(--my-color)", style);
        result.Should().Be("purple");
    }

    [Fact]
    public void CustomProperty_DirectResolve_MixedWithText()
    {
        var style = new ComputedStyle();
        style.Set("--size", "10px");

        var result = CssVariableResolver.ResolveVariables("calc(var(--size) + 5px)", style);
        result.Should().Be("calc(10px + 5px)");
    }

    [Fact]
    public void CustomProperty_DirectResolve_NoVarReturnsOriginal()
    {
        var style = new ComputedStyle();
        var result = CssVariableResolver.ResolveVariables("red", style);
        result.Should().Be("red");
    }

    [Fact]
    public void CustomProperty_DirectResolve_UndefinedNoFallback()
    {
        var style = new ComputedStyle();
        var result = CssVariableResolver.ResolveVariables("var(--missing)", style);
        // When undefined with no fallback, the var() resolves to empty
        result.Should().Be("");
    }

    [Fact]
    public void CustomProperty_InlineStyle_DeclaredAndResolved()
    {
        var doc = HtmlParser.Parse("<div style='--brand: orange; color: var(--brand)'>test</div>");
        var resolver = new CascadeResolver(Array.Empty<CssStyleSheet>());

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var style = resolver.Resolve(div, null);

        style.Color.Should().Be("orange");
    }

    [Fact]
    public void CustomProperty_FallbackWithComma()
    {
        // The fallback value can itself contain commas (e.g., font-family fallback)
        var style = new ComputedStyle();
        var result = CssVariableResolver.ResolveVariables("var(--font, Arial, sans-serif)", style);
        result.Should().Be("Arial, sans-serif");
    }

    [Fact]
    public void IsCustomProperty_ValidCustomProperties()
    {
        CssVariableResolver.IsCustomProperty("--my-var").Should().BeTrue();
        CssVariableResolver.IsCustomProperty("--x").Should().BeTrue();
        CssVariableResolver.IsCustomProperty("--color-primary").Should().BeTrue();
    }

    [Fact]
    public void IsCustomProperty_NotCustomProperties()
    {
        CssVariableResolver.IsCustomProperty("color").Should().BeFalse();
        CssVariableResolver.IsCustomProperty("-webkit-flex").Should().BeFalse();
        CssVariableResolver.IsCustomProperty("--").Should().BeFalse();
        CssVariableResolver.IsCustomProperty("").Should().BeFalse();
    }
}
