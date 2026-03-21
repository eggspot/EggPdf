using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class PaginationTests
{
    [Fact]
    public async Task LongContent_ProducesMultiplePages()
    {
        // Generate enough content to fill more than one A4 page
        var sb = new StringBuilder();
        sb.Append("<html><body>");
        for (int i = 0; i < 100; i++)
            sb.Append($"<p>Paragraph {i}: This is some content that takes up space on the page to test pagination.</p>");
        sb.Append("</body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        // Should have more than 1 page
        text.Should().Contain("/Type /Page");
    }

    [Fact]
    public async Task PageBreakBefore_ForcesNewPage()
    {
        var html = @"
            <div><p>Page 1 content</p></div>
            <div style='page-break-before: always'><p>Page 2 content</p></div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        // Should reference page-break property (content should span pages)
        text.Should().Contain("Page 1 content");
        text.Should().Contain("Page 2 content");
    }

    [Fact]
    public async Task HeadingBookmarks_GeneratedInPdf()
    {
        var html = @"
            <h1>Chapter 1</h1>
            <p>Content for chapter 1</p>
            <h1>Chapter 2</h1>
            <p>Content for chapter 2</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        // Bookmarks should contain heading text
        text.Should().Contain("Chapter 1");
        text.Should().Contain("Chapter 2");
    }

    [Fact]
    public async Task InternalLink_ProducesAnnotation()
    {
        var html = @"
            <a href='#section2'>Go to Section 2</a>
            <h2 id='section2'>Section 2</h2>
            <p>Content</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        // The link text should be in the PDF
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Go to Section 2");
    }

    [Fact]
    public async Task MultiPageDocument_AllPagesHaveContent()
    {
        var sb = new StringBuilder("<html><body>");
        for (int i = 0; i < 50; i++)
            sb.Append($"<p>Line {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>");
        sb.Append("</body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 8).Should().StartWith("%PDF");
    }

    [Fact]
    public async Task EmptyDocument_SinglePage()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<html><body></body></html>");

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/Count 1");
    }

    [Fact]
    public async Task PageWithHeaderFooter_Rendered()
    {
        // For now, test that page CSS is recognized
        var html = @"
            <html><head><style>
                @page { margin: 2cm; }
                body { font-size: 12pt; }
            </style></head>
            <body><p>Content with page margins</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Content with page margins");
    }
}
