using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class InlineBoundarySpaceTests
{
    private static async Task<string> RenderAscii(string body) =>
        Encoding.ASCII.GetString(await HtmlToPdf.RenderAsync("<html><body>" + body + "</body></html>"));

    [Fact]
    public async Task TextNode_TrailingSpaceBeforeStrong_IsPreserved()
    {
        var text = await RenderAscii("<p>foo <strong>bar</strong> baz</p>");

        text.Should().Contain("( bar)", "the space between the text node and the inline element must be preserved");
        text.Should().Contain("( baz)", "the space after the inline element must be preserved");
    }

    [Fact]
    public async Task Nbsp_BeforeInlineElement_DoesNotDoubleSpace()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>Tax ID:&#160;<strong>VALUE</strong></p></body></html>");

        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
        text.Should().NotContain("( VALUE)", "the NBSP already provides the gap; a synthesized space would double it");
        text.Should().Contain("(VALUE)");
    }

    [Fact]
    public async Task StrongBetweenTextRuns_MidSentence_KeepsSpaces()
    {
        var text = await RenderAscii("<div>alpha beta <strong>gamma</strong> delta</div>");

        text.Should().Contain("( gamma)");
        text.Should().Contain("( delta)");
    }

    [Fact]
    public async Task WhitespaceOnlyTextNodeBetweenSiblingSpans_CollapsesToOneSpace()
    {
        var text = await RenderAscii("<div><span><span>Label:</span>\n        <span>Value</span></span></div>");

        text.Should().Contain("( Value)",
            "a whitespace-only text node between sibling inline elements must still collapse to one boundary space");
    }

    [Theory]
    [InlineData("<p>See <a href=\"#t\">terms</a>. Call now</p>", "(.)", "( .)")]
    [InlineData("<p><b>Total</b>: 100</p>", "(:)", "( :)")]
    [InlineData("<p><i>(note</i>) here</p>", "(\\))", "( \\))")]
    [InlineData("<p>H<sub>2</sub>O water</p>", "(O)", "( O)")]
    [InlineData("<p><b>A</b>B</p>", "(B)", "( B)")]
    public async Task TextDirectlyAfterInlineElement_NoSpaceInSource_GetsNoSpace(string body, string expected, string unexpected)
    {
        var text = await RenderAscii(body);

        text.Should().Contain(expected + " Tj");
        text.Should().NotContain(unexpected + " Tj");
    }

    [Fact]
    public async Task InlineElementEndingWithSpace_FollowingTextGetsOneSpace()
    {
        var text = await RenderAscii("<p><b>Total </b>due</p>");

        text.Should().Contain("( due) Tj");
    }

    [Fact]
    public async Task TextAfterInlineImage_NoSpaceInSource_GetsNoSpace()
    {
        var text = await RenderAscii("<p><img src='x.png' width='10' height='10'>Label</p>");

        text.Should().Contain("(Label) Tj");
    }

    [Theory]
    [InlineData("<p><b>Alpha</b> <i>Beta</i></p>", "( Beta)")]
    [InlineData("<p><b>Name:</b>\n    <span>John</span></p>", "( John)")]
    [InlineData("<p>x <b>A</b> <!--c-->y</p>", "( y)")]
    public async Task WhitespaceOnlyTextNodeBetweenInlineContent_CollapsesToOneSpace(string body, string expected)
    {
        var text = await RenderAscii(body);

        text.Should().Contain(expected + " Tj");
    }

    [Fact]
    public async Task InlineElementEndingWithSpace_ThenTextNodesSplitByComment_NoExtraSpace()
    {
        var text = await RenderAscii("<p><b>A </b>x<!--c-->y</p>");

        text.Should().Contain("(y) Tj");
    }

    [Fact]
    public async Task TextNodesSplitByComment_KeepTheirSpace()
    {
        var text = await RenderAscii("<p>Total: <!-- amount -->10 <b>EUR</b></p>");

        text.Should().Contain("( 10) Tj");
    }
}
