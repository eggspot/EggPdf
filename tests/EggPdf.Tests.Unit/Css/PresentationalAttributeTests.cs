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
        if (font == null) return;

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
    public void WidthAttribute_HtmlDimension_MapsToPxOrPercent(string html, string expected)
    {
        ResolveFirst(html, "img").Width.Should().Be(expected);
    }

    [Theory]
    [InlineData("<img width='abc'>")]
    [InlineData("<img width='-5'>")]
    [InlineData("<img width='auto'>")]
    public void WidthAttribute_Invalid_IsIgnored(string html)
    {
        ResolveFirst(html, "img").Has("width").Should().BeFalse();
    }

    [Fact]
    public void TableWidthAttribute_Zero_IsIgnored()
    {
        ResolveFirst("<table width='0'><tr><td>x</td></tr></table>", "table").Has("width").Should().BeFalse();
    }

    [Fact]
    public void SvgWidthAttribute_CssUnit_KeepsTheUnit()
    {
        ResolveFirst("<svg width='10em' height='50%'></svg>", "svg").Width.Should().Be("10em");
        ResolveFirst("<svg width='10em' height='50%'></svg>", "svg").Height.Should().Be("50%");
    }

    [Theory]
    [InlineData("0", "0px")]
    [InlineData("5px", "5px")]
    [InlineData(" 7", "7px")]
    public void CellSpacingAttribute_OnTable_MapsToBorderSpacing(string value, string expected)
    {
        ResolveFirst("<table cellspacing='" + value + "'><tr><td>x</td></tr></table>", "table")
            .Get("border-spacing").Should().Be(expected);
    }

    [Fact]
    public void CellPaddingAttribute_OnTable_PadsEveryCellSide()
    {
        var td = ResolveFirst("<table cellpadding='8'><tr><td>x</td></tr></table>", "td");

        td.Get("padding-top").Should().Be("8px");
        td.Get("padding-right").Should().Be("8px");
        td.Get("padding-bottom").Should().Be("8px");
        td.Get("padding-left").Should().Be("8px");
    }

    [Fact]
    public void CellPaddingAttribute_CellInlineStyle_InlineStyleWins()
    {
        var td = ResolveFirst("<table cellpadding='8'><tr><td style='padding-left:2px'>x</td></tr></table>", "td");

        td.Get("padding-left").Should().Be("2px");
        td.Get("padding-top").Should().Be("8px");
    }

    [Fact]
    public void TableBorderAttribute_WiderThanOne_CellsGetOnePixelBorder()
    {
        const string html = "<table border='5'><tr><td>x</td></tr></table>";

        ResolveFirst(html, "table").Get("border-top-width").Should().Be("5px");
        ResolveFirst(html, "td").Get("border-top-width").Should().Be("1px");
        ResolveFirst(html, "td").Get("border-top-style").Should().Be("solid");
    }

    [Fact]
    public void TableBorderAttribute_NoValue_IsOnePixel()
    {
        const string html = "<table border><tr><td>x</td></tr></table>";

        ResolveFirst(html, "table").Get("border-top-width").Should().Be("1px");
        ResolveFirst(html, "td").Get("border-top-width").Should().Be("1px");
    }

    [Fact]
    public void TableBorderAttribute_PxSuffix_IsParsed()
    {
        ResolveFirst("<table border='2px'><tr><td>x</td></tr></table>", "table")
            .Get("border-top-width").Should().Be("2px");
    }

    [Fact]
    public void TableBorderAttribute_Zero_NoBorders()
    {
        const string html = "<table border='0'><tr><td>x</td></tr></table>";

        ResolveFirst(html, "table").Has("border-top-style").Should().BeFalse();
        ResolveFirst(html, "td").Has("border-top-style").Should().BeFalse();
    }

    [Fact]
    public void TableAlignAttribute_Center_MapsToAutoMarginsNotTextAlign()
    {
        var table = ResolveFirst("<table align='center'><tr><td>x</td></tr></table>", "table");

        table.Get("margin-left").Should().Be("auto");
        table.Get("margin-right").Should().Be("auto");
        table.Has("text-align").Should().BeFalse();
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void TableAlignAttribute_LeftOrRight_Floats(string align)
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
    [InlineData("absbottom", "vertical-align", "bottom")]
    [InlineData("abscenter", "vertical-align", "middle")]
    public void ImgAlignAttribute_Keyword_MapsToFloatOrVerticalAlign(string align, string property, string expected)
    {
        var img = ResolveFirst("<img align='" + align + "'>", "img");

        img.Get(property).Should().Be(expected);
        img.Has("text-align").Should().BeFalse();
    }

    [Fact]
    public void CellAlignAttribute_Middle_CentersText()
    {
        ResolveFirst("<table><tr><td align='middle'>x</td></tr></table>", "td").TextAlign.Should().Be("center");
    }

    [Fact]
    public void CellNowrapAttribute_Present_MapsToWhiteSpaceNowrap()
    {
        ResolveFirst("<table><tr><td nowrap>x</td></tr></table>", "td").Get("white-space").Should().Be("nowrap");
    }

    [Fact]
    public void ImgSpaceAttributes_HspaceAndVspace_MapToMargins()
    {
        var img = ResolveFirst("<img hspace='10' vspace='4px'>", "img");

        img.Get("margin-left").Should().Be("10px");
        img.Get("margin-right").Should().Be("10px");
        img.Get("margin-top").Should().Be("4px");
        img.Get("margin-bottom").Should().Be("4px");
    }

    [Fact]
    public void ImgBorderAttribute_NonZero_MapsToSolidBorder()
    {
        var img = ResolveFirst("<img border='2'>", "img");

        img.Get("border-top-width").Should().Be("2px");
        img.Get("border-top-style").Should().Be("solid");
        ResolveFirst("<img border='0'>", "img").Has("border-top-style").Should().BeFalse();
    }

    [Theory]
    [InlineData("ol", "a", "lower-alpha")]
    [InlineData("ol", "A", "upper-alpha")]
    [InlineData("ol", "i", "lower-roman")]
    [InlineData("ol", "I", "upper-roman")]
    [InlineData("ol", "1", "decimal")]
    [InlineData("ul", "square", "square")]
    [InlineData("ul", "CIRCLE", "circle")]
    public void ListTypeAttribute_KnownType_MapsToListStyleType(string tag, string type, string expected)
    {
        ResolveFirst("<" + tag + " type='" + type + "'><li>x</li></" + tag + ">", tag)
            .Get("list-style-type").Should().Be(expected);
    }

    [Fact]
    public void ListItemTypeAttribute_OnItem_OverridesListStyleForThatItem()
    {
        ResolveFirst("<ol><li type='I'>x</li></ol>", "li").Get("list-style-type").Should().Be("upper-roman");
    }

    [Fact]
    public void OrderedListTypeAttribute_UnorderedKeyword_KeepsDecimal()
    {
        ResolveFirst("<ol type='square'><li>x</li></ol>", "ol").Get("list-style-type").Should().Be("decimal");
    }

    [Theory]
    [InlineData("1", "x-small")]
    [InlineData("3", "medium")]
    [InlineData("7", "xxx-large")]
    [InlineData("9", "xxx-large")]
    [InlineData("0", "x-small")]
    [InlineData("+1", "large")]
    [InlineData("-2", "x-small")]
    [InlineData("+9", "xxx-large")]
    public void FontSizeAttribute_AbsoluteOrRelative_MapsToLegacyKeyword(string size, string expected)
    {
        ResolveFirst("<p><font size='" + size + "'>x</font></p>", "font").Get("font-size").Should().Be(ComputedFontSize(expected));
    }

    [Theory]
    [InlineData("big")]
    [InlineData("+")]
    public void FontSizeAttribute_Invalid_IsIgnored(string size)
    {
        ResolveFirst("<p><font size='" + size + "'>x</font></p>", "font").Get("font-size")
            .Should().Be(ResolveFirst("<p><font>x</font></p>", "font").Get("font-size"));
    }

    private static string ComputedFontSize(string keyword)
    {
        var style = new ComputedStyle();
        style.Set("font-size", keyword);
        ComputedFontMetrics.ComputeFontSizeAndLineHeight(style, null, 16f);
        return style.Get("font-size")!;
    }

    [Fact]
    public void BodyMarginAttributes_WidthAndHeight_SetBothSides()
    {
        var body = ResolveFirst("<body leftmargin='0' topmargin='5' marginwidth='3'>x</body>", "body");

        body.Get("margin-left").Should().Be("3px");
        body.Get("margin-right").Should().Be("3px");
        body.Get("margin-top").Should().Be("5px");
        body.Get("margin-bottom").Should().Be("5px");
    }

    [Fact]
    public void BodyMarginAttributes_LeftAndTopOnly_SetBothSidesLikeChrome()
    {
        var body = ResolveFirst("<body leftmargin='0' topmargin='0'>x</body>", "body");

        body.Get("margin-right").Should().Be("0px");
        body.Get("margin-bottom").Should().Be("0px");
    }

    [Theory]
    [InlineData("<table><colgroup><col width='2*'></colgroup><tr><td>x</td></tr></table>", "col")]
    [InlineData("<table><tr><td width='3*'>x</td></tr></table>", "td")]
    public void WidthAttribute_RelativeStar_IsIgnored(string html, string tag)
    {
        ResolveFirst(html, tag).Has("width").Should().BeFalse();
    }

    [Theory]
    [InlineData("image", true)]
    [InlineData("text", false)]
    [InlineData("submit", false)]
    public void InputAlignAttribute_OnlyImageInputs_Float(string type, bool floats)
    {
        var input = ResolveFirst("<input type='" + type + "' align='right'>", "input");

        input.Has("float").Should().Be(floats);
        input.Has("text-align").Should().BeFalse();
    }

    [Fact]
    public void BodyTextAttribute_Color_MapsToColor()
    {
        ResolveFirst("<body text='#333333'>x</body>", "body").Color.Should().Be("#333333");
    }

    [Fact]
    public void BackgroundAttribute_OnTable_MapsToBackgroundImage()
    {
        ResolveFirst("<table background='bg.png'><tr><td>x</td></tr></table>", "table")
            .Get("background-image").Should().Be("url('bg.png')");
    }

    [Fact]
    public void ParseHtmlDimension_ZeroRejectedOnlyWhenAsked()
    {
        BasicStyleResolver.ParseHtmlDimension("0", rejectZero: true).Should().BeNull();
        BasicStyleResolver.ParseHtmlDimension("0.0%", rejectZero: true).Should().BeNull();
        BasicStyleResolver.ParseHtmlDimension("0", rejectZero: false).Should().Be("0px");
    }
}
