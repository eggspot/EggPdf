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

    // ── env() tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Env_SafeAreaInset_ReturnsZero()
    {
        // env(safe-area-inset-top) should resolve to 0px for PDF rendering
        var style = new ComputedStyle();
        var result = CssVariableResolver.ResolveVariables("env(safe-area-inset-top)", style);
        result.Should().Be("0px");
    }

    [Fact]
    public void Env_WithFallback_UsesFallback()
    {
        // env(--unknown, 10px) — unknown env var, uses fallback
        var style = new ComputedStyle();
        var result = CssVariableResolver.ResolveVariables("env(--unknown, 10px)", style);
        result.Should().Be("10px");
    }

    [Fact]
    public void Env_InCalc_Works()
    {
        // calc(100px + env(safe-area-inset-top)) should resolve to calc(100px + 0px)
        var style = new ComputedStyle();
        var result = CssVariableResolver.ResolveVariables("calc(100px + env(safe-area-inset-top))", style);
        result.Should().Be("calc(100px + 0px)");
    }

    // ── @property initial-value wiring ───────────────────────────────────────

    [Fact]
    public void AtProperty_InitialValue_UsedWhenPropertyNotSet()
    {
        // @property --brand-color { initial-value: red; inherits: false; }
        // div { color: var(--brand-color); }  -- --brand-color never explicitly set
        // Expected: color resolves to "red" from @property initial-value
        var doc = HtmlParser.Parse("<div>X</div>");
        var sheet = CssStyleSheetParser.Parse(@"
            @property --brand-color { syntax: '<color>'; inherits: false; initial-value: red; }
            div { color: var(--brand-color); }
        ");
        var resolver = new CascadeResolver(new[] { sheet });
        var html = doc.DocumentElement!;
        var htmlStyle = resolver.Resolve(html, null);
        var body = doc.Body!;
        var bodyStyle = resolver.Resolve(body, htmlStyle);
        var div = body.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var style = resolver.Resolve(div, bodyStyle);

        style.Color.Should().Be("red",
            "var(--brand-color) should resolve to the @property initial-value when not explicitly set");
    }

    [Fact]
    public void AtProperty_InitialValue_OverriddenByExplicitValue()
    {
        // When the custom property IS explicitly set, explicit value takes priority
        var doc = HtmlParser.Parse("<div>X</div>");
        var sheet = CssStyleSheetParser.Parse(@"
            @property --brand-color { syntax: '<color>'; inherits: false; initial-value: red; }
            :root { --brand-color: blue; }
            div { color: var(--brand-color); }
        ");
        var resolver = new CascadeResolver(new[] { sheet });
        var html = doc.DocumentElement!;
        var htmlStyle = resolver.Resolve(html, null);
        var body = doc.Body!;
        var bodyStyle = resolver.Resolve(body, htmlStyle);
        var div = body.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var style = resolver.Resolve(div, bodyStyle);

        style.Color.Should().Be("blue",
            "explicit custom property value should override @property initial-value");
    }

    // ── @layer cascade priority ───────────────────────────────────────────────

    [Fact]
    public void Layer_RulesLoseToUnlayeredRules_SameSpecificity()
    {
        // Unlayered rule should win even when the layered rule appears LATER in source
        // (layer order is lower priority than being unlayered)
        var css = "p { color: blue; } @layer base { p { color: red; } }";
        var doc = HtmlParser.Parse("<p>Text</p>");
        var sheet = CssStyleSheetParser.Parse(css);
        var resolver = new CascadeResolver(new[] { sheet });
        var p = doc.FindByTag("body")!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);
        style.Color.Should().Be("blue",
            "unlayered rule must win over layered rule even if layered appears later in source");
    }

    [Fact]
    public void Layer_LaterLayerWinsOverEarlierLayer()
    {
        // Later-declared layer wins over earlier layer at same specificity
        var css = "@layer base { p { color: red; } } @layer theme { p { color: green; } }";
        var doc = HtmlParser.Parse("<p>Text</p>");
        var sheet = CssStyleSheetParser.Parse(css);
        var resolver = new CascadeResolver(new[] { sheet });
        var p = doc.FindByTag("body")!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);
        style.Color.Should().Be("green",
            "later @layer (theme) should win over earlier @layer (base)");
    }

    // ── @container cascade evaluation ────────────────────────────────────────

    [Fact]
    public void ContainerQuery_MatchingCondition_AppliesRule()
    {
        // With a 600px page width, @container (min-width: 300px) should match
        var css = "@container (min-width: 300px) { p { color: green; } }";
        var doc = HtmlParser.Parse("<p>Text</p>");
        var sheet = CssStyleSheetParser.Parse(css);
        // Pass page width 600 so the container query matches
        var resolver = new CascadeResolver(new[] { sheet }, pageWidth: 600f);
        var p = doc.FindByTag("body")!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);
        style.Color.Should().Be("green",
            "@container (min-width: 300px) must match when container width is 600");
    }

    [Fact]
    public void ContainerQuery_NonMatchingCondition_SkipsRule()
    {
        // With a 200px page width, @container (min-width: 400px) must not match
        var css = "@container (min-width: 400px) { p { color: red; } }";
        var doc = HtmlParser.Parse("<p>Text</p>");
        var sheet = CssStyleSheetParser.Parse(css);
        var resolver = new CascadeResolver(new[] { sheet }, pageWidth: 200f);
        var p = doc.FindByTag("body")!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);
        style.Color.Should().NotBe("red",
            "@container (min-width: 400px) must not match when container width is 200");
    }

    [Fact]
    public void Layer_HigherSpecificityInEarlierLayer_WinsOverLowerSpecificityUnlayered()
    {
        // A higher-specificity layered rule should still win (specificity > layer order)
        var css = "@layer base { #id { color: red; } } p { color: blue; }";
        var doc = HtmlParser.Parse("<p id='id'>Text</p>");
        var sheet = CssStyleSheetParser.Parse(css);
        var resolver = new CascadeResolver(new[] { sheet });
        var p = doc.FindByTag("body")!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);
        style.Color.Should().Be("red",
            "higher-specificity rule in an earlier layer wins over lower-specificity unlayered rule");
    }

    // ── ::placeholder pseudo-element ─────────────────────────────────────────

    [Fact]
    public void Placeholder_CssRule_AppliesColorToPlaceholder()
    {
        var css = "input::placeholder { color: #aabbcc; }";
        var doc = HtmlParser.Parse("<input placeholder='hint'>");
        var sheet = CssStyleSheetParser.Parse(css);
        var resolver = new CascadeResolver(new[] { sheet });
        var input = doc.FindByTag("body")!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "input");
        var phStyle = resolver.ResolvePseudoElement(input, "placeholder", null);
        phStyle.Should().NotBeNull("::placeholder rule should produce a style");
        phStyle!.Color.Should().Be("#aabbcc",
            "::placeholder color should be applied from the CSS rule");
    }

    [Fact]
    public void Placeholder_LegacySingleColon_AlsoMatches()
    {
        var css = "input:placeholder { color: gray; }";
        var doc = HtmlParser.Parse("<input placeholder='hint'>");
        var sheet = CssStyleSheetParser.Parse(css);
        var resolver = new CascadeResolver(new[] { sheet });
        var input = doc.FindByTag("body")!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "input");
        var phStyle = resolver.ResolvePseudoElement(input, "placeholder", null);
        phStyle.Should().NotBeNull("single-colon :placeholder should also produce a style");
    }
}
