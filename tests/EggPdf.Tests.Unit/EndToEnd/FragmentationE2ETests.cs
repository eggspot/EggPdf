using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class FragmentationE2ETests
{
    [Fact]
    public async Task BreakInsideAvoid_KeepsElementTogether()
    {
        var html = @"
            <div style='height: 1000px'>Spacer to push card near page bottom</div>
            <div style='break-inside: avoid; height: 200px; background: #eee; border: 1px solid black'>
                <h2>Card Title</h2>
                <p>This card should not be split across pages</p>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Card Title");

        PdfAssert.PageCount(text).Should().Be(2);
        PdfAssert.TextPosition(text, "Card Title").Y.Should().BeGreaterThan(700f,
            "the 200px card would straddle the page break after the 1000px spacer, so break-inside: avoid moves it " +
            "whole to the top of page 2 instead of starting it near the bottom of page 1");
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
        PdfAssert.PageCount(text).Should().Be(2, "page-break-after: always puts the second section on a new page");
    }

    [Fact]
    public async Task BreakInsideAvoid_LargeElement_StillRendered()
    {
        var html = @"
            <div style='break-inside: avoid; height: 2000px; background: #eee'>
                <p>Very tall element that cannot fit on one page</p>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Very tall element", "an element taller than a page can't avoid breaking and still renders");
    }

    [Fact]
    public async Task NoBreakProperties_NormalPagination()
    {
        var html = "<p>Normal content without break properties</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        PdfAssert.PageCount(PdfAssert.ValidPdf(pdf, "Normal content")).Should().Be(1);
    }
}
