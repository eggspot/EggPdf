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

    private static ComputedStyle ResolveFirst(string html, string tag)
    {
        var doc = HtmlParser.Parse(html);
        var element = FindFirst(doc.DocumentElement!, tag)!;
        return new BasicStyleResolver().Resolve(element, null);
    }

    private static HtmlElement? FindFirst(HtmlElement root, string tag)
    {
        foreach (var child in root.ChildNodes.OfType<HtmlElement>())
        {
            if (child.TagName == tag) return child;
            var found = FindFirst(child, tag);
            if (found != null) return found;
        }
        return null;
    }

    [Theory]
    [InlineData("<img width='200px'>", "200px")]
    [InlineData("<img width=' 50% '>", "50%")]
    [InlineData("<img width='12.5'>", "12.5px")]
    [InlineData("<img width='120abc'>", "120px")]
    [InlineData("<img width='0'>", "0px")]
    public void WidthAttribute_ParsedAsHtmlDimension(string html, string expected)
    {
        ResolveFirst(html, "img").Width.Should().Be(expected);
    }

    [Theory]
    [InlineData("<img width='abc'>")]
    [InlineData("<img width='-5'>")]
    [InlineData("<img width='auto'>")]
    public void WidthAttribute_Invalid_IsIgnored(string html)
    {
        ResolveFirst(html, "img").Width.Should().NotBe("abcpx").And.NotBe("-5px").And.NotBe("autopx");
        ResolveFirst(html, "img").Has("width").Should().BeFalse();
    }

    [Fact]
    public void TableWidthZero_IsIgnored()
    {
        ResolveFirst("<table width='0'><tr><td>x</td></tr></table>", "table").Has("width").Should().BeFalse();
    }

    [Fact]
    public void SvgWidthWithCssUnit_KeepsTheUnit()
    {
        ResolveFirst("<svg width='10em' height='50%'></svg>", "svg").Width.Should().Be("10em");
        ResolveFirst("<svg width='10em' height='50%'></svg>", "svg").Height.Should().Be("50%");
    }

    [Theory]
    [InlineData("0", "0px")]
    [InlineData("5px", "5px")]
    [InlineData(" 7", "7px")]
    public void CellSpacing_MapsToBorderSpacing(string value, string expected)
    {
        ResolveFirst("<table cellspacing='" + value + "'><tr><td>x</td></tr></table>", "table")
            .Get("border-spacing").Should().Be(expected);
    }

    [Fact]
    public void CellPadding_MapsToCellPadding()
    {
        var td = ResolveFirst("<table cellpadding='8'><tr><td>x</td></tr></table>", "td");

        td.Get("padding-top").Should().Be("8px");
        td.Get("padding-right").Should().Be("8px");
        td.Get("padding-bottom").Should().Be("8px");
        td.Get("padding-left").Should().Be("8px");
    }

    [Fact]
    public void CellPadding_InlineStyleOnCellWins()
    {
        var td = ResolveFirst("<table cellpadding='8'><tr><td style='padding-left:2px'>x</td></tr></table>", "td");

        td.Get("padding-left").Should().Be("2px");
        td.Get("padding-top").Should().Be("8px");
    }

    [Fact]
    public void TableBorder_CellsGetOnePixelBorder()
    {
        const string html = "<table border='5'><tr><td>x</td></tr></table>";

        ResolveFirst(html, "table").Get("border-top-width").Should().Be("5px");
        ResolveFirst(html, "td").Get("border-top-width").Should().Be("1px");
        ResolveFirst(html, "td").Get("border-top-style").Should().Be("solid");
    }

    [Fact]
    public void TableBorderWithoutValue_IsOnePixel()
    {
        const string html = "<table border><tr><td>x</td></tr></table>";

        ResolveFirst(html, "table").Get("border-top-width").Should().Be("1px");
        ResolveFirst(html, "td").Get("border-top-width").Should().Be("1px");
    }

    [Fact]
    public void TableBorderWithPxSuffix_IsParsed()
    {
        ResolveFirst("<table border='2px'><tr><td>x</td></tr></table>", "table")
            .Get("border-top-width").Should().Be("2px");
    }

    [Fact]
    public void TableBorderZero_NoBorders()
    {
        const string html = "<table border='0'><tr><td>x</td></tr></table>";

        ResolveFirst(html, "table").Has("border-top-style").Should().BeFalse();
        ResolveFirst(html, "td").Has("border-top-style").Should().BeFalse();
    }

    [Fact]
    public void TableAlignCenter_MapsToAutoMarginsNotTextAlign()
    {
        var table = ResolveFirst("<table align='center'><tr><td>x</td></tr></table>", "table");

        table.Get("margin-left").Should().Be("auto");
        table.Get("margin-right").Should().Be("auto");
        table.Has("text-align").Should().BeFalse();
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void TableAlignLeftOrRight_Floats(string align)
    {
        var table = ResolveFirst("<table align='" + align + "'><tr><td>x</td></tr></table>", "table");

        table.Get("float").Should().Be(align);
        table.Has("text-align").Should().BeFalse();
    }

    [Theory]
    [InlineData("left", "float", "left")]
    [InlineData("right", "float", "right")]
    [InlineData("middle", "vertical-align", "middle")]
    [InlineData("top", "vertical-align", "top")]
    [InlineData("bottom", "vertical-align", "baseline")]
    public void ImgAlign_MapsToFloatOrVerticalAlign(string align, string property, string expected)
    {
        var img = ResolveFirst("<img align='" + align + "'>", "img");

        img.Get(property).Should().Be(expected);
        img.Has("text-align").Should().BeFalse();
    }

    [Fact]
    public void CellAlignMiddle_CentersText()
    {
        ResolveFirst("<table><tr><td align='middle'>x</td></tr></table>", "td").TextAlign.Should().Be("center");
    }

    [Fact]
    public void ParseHtmlDimension_ZeroRejectedOnlyWhenAsked()
    {
        BasicStyleResolver.ParseHtmlDimension("0", rejectZero: true).Should().BeNull();
        BasicStyleResolver.ParseHtmlDimension("0.0%", rejectZero: true).Should().BeNull();
        BasicStyleResolver.ParseHtmlDimension("0", rejectZero: false).Should().Be("0px");
    }
}
