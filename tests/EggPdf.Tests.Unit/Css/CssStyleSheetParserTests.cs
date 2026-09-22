using System.Linq;
using EggPdf.Css.Parser;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CssStyleSheetParserTests
{
    [Fact]
    public void EmptyStyleSheet_ReturnsNoRules()
    {
        var sheet = CssStyleSheetParser.Parse("");
        sheet.Rules.Should().BeEmpty();
    }

    [Fact]
    public void SingleRule_ParsedCorrectly()
    {
        var sheet = CssStyleSheetParser.Parse("p { color: red; }");

        sheet.Rules.Should().HaveCount(1);
        sheet.Rules[0].SelectorText.Should().Be("p");
        sheet.Rules[0].Declarations.Should().HaveCount(1);
        sheet.Rules[0].Declarations[0].Property.Should().Be("color");
        sheet.Rules[0].Declarations[0].Value.Should().Be("red");
    }

    [Fact]
    public void MultipleRules_AllParsed()
    {
        var css = @"
            h1 { font-size: 2em; font-weight: bold; }
            p { color: #333; margin: 1em 0; }
            .highlight { background-color: yellow; }
        ";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.Rules.Should().HaveCount(3);
        sheet.Rules[0].SelectorText.Should().Be("h1");
        sheet.Rules[1].SelectorText.Should().Be("p");
        sheet.Rules[2].SelectorText.Should().Be(".highlight");
    }

    [Fact]
    public void ComplexSelector_PreservedAsText()
    {
        var sheet = CssStyleSheetParser.Parse("div.container > p.text { color: blue; }");

        sheet.Rules[0].SelectorText.Should().Be("div.container > p.text");
    }

    [Fact]
    public void MultipleSelectors_PreservedAsList()
    {
        var sheet = CssStyleSheetParser.Parse("h1, h2, h3 { font-weight: bold; }");

        sheet.Rules[0].SelectorText.Should().Be("h1, h2, h3");
    }

    [Fact]
    public void Important_Detected()
    {
        var sheet = CssStyleSheetParser.Parse("p { color: red !important; }");

        sheet.Rules[0].Declarations[0].Important.Should().BeTrue();
        sheet.Rules[0].Declarations[0].Value.Should().Be("red");
    }

    [Fact]
    public void MediaRule_Parsed()
    {
        var css = "@media print { body { font-size: 12pt; } }";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.MediaRules.Should().HaveCount(1);
        sheet.MediaRules[0].MediaQuery.Should().Be("print");
        sheet.MediaRules[0].Rules.Should().HaveCount(1);
        sheet.MediaRules[0].Rules[0].SelectorText.Should().Be("body");
    }

    [Fact]
    public void FontFaceRule_Parsed()
    {
        var css = "@font-face { font-family: 'MyFont'; src: url('font.woff2'); }";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.FontFaceRules.Should().HaveCount(1);
        sheet.FontFaceRules[0].Declarations.Should().Contain(d => d.Property == "font-family");
    }

    [Fact]
    public void PageRule_Parsed()
    {
        var css = "@page { margin: 2cm; size: A4; }";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.PageRules.Should().HaveCount(1);
        sheet.PageRules[0].Declarations.Should().Contain(d => d.Property == "margin");
    }

    [Fact]
    public void CommentsBetweenRules_Stripped()
    {
        var css = "/* comment */ p { color: red; } /* another */";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.Rules.Should().HaveCount(1);
    }

    [Fact]
    public void MalformedRule_Skipped()
    {
        var css = "p { color: red; } @@invalid$$ { } h1 { font-size: 2em; }";
        var sheet = CssStyleSheetParser.Parse(css);

        // Should get at least the valid rules
        sheet.Rules.Should().Contain(r => r.SelectorText == "p");
        sheet.Rules.Should().Contain(r => r.SelectorText == "h1");
    }

    [Fact]
    public void ValueWithMultipleWords_PreservedComplete()
    {
        var css = "p { font-family: Arial, Helvetica, sans-serif; border: 1px solid #ddd; }";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.Rules[0].Declarations.Should().Contain(d =>
            d.Property == "font-family" && d.Value == "Arial, Helvetica, sans-serif");
        sheet.Rules[0].Declarations.Should().Contain(d =>
            d.Property == "border" && d.Value == "1px solid #ddd");
    }

    [Fact]
    public void NeverThrows_OnAnyInput()
    {
        var inputs = new[] { "", " ", "{}", "{{", "}}", "@", "p{", "p { color: }", ";;;", "/* unterminated" };
        foreach (var input in inputs)
        {
            var act = () => CssStyleSheetParser.Parse(input);
            act.Should().NotThrow($"input '{input}' should not throw");

            var sheet = act();
            sheet.Should().NotBeNull($"input '{input}' still yields a (possibly empty) stylesheet");
            sheet.Rules.Should().NotBeNull();
        }

        CssStyleSheetParser.Parse("").Rules.Should().BeEmpty("empty input has no rules");
        CssStyleSheetParser.Parse(";;;").Rules.Should().BeEmpty("stray semicolons are not rules");
    }

    // --- @supports ---

    [Fact]
    public void Supports_KnownProperty_RulesIncluded()
    {
        // @supports (display: flex) should include its rules (display is a known property)
        var sheet = CssStyleSheetParser.Parse("@supports (display: flex) { p { color: red; } }");

        sheet.Rules.Should().Contain(r => r.SelectorText == "p" &&
            r.Declarations.Any(d => d.Property == "color" && d.Value == "red"),
            "rules inside @supports for a supported property should be included");
    }

    [Fact]
    public void Supports_NotVendorPrefixedProperty_RulesIncluded()
    {
        // Vendor-prefixed properties count as unsupported, so `not (...)` holds and the rules apply
        var sheet = CssStyleSheetParser.Parse("@supports not (-webkit-appearance: none) { p { color: green; } }");

        sheet.Rules.Should().Contain(r => r.SelectorText == "p" &&
            r.Declarations.Any(d => d.Property == "color" && d.Value == "green"));

        var positive = CssStyleSheetParser.Parse("@supports (-webkit-appearance: none) { p { color: green; } }");
        positive.Rules.Should().BeEmpty("the un-negated vendor-prefixed test fails, so its rules are dropped");
    }

    [Fact]
    public void Supports_AndCondition_RequiresEveryPart()
    {
        var both = CssStyleSheetParser.Parse(
            "@supports (display: grid) and (gap: 1px) { div { grid-template-columns: 1fr 1fr; } }");
        both.Rules.Should().Contain(r => r.SelectorText == "div" &&
            r.Declarations.Any(d => d.Property == "grid-template-columns"));

        var oneFails = CssStyleSheetParser.Parse(
            "@supports (display: grid) and (-webkit-appearance: none) { div { color: red; } }");
        oneFails.Rules.Should().BeEmpty("an `and` condition needs every part to hold");
    }

    [Fact]
    public void PageRule_InsideMediaPrint_IsAddedToPageRules()
    {
        var sheet = CssStyleSheetParser.Parse("@media print { @page { margin: 5mm; } }");

        sheet.PageRules.Should().HaveCount(1, "nested @page inside @media print should be parsed");
        sheet.PageRules[0].Declarations.Should().Contain(d => d.Property == "margin" && d.Value == "5mm");
    }

    [Fact]
    public void PageRule_InsideMediaPrint_WithStyleRules_BothParsed()
    {
        var css = "@media print { body { margin: 0; } @page { margin: 5mm; } }";
        var sheet = CssStyleSheetParser.Parse(css);

        sheet.MediaRules.Should().HaveCount(1);
        sheet.MediaRules[0].Rules.Should().HaveCount(1, "body rule should be in media rules");
        sheet.PageRules.Should().HaveCount(1, "@page should be extracted to top-level PageRules");
    }

    // ── @container tests ─────────────────────────────────────────────────────

    [Fact]
    public void ContainerQuery_IsCapturedWithItsConditionAndRules()
    {
        var sheet = CssStyleSheetParser.Parse("@container (min-width: 300px) { p { color: red; } }");

        sheet.ContainerRules.Should().HaveCount(1);
        var rule = sheet.ContainerRules[0];
        rule.ContainerName.Should().BeEmpty();
        rule.Condition.Replace(" ", "").Should().Be("(min-width:300px)");
        rule.Rules.Should().ContainSingle(r => r.SelectorText == "p" && r.Declarations.Any(d => d.Property == "color" && d.Value == "red"));
    }

    [Fact]
    public void ContainerQuery_Named_KeepsTheContainerName()
    {
        var sheet = CssStyleSheetParser.Parse("@container card (min-width: 300px) { p { color: blue; } }");

        sheet.ContainerRules.Should().HaveCount(1);
        sheet.ContainerRules[0].ContainerName.Should().Be("card");
        sheet.ContainerRules[0].Condition.Replace(" ", "").Should().Be("(min-width:300px)");
    }

    // ── @scope ────────────────────────────────────────────────────────────────

    [Fact]
    public void Scope_RulesFlattened_IntoRegularRules()
    {
        // @scope (.card) { p { color: red; } } → .card p { color: red; }
        var css = "@scope (.card) { p { color: red; } }";
        var sheet = CssStyleSheetParser.Parse(css);
        // Should produce at least one rule with the scoped selector
        sheet.Rules.Should().HaveCountGreaterThan(0, "@scope rules should be flattened into regular rules");
        sheet.Rules.Should().Contain(r =>
            r.SelectorText.IndexOf(".card", StringComparison.OrdinalIgnoreCase) >= 0,
            "scoped selector should include the scope root");
    }

    [Fact]
    public void Scope_WithLowerBound_FlattensEveryInnerRuleUnderTheScopeRoot()
    {
        var sheet = CssStyleSheetParser.Parse(
            "@scope (.container) to (.exclude) { h1 { font-size: 2em; } p { color: blue; } }");

        sheet.Rules.Should().HaveCount(2);
        sheet.Rules.Should().Contain(r => r.SelectorText.Contains(".container") && r.SelectorText.Contains("h1")
            && r.Declarations.Any(d => d.Property == "font-size" && d.Value == "2em"));
        sheet.Rules.Should().Contain(r => r.SelectorText.Contains(".container") && r.SelectorText.Contains("p")
            && r.Declarations.Any(d => d.Property == "color" && d.Value == "blue"));
    }

    // ── @property ────────────────────────────────────────────────────────────

    [Fact]
    public void Property_AtRule_ParsedWithName()
    {
        var css = "@property --my-color { syntax: '<color>'; inherits: false; initial-value: red; }";
        var sheet = CssStyleSheetParser.Parse(css);
        sheet.PropertyRules.Should().HaveCount(1);
        sheet.PropertyRules[0].Name.Should().Be("--my-color");
    }

    [Fact]
    public void Property_AtRule_ParsesInitialValue()
    {
        var css = "@property --my-size { syntax: '<length>'; inherits: false; initial-value: 16px; }";
        var sheet = CssStyleSheetParser.Parse(css);
        sheet.PropertyRules.Should().HaveCount(1);
        sheet.PropertyRules[0].InitialValue.Should().Be("16px");
    }

    [Fact]
    public void Property_AtRule_ParsesInherits()
    {
        var css = "@property --heading-color { syntax: '<color>'; inherits: true; initial-value: blue; }";
        var sheet = CssStyleSheetParser.Parse(css);
        sheet.PropertyRules.Should().HaveCount(1);
        sheet.PropertyRules[0].Inherits.Should().BeTrue();
    }

    [Fact]
    public void Property_AtRule_AngleSyntax_IsRegistered()
    {
        var sheet = CssStyleSheetParser.Parse(
            "@property --gradient-angle { syntax: '<angle>'; inherits: false; initial-value: 0deg; }");

        sheet.PropertyRules.Should().ContainSingle();
        sheet.PropertyRules[0].Name.Should().Be("--gradient-angle");
        sheet.PropertyRules[0].InitialValue.Should().Be("0deg");
        sheet.PropertyRules[0].Inherits.Should().BeFalse();
    }

    // ── @page margin boxes ────────────────────────────────────────────────────

    [Fact]
    public void PageRule_MarginBox_BottomCenter_Parsed()
    {
        var css = "@page { @bottom-center { content: counter(page); } }";
        var sheet = CssStyleSheetParser.Parse(css);
        sheet.PageRules.Should().HaveCount(1);
        var rule = sheet.PageRules[0];
        rule.MarginBoxes.Should().HaveCount(1);
        rule.MarginBoxes[0].Position.Should().Be("bottom-center");
        rule.MarginBoxes[0].Declarations.Should().Contain(d => d.Property == "content");
    }

    [Fact]
    public void PageRule_MarginBox_And_Declarations_CoExist()
    {
        var css = "@page { size: letter; @top-right { content: 'Header'; } margin: 1in; }";
        var sheet = CssStyleSheetParser.Parse(css);
        sheet.PageRules.Should().HaveCount(1);
        var rule = sheet.PageRules[0];
        // Both the regular declarations and the margin box should be parsed
        rule.Declarations.Should().Contain(d => d.Property == "size");
        rule.Declarations.Should().Contain(d => d.Property == "margin");
        rule.MarginBoxes.Should().HaveCount(1);
        rule.MarginBoxes[0].Position.Should().Be("top-right");
    }

    [Fact]
    public void PageRule_MultipleMarginBoxes_AllParsed()
    {
        var css = @"@page {
            @top-center { content: 'Title'; }
            @bottom-left { content: counter(page); }
            @bottom-right { content: counter(pages); }
        }";
        var sheet = CssStyleSheetParser.Parse(css);
        sheet.PageRules.Should().HaveCount(1);
        sheet.PageRules[0].MarginBoxes.Should().HaveCount(3);
    }
}
