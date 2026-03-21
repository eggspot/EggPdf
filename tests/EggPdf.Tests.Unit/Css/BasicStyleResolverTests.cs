using EggPdf.Css;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class BasicStyleResolverTests
{
    private readonly BasicStyleResolver _resolver = new();

    [Fact]
    public void Div_DisplayBlock()
    {
        var elem = new HtmlElement("div");
        var style = _resolver.Resolve(elem, null);

        style.Display.Should().Be("block");
    }

    [Fact]
    public void Span_DisplayInline()
    {
        var elem = new HtmlElement("span");
        var style = _resolver.Resolve(elem, null);

        style.Display.Should().Be("inline");
    }

    [Fact]
    public void H1_HasCorrectDefaults()
    {
        var elem = new HtmlElement("h1");
        var style = _resolver.Resolve(elem, null);

        style.Display.Should().Be("block");
        style.FontSize.Should().Be("2em");
        style.FontWeight.Should().Be("bold");
    }

    [Fact]
    public void P_HasMargins()
    {
        var elem = new HtmlElement("p");
        var style = _resolver.Resolve(elem, null);

        style.MarginTop.Should().Be("1em");
        style.MarginBottom.Should().Be("1em");
    }

    [Fact]
    public void A_HasColorAndUnderline()
    {
        var elem = new HtmlElement("a");
        var style = _resolver.Resolve(elem, null);

        style.Color.Should().Be("blue");
        style.Get("text-decoration").Should().Be("underline");
    }

    [Fact]
    public void InlineStyle_OverridesDefaults()
    {
        var elem = new HtmlElement("div");
        elem.SetAttribute("style", "color: red; display: flex");
        var style = _resolver.Resolve(elem, null);

        style.Color.Should().Be("red");
        style.Display.Should().Be("flex");
    }

    [Fact]
    public void InheritedProperty_InheritsFromParent()
    {
        var parentStyle = new ComputedStyle();
        parentStyle.Set("color", "blue");
        parentStyle.Set("font-family", "Arial");

        var child = new HtmlElement("span");
        var style = _resolver.Resolve(child, parentStyle);

        style.Color.Should().Be("blue");
        style.FontFamily.Should().Be("Arial");
    }

    [Fact]
    public void NonInheritedProperty_DoesNotInherit()
    {
        var parentStyle = new ComputedStyle();
        parentStyle.Set("margin-top", "20px");
        parentStyle.Set("padding-left", "10px");

        var child = new HtmlElement("span");
        var style = _resolver.Resolve(child, parentStyle);

        style.MarginTop.Should().BeNull();
        style.PaddingLeft.Should().BeNull();
    }

    [Fact]
    public void HiddenAttribute_DisplayNone()
    {
        var elem = new HtmlElement("div");
        elem.SetAttribute("hidden", "");
        var style = _resolver.Resolve(elem, null);

        style.Display.Should().Be("none");
    }

    [Fact]
    public void UnknownElement_DisplayInline()
    {
        var elem = new HtmlElement("custom-element");
        var style = _resolver.Resolve(elem, null);

        style.Display.Should().Be("inline");
    }

    [Fact]
    public void Table_DisplayTable()
    {
        var elem = new HtmlElement("table");
        var style = _resolver.Resolve(elem, null);

        style.Display.Should().Be("table");
    }

    [Fact]
    public void Td_DisplayTableCell()
    {
        var elem = new HtmlElement("td");
        var style = _resolver.Resolve(elem, null);

        style.Display.Should().Be("table-cell");
    }

    [Fact]
    public void Code_MonospaceFont()
    {
        var elem = new HtmlElement("code");
        var style = _resolver.Resolve(elem, null);

        style.FontFamily.Should().Be("monospace");
    }

    [Fact]
    public void Strong_Bold()
    {
        var elem = new HtmlElement("strong");
        var style = _resolver.Resolve(elem, null);

        style.FontWeight.Should().Be("bold");
    }

    [Fact]
    public void Body_Has8pxMargin()
    {
        var elem = new HtmlElement("body");
        var style = _resolver.Resolve(elem, null);

        style.MarginTop.Should().Be("8px");
        style.MarginRight.Should().Be("8px");
        style.MarginBottom.Should().Be("8px");
        style.MarginLeft.Should().Be("8px");
    }
}
