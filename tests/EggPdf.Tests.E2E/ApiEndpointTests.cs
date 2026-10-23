using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.E2E;

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

        resp.Headers.Should().ContainKey("X-EggPdf-Duration-Ms");
        resp.Headers.Should().ContainKey("X-EggPdf-Size");
        resp.Headers.Should().NotContainKey("X-EggPdf-Warnings", "an ordinary document raises no warnings");
        resp.Headers.GetValues("X-EggPdf-Page-Count").Should().ContainSingle().Which.Should().Be("1");
    }

    [Fact]
    public async Task Render_MultiPageHtml_ReportsPageCountHeader()
    {
        var html = "<div style=\"page-break-after:always\">one</div><div style=\"page-break-after:always\">two</div><div>three</div>";
        var content = new StringContent(JsonSerializer.Serialize(new { html }), Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        resp.Headers.GetValues("X-EggPdf-Page-Count").Should().ContainSingle().Which.Should().Be("3");
    }

    [Fact]
    public async Task Render_CharacterNoFontCovers_ReportsGlyphMissingWarningHeader()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html = "<p style=\"font-family:Arial\">Ref \U0010FFFD</p>" }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        resp.Headers.GetValues("X-EggPdf-Warnings").Should().ContainSingle()
            .Which.Should().Contain("FONT_GLYPH_MISSING").And.Contain("U+10FFFD");
    }

    [Fact]
    public async Task Render_ConformanceOption_ReturnsPdfAOutput()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html = "<h1>Test</h1>", options = new { conformance = "PdfA2b" } }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var pdfText = Encoding.Latin1.GetString(bytes);
        pdfText.Should().Contain("/OutputIntents [");
    }

    [Theory]
    [InlineData("/api/render")]
    [InlineData("/api/render/pages")]
    public async Task Render_LocalFileReferences_NotReadByDefault(string endpoint)
    {
        var cssPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "eggpdf_e2e_" + System.Guid.NewGuid().ToString("N") + ".css");
        await System.IO.File.WriteAllTextAsync(cssPath, ".b { font-weight: bold; }");
        try
        {
            var html = $"<html><head><link rel=\"stylesheet\" href=\"{cssPath}\"></head><body><p class=\"b\">x</p></body></html>";
            var content = new StringContent(JsonSerializer.Serialize(new { html }), Encoding.UTF8, "application/json");

            var resp = await _client.PostAsync($"{_fixture.BaseUrl}{endpoint}", content);

            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
            Encoding.Latin1.GetString(await resp.Content.ReadAsByteArrayAsync()).Should().NotContain("Helvetica-Bold");
            if (endpoint == "/api/render")
                resp.Headers.GetValues("X-EggPdf-Warnings").Should().ContainSingle().Which.Should().Contain("RESOURCE_BLOCKED");
        }
        finally
        {
            System.IO.File.Delete(cssPath);
        }
    }

    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Stamp_ImageOnPage_ReturnsTheUpdatedPdf()
    {
        var original = EggPdf.HtmlToPdf.Render("<p>contract</p>");
        var content = new StringContent(JsonSerializer.Serialize(new
        {
            pdf = System.Convert.ToBase64String(original),
            stamps = new[] { new { page = 1, x = 10.0, y = 20.0, width = 100.0, height = 50.0, image = OnePixelPng, fit = "contain" } },
        }), Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/stamp", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var stamped = await resp.Content.ReadAsByteArrayAsync();
        stamped.Length.Should().BeGreaterThan(original.Length);
        EggPdf.Pdf.PdfReader.Open(stamped).PageCount.Should().Be(1);
    }

    [Fact]
    public async Task Stamp_PageOutOfRange_ReturnsBadRequest()
    {
        var content = new StringContent(JsonSerializer.Serialize(new
        {
            pdf = System.Convert.ToBase64String(EggPdf.HtmlToPdf.Render("<p>x</p>")),
            stamps = new[] { new { page = 5, x = 0.0, y = 0.0, width = 10.0, height = 10.0, image = OnePixelPng, fit = "stretch" } },
        }), Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/stamp", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stamp_NullStampEntry_ReturnsBadRequest()
    {
        var pdf = System.Convert.ToBase64String(EggPdf.HtmlToPdf.Render("<p>x</p>"));
        var content = new StringContent("{\"pdf\":\"" + pdf + "\",\"stamps\":[null]}", Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/stamp", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RenderUrl_LoopbackAddress_Refused()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { url = $"{_fixture.BaseUrl}/health" }), Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render/url", content);

        resp.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Render_PdfA1ConformanceOption_ReturnsBadRequestWhenTransparencyIsUsed()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                html = "<h1 style=\"opacity:0.5\">Test</h1>",
                options = new { conformance = "PdfA1b" },
            }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Render_InvoiceOption_EmbedsFacturXAttachment()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                html = "<h1>Invoice</h1>",
                options = new
                {
                    conformance = "PdfA3b",
                    invoice = new
                    {
                        invoiceNumber = "2026-042",
                        sellerName = "Seller GmbH",
                        buyerName = "Buyer SARL",
                        taxBasisTotal = 100.00,
                        taxTotal = 20.00,
                        grandTotal = 120.00,
                        duePayableAmount = 120.00,
                    },
                },
            }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var pdfText = Encoding.Latin1.GetString(bytes);
        pdfText.Should().Contain("<rsm:CrossIndustryInvoice");
        pdfText.Should().Contain("/AFRelationship /Data");
    }

    [Fact]
    public async Task Render_InvoiceWithLineItems_ProducesEn16931WithAlternativeRelationship()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                html = "<h1>Invoice</h1>",
                options = new
                {
                    conformance = "PdfA3b",
                    invoice = new
                    {
                        invoiceNumber = "2026-042",
                        sellerName = "Seller GmbH",
                        buyerName = "Buyer SARL",
                        lineItems = new[]
                        {
                            new
                            {
                                lineId = "1", itemName = "Consulting", netUnitPrice = 100.00,
                                billedQuantity = 3, unitCode = "HUR", lineTotalAmount = 300.00,
                                vatCategoryCode = "S", vatRatePercent = 20,
                            },
                        },
                    },
                },
            }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var pdfText = Encoding.Latin1.GetString(bytes);
        pdfText.Should().Contain("<ram:IncludedSupplyChainTradeLineItem>");
        pdfText.Should().Contain("<fx:ConformanceLevel>EN16931</fx:ConformanceLevel>");
        pdfText.Should().Contain("/AFRelationship /Alternative");
    }

    [Fact]
    public async Task Render_InvalidConformanceValue_ReturnsBadRequest()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html = "<h1>x</h1>", options = new { conformance = "bogus" } }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Render_InvoiceWithoutPdfA3_ReturnsBadRequest()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                html = "<h1>x</h1>",
                options = new { conformance = "PdfA2b", invoice = new { invoiceNumber = "1", sellerName = "S", buyerName = "B" } },
            }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Render_TaggedOption_ReturnsPdfWithStructTreeRoot()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { html = "<h1>Title</h1><p>Text</p>", options = new { tagged = true } }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var pdfText = Encoding.Latin1.GetString(bytes);
        pdfText.Should().Contain("/Type /StructTreeRoot");
    }

    [Fact]
    public async Task Render_TaggedWithNamedPageGroups_ReturnsTaggedPdfAcrossAllGroups()
    {
        var html = "<html><head><style>@page wide{size:landscape}.c{page:wide}</style></head>" +
                    "<body><h1>x</h1><div class=\"c\"><p>y</p></div></body></html>";
        var content = new StringContent(
            JsonSerializer.Serialize(new { html, options = new { tagged = true } }),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync($"{_fixture.BaseUrl}/api/render", content);

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var pdfText = Encoding.Latin1.GetString(bytes);
        pdfText.Should().Contain("/Type /StructTreeRoot");
        pdfText.Should().Contain("/Type /StructElem /S /H1");
        pdfText.Should().Contain("/Type /StructElem /S /P");
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
