using System.Linq;
using EggPdf.Html;
using EggPdf.Html.Dom;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Html;

public class PreformattedTextParsingTests
{
    private static HtmlElement Element(string bodyHtml, string tag) =>
        HtmlParser.Parse("<html><body>" + bodyHtml + "</body></html>").Body!
            .ChildNodes.OfType<HtmlElement>().Single(e => e.TagName == tag);

    private static string Text(HtmlNode node) =>
        string.Concat(node.ChildNodes.Select(c => c is HtmlTextNode t ? t.Data : Text(c)));

    [Theory]
    [InlineData("<pre>\nline one\nline two</pre>", "pre", "line one\nline two")]
    [InlineData("<pre>\r\nline one</pre>", "pre", "line one")]
    [InlineData("<pre>\n\nkept blank</pre>", "pre", "\nkept blank")]
    [InlineData("<pre>no newline</pre>", "pre", "no newline")]
    [InlineData("<listing>\ncode</listing>", "listing", "code")]
    [InlineData("<textarea>\nnote</textarea>", "textarea", "note")]
    public void PreformattedElement_DropsOneLeadingNewline(string html, string tag, string expectedText)
    {
        Text(Element(html, tag)).Should().Be(expectedText);
    }

    [Fact]
    public void Pre_NewlineAfterChildElementStart_IsKept()
    {
        Text(Element("<pre><b>\nbold</b></pre>", "pre")).Should().Be("\nbold");
    }
}
