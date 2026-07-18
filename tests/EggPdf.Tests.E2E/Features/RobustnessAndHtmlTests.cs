using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.E2E.Features;

/// <summary>
/// E2E tests for HTML elements, PDF compliance, and robustness through the HTTP API.
/// Covers: semantic elements, form elements, malformed HTML, large/deep docs,
/// unicode/CJK/emoji, PDF version/trailer, complete document templates.
/// </summary>
[Collection("E2E")]
public class RobustnessAndHtmlTests
{
    private readonly ServiceFixture _fixture;
    private readonly HttpClient _client = new();

    public RobustnessAndHtmlTests(ServiceFixture fixture) { _fixture = fixture; }

    private async Task<string> RenderPdf(string html)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        return PdfTextDecoder.DecodeWithText(bytes);
    }

    private async Task AssertRenderDoesNotCrash(string html)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    // === Semantic HTML Elements ===

    [Fact]
    public async Task SemanticElements_AllRendered()
    {
        var text = await RenderPdf(@"
            <header><h1>Header</h1></header>
            <nav><a href='#'>Nav Link</a></nav>
            <main>
                <article><h2>Article</h2><p>Article content</p></article>
                <section><h2>Section</h2><p>Section content</p></section>
                <aside><p>Sidebar</p></aside>
            </main>
            <footer><p>Footer</p></footer>");
        text.Should().Contain("Header");
        text.Should().Contain("Nav Link");
        text.Should().Contain("Article");
        text.Should().Contain("Section content");
        text.Should().Contain("Sidebar");
        text.Should().Contain("Footer");
    }

    [Fact]
    public async Task FigureElement()
    {
        var text = await RenderPdf(@"
            <figure>
                <div style='width: 200px; height: 100px; background-color: #eee'></div>
                <figcaption>Figure 1: A diagram</figcaption>
            </figure>");
        text.Should().Contain("Figure 1: A diagram");
    }

    [Fact]
    public async Task BlockquoteElement()
    {
        var text = await RenderPdf("<blockquote>This is a quote.</blockquote>");
        text.Should().Contain("This is a quote");
    }

    [Fact]
    public async Task CodeAndKbdElements()
    {
        var text = await RenderPdf("<p>Use <code>console.log()</code> or press <kbd>Ctrl+C</kbd></p>");
        text.Should().Contain("console.log");
        text.Should().Contain("Ctrl+C");
    }

    [Fact]
    public async Task MarkAndSmallElements()
    {
        var text = await RenderPdf("<p><mark>Highlighted</mark> and <small>Small text</small></p>");
        text.Should().Contain("Highlighted");
        text.Should().Contain("Small text");
    }

    [Fact]
    public async Task DeprecatedElements_CenterFontUS()
    {
        var text = await RenderPdf(@"
            <center>Centered text</center>
            <font color='red' face='serif'>Font tag</font>
            <u>Underlined via u</u>
            <s>Strikethrough via s</s>
            <del>Deleted text</del>");
        text.Should().Contain("Centered text");
        text.Should().Contain("Font tag");
        text.Should().Contain("Underlined via u");
        text.Should().Contain("Strikethrough via s");
        text.Should().Contain("Deleted text");
    }

    [Fact]
    public async Task PreElement_PreservesFormatting()
    {
        var text = await RenderPdf("<pre>  indented\n    more indent</pre>");
        text.Should().Contain("indented");
        text.Should().Contain("more indent");
    }

    [Fact]
    public async Task DetailsAndSummary()
    {
        var text = await RenderPdf("<details><summary>Click to expand</summary><p>Hidden content</p></details>");
        text.Should().Contain("Click to expand");
    }

    // === Presentational HTML Attributes ===

    [Fact]
    public async Task BgcolorAttribute()
    {
        var text = await RenderPdf("<table><tr><td bgcolor='yellow'>Yellow cell</td></tr></table>");
        text.Should().Contain("Yellow cell");
    }

    [Fact]
    public async Task AlignAttribute_Center()
    {
        var text = await RenderPdf("<p align='center'>Center aligned</p>");
        text.Should().Contain("Center aligned");
    }

    [Fact]
    public async Task FontElement_ColorAndFace()
    {
        var text = await RenderPdf("<font color='red' face='serif'>Styled font tag</font>");
        text.Should().Contain("Styled font tag");
        text.Should().Contain("Times");
    }

    // === Form Elements ===

    [Fact]
    public async Task FormElements_RenderWithoutCrash()
    {
        await AssertRenderDoesNotCrash(@"
            <form>
                <input type='text' value='John Doe'>
                <input type='checkbox' checked>
                <select><option selected>Option 1</option></select>
                <textarea>Notes here</textarea>
                <button>Submit</button>
            </form>");
    }

    // === SVG ===

    [Fact]
    public async Task SvgInline_DoesNotCrash()
    {
        await AssertRenderDoesNotCrash(@"
            <svg width='100' height='100'>
                <circle cx='50' cy='50' r='40' fill='red'/>
            </svg>");
    }

    // === Gradient ===

    [Fact]
    public async Task GradientBackground_DoesNotCrash()
    {
        await AssertRenderDoesNotCrash("<div style='background: linear-gradient(red, blue); width: 200px; height: 100px'>Gradient</div>");
    }

    // === PDF Compliance ===

    [Fact]
    public async Task PdfHeader_Version17()
    {
        var text = await RenderPdf("<p>Version check</p>");
        text.Should().StartWith("%PDF-1.7");
    }

    [Fact]
    public async Task PdfTrailer_HasEof()
    {
        var text = await RenderPdf("<p>EOF check</p>");
        text.Should().Contain("%%EOF");
    }

    [Fact]
    public async Task PdfHasProducer()
    {
        var text = await RenderPdf("<p>Producer check</p>");
        text.Should().Contain("EggPdf");
    }

    // === Robustness ===

    [Fact]
    public async Task MalformedHtml_ProducesValidPdf()
    {
        var text = await RenderPdf("<p>Unclosed paragraph<div>Misnested<b><i></b></i>tags</div><<>>");
        text.Should().StartWith("%PDF-1.7");
    }

    [Fact]
    public async Task VeryLargeHtml_DoesNotCrash()
    {
        var sb = new StringBuilder("<html><body>");
        for (int i = 0; i < 500; i++)
            sb.Append($"<div><p>Section {i} with content.</p></div>");
        sb.Append("</body></html>");
        await AssertRenderDoesNotCrash(sb.ToString());
    }

    [Fact]
    public async Task DeeplyNested_DoesNotCrash()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 50; i++) sb.Append("<div>");
        sb.Append("Deep content");
        for (int i = 0; i < 50; i++) sb.Append("</div>");
        await AssertRenderDoesNotCrash(sb.ToString());
    }

    [Fact]
    public async Task LargeTable_DoesNotCrash()
    {
        var sb = new StringBuilder("<table><thead><tr><th>ID</th><th>Name</th><th>Value</th></tr></thead><tbody>");
        for (int i = 0; i < 200; i++)
            sb.Append($"<tr><td>{i}</td><td>Item {i}</td><td>${i * 10}.00</td></tr>");
        sb.Append("</tbody></table>");
        await AssertRenderDoesNotCrash(sb.ToString());
    }

    [Fact]
    public async Task Unicode_DoesNotCrash()
    {
        await AssertRenderDoesNotCrash("<p>Vietnamese: Xin chào</p><p>Thai: สวัสดี</p>");
    }

    [Fact]
    public async Task CjkText_DoesNotCrash()
    {
        await AssertRenderDoesNotCrash("<p>Chinese: 你好世界</p>");
    }

    [Fact]
    public async Task Emoji_DoesNotCrash()
    {
        await AssertRenderDoesNotCrash("<p>Hello World 🌍🎉</p>");
    }

    [Fact]
    public async Task CssNesting_DoesNotCrash()
    {
        await AssertRenderDoesNotCrash("<style>div { & p { color: red; } }</style><div><p>Nested CSS</p></div>");
    }

    [Fact]
    public async Task ContainerQuery_DoesNotCrash()
    {
        await AssertRenderDoesNotCrash("<style>@container (min-width: 300px) { p { color: blue; } }</style><p>Container query</p>");
    }

    [Fact]
    public async Task MultiColumn_DoesNotCrash()
    {
        await AssertRenderDoesNotCrash("<div style='column-count: 2; column-gap: 20px'><p>Column content.</p></div>");
    }

    [Fact]
    public async Task TextShadow_DoesNotCrash()
    {
        await AssertRenderDoesNotCrash("<h1 style='text-shadow: 2px 2px 4px #000'>Shadow text</h1>");
    }

    // === Complete Document Template ===

    [Fact]
    public async Task FullInvoiceDocument_AllElementsRendered()
    {
        var text = await RenderPdf(@"
            <html><head><style>
                body { font-family: Arial, sans-serif; }
                h1 { color: #333; }
                table { width: 100%; border-collapse: collapse; }
                th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
                th { background-color: #4CAF50; color: white; }
                .total { font-weight: bold; font-size: 18px; }
                @page { margin: 2cm; }
            </style></head><body>
                <h1>Invoice #2024-001</h1>
                <p>Date: 2024-01-15 | Customer: Acme Corp</p>
                <table>
                    <thead><tr><th>Item</th><th>Qty</th><th>Price</th><th>Total</th></tr></thead>
                    <tbody>
                        <tr><td>Widget A</td><td>10</td><td>$5.00</td><td>$50.00</td></tr>
                        <tr><td>Widget B</td><td>5</td><td>$12.00</td><td>$60.00</td></tr>
                        <tr><td>Service Fee</td><td>1</td><td>$25.00</td><td>$25.00</td></tr>
                    </tbody>
                </table>
                <p class='total'>Total: $135.00</p>
                <p><a href='https://example.com/pay'>Pay Now</a></p>
            </body></html>");

        text.Should().Contain("Invoice");
        text.Should().Contain("Widget A");
        text.Should().Contain("$135.00");
        text.Should().Contain("https://example.com/pay");
    }

    [Fact]
    public async Task CompleteReport_AllElements()
    {
        var text = await RenderPdf(@"<html><head><style>
                body { font-family: Arial; }
                h1 { color: navy; }
                .info { background-color: #f0f0f0; padding: 10px; }
                table { width: 100%; border-collapse: collapse; }
                td, th { border: 1px solid #ddd; padding: 5px; }
            </style></head><body>
                <h1>Report Title</h1>
                <div class='info'><p>Generated on 2024-01-15</p></div>
                <table><tr><th>Metric</th><th>Value</th></tr>
                <tr><td>Revenue</td><td>$1.2M</td></tr></table>
                <p><strong>Conclusion:</strong> Results are positive.</p>
                <p><a href='https://example.com'>Full report</a></p>
            </body></html>");

        text.Should().Contain("Report Title");
        text.Should().Contain("Generated on");
        text.Should().Contain("Revenue");
        text.Should().Contain("$1.2M");
        text.Should().Contain("Conclusion");
        text.Should().Contain("https://example.com");
    }

    // === Response Headers ===

    [Fact]
    public async Task RenderResponse_HasDurationHeader()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html = "<h1>Header check</h1>" }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);
        resp.Headers.Should().ContainKey("X-EggPdf-Duration-Ms");
        resp.Headers.Should().ContainKey("X-EggPdf-Size");
    }

    [Fact]
    public async Task RenderResponse_ContentTypePdf()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html = "<h1>Content type</h1>" }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    }
}
