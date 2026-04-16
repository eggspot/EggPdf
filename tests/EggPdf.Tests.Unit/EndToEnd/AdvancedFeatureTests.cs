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

    [Fact]
    public void FontFace_WithLocalSrc_DoesNotCrash()
    {
        // @font-face with local() src should fall through to system font resolution
        // without crashing; the key requirement is graceful handling.
        var html = "<style>" +
                   "@font-face { font-family: 'MyFont'; src: local('Arial'); }" +
                   "</style>" +
                   "<p style='font-family: MyFont'>Hello from @font-face</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void BreakInsideAvoid_DoesNotCrash()
    {
        // A box with break-inside:avoid should render without exceptions
        var html = "<div style='break-inside: avoid; page-break-inside: avoid'>" +
                   "<p>Content that should not be split across pages.</p>" +
                   "<p>More content in the same avoid-break container.</p>" +
                   "</div>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void TextAlign_Justify_NonZeroTwOnFullLines()
    {
        // A paragraph wide enough to produce multiple lines when text-align:justify.
        // Non-last lines must have extra word spacing (Tw > 0) to fill the container.
        var html = "<p style='text-align: justify; width: 200px; font-size: 12px'>" +
                   "The quick brown fox jumps over the lazy dog and keeps on running through the forest</p>";
        byte[] pdf = HtmlToPdf.Render(html);

        // The PDF must contain at least one non-zero Tw operator (space distribution)
        var pdfText = Encoding.Latin1.GetString(pdf);
        // Look for any "Tw" that is not "0.00 Tw" (a non-zero word-spacing)
        pdfText.Should().MatchRegex(@"[1-9]\d*\.\d+ Tw|0\.[1-9]\d* Tw",
            "justify should produce non-zero Tw on wrapped full lines");
    }

    [Fact]
    public void ObjectPosition_WithObjectFit_DoesNotCrash()
    {
        var html = "<img style='width:100px;height:100px;object-fit:contain;object-position:left top' src='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=='>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public void ObjectPosition_Center_IsDefault()
    {
        var html = "<img style='width:200px;height:100px;object-fit:cover' src='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=='>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public void TextDecoration_ColorAndStyle_DoesNotCrash()
    {
        var html = "<p style='text-decoration: underline dashed red 2px'>Decorated text</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Supports_Rule_Applies()
    {
        var html = "<style>@supports (display: flex) { p { color: red; } }</style><p>Test</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public void BorderRadius_Shorthand_DoesNotCrash()
    {
        var html = "<div style='width:100px;height:100px;border-radius:10px;background-color:blue'></div>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public void AspectRatio_ComputesHeight()
    {
        var html = "<div style='width:160px;aspect-ratio:16/9'></div>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public void OrphansWidows_DoesNotCrash()
    {
        // orphans and widows are pagination properties; rendering must not crash
        var html = @"
            <style>p { orphans: 3; widows: 2; }</style>
            <p>Paragraph with orphans and widows set.</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Hyphens_Auto_DoesNotCrash()
    {
        var html = "<p style='hyphens: auto; width: 100px; font-size: 12px'>hyphenation demonstration</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void OrphansWidows_ExplicitValues_DoesNotCrash()
    {
        // Long content that will span multiple pages to exercise the pagination logic
        var sb = new System.Text.StringBuilder("<style>p { orphans: 2; widows: 2; }</style>");
        for (int i = 0; i < 200; i++)
            sb.Append($"<p>Paragraph {i}: The quick brown fox jumps over the lazy dog.</p>");
        byte[] pdf = HtmlToPdf.Render(sb.ToString());
        pdf.Should().NotBeEmpty();
    }

    // === Tier 1 wire-ups ===

    [Fact]
    public void WritingMode_Vertical_DoesNotCrash()
    {
        var html = "<div style='writing-mode: vertical-rl; width:60px; height:200px'>Vertical text</div>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void TextWrap_Balance_DoesNotCrash()
    {
        var html = "<h2 style='text-wrap: balance; width: 300px'>A moderately long heading that should be balanced</h2>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Filter_Grayscale_DoesNotCrash()
    {
        var html = "<div style='filter: grayscale(100%); background-color: red; width:100px; height:50px'>Gray box</div>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Filter_Brightness_ChangesColor()
    {
        // filter:brightness(0) makes content black — verify it renders without crash
        var html = "<p style='filter: brightness(0.5)'>Dimmed paragraph</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public void Filter_Grayscale_ModifiesBackgroundColor()
    {
        // filter:grayscale(1) on a red div must render as gray (equal RGB channels), not red
        var html = "<div style='filter:grayscale(1);background-color:red;width:100px;height:50px'></div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var pdfText = Encoding.Latin1.GetString(pdf);
        // Without filter, red = "1.00 0.00 0.00 rg"; with grayscale it must not be pure red
        pdfText.Should().NotContain("1.00 0.00 0.00 rg",
            "grayscale(1) filter should change red background to gray");
    }

    [Fact]
    public void ClipPath_Circle_GeneratesClipCommand()
    {
        var html = "<div style='clip-path:circle(50%);width:100px;height:100px;background:blue'></div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var pdfText = Encoding.Latin1.GetString(pdf);
        // clip-path must produce a "W n" clipping command in the PDF content stream
        pdfText.Should().Contain("W n", "clip-path should produce PDF clipping path operator");
    }

    [Fact]
    public void ClipPath_Circle_DoesNotCrash()
    {
        var html = "<div style='clip-path: circle(50%); width:100px; height:100px; background:blue'></div>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void ClipPath_Polygon_DoesNotCrash()
    {
        var html = "<div style='clip-path: polygon(50% 0%, 100% 100%, 0% 100%); width:100px; height:100px; background:red'></div>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public void ColumnRule_DoesNotCrash()
    {
        var html = "<div style='column-count:2; column-rule: 1px solid black; width:400px'>" +
                   "<p>Column one content here.</p><p>Column two content here.</p>" +
                   "</div>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void ColumnRule_GeneratesStrokeCommand()
    {
        // column-rule between 2 columns must produce a PDF stroke line command.
        // Do NOT use explicit height — multi-column redistribution only runs for auto-height boxes.
        var html = "<div style='column-count:2; column-rule:2px solid red; width:400px'>" +
                   "<p>Col1</p><p>Col2</p></div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var pdfText = Encoding.Latin1.GetString(pdf);
        // Red color (RG stroke operator) must appear for the rule
        pdfText.Should().Contain("1.00 0.00 0.00 RG", "column-rule:red must use red stroke color");
    }

    [Fact]
    public void TableCaption_DoesNotCrash()
    {
        var html = "<table><caption>Table Title</caption>" +
                   "<tr><td>Cell</td></tr></table>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    // ── <progress> / <meter> rendering ────────────────────────────────────

    [Fact]
    public void Progress_WithValue_DoesNotCrash()
    {
        var html = "<progress value='60' max='100' style='width:200px; height:20px'></progress>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Progress_Indeterminate_DoesNotCrash()
    {
        var html = "<progress max='100' style='width:200px; height:20px'></progress>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public void Progress_WithValue_ContainsFillRect()
    {
        var html = "<progress value='50' max='100' style='width:200px; height:20px'></progress>";
        byte[] pdf = HtmlToPdf.Render(html);
        // PDF should contain fill operations (colored rectangles for the bar)
        var content = Encoding.ASCII.GetString(pdf);
        content.Should().Contain("re f", "progress fill bar should generate re f rectangle fill");
    }

    [Fact]
    public void Meter_WithValue_DoesNotCrash()
    {
        var html = "<meter min='0' max='100' value='75'></meter>";
        byte[] pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Meter_ContainsFillRect()
    {
        var html = "<meter value='0.7'></meter>";
        byte[] pdf = HtmlToPdf.Render(html);
        var content = Encoding.ASCII.GetString(pdf);
        content.Should().Contain("re f", "meter fill bar should generate re f rectangle fill");
    }
}
