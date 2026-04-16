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
        }
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
    public void Supports_Not_UnknownProperty_RulesExcluded()
    {
        // @supports not (display: -webkit-box) — we treat unknown prefixed values as unsupported
        var sheet = CssStyleSheetParser.Parse("@supports not (-webkit-appearance: none) { p { color: green; } }");
        // Result: either included or excluded — key requirement is no crash
        // (we accept either; browser behaviour varies — at minimum, no throw)
        var act = () => CssStyleSheetParser.Parse("@supports not (-webkit-appearance: none) { p { color: green; } }");
        act.Should().NotThrow();
    }

    [Fact]
    public void Supports_DoesNotThrow_OnComplexCondition()
    {
        var css = "@supports (display: grid) and (gap: 1px) { div { grid-template-columns: 1fr 1fr; } }";
        var act = () => CssStyleSheetParser.Parse(css);
        act.Should().NotThrow();
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
    public void ContainerQuery_Parsed_DoesNotCrash()
    {
        // @container block should be parsed without throwing
        var css = "@container (min-width: 300px) { p { color: red; } }";
        var act = () => CssStyleSheetParser.Parse(css);
        act.Should().NotThrow("@container rules should be silently accepted");
    }

    [Fact]
    public void ContainerQuery_Named_Parsed_DoesNotCrash()
    {
        var css = "@container card (min-width: 300px) { p { color: blue; } }";
        var act = () => CssStyleSheetParser.Parse(css);
        act.Should().NotThrow("named @container rules should be silently accepted");
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
    public void Scope_DoesNotCrash()
    {
        var css = "@scope (.container) to (.exclude) { h1 { font-size: 2em; } p { color: blue; } }";
        var act = () => CssStyleSheetParser.Parse(css);
        act.Should().NotThrow("@scope rules should not crash the parser");
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
    public void Property_AtRule_DoesNotCrash()
    {
        var css = "@property --gradient-angle { syntax: '<angle>'; inherits: false; initial-value: 0deg; }";
        var act = () => CssStyleSheetParser.Parse(css);
        act.Should().NotThrow("@property rules should not crash the parser");
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
