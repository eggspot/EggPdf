using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.E2E;

/// <summary>
/// Tests for the REST API endpoints via actual HTTP requests.
/// </summary>
[Collection("E2E")]
public class ApiEndpointTests
{
    private readonly ServiceFixture _fixture;
    private readonly HttpClient _client = new();

    public ApiEndpointTests(ServiceFixture fixture) { _fixture = fixture; }

    [Fact]
    public async Task Health_Returns200()
    {
        var resp = await _client.GetAsync($"{_fixture.BaseUrl}/health");

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("healthy");
        body.Should().Contain("version");
    }

    [Fact]
    public async Task ApiInfo_ReturnsFeatures()
    {
        var resp = await _client.GetAsync($"{_fixture.BaseUrl}/api/info");

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("EggPdf");
        body.Should().Contain("features");
    }

    [Fact]
    public async Task ApiFonts_ReturnsFontList()
    {
        var resp = await _client.GetAsync($"{_fixture.BaseUrl}/api/fonts");

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Helvetica");
    }

    [Fact]
    public async Task Render_ValidHtml_ReturnsPdf()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html = "<h1>Test</h1>" }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");

        // Check response headers
        resp.Headers.Should().ContainKey("X-EggPdf-Duration-Ms");
        resp.Headers.Should().ContainKey("X-EggPdf-Size");
    }

    [Fact]
    public async Task Render_EmptyHtml_ReturnsBadRequest()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html = "" }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Render_ComplexHtml_ReturnsPdf()
    {
        var html = @"<html><head><style>
            body { font-family: Arial; }
            table { width: 100%; border-collapse: collapse; }
            th, td { border: 1px solid #ddd; padding: 8px; }
        </style></head><body>
            <h1>Invoice</h1>
            <table><tr><th>Item</th><th>Price</th></tr>
            <tr><td>Widget</td><td>$50</td></tr></table>
        </body></html>";

        var content = new StringContent(
            JsonSerializer.Serialize(new { html }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var pdfText = PdfTextDecoder.Decode(bytes);
        pdfText.Should().Contain("Invoice");
        pdfText.Should().Contain("Widget");
    }

    [Fact]
    public async Task Encrypt_ReturnsEncryptedPdf()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                html = "<html><body><p>Confidential body text</p></body></html>",
                ownerPassword = "owner-secret",
                allowCopying = false,
            }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/encrypt", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");

        var raw = Encoding.Latin1.GetString(bytes);
        raw.Should().Contain("/Encrypt <<", "the response must actually be encrypted");
        raw.Should().NotContain("Confidential body text",
            "an encrypted PDF must not leak plaintext content");
    }

    [Fact]
    public async Task PrintPreview_ReturnsHtml()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html = "<h1>Preview Test</h1>" }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render/print-preview", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Preview Test");
        body.Should().Contain("@page");
    }

    [Fact]
    public async Task E2EPage_LoadsSuccessfully()
    {
        var resp = await _client.GetAsync($"{_fixture.BaseUrl}/e2e");

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("E2E Visual Comparison");
        body.Should().Contain("browserFrame");
        body.Should().Contain("pdfFrame");
    }

    [Fact]
    public async Task RenderImage_ValidHtml_ReturnsPdf()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html = "<h1>Image render</h1>" }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render/image", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task RenderImage_EmptyHtml_ReturnsBadRequest()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html = "" }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render/image", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StaticFiles_IndexHtmlServed()
    {
        var resp = await _client.GetAsync($"{_fixture.BaseUrl}/");

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("EggPdf");
        body.Should().Contain("htmlInput");
    }
}
