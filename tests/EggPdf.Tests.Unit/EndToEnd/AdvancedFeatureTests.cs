using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class AdvancedFeatureTests
{
    // === Phase 12: Ecosystem features ===

    [Fact]
    public void SyncApi_Works()
    {
        byte[] pdf = HtmlToPdf.Render("<h1>Sync API</h1>");
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task StreamApi_Works()
    {
        using var ms = new System.IO.MemoryStream();
        await HtmlToPdf.RenderAsync("<h1>Stream API</h1>", ms);
        ms.Length.Should().BeGreaterThan(100);
    }

    [Fact]
    public async Task FileApi_Works()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"eggpdf_{System.Guid.NewGuid():N}.pdf");
        try
        {
            await HtmlToPdf.RenderToFileAsync("<h1>File API</h1>", path);
            System.IO.File.Exists(path).Should().BeTrue();
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    // === Phase 13: Business PDF features ===

    [Fact]
    public async Task FormElements_RenderWithoutCrash()
    {
        var html = @"
            <form>
                <input type='text' value='John Doe'>
                <input type='checkbox' checked>
                <select><option selected>Option 1</option></select>
                <textarea>Notes here</textarea>
                <button>Submit</button>
            </form>";

        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LargeTable_DoesNotCrash()
    {
        var sb = new StringBuilder("<table>");
        sb.Append("<thead><tr><th>ID</th><th>Name</th><th>Value</th></tr></thead><tbody>");
        for (int i = 0; i < 500; i++)
            sb.Append($"<tr><td>{i}</td><td>Item {i}</td><td>${i * 10}.00</td></tr>");
        sb.Append("</tbody></table>");

        var act = async () => await HtmlToPdf.RenderAsync(sb.ToString());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DocumentWithExternalLink_LinkInPdf()
    {
        var html = "<p>Visit <a href='https://github.com/eggspot/EggPdf'>EggPdf</a></p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("https://github.com/eggspot/EggPdf");
    }

    // === Phase 14: Compliance ===

    [Fact]
    public async Task PdfHeader_Version17()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<p>Version check</p>");
        Encoding.ASCII.GetString(pdf, 0, 8).Should().StartWith("%PDF-1.7");
    }

    [Fact]
    public async Task PdfTrailer_HasEof()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<p>EOF check</p>");
        var tail = Encoding.ASCII.GetString(pdf, pdf.Length - 10, 10);
        tail.Should().Contain("%%EOF");
    }

    [Fact]
    public async Task PdfHasProducer()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<p>Producer check</p>");
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("EggPdf");
    }

    // === Cross-cutting: Robustness ===

    [Fact]
    public async Task CancellationToken_Respected()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel(); // immediately cancelled

        var act = async () => await HtmlToPdf.RenderAsync("<p>Should cancel</p>", cts.Token);
        await act.Should().ThrowAsync<System.OperationCanceledException>();
    }

    [Fact]
    public async Task VeryLargeHtml_DoesNotCrash()
    {
        var sb = new StringBuilder("<html><body>");
        for (int i = 0; i < 1000; i++)
            sb.Append($"<div><p>Section {i} with content.</p></div>");
        sb.Append("</body></html>");

        var act = async () => await HtmlToPdf.RenderAsync(sb.ToString());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeeplyNestedHtml_DoesNotCrash()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 50; i++) sb.Append("<div>");
        sb.Append("Deep content");
        for (int i = 0; i < 50; i++) sb.Append("</div>");

        var act = async () => await HtmlToPdf.RenderAsync(sb.ToString());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MalformedHtml_ProducesValidPdf()
    {
        var html = "<p>Unclosed paragraph<div>Misnested<b><i></b></i>tags</div><<>>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task UnicodeContent_DoesNotCrash()
    {
        var html = "<p>Vietnamese: Xin chào thế giới</p><p>Thai: สวัสดีชาวโลก</p><p>Arabic: مرحبا بالعالم</p>";

        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FullInvoiceTemplate_ProducesValidPdf()
    {
        var html = @"
            <html>
            <head>
                <style>
                    body { font-family: Arial, sans-serif; }
                    h1 { color: #333; }
                    table { width: 100%; border-collapse: collapse; }
                    th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
                    th { background-color: #4CAF50; color: white; }
                    .total { font-weight: bold; font-size: 18px; }
                    @page { margin: 2cm; }
                </style>
            </head>
            <body>
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
            </body>
            </html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Invoice");
        text.Should().Contain("Widget A");
        text.Should().Contain("$135.00");
        text.Should().Contain("https://example.com/pay");
    }
}
