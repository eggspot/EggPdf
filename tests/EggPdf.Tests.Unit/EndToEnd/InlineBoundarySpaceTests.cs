using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Whitespace at the boundary between a text node and an inline element must
/// collapse to a single space, not disappear ("Căn cứ&lt;strong&gt;Luật" bug).
/// </summary>
public class InlineBoundarySpaceTests
{
    [Fact]
    public async Task TextNode_TrailingSpaceBeforeStrong_IsPreserved()
    {
        var pdf = await HtmlToPdf.RenderAsync("<html><body><p>foo <strong>bar</strong> baz</p></body></html>");
        var text = Encoding.ASCII.GetString(pdf);

        // The strong run must start with the boundary space: "( bar)" not "(bar)"
        text.Should().Contain("( bar)",
            "the space between the text node and the inline element must be preserved");
        text.Should().Contain("( baz)",
            "the space after the inline element must be preserved");
    }

    [Fact]
    public async Task Nbsp_BeforeInlineElement_DoesNotDoubleSpace()
    {
        // "Tax ID:&nbsp;<strong>value</strong>" — the NBSP is rendered content;
        // no additional boundary space may be synthesized.
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>Tax ID:&#160;<strong>VALUE</strong></p></body></html>");

        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
        text.Should().NotContain("( VALUE)",
            "the NBSP already provides the gap — a synthesized space would double it");
        text.Should().Contain("(VALUE)");
    }

    [Fact]
    public async Task StrongBetweenTextRuns_MidSentence_KeepsSpaces()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div>alpha beta <strong>gamma</strong> delta</div></body></html>");
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("( gamma)");
        text.Should().Contain("( delta)");
    }
}
