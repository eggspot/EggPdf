using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Runs of spaces, tabs, and newlines inside a text run must collapse to a
/// single space (CSS white-space: normal). Guards the single-pass rewrite of
/// the old Replace-in-a-loop collapse in BlockLayout.
/// </summary>
public class WhitespaceCollapseTests
{
    [Fact]
    public async Task ConsecutiveSpacesTabsNewlines_CollapseToSingleSpace()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>alpha  \t\n  beta      gamma</p></body></html>");
        var text = Encoding.ASCII.GetString(pdf);

        // Adjacent word boxes of one run merge into a single text op
        text.Should().Contain("(alpha beta gamma)",
            "runs of mixed whitespace collapse to single spaces");
    }

    [Fact]
    public async Task LeadingAndTrailingWhitespace_IsTrimmedFromTheRun()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>\n\t  hello  \t\n</p></body></html>");
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("(hello)", "edge whitespace of a block's text is trimmed");
        text.Should().NotContain("( hello)");
    }

    [Fact]
    public async Task InteriorNbsp_IsPreservedNotCollapsed()
    {
        // NBSP is rendered content, not collapsible whitespace: "a&nbsp;&nbsp;b"
        // keeps both NBSPs while surrounding ASCII spaces still collapse.
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>a&#160;&#160;b   c</p></body></html>");
        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

        var nbsp = (char)0x00A0; // named char keeps this source file pure ASCII
        text.Should().Contain("(a" + nbsp + nbsp + "b c)",
            "interior non-breaking spaces are content while ASCII space runs collapse");
    }
}
