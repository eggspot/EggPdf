using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class PdfRenderingRegressionTests
{
    private const string StrokedLineSegment = @"-?[\d.]+ -?[\d.]+ l S";

    [Fact]
    public async Task TableBorderCollapse_NoDuplicateBackgrounds()
    {
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

        int fillCount = Regex.Matches(text, @"re f").Count;
        fillCount.Should().Be(3,
            "only the white page canvas and the 2 header cells paint backgrounds; anonymous text boxes inside cells must not");
    }

    [Fact]
    public async Task BorderBottom_OnlyRendered()
    {
        var html = @"<html><head><style>
            h2 { border-bottom: 2px solid #ddd; padding-bottom: 5px; }
        </style></head><body><h2>Heading</h2></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Heading");
        text.Should().MatchRegex(StrokedLineSegment,
            "a border-bottom alone must be stroked, not skipped because the top border has no style");
    }

    [Fact]
    public async Task BulletCharacter_RendersCorrectly()
    {
        var html = "<ul><li>Item 1</li><li>Item 2</li></ul>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().Contain("Item 1");
        text.Should().Contain("Item 2");
        text.Should().NotContain("? Item", "the bullet is WinAnsi 0x95, which an ASCII encoding would turn into '?'");
    }

    [Fact]
    public async Task TextDecoration_UnderlineMatchesTextWidth()
    {
        var html = @"<p><a href='#'>Short</a></p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Short");
        text.Should().MatchRegex(StrokedLineSegment, "the link's underline is stroked as a line");
    }

    [Fact]
    public async Task MultiWordLink_AnnotationRectCoversFullText()
    {
        byte[] oneWordPdf = await HtmlToPdf.RenderAsync("<p><a href='https://example.com'>Click</a></p>");
        byte[] twoWordPdf = await HtmlToPdf.RenderAsync("<p><a href='https://example.com'>Click here</a></p>");

        float oneWordRectWidth = ExtractLinkRectWidth(Encoding.ASCII.GetString(oneWordPdf));
        float twoWordRectWidth = ExtractLinkRectWidth(Encoding.ASCII.GetString(twoWordPdf));

        twoWordRectWidth.Should().BeGreaterThan(oneWordRectWidth * 1.5f,
            "a multi-word link is split into one box per word for line breaking, and its annotation must still cover " +
            "every word, not just the first, or most of 'Click here' is unclickable");
    }

    private static float ExtractLinkRectWidth(string pdfText)
    {
        var match = Regex.Match(pdfText, @"/Subtype /Link /Rect \[([\d.]+) [\d.]+ ([\d.]+) [\d.]+\]");
        match.Success.Should().BeTrue("a /Subtype /Link annotation with a /Rect must be present");
        float x1 = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        float x2 = float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return x2 - x1;
    }

    private static float BaselineOfRunContaining(string pdfText, string word)
    {
        var m = Regex.Match(pdfText, @"-?\d+\.\d+ (-?\d+\.\d+) Td \([^)]*" + Regex.Escape(word) + @"[^)]*\) Tj");
        m.Success.Should().BeTrue($"a text run containing '{word}' must be painted");
        return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task BrAfterText_NoDoubleLineHeight()
    {
        var html = @"<p>Line1<br>Line2<br>Line3</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        float line1 = BaselineOfRunContaining(text, "Line1");
        float line2 = BaselineOfRunContaining(text, "Line2");
        float line3 = BaselineOfRunContaining(text, "Line3");

        (line1 - line2).Should().BeApproximately(line2 - line3, 0.1f, "every <br> advances by the same single line");
        (line1 - line2).Should().BeInRange(10f, 16f, "one 12pt line of normal line height, not two");
    }

    [Fact]
    public async Task InvoiceTemplate_RendersCompletely()
    {
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

        text.Should().Contain("Invoice #2024-001");
        text.Should().Contain("Web Development");
        text.Should().Contain("$6,000");
        text.Should().Contain("Total: $9,400.00");
        text.Should().Contain("Pay");
        text.Should().Contain("Now");

        var ascii = Encoding.ASCII.GetString(pdf);
        int fillCount = Regex.Matches(ascii, @"re f").Count;
        fillCount.Should().Be(5,
            "the white page canvas and the 4 header cell backgrounds are the only fills; data cells have no background");

        ascii.Should().MatchRegex(StrokedLineSegment, "h1 border-bottom should render");
    }

    [Fact]
    public async Task AnnualReportTemplate_H2BordersRender()
    {
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

        int moveToCount = Regex.Matches(text, @"\d+\.\d+ \d+\.\d+ m").Count;
        moveToCount.Should().BeGreaterOrEqualTo(2, "each h2's border-bottom line starts with a moveto");
    }

    [Fact]
    public async Task LetterTemplate_BrSpacing()
    {
        var html = @"<html><head><style>
            body{font-family:'Times New Roman';margin:60px;line-height:1.8}
        </style></head><body>
            <p>Sincerely,<br><strong>John Smith</strong><br>CEO, Acme Corp</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        float closing = BaselineOfRunContaining(text, "Sincerely,");
        float name = BaselineOfRunContaining(text, "John");
        float title = BaselineOfRunContaining(text, "CEO,");

        (closing - name).Should().BeApproximately(name - title, 0.5f,
            "the <br> before and after the <strong> name each advance by one line");
        (closing - name).Should().BeInRange(18f, 24f, "one line at line-height 1.8 of 12pt is 21.6pt, not two");
    }
}
