using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace EggPdf.Tests.E2E;

/// <summary>
/// Visual comparison E2E tests following the flow from 08-e2e-visual-testing.md:
///
/// For each test case HTML:
///   1. Render the HTML as a print-preview page via /api/render/print-preview
///   2. Load that page in Playwright and screenshot (browser Print rendering = reference)
///   3. Render the same HTML as PDF via /api/render
///   4. Save PDF to temp file, navigate Playwright to it (Chrome renders the PDF)
///   5. Screenshot the rendered PDF page
///   6. Pixel-diff the two screenshots
///   7. Assert diff &lt; threshold
///
/// This verifies that EggPdf's PDF output visually matches Chrome's print rendering.
/// </summary>
[Collection("E2E")]
public class VisualComparisonFlowTests
{
    private readonly ServiceFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly HttpClient _client = new();

    /// <summary>
    /// Minimum fraction of pixels that must match between browser print preview
    /// and EggPdf PDF rendering. Generous threshold because Chrome's HTML renderer
    /// and PDF viewer (PDFium) use different engines for fonts, anti-aliasing, etc.
    /// As EggPdf improves Chrome Print parity, this threshold should tighten.
    /// </summary>
    private const double MinSimilarity = 0.50;

    /// <summary>
    /// Per-channel tolerance (0-255) for pixel comparison.
    /// Accounts for sub-pixel rendering differences between HTML and PDF.
    /// </summary>
    private const int ChannelTolerance = 40;

