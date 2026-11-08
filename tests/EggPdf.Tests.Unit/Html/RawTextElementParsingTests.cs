using System.Linq;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Html;

public class RawTextElementParsingTests
{
    private static HtmlDocument Parse(string html) => HtmlParser.Parse(html);

    private static HtmlElement Find(HtmlNode root, string tag) => FindOrNull(root, tag)!;

    private static HtmlElement? FindOrNull(HtmlNode root, string tag)
    {
        foreach (var child in root.ChildNodes.OfType<HtmlElement>())
        {
            if (child.TagName == tag) return child;
            var found = FindOrNull(child, tag);
            if (found != null) return found;
        }
        return null;
    }

    private static string TextOf(HtmlElement element) =>
        string.Concat(element.ChildNodes.OfType<HtmlTextNode>().Select(t => t.Data));

    [Theory]
    [InlineData("<title>a </ b</title><p>after</p>", "title", "a </ b")]
    [InlineData("<title>a </> b</title><p>after</p>", "title", "a </> b")]
    [InlineData("<title>1 < 2</title><p>after</p>", "title", "1 < 2")]
    [InlineData("<textarea>x </ y</textarea><p>after</p>", "textarea", "x </ y")]
    [InlineData("<script>s=\"</\"+x;</script><p>after</p>", "script", "s=\"</\"+x;")]
    [InlineData("<script>document.write(\"<b class='x'>hi</b></div>\")</script><p>after</p>", "script", "document.write(\"<b class='x'>hi</b></div>\")")]
    [InlineData("<style>/* <!-- */ a > b { content: \"</p>\" }</style><p>after</p>", "style", "/* <!-- */ a > b { content: \"</p>\" }")]
    public void RawTextElement_MarkupLikeContent_IsKeptVerbatimAndEndsAtItsEndTag(string html, string tag, string expected)
    {
        var doc = Parse("<html><head></head><body>" + html + "</body></html>");

        TextOf(Find(doc, tag)).Should().Be(expected);
        TextOf(Find(doc, "p")).Should().Be("after");
    }

    [Theory]
    [InlineData("style", "a{content:\"&amp;\"}", "a{content:\"&amp;\"}")]
    [InlineData("script", "x = 1 &lt; 2", "x = 1 &lt; 2")]
    [InlineData("title", "Tom &amp; Jerry", "Tom & Jerry")]
    [InlineData("textarea", "a &lt; b", "a < b")]
    public void RawTextElement_CharacterReferences_DecodedOnlyInTitleAndTextarea(string tag, string content, string expected)
    {
        var doc = Parse("<html><body><" + tag + ">" + content + "</" + tag + "></body></html>");

        TextOf(Find(doc, tag)).Should().Be(expected);
    }

    [Theory]
    [InlineData("<script>a</SCRIPT><p>after</p>")]
    [InlineData("<script>a</script ><p>after</p>")]
    [InlineData("<script>a</script\n><p>after</p>")]
    public void RawTextElement_EndTagCaseOrWhitespace_StillEndsTheElement(string html)
    {
        var doc = Parse("<html><body>" + html + "</body></html>");

        TextOf(Find(doc, "script")).Should().Be("a");
        TextOf(Find(doc, "p")).Should().Be("after");
    }

    [Fact]
    public void RawTextElement_EndTagWithLongerName_DoesNotEndTheElement()
    {
        var doc = Parse("<html><body><script>a</scripts>b</script><p>after</p></body></html>");

        TextOf(Find(doc, "script")).Should().Be("a</scripts>b");
    }

    [Fact]
    public void RawTextElement_Unterminated_TakesRestOfInput()
    {
        var doc = Parse("<html><body><style>a{color:red}");

        TextOf(Find(doc, "style")).Should().Be("a{color:red}");
    }

    [Fact]
    public void XmpElement_MarkupContent_IsRawText()
    {
        var doc = Parse("<html><body><xmp><b>bold?</b></xmp></body></html>");

        var xmp = Find(doc, "xmp");
        xmp.ChildNodes.OfType<HtmlElement>().Should().BeEmpty();
        TextOf(xmp).Should().Be("<b>bold?</b>");
    }
}
