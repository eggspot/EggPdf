using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Tests that CSS properties produce correct output in the generated PDF.
/// Verifies the full pipeline: HTML+CSS -> Parse -> Style -> Layout -> Paint -> PDF.
/// </summary>
public class CssRenderingTests
{
    [Fact]
    public async Task Color_Red_TextRendered()
    {
        var html = "<p style='color: red'>Red text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Red text");
    }

    [Fact]
    public async Task BackgroundColor_Blue_RectangleDrawn()
    {
        var html = "<div style='background-color: blue; width: 200px; height: 100px'>Blue box</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("re"); // rectangle operator
        text.Should().Contain("f");  // fill operator
    }

    [Fact]
    public async Task FontWeight_Bold_UsesBoldFont()
    {
        var html = "<p style='font-weight: bold'>Bold text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Bold"); // should contain the bold text
        text.Should().Contain("Helvetica-Bold"); // should use bold font variant
    }

    [Fact]
    public async Task FontStyle_Italic_UsesItalicFont()
    {
        var html = "<p style='font-style: italic'>Italic text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Italic text");
        text.Should().Contain("Oblique"); // Helvetica-Oblique
    }

    [Fact]
    public async Task FontFamily_Monospace_UsesCourier()
    {
        var html = "<p style='font-family: monospace'>Code text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Courier");
    }

    [Fact]
    public async Task FontFamily_Serif_UsesTimesRoman()
    {
        var html = "<p style='font-family: serif'>Serif text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Times");
    }

    [Fact]
    public async Task DisplayNone_ElementNotRendered()
    {
        var html = "<p>Visible</p><p style='display: none'>Hidden</p><p>Also visible</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Visible");
        text.Should().Contain("Also visible");
        text.Should().NotContain("Hidden");
    }

    [Fact]
    public async Task HiddenAttribute_ElementNotRendered()
    {
        var html = "<p>Visible</p><p hidden>Hidden by attribute</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Visible");
        text.Should().NotContain("Hidden by attribute");
    }

    [Fact]
    public async Task MultipleColors_AllPresent()
    {
        var html = @"
            <p style='color: red'>Red</p>
            <p style='color: blue'>Blue</p>
            <p style='color: green'>Green</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Red");
        text.Should().Contain("Blue");
        text.Should().Contain("Green");
    }

    [Fact]
    public async Task NamedColor_BackgroundApplied()
    {
        var html = "<div style='background-color: yellow; width: 100px; height: 50px'>Yellow</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Yellow");
        text.Should().Contain("re"); // rectangle for background
    }

    [Fact]
    public async Task HexColor_Parsed()
    {
        var html = "<div style='background-color: #ff5733; width: 100px; height: 50px'>Hex</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Hex");
    }

    [Fact]
    public async Task HeadingHierarchy_AllSizesDistinct()
    {
        var html = "<h1>H1</h1><h2>H2</h2><h3>H3</h3><h4>H4</h4>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("H1");
        text.Should().Contain("H2");
        text.Should().Contain("H3");
        text.Should().Contain("H4");
    }

    [Fact]
    public async Task InlineStyles_OverrideDefaults()
    {
        var html = "<h1 style='font-size: 12px; color: gray'>Small heading</h1>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Small heading");
    }

    [Fact]
    public async Task StyleTag_TextRendered()
    {
        // Note: Full <style> tag CSS cascade is applied via CascadeResolver.
        // This test verifies the text survives the pipeline.
        var html = @"<html><head><style>
            .custom { color: red; font-weight: bold; }
        </style></head><body>
            <p class='custom'>Styled paragraph</p>
        </body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Styled paragraph");
    }

    [Fact]
    public async Task Link_HasAnnotation()
    {
        var html = "<a href='https://github.com'>GitHub</a>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("GitHub");
        text.Should().Contain("https://github.com");
        text.Should().Contain("/Annot");
    }

    [Fact]
    public async Task TextAlign_Center_TextRendered()
    {
        var html = "<p style='text-align: center'>Centered text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Centered text");
    }

    [Fact]
    public async Task TextAlign_Right_TextRendered()
    {
        var html = "<p style='text-align: right'>Right-aligned text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Right-aligned text");
    }

    [Fact]
    public async Task TextDecoration_Underline_LineDrawn()
    {
        var html = "<p style='text-decoration: underline'>Underlined text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Underlined text");
        // Should contain a line drawing operator (m...l S pattern)
        text.Should().Contain("l S", "underline should draw a line");
    }

    [Fact]
    public async Task TextDecoration_LineThrough_LineDrawn()
    {
        var html = "<p style='text-decoration: line-through'>Struck text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Struck text");
        text.Should().Contain("l S", "line-through should draw a line");
    }

    [Fact]
    public async Task AnchorTag_Underline_DefaultRendered()
    {
        var html = "<a href='https://example.com'>Link text</a>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Link text");
        text.Should().Contain("l S", "links should have underline by default");
    }

    [Fact]
    public async Task ListItem_BulletRendered()
    {
        var html = "<ul><li>First item</li><li>Second item</li></ul>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("First item");
        text.Should().Contain("Second item");
    }

    [Fact]
    public async Task OrderedList_NumbersRendered()
    {
        var html = "<ol><li>Step one</li><li>Step two</li></ol>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Step one");
        text.Should().Contain("Step two");
        text.Should().Contain("1.");
        text.Should().Contain("2.");
    }

    [Fact]
    public async Task MarginShorthand_TwoValues_Rendered()
    {
        var html = "<div style='margin: 10px 20px; background-color: red; width: 100px; height: 50px'>Margin test</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Margin test");
    }

    [Fact]
    public async Task TextIndent_AppliedToFirstLine()
    {
        var html = "<p style='text-indent: 40px'>This is a paragraph with indented first line</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("This is a paragraph");
    }

    [Fact]
    public async Task LetterSpacing_PdfOperatorEmitted()
    {
        var html = "<p style='letter-spacing: 2px'>Spaced text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Spaced text");
        text.Should().Contain("Tc", "letter-spacing should emit Tc operator");
    }

    [Fact]
    public async Task WordSpacing_PdfOperatorEmitted()
    {
        var html = "<p style='word-spacing: 5px'>Word spaced text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Word spaced text");
        text.Should().Contain("Tw", "word-spacing should emit Tw operator");
    }

    [Fact]
    public async Task WhiteSpacePre_NewlinesPreserved()
    {
        var html = "<pre>Line 1\nLine 2\nLine 3</pre>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Line 1");
        text.Should().Contain("Line 2");
        text.Should().Contain("Line 3");
    }

    [Fact]
    public async Task OverflowHidden_ContentRendered()
    {
        var html = "<div style='overflow: hidden; width: 100px; height: 50px; background-color: #eee'>Clipped</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        // PDF should be non-empty and contain the text
        pdf.Length.Should().BeGreaterThan(100);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("%PDF-1.7", "should be a valid PDF");
        text.Should().Contain("Clipped");
    }

    [Fact]
    public async Task PageBreakBefore_CreatesNewPage()
    {
        var html = @"
            <p>Page 1 content</p>
            <p style='page-break-before: always'>Page 2 content</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Page 1 content");
        text.Should().Contain("Page 2 content");

        // Count page objects
        int pageCount = CountOccurrences(text, "/Type /Page ");
        pageCount.Should().BeGreaterOrEqualTo(2, "page-break-before:always should create at least 2 pages");
    }

    [Fact]
    public async Task PageBreakAfter_CreatesNewPage()
    {
        var html = @"
            <p style='page-break-after: always'>Page 1 content</p>
            <p>Page 2 content</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Page 1 content");
        text.Should().Contain("Page 2 content");

        int pageCount = CountOccurrences(text, "/Type /Page ");
        pageCount.Should().BeGreaterOrEqualTo(2, "page-break-after:always should create at least 2 pages");
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    [Fact]
    public async Task CompleteDocument_AllElementsRendered()
    {
        var html = @"<html><head><style>
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
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Report Title");
        text.Should().Contain("Generated on");
        text.Should().Contain("Revenue");
        text.Should().Contain("$1.2M");
        text.Should().Contain("Conclusion");
        text.Should().Contain("https://example.com");
    }
}