    public VisualComparisonFlowTests(ServiceFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    // === Test case HTML snippets (same as /e2e page) ===
    private static readonly (string name, string html)[] TestCases = new[]
    {
        ("heading", "<h1>Hello World</h1><p>This is a paragraph with <strong>bold</strong> and <em>italic</em> text.</p>"),
        ("table", "<table border=\"1\" style=\"width:100%;border-collapse:collapse\"><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody><tr><td>Alpha</td><td>100</td></tr><tr><td>Beta</td><td>200</td></tr></tbody></table>"),
        ("invoice", "<h1>Invoice #001</h1><p>Date: 2024-01-15</p><table border=\"1\" style=\"width:100%;border-collapse:collapse\"><tr><th>Item</th><th>Price</th></tr><tr><td>Widget</td><td>$50</td></tr><tr><td>Gadget</td><td>$75</td></tr></table><p><strong>Total: $125</strong></p>"),
        ("styles", "<div style=\"background-color:#eef;padding:20px;border-radius:8px\"><h2 style=\"color:#6c5ce7\">Styled Box</h2><p style=\"font-size:14px;line-height:1.6\">Text with <span style=\"color:red\">red</span>, <span style=\"color:blue\">blue</span>, and <strong>bold</strong> formatting.</p></div>"),
        ("list", "<h2>Features</h2><ul><li>Item One</li><li>Item Two</li><li>Item Three</li></ul><ol><li>First</li><li>Second</li><li>Third</li></ol>"),
    };

    // ============================================================
    // Core visual comparison: Print Preview vs PDF for each test case
    // ============================================================

    [Theory]
    [InlineData(0)] // heading
    [InlineData(1)] // table
    [InlineData(2)] // invoice
    [InlineData(3)] // styles
    [InlineData(4)] // list
    public async Task PixelCompare_PrintPreviewVsPdf(int testIndex)
    {
        var (name, html) = TestCases[testIndex];
        _output.WriteLine($"Test case: {name}");

        var viewport = new ViewportSize { Width = 800, Height = 600 };

        // Step 1: Get print-preview HTML and screenshot it
        var printHtml = await GetPrintPreviewHtml(html);
        var printPage = await _fixture.Browser!.NewPageAsync(new() { ViewportSize = viewport });
        await printPage.SetContentAsync(printHtml);
        await printPage.WaitForTimeoutAsync(500);
        var printShot = await printPage.ScreenshotAsync();
        await printPage.CloseAsync();

        _output.WriteLine($"  Print preview screenshot: {printShot.Length:N0} bytes");

        // Step 2: Get PDF, serve via HTTP route interception (Chrome renders inline PDFs)
        var pdfBytes = await GetPdfBytes(html);
        pdfBytes.Length.Should().BeGreaterThan(100);

        var pdfShot = await ScreenshotPdfViaRoute(pdfBytes, viewport);

        _output.WriteLine($"  PDF screenshot:           {pdfShot.Length:N0} bytes");

        // Step 3: Pixel-diff
        var (printW, printH, _) = PixelComparer.DecodePng(printShot);
        var (pdfW, pdfH, _) = PixelComparer.DecodePng(pdfShot);
        _output.WriteLine($"  Print dimensions: {printW}x{printH}");
        _output.WriteLine($"  PDF dimensions:   {pdfW}x{pdfH}");

        double similarity = PixelComparer.Compare(printShot, pdfShot, ChannelTolerance);
        _output.WriteLine($"  Pixel similarity: {similarity:P1} (threshold: {MinSimilarity:P0})");

        similarity.Should().BeGreaterOrEqualTo(MinSimilarity,
            $"'{name}' print-vs-PDF pixel similarity {similarity:P1} below threshold {MinSimilarity:P0}");
    }

    // ============================================================
    // WebUI Flow: Type HTML → Print Preview → Generate PDF → Compare
    // ============================================================

    [Fact]
    public async Task WebUI_TypeHtml_PrintPreviewVsPdfPreview()
    {
        var testHtml = "<html><body><h1>Visual Test</h1><p>Comparing browser rendering against EggPdf PDF output.</p></body></html>";
        var viewport = new ViewportSize { Width = 800, Height = 600 };

        // Step 1: Get print-preview rendering via WebUI
        var page = await _fixture.Browser!.NewPageAsync(new() { ViewportSize = new() { Width = 1400, Height = 900 } });
        await page.GotoAsync(_fixture.BaseUrl);

        var editor = page.Locator("#htmlInput");
        await editor.WaitForAsync(new() { Timeout = 5000 });
        await editor.FillAsync(testHtml);
        await page.WaitForTimeoutAsync(800); // debounced preview

        // Verify print preview renders
        var previewFrame = page.Locator("#previewFrame");
        (await previewFrame.IsVisibleAsync()).Should().BeTrue();
        var printShot = await previewFrame.ScreenshotAsync();
        printShot.Length.Should().BeGreaterThan(100, "print preview should render content");

        _output.WriteLine($"Print preview screenshot: {printShot.Length:N0} bytes");
        await page.CloseAsync();

        // Step 2: Render PDF and screenshot it via route interception
        var pdfBytes = await GetPdfBytes(testHtml);
        var pdfShot = await ScreenshotPdfViaRoute(pdfBytes, viewport);

        _output.WriteLine($"PDF screenshot:           {pdfShot.Length:N0} bytes");

        double similarity = PixelComparer.Compare(printShot, pdfShot, ChannelTolerance);
        _output.WriteLine($"Pixel similarity: {similarity:P1}");

        similarity.Should().BeGreaterOrEqualTo(MinSimilarity,
            $"WebUI print-vs-PDF similarity {similarity:P1} below threshold {MinSimilarity:P0}");
    }

    // ============================================================
    // Print Preview consistency: same HTML → same screenshot
    // ============================================================

    [Fact]
    public async Task PrintPreview_SameHtml_ConsistentRendering()
    {
        var html = "<h1>Consistency Test</h1><p>This should render the same way every time.</p>";
        var viewport = new ViewportSize { Width = 800, Height = 600 };

        var printHtml = await GetPrintPreviewHtml(html);

        var page1 = await _fixture.Browser!.NewPageAsync(new() { ViewportSize = viewport });
        var page2 = await _fixture.Browser!.NewPageAsync(new() { ViewportSize = viewport });

        await page1.SetContentAsync(printHtml);
        await page1.WaitForTimeoutAsync(500);
        await page2.SetContentAsync(printHtml);
        await page2.WaitForTimeoutAsync(500);

        var shot1 = await page1.ScreenshotAsync();
        var shot2 = await page2.ScreenshotAsync();

        double similarity = PixelComparer.Compare(shot1, shot2, 5);
        _output.WriteLine($"Print preview consistency: {similarity:P1}");

        similarity.Should().BeGreaterOrEqualTo(0.99, "same HTML should produce identical print preview");

        await page1.CloseAsync();
        await page2.CloseAsync();
    }

    // ============================================================
    // E2E Page: run all 5 test cases, verify both frames rendered
    // ============================================================

    [Fact]
    public async Task E2EPage_RunAll_BothFramesRender()
    {
        var page = await _fixture.Browser!.NewPageAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 1200, Height = 800 }
        });

        await page.GotoAsync($"{_fixture.BaseUrl}/e2e");
        await page.Locator("#result .pass").WaitForAsync(new() { Timeout = 15000 });

        foreach (var (name, _) in TestCases)
        {
            await page.Locator("#testCase").SelectOptionAsync(name);
            await page.WaitForFunctionAsync(
                $"() => document.getElementById('result').textContent.includes('{name}')",
                null, new() { Timeout = 15000 });
            await page.WaitForTimeoutAsync(500);

            // Verify browser frame has content (non-empty srcdoc)
            var srcdoc = await page.Locator("#browserFrame").GetAttributeAsync("srcdoc");
            srcdoc.Should().NotBeNullOrEmpty($"browser frame for '{name}' should have srcdoc");

            // Verify PDF frame has blob URL (PDF was rendered)
            var pdfSrc = await page.Locator("#pdfFrame").GetAttributeAsync("src");
            pdfSrc.Should().Contain("blob:", $"PDF frame for '{name}' should have blob URL");

            _output.WriteLine($"[{name}] browser frame has content, PDF frame has blob URL");
        }

        await page.CloseAsync();
    }

    // ============================================================
    // Helpers
    // ============================================================

    /// <summary>
    /// Renders a PDF in headless Chrome using pdf.js to draw onto a canvas element.
    /// Chrome headless cannot render PDFs directly (triggers download), so we use
    /// pdf.js (Mozilla's PDF renderer) to render the first page to a canvas,
    /// then screenshot the canvas.
    /// </summary>
    private async Task<byte[]> ScreenshotPdfViaRoute(byte[] pdfBytes, ViewportSize viewport)
    {
        var pdfPage = await _fixture.Browser!.NewPageAsync(new() { ViewportSize = viewport });

        // HTML page with pdf.js that renders PDF to a <canvas>
        var pdfViewerHtml = @"<!DOCTYPE html>
<html><head>
<script src='https://cdnjs.cloudflare.com/ajax/libs/pdf.js/4.0.379/pdf.min.mjs' type='module'></script>
</head>
<body style='margin:0;background:white'>
<canvas id='pdfCanvas'></canvas>
<script type='module'>
import * as pdfjsLib from 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/4.0.379/pdf.min.mjs';
pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/4.0.379/pdf.worker.min.mjs';
window.renderPdf = async function(base64Data) {
  const raw = atob(base64Data);
  const arr = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) arr[i] = raw.charCodeAt(i);
  const pdf = await pdfjsLib.getDocument({data: arr}).promise;
  const page = await pdf.getPage(1);
  const vp = page.getViewport({scale: 1.0});
  const canvas = document.getElementById('pdfCanvas');
  canvas.width = vp.width;
  canvas.height = vp.height;
  await page.render({canvasContext: canvas.getContext('2d'), viewport: vp}).promise;
  window._pdfRendered = true;
};
</script>
</body></html>";

        await pdfPage.SetContentAsync(pdfViewerHtml);
        await pdfPage.WaitForTimeoutAsync(2000); // Let pdf.js load from CDN

        // Pass PDF data and render
        var base64 = Convert.ToBase64String(pdfBytes);
        await pdfPage.EvaluateAsync($"window.renderPdf('{base64}')");

        // Wait for rendering to complete
        await pdfPage.WaitForFunctionAsync("() => window._pdfRendered === true",
            null, new() { Timeout = 15000 });
        await pdfPage.WaitForTimeoutAsync(500);

        // Screenshot the canvas
        var screenshot = await pdfPage.Locator("#pdfCanvas").ScreenshotAsync();
        await pdfPage.CloseAsync();
        return screenshot;
    }

    private async Task<string> GetPrintPreviewHtml(string html)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render/print-preview", content);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<byte[]> GetPdfBytes(string html)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html }),
            Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        return await resp.Content.ReadAsByteArrayAsync();
    }
}
