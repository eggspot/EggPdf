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
        var sb = new StringBuilder();
        sb.Append("<html><body>");
        for (int i = 0; i < 100; i++)
            sb.Append($"<p>Paragraph {i}: This is some content that takes up space on the page to test pagination.</p>");
        sb.Append("</body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());

        var text = PdfAssert.ValidPdf(pdf);
        PdfAssert.PageCount(text).Should().BeGreaterThan(1, "100 paragraphs don't fit on one A4 page");
    }

    [Fact]
    public async Task PageBreakBefore_ForcesNewPage()
    {
        var html = @"
            <div><p>Page 1 content</p></div>
            <div style='page-break-before: always'><p>Page 2 content</p></div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "Page 1 content", "Page 2 content");
        PdfAssert.PageCount(text).Should().Be(2, "page-break-before: always starts the second div on a new page");
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

        var text = PdfAssert.ValidPdf(pdf);
        text.Should().Contain("/Outlines", "headings produce a bookmark outline");
        text.Should().Contain("/Title (Chapter 1)");
        text.Should().Contain("/Title (Chapter 2)");
    }

    [Fact]
    public async Task InternalLink_ProducesAnnotation()
    {
        var html = @"
            <a href='#section2'>Go to Section 2</a>
            <h2 id='section2'>Section 2</h2>
            <p>Content</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "Go");
        text.Should().Contain("/Subtype /Link", "the internal link becomes a link annotation");
    }

    [Fact]
    public async Task MultiPageDocument_AllPagesHaveContent()
    {
        var sb = new StringBuilder("<html><body>");
        for (int i = 0; i < 50; i++)
            sb.Append($"<p>Line {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>");
        sb.Append("</body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());

        var text = PdfAssert.ValidPdf(pdf);
        for (int i = 0; i < 50; i += 10)
            text.Should().Contain($"Line {i}:", "no line may be lost across page boundaries");
    }

    [Fact]
    public async Task EmptyDocument_SinglePage()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<html><body></body></html>");

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/Count 1");
    }

    [Fact]
    public async Task PageMarginRule_ContentRendersInsideTheMargin()
    {
        var html = @"
            <html><head><style>
                @page { margin: 2cm; }
                body { font-size: 12pt; }
            </style></head>
            <body><p>Content with page margins</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf);
        PdfAssert.TextPosition(text, "Content with page margins").X.Should().BeGreaterThan(56f,
            "the 2cm (56.7pt) page margin plus the body margin push the text in from the sheet's edge");
    }
}
