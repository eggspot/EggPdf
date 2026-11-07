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
}
