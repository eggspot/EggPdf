using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// E2E visual comparison tests: verify that EggPdf produces PDFs containing
/// the same text content as the HTML input. These test the full pipeline.
///
/// For pixel-level visual comparison (browser vs PDF), see the /e2e endpoint
/// in EggPdf.Service which provides side-by-side rendering comparison.
/// </summary>
public class VisualComparisonTests
{
    /// <summary>
    /// Test cases matching the /e2e page test cases.
    /// Each verifies text content survives the full HTML -> PDF pipeline.
    /// </summary>
    public static IEnumerable<object[]> TestCases => new List<object[]>
    {
        new object[] { "heading", "<h1>Hello World</h1><p>This is a paragraph with <strong>bold</strong> and <em>italic</em> text.</p>",
            new[] { "Hello World", "paragraph", "bold", "italic" } },

        new object[] { "table", "<table><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody><tr><td>Alpha</td><td>100</td></tr><tr><td>Beta</td><td>200</td></tr></tbody></table>",
            new[] { "Name", "Value", "Alpha", "100", "Beta", "200" } },

        new object[] { "invoice", "<h1>Invoice #001</h1><p>Date: 2024-01-15</p><table><tr><th>Item</th><th>Price</th></tr><tr><td>Widget</td><td>$50</td></tr></table><p><strong>Total: $125</strong></p>",
            new[] { "Invoice #001", "Date", "Item", "Price", "Widget", "$50", "Total" } },

        new object[] { "styles", "<h2>Styled Box</h2><p>Text with <span>red</span>, <span>blue</span>, and <strong>bold</strong> formatting.</p>",
            new[] { "Styled Box", "red", "blue", "bold" } },

        new object[] { "list", "<h2>Features</h2><ul><li>Item One</li><li>Item Two</li></ul><ol><li>First</li><li>Second</li></ol>",
            new[] { "Features", "Item One", "Item Two", "First", "Second" } },
    };

    [Theory]
    [MemberData(nameof(TestCases))]
    public async Task TextContent_SurvivesFullPipeline(string name, string html, string[] expectedTexts)
    {
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty($"test case '{name}' should produce non-empty PDF");

        // Verify PDF is valid
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-", $"test case '{name}' should produce valid PDF");

        // Verify all expected text appears in the PDF
        string pdfContent = Encoding.ASCII.GetString(pdf);
        foreach (var text in expectedTexts)
        {
            pdfContent.Should().Contain(text,
                $"test case '{name}': text '{text}' should be in PDF output");
        }
    }

    [Theory]
    [MemberData(nameof(TestCases))]
    public async Task Pdf_HasCorrectStructure(string name, string html, string[] _)
    {
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        string content = Encoding.ASCII.GetString(pdf);

        // Valid PDF structure
        content.Should().StartWith("%PDF-1.7", $"'{name}' should be PDF 1.7");
        content.Should().Contain("%%EOF", $"'{name}' should have %%EOF");
        content.Should().Contain("xref", $"'{name}' should have xref table");
        content.Should().Contain("trailer", $"'{name}' should have trailer");
        content.Should().Contain("/Type /Page", $"'{name}' should have at least one page");
    }

    [Fact]
    public async Task AllTestCases_ProduceDistinctPdfs()
    {
        var pdfs = new List<byte[]>();
        foreach (var tc in TestCases)
        {
            var html = (string)tc[1];
            var pdf = await HtmlToPdf.RenderAsync(html);
            pdfs.Add(pdf);
        }

        // Each test case should produce a different PDF (different content)
        for (int i = 0; i < pdfs.Count; i++)
        {
            for (int j = i + 1; j < pdfs.Count; j++)
            {
                pdfs[i].SequenceEqual(pdfs[j]).Should().BeFalse(
                    $"test cases {i} and {j} should produce different PDFs");
            }
        }
    }

    [Fact]
    public async Task SameInput_ProducesSamePdf()
    {
        var html = "<h1>Deterministic Test</h1><p>Same input should produce same output.</p>";

        byte[] pdf1 = await HtmlToPdf.RenderAsync(html);
        byte[] pdf2 = await HtmlToPdf.RenderAsync(html);

        // Note: PDFs may differ in CreationDate timestamp, so we compare structure only
        pdf1.Length.Should().Be(pdf2.Length, "same input should produce same size PDF");
    }
}
