using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class FragmentationE2ETests
{
    [Fact]
    public async Task BreakInsideAvoid_KeepsElementTogether()
    {
        // Create a document where a card would normally be split across pages
        var html = @"
            <div style='height: 780px'>Spacer to push card near page bottom</div>
            <div style='break-inside: avoid; height: 200px; background: #eee; border: 1px solid black'>
                <h2>Card Title</h2>
                <p>This card should not be split across pages</p>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Card Title");
        // Should produce at least 2 pages
        int pageCount = Regex.Matches(text, @"/Type /Page\b").Count;
        pageCount.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task PageBreakBefore_ForcesNewPage()
    {
        var html = @"
            <h1>Page 1</h1>
            <div style='page-break-before: always'>
                <h1>Page 2</h1>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Page 1");
        text.Should().Contain("Page 2");
        int pageCount = Regex.Matches(text, @"/Type /Page\b").Count;
        pageCount.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task PageBreakAfter_ForcesNewPage()
    {
        var html = @"
            <div style='page-break-after: always'>
                <h1>First Section</h1>
            </div>
            <h1>Second Section</h1>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("First Section");
        text.Should().Contain("Second Section");
    }

    [Fact]
    public async Task BreakInsideAvoid_LargeElement_StillRendered()
    {
        // Element too large to fit on one page - should render anyway
        var html = @"
            <div style='break-inside: avoid; height: 2000px; background: #eee'>
                <p>Very tall element that cannot fit on one page</p>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Very tall element");
    }

    [Fact]
    public async Task NoBreakProperties_NormalPagination()
    {
        var html = "<p>Normal content without break properties</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
    }
}
