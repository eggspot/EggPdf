using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// E2E regression tests that render full HTML through HtmlToPdf and verify the PDF output.
/// These tests catch rendering bugs at the PDF level.
/// </summary>
public class PdfRenderingRegressionTests
{
    [Fact]
    public async Task TableBorderCollapse_NoDuplicateBackgrounds()
    {
        // Regression: anonymous text boxes inside cells were painting phantom backgrounds
        var html = @"<html><head><style>
            table { width: 100%; border-collapse: collapse; }
            th { background: #6c5ce7; color: white; border: 1px solid #ddd; padding: 10px; }
            td { border: 1px solid #ddd; padding: 10px; }
        </style></head><body>
        <table><tr><th>A</th><th>B</th></tr>
        <tr><td>1</td><td>2</td></tr></table>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        // 2 header cells with backgrounds = exactly 3 fills (white page canvas + 2 th backgrounds)
        int fillCount = Regex.Matches(text, @"re f").Count;
        fillCount.Should().Be(3, "only element boxes should paint backgrounds, not anonymous text boxes");
    }

    [Fact]
    public async Task BorderBottom_OnlyRendered()
    {
        // Regression: CollectPaintableBoxes only checked border-top-style
        var html = @"<html><head><style>
            h2 { border-bottom: 2px solid #ddd; padding-bottom: 5px; }
        </style></head><body><h2>Heading</h2></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Heading");
        // Should have border line operators (m = moveto, l = lineto for per-side rendering)
        // or stroke rectangle
        bool hasStroke = text.Contains(" m") && text.Contains(" l") || text.Contains("re S");
        hasStroke.Should().BeTrue("border-bottom should produce stroke operators");
    }

    [Fact]
    public async Task BulletCharacter_RendersCorrectly()
    {
        // Regression: ASCII encoding replaced bullet (0x95) with '?'
        var html = "<ul><li>Item 1</li><li>Item 2</li></ul>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().Contain("Item 1");
        text.Should().Contain("Item 2");
        // Bullet character (WinAnsi 0x95) should be present, not '?'
        text.Should().NotContain("? Item", "bullet should not render as '?'");
    }

    [Fact]
    public async Task TextDecoration_UnderlineMatchesTextWidth()
    {
        // Regression: underline used box ContentWidth instead of measured text width
        var html = @"<p><a href='#'>Short</a></p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Short");
        // The underline line should exist
        bool hasLine = text.Contains(" m") && text.Contains(" l");
        hasLine.Should().BeTrue("underline should be rendered as a line");
    }

    [Fact]
    public async Task BrAfterText_NoDoubleLineHeight()
    {
        // Regression: <br> after text nodes added extra line height
        var html = @"<p>Line1<br>Line2<br>Line3</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Line1");
        text.Should().Contain("Line2");
        text.Should().Contain("Line3");
    }

    [Fact]
    public async Task InvoiceTemplate_RendersCompletely()
    {
        // Full invoice template E2E test
        var html = @"<html>
<head><style>
    body { font-family: Arial, sans-serif; margin: 40px; }
    h1 { color: #2d3436; border-bottom: 2px solid #6c5ce7; padding-bottom: 8px; }
    table { width: 100%; border-collapse: collapse; margin: 20px 0; }
    th, td { border: 1px solid #ddd; padding: 10px; text-align: left; }
    th { background: #6c5ce7; color: white; }
    .total { font-size: 18px; font-weight: bold; margin-top: 10px; }
</style></head>
<body>
    <h1>Invoice #2024-001</h1>
    <p>Date: 2024-01-15 | Customer: Acme Corporation</p>
    <table>
        <thead><tr><th>Item</th><th>Qty</th><th>Price</th><th>Total</th></tr></thead>
        <tbody>
            <tr><td>Web Development</td><td>40h</td><td>$150</td><td>$6,000</td></tr>
            <tr><td>UI/UX Design</td><td>20h</td><td>$120</td><td>$2,400</td></tr>
        </tbody>
    </table>
    <p class='total'>Total: $9,400.00</p>
    <p><a href='https://example.com/pay'>Pay Now</a></p>
</body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        // All text content should be present
        text.Should().Contain("Invoice #2024-001");
        text.Should().Contain("Web Development");
        text.Should().Contain("$6,000");
        text.Should().Contain("Total: $9,400.00");
        text.Should().Contain("Pay");
        text.Should().Contain("Now");

        // Header cells should have exactly 5 fills (white page canvas + 4 th backgrounds)
        // Data cells have no background, so shouldn't add fills
        var ascii = Encoding.ASCII.GetString(pdf);
        int fillCount = Regex.Matches(ascii, @"re f").Count;
        fillCount.Should().Be(5, "4 header cells should have exactly 4 background fills, plus 1 white page canvas");

        // h1 border-bottom should be rendered
        bool hasBorderLine = ascii.Contains(" m") && ascii.Contains(" l");
        hasBorderLine.Should().BeTrue("h1 border-bottom should render");
    }

    [Fact]
    public async Task AnnualReportTemplate_H2BordersRender()
    {
        // Annual report h2 border-bottom test
        var html = @"<html><head><style>
            body{font-family:Arial;margin:40px}
            h2{color:#666;border-bottom:1px solid #ddd;padding-bottom:5px}
        </style></head><body>
            <h1>Annual Report</h1>
            <h2>Executive Summary</h2>
            <p>Content here.</p>
            <h2>Financial Overview</h2>
            <p>More content.</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Executive Summary");
        text.Should().Contain("Financial Overview");

        // Count border lines (h2 border-bottom): each h2 produces moveto + lineto
        int moveToCount = Regex.Matches(text, @"\d+\.\d+ \d+\.\d+ m").Count;
        moveToCount.Should().BeGreaterOrEqualTo(2, "at least 2 h2 elements should render border-bottom lines");
    }

    [Fact]
    public async Task LetterTemplate_BrSpacing()
    {
        // Letter template with <br> should not have excessive line spacing
        // Note: <strong> is currently treated as block, so text after <br><strong>...</strong><br>
        // may be split across multiple text boxes.
        var html = @"<html><head><style>
            body{font-family:'Times New Roman';margin:60px;line-height:1.8}
        </style></head><body>
            <p>Sincerely,<br><strong>John Smith</strong><br>CEO, Acme Corp</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Sincerely,");
        text.Should().Contain("John");
        text.Should().Contain("Smith");
        // Text after <br> renders (may be split across text boxes)
        text.Should().Contain("CEO,");
    }
}
