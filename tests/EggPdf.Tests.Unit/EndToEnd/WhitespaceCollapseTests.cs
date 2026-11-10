using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class WhitespaceCollapseTests
{
    [Fact]
    public async Task ConsecutiveSpacesTabsNewlines_CollapseToSingleSpace()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>alpha  \t\n  beta      gamma</p></body></html>");
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("(alpha beta gamma)",
            "with white-space: normal, runs of spaces, tabs and newlines collapse to single spaces, and the run's " +
            "word boxes merge into one text operation");
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
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>a&#160;&#160;b   c</p></body></html>");
        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

        var nbsp = (char)0x00A0;
        text.Should().Contain("(a" + nbsp + nbsp + "b c)",
            "non-breaking spaces are rendered content, not collapsible whitespace, while ASCII space runs collapse");
    }
}
