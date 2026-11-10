using System.Linq;
using EggPdf.Css;
using EggPdf.Css.Parser;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CssNestingTests
{
    private const string Red = "1.00 0.00 0.00 rg";
    private const string Blue = "0.00 0.00 1.00 rg";

    private static string Render(string html, params string[] visibleText)
        => PdfAssert.ValidPdf(HtmlToPdf.Render(html), visibleText.Select(t => $"({t}) Tj").ToArray());

    [Fact]
    public void Nesting_ThreeLevelsDeep_IsFullyExpanded()
    {
        var text = Render("<style>.a { .b { .c { color: red } } }</style><div class=a><div class=b><p class=c>Deep</p></div></div>", "Deep");

        PdfAssert.TextPaintedWith(text, "Deep", Red).Should().BeTrue();
    }

    [Fact]
    public void Nesting_ParentSelectorList_AppliesToEachParent()
    {
        var text = Render(
            "<style>.a, .b { & .c { color: red } }</style>" +
            "<div class=a><p class=c>InA</p></div><div class=b><p class=c>InB</p></div><p class=c>Outside</p>",
            "InA", "InB", "Outside");

        PdfAssert.TextPaintedWith(text, "InA", Red).Should().BeTrue();
        PdfAssert.TextPaintedWith(text, "InB", Red).Should().BeTrue();
        PdfAssert.TextPaintedWith(text, "Outside", Red).Should().BeFalse("\".a, .b { & .c }\" means \".a .c, .b .c\", not \".a, .b .c\"");
    }

    [Fact]
    public void Nesting_NestedSelectorList_IsRelativeToTheParentForEachItem()
    {
        var text = Render(
            "<style>.a { .b, .c { color: red } }</style><div class=a><p class=b>B</p><p class=c>C</p></div><p class=c>Stray</p>",
            "B", "C", "Stray");

        PdfAssert.TextPaintedWith(text, "B", Red).Should().BeTrue();
        PdfAssert.TextPaintedWith(text, "C", Red).Should().BeTrue();
        PdfAssert.TextPaintedWith(text, "Stray", Red).Should().BeFalse();
    }

    [Fact]
    public void Nesting_MediaRuleInsideStyleRule_WrapsTheParentSelector()
    {
        var text = Render("<style>.a { color: blue; @media print { color: red } }</style><p class=a>Printed</p>", "Printed");

        PdfAssert.TextPaintedWith(text, "Printed", Red).Should().BeTrue("a PDF is print media, so the nested @media print rule applies to .a");
    }

    [Fact]
    public void Nesting_InsideTopLevelMediaRule_IsExpanded()
    {
        var text = Render("<style>@media print { .a { & .b { color: red } } }</style><div class=a><p class=b>Inside</p></div>", "Inside");

        PdfAssert.TextPaintedWith(text, "Inside", Red).Should().BeTrue();
    }

    [Fact]
    public void Nesting_DeclarationAfterNestedRuleWithoutSemicolon_IsKept()
    {
        var text = Render("<style>.a { .b { color: red } color: blue }</style><p class=a>Last</p>", "Last");

        PdfAssert.TextPaintedWith(text, "Last", Blue).Should().BeTrue();
    }

    [Fact]
    public void Nesting_BraceInsideComment_DoesNotEndTheBlock()
    {
        var text = Render("<style>.a { /* } */ color: red; .b { color: blue } }</style><p class=a>Commented</p>", "Commented");

        PdfAssert.TextPaintedWith(text, "Commented", Red).Should().BeTrue();
    }

    [Fact]
    public void Nesting_SemicolonInsideUrl_DoesNotSplitTheDeclaration()
    {
        var css = CssNestingResolver.PreprocessNesting(
            ".a { background-image: url(data:image/png;base64,AAAA); .b { color: red } }");

        var sheet = CssStyleSheetParser.Parse(css);

        sheet.Rules.Should().Contain(r => r.SelectorText.Trim() == ".a" &&
            r.Declarations.Any(d => d.Property == "background-image" && d.Value.Contains("base64,AAAA")));
    }

    [Fact]
    public void Nesting_KeyframesAndFontFace_PassThroughUntouched()
    {
        const string css = "@keyframes spin { from { opacity: 0 } to { opacity: 1 } } .a { .b { color: red } }";

        var flattened = CssNestingResolver.PreprocessNesting(css);

        flattened.Should().Contain("@keyframes spin { from { opacity: 0 } to { opacity: 1 } }");
        flattened.Should().Contain(".a .b {");
    }

    [Fact]
    public void Nesting_PathologicallyDeep_DoesNotThrow()
    {
        var css = string.Concat(Enumerable.Repeat(".a { ", 5000)) + "color: red" + string.Concat(Enumerable.Repeat(" }", 5000));

        var act = () => CssStyleSheetParser.Parse(css);

        act.Should().NotThrow();
    }

    [Fact]
    public void LegacyHtmlCommentMarkersInStyle_AreIgnored()
    {
        var text = Render("<style><!-- p { color: red } --></style><p>Legacy</p>", "Legacy");

        PdfAssert.TextPaintedWith(text, "Legacy", Red).Should().BeTrue("CDO and CDC tokens are ignored at the top level of a stylesheet");
    }
}
