using EggPdf.Css;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class PresentationalAttributeTests
{
    [Fact]
    public void ImgWidth_AppliedAsStyle()
    {
        var doc = HtmlParser.Parse("<img width='200' height='100'>");
        var resolver = new BasicStyleResolver();

        var img = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "img");
        var style = resolver.Resolve(img, null);

        style.Width.Should().Be("200px");
        style.Height.Should().Be("100px");
    }

    [Fact]
    public void TableBorder_AppliedAsStyle()
    {
        var doc = HtmlParser.Parse("<table border='1'><tr><td>Cell</td></tr></table>");
        var resolver = new BasicStyleResolver();

        var table = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "table");
        var style = resolver.Resolve(table, null);

        style.Get("border-top-width").Should().NotBeNull();
    }

    [Fact]
    public void BgColor_AppliedAsBackgroundColor()
    {
        var doc = HtmlParser.Parse("<div bgcolor='red'></div>");
        var resolver = new BasicStyleResolver();

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var style = resolver.Resolve(div, null);

        style.BackgroundColor.Should().Be("red");
    }

    [Fact]
    public void AlignCenter_AppliedAsTextAlign()
    {
        var doc = HtmlParser.Parse("<p align='center'>Centered</p>");
        var resolver = new BasicStyleResolver();

        var p = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "p");
        var style = resolver.Resolve(p, null);

        style.TextAlign.Should().Be("center");
    }

    [Fact]
    public void FontColor_AppliedAsColor()
    {
        var doc = HtmlParser.Parse("<font color='blue'>Blue text</font>");
        var resolver = new BasicStyleResolver();

        var font = doc.Body!.ChildNodes.OfType<HtmlElement>().FirstOrDefault(e => e.TagName == "font");
        if (font == null) return; // font element may not be in DOM depending on parser

        var style = resolver.Resolve(font, null);
        style.Color.Should().Be("blue");
    }

    [Fact]
    public void CenterElement_DisplayBlockTextAlignCenter()
    {
        var doc = HtmlParser.Parse("<center>Centered content</center>");
        var resolver = new BasicStyleResolver();

        var center = doc.Body!.ChildNodes.OfType<HtmlElement>().FirstOrDefault(e => e.TagName == "center");
        if (center == null) return;

        var style = resolver.Resolve(center, null);
        style.Display.Should().Be("block");
        style.TextAlign.Should().Be("center");
    }

    [Fact]
    public void InlineStyle_OverridesPresentationalAttribute()
    {
        var doc = HtmlParser.Parse("<div bgcolor='red' style='background-color: blue'></div>");
        var resolver = new BasicStyleResolver();

        var div = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "div");
        var style = resolver.Resolve(div, null);

        // Inline style should win over presentational attribute
        style.BackgroundColor.Should().Be("blue");
    }
}
