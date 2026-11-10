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

        var html = doc.DocumentElement!;
        var htmlStyle = resolver.Resolve(html, null);

        var body = doc.Body!;
        var bodyStyle = resolver.Resolve(body, htmlStyle);

        var div = body.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var style = resolver.Resolve(div, bodyStyle);

        style.Color.Should().Be("red", ":root's custom property reaches the div through html and body");
    }

    [Fact]
    public void CustomProperty_WithFallback()
    {
        var doc = HtmlParser.Parse("<div>Hello</div>");
        var sheet = CssStyleSheetParser.Parse("div { color: var(--accent, blue); }");
        var resolver = new CascadeResolver(new[] { sheet });

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var style = resolver.Resolve(div, null);

        style.Color.Should().Be("blue", "--accent is never defined, so the fallback applies");
    }

    [Fact]
    public void CustomProperty_Inherited()
    {
        var doc = HtmlParser.Parse("<div><span>text</span></div>");
        var sheet = CssStyleSheetParser.Parse("div { --text-color: green; } span { color: var(--text-color); }");
        var resolver = new CascadeResolver(new[] { sheet });

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var divStyle = resolver.Resolve(div, null);

        divStyle.Get("--text-color").Should().Be("green");

        var span = div.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "span");
        var spanStyle = resolver.Resolve(span, divStyle);

        spanStyle.Color.Should().Be("green", "the span inherits --text-color from the div");
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
        result.Should().Be("", "an undefined variable without a fallback resolves to nothing");
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
        var style = new ComputedStyle();
        var result = CssVariableResolver.ResolveVariables("var(--font, Arial, sans-serif)", style);
        result.Should().Be("Arial, sans-serif", "the fallback keeps its own commas");
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

    [Fact]
    public void Env_SafeAreaInset_ReturnsZero()
    {
        var style = new ComputedStyle();
        var result = CssVariableResolver.ResolveVariables("env(safe-area-inset-top)", style);
        result.Should().Be("0px");
    }

    [Fact]
    public void Env_WithFallback_UsesFallback()
    {
        var style = new ComputedStyle();
        var result = CssVariableResolver.ResolveVariables("env(--unknown, 10px)", style);
        result.Should().Be("10px");
    }

    [Fact]
    public void Env_InCalc_Works()
    {
        var style = new ComputedStyle();
        var result = CssVariableResolver.ResolveVariables("calc(100px + env(safe-area-inset-top))", style);
        result.Should().Be("calc(100px + 0px)");
    }

    [Fact]
    public void AtProperty_InitialValue_UsedWhenPropertyNotSet()
    {
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

    [Fact]
    public void Layer_RulesLoseToUnlayeredRules_SameSpecificity()
    {
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
        var css = "@layer base { p { color: red; } } @layer theme { p { color: green; } }";
        var doc = HtmlParser.Parse("<p>Text</p>");
        var sheet = CssStyleSheetParser.Parse(css);
        var resolver = new CascadeResolver(new[] { sheet });
        var p = doc.FindByTag("body")!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);
        style.Color.Should().Be("green",
            "later @layer (theme) should win over earlier @layer (base)");
    }

    [Fact]
    public void ContainerQuery_MatchingCondition_AppliesRule()
    {
        var css = "@container (min-width: 300px) { p { color: green; } }";
        var doc = HtmlParser.Parse("<p>Text</p>");
        var sheet = CssStyleSheetParser.Parse(css);
        var resolver = new CascadeResolver(new[] { sheet }, pageWidth: 600f);
        var p = doc.FindByTag("body")!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);
        style.Color.Should().Be("green",
            "@container (min-width: 300px) must match when container width is 600");
    }

    [Fact]
    public void ContainerQuery_NonMatchingCondition_SkipsRule()
    {
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
    public void Layer_HigherSpecificityInALayer_LosesToLowerSpecificityUnlayered()
    {
        var css = "@layer base { #id { color: red; } } p { color: blue; }";
        var doc = HtmlParser.Parse("<p id='id'>Text</p>");
        var sheet = CssStyleSheetParser.Parse(css);
        var resolver = new CascadeResolver(new[] { sheet });
        var p = doc.FindByTag("body")!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);
        style.Color.Should().Be("blue",
            "layer order is compared before specificity (CSS Cascade 5), so the unlayered rule wins");
    }

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

    private static ComputedStyle ResolveChain(string css, string html, params string[] tagPath)
    {
        var doc = HtmlParser.Parse(html);
        var resolver = new CascadeResolver(new[] { CssStyleSheetParser.Parse(css) });
        HtmlElement element = doc.DocumentElement!;
        var style = resolver.Resolve(element, null);
        element = doc.Body!;
        style = resolver.Resolve(element, style);
        foreach (var tag in tagPath)
        {
            element = element.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == tag);
            style = resolver.Resolve(element, style);
        }
        return style;
    }

    [Fact]
    public void CustomProperty_ReferencingAnother_IsComputedWhereItIsDeclared()
    {
        var style = ResolveChain(
            ":root { --base: 1px; --gap: var(--base); } div { --base: 5px; width: var(--gap); }",
            "<div>x</div>", "div");

        style.Get("width").Should().Be("1px", "--gap is computed on :root, and the div inherits the computed 1px");
        style.Get("--gap").Should().Be("1px");
    }

    [Fact]
    public void CustomProperties_InACycle_AreInvalidSoTheFallbackApplies()
    {
        var style = ResolveChain(
            "div { --a: var(--b); --b: var(--a); width: var(--a, 7px); }",
            "<div>x</div>", "div");

        style.Get("width").Should().Be("7px");
        style.Get("--a").Should().BeNull();
        style.Get("--b").Should().BeNull();
    }

    [Fact]
    public void CustomProperty_ReferencingACycleWithFallback_UsesTheFallback()
    {
        var style = ResolveChain(
            "div { --a: var(--b); --b: var(--a); --c: var(--a, 3px); width: var(--c); }",
            "<div>x</div>", "div");

        style.Get("--c").Should().Be("3px", "--c references the cycle but is not part of it");
        style.Get("width").Should().Be("3px");
    }

    [Fact]
    public void CustomProperty_WithMissingReferenceAndNoFallback_IsInvalid()
    {
        var style = ResolveChain(
            "div { --a: var(--missing); width: var(--a, 9px); }",
            "<div>x</div>", "div");

        style.Get("width").Should().Be("9px");
    }

    [Fact]
    public void CustomProperty_WideReferenceChain_ResolvesWithoutBlowingUp()
    {
        var css = new System.Text.StringBuilder("div { --v0: 1px; ");
        for (int i = 1; i <= 40; i++)
            css.Append("--v").Append(i).Append(": var(--v").Append(i - 1).Append(") var(--v").Append(i - 1).Append("); ");
        css.Append("--last: var(--v3); width: var(--last); }");

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var style = ResolveChain(css.ToString(), "<div>x</div>", "div");
        watch.Stop();

        style.Get("width").Should().Be("1px 1px 1px 1px 1px 1px 1px 1px");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "each custom property is computed once, not once per path");
    }

    [Fact]
    public void VarInsideAString_IsLeftAsText()
    {
        var style = ResolveChain(
            "div { --x: red; content: \"var(--x)\"; color: var(--x); }",
            "<div>x</div>", "div");

        style.Get("content").Should().Contain("var(--x)", "text inside a string is never a var() reference");
        style.Color.Should().Be("red");
    }

    [Fact]
    public void FunctionNameEndingInVar_IsNotAVarReference()
    {
        var style = ResolveChain(
            "div { --x: red; font-family: myvar(--x); }",
            "<div>x</div>", "div");

        style.Get("font-family").Should().Be("myvar(--x)");
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
