using System.Linq;
using EggPdf.Css;
using EggPdf.Css.Cascade;
using EggPdf.Css.Parser;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class LogicalPropertyCascadeTests
{
    private static ComputedStyle Resolve(string css, string html, string tag)
    {
        var doc = HtmlParser.Parse(html);
        var resolver = new CascadeResolver(new[] { CssStyleSheetParser.Parse(css) });
        var style = resolver.Resolve(doc.DocumentElement!, null);
        HtmlElement element = doc.Body!;
        style = resolver.Resolve(element, style);
        element = element.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == tag);
        return resolver.Resolve(element, style);
    }

    [Fact]
    public void LogicalAfterPhysical_TheLogicalDeclarationWins()
    {
        Resolve("p { margin: 10px; margin-inline-start: 50px; }", "<p>x</p>", "p").Get("margin-left").Should().Be("50px");
    }

    [Fact]
    public void PhysicalAfterLogical_ThePhysicalDeclarationWins()
    {
        Resolve("p { margin-inline-start: 50px; margin-left: 10px; }", "<p>x</p>", "p").Get("margin-left").Should().Be("10px");
    }

    [Fact]
    public void LogicalAuthorDeclaration_OverridesThePhysicalUserAgentDefault()
    {
        Resolve("ul { padding-inline-start: 0; }", "<ul><li>x</li></ul>", "ul").Get("padding-left").Should().Be("0");
    }

    [Fact]
    public void TwoValueLogicalShorthand_SetsStartAndEndSeparately()
    {
        var style = Resolve("div { margin-inline: 10px 20px; }", "<div>x</div>", "div");

        style.Get("margin-left").Should().Be("10px");
        style.Get("margin-right").Should().Be("20px");
    }

    [Fact]
    public void RightToLeftElement_InlineStartIsTheRightSide()
    {
        var style = Resolve("div { margin-inline-start: 5px; }", "<div dir='rtl'>x</div>", "div");

        style.Get("margin-right").Should().Be("5px");
    }

    [Fact]
    public void VerticalWritingMode_BlockStartIsTheRightSide()
    {
        var style = Resolve("div { writing-mode: vertical-rl; margin-block-start: 7px; }", "<div>x</div>", "div");

        style.Get("margin-right").Should().Be("7px");
    }
}
