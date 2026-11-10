using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class CssRenderingTests
{
    private const string Red = "1.00 0.00 0.00 rg";
    private const string Blue = "0.00 0.00 1.00 rg";
    private const string CssGreen = "0.00 0.50 0.00 rg";

    private static float FontSizeOf(string pdfText, string run)
    {
        var m = Regex.Match(pdfText, @"([\d.]+) Tf[^\n]*\(" + Regex.Escape(run) + @"\) Tj");
        m.Success.Should().BeTrue($"a text run '{run}' must be painted");
        return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Color_Red_PaintsTextRed()
    {
        var html = "<p style='color: red'>Red text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "Red text");
        PdfAssert.TextPaintedWith(text, "Red text", Red).Should().BeTrue();
    }

    [Fact]
    public async Task BackgroundColor_Blue_RectangleDrawn()
    {
        var html = "<div style='background-color: blue; width: 200px; height: 100px'>Blue box</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "Blue box");
        text.Should().Contain(Blue + "\n");
        text.Should().Contain("150.00 75.00 re f", "the 200x100px background is filled as a 150x75pt rectangle");
    }

    [Fact]
    public async Task FontWeight_Bold_UsesBoldFont()
    {
        var html = "<p style='font-weight: bold'>Bold text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Bold text");
        text.Should().Contain("Helvetica-Bold", "bold text uses the bold font variant");
    }

    [Fact]
    public async Task FontStyle_Italic_UsesItalicFont()
    {
        var html = "<p style='font-style: italic'>Italic text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Italic text");
        text.Should().Contain("Oblique", "Helvetica's italic is Helvetica-Oblique");
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

        var text = PdfAssert.ValidPdf(pdf);
        PdfAssert.TextPaintedWith(text, "Red", Red).Should().BeTrue();
        PdfAssert.TextPaintedWith(text, "Blue", Blue).Should().BeTrue();
        PdfAssert.TextPaintedWith(text, "Green", CssGreen).Should().BeTrue("CSS green is #008000");
    }

    [Fact]
    public async Task NamedColor_BackgroundApplied()
    {
        var html = "<div style='background-color: yellow; width: 100px; height: 50px'>Yellow</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "Yellow");
        text.Should().Contain("1.00 1.00 0.00 rg\n", "the named color yellow is #ffff00");
        text.Should().Contain("75.00 37.50 re f");
    }

    [Fact]
    public async Task HexColor_Parsed()
    {
        var html = "<div style='background-color: #ff5733; width: 100px; height: 50px'>Hex</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "Hex");
        text.Should().Contain("1.00 0.34 0.20 rg", "#ff5733 is (255, 87, 51)");
    }

    [Fact]
    public async Task HeadingHierarchy_AllSizesDistinct()
    {
        var html = "<h1>H1</h1><h2>H2</h2><h3>H3</h3><h4>H4</h4>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf);
        float h1 = FontSizeOf(text, "H1"), h2 = FontSizeOf(text, "H2"), h3 = FontSizeOf(text, "H3"), h4 = FontSizeOf(text, "H4");
        h1.Should().BeGreaterThan(h2);
        h2.Should().BeGreaterThan(h3);
        h3.Should().BeGreaterThan(h4, "each heading level down is smaller: 2em, 1.5em, 1.17em, 1em");
    }

    [Fact]
    public async Task InlineStyles_OverrideDefaults()
    {
        var html = "<h1 style='font-size: 12px; color: gray'>Small heading</h1>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf);
        FontSizeOf(text, "Small heading").Should().Be(9f, "the inline 12px replaces h1's 2em and is 9pt");
        PdfAssert.TextPaintedWith(text, "Small heading", "0.50 0.50 0.50 rg").Should().BeTrue("gray is #808080");
    }

    [Fact]
    public async Task StyleTag_ClassRule_AppliesColorAndWeight()
    {
        var html = @"<html><head><style>
            .custom { color: red; font-weight: bold; }
        </style></head><body>
            <p class='custom'>Styled paragraph</p>
        </body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "Styled paragraph");
        PdfAssert.TextPaintedWith(text, "Styled paragraph", Red).Should().BeTrue();
        text.Should().Contain("Helvetica-Bold");
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
    public async Task TextAlign_Center_MovesTextTowardThePageMiddle()
    {
        var html = "<p style='text-align: center'>Centered text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf);
        PdfAssert.TextPosition(text, "Centered text").X.Should().BeInRange(200f, 300f,
            "a short line centred on the 595pt page starts well right of the 6pt body margin");
    }

    [Fact]
    public async Task TextAlign_Right_MovesTextToTheRightEdge()
    {
        var html = "<p style='text-align: right'>Right-aligned text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf);
        PdfAssert.TextPosition(text, "Right-aligned text").X.Should().BeGreaterThan(400f,
            "a short right-aligned line ends at the right content edge of the 595pt page");
    }

    [Fact]
    public async Task TextDecoration_Underline_LineDrawn()
    {
        var html = "<p style='text-decoration: underline'>Underlined text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Underlined text");
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
        text.Should().Contain("Link");
        text.Should().Contain("text");
        text.Should().Contain("l S", "links should have underline by default");
    }

    [Fact]
    public async Task ListItem_BulletRendered()
    {
        var html = "<ul><li>First item</li><li>Second item</li></ul>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "First item", "Second item");
        PdfAssert.Count(text, "(" + (char)0x95 + ") Tj").Should().Be(2, "each item gets a WinAnsi bullet (0x95) marker");
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

        var text = PdfAssert.ValidPdf(pdf, "Margin test");
        text.Should().MatchRegex(@"\n21\.00 [\d.]+ 75\.00 37\.50 re f",
            "the second value is the left margin: 8px body + 20px = 28px = 21pt");
    }

    [Fact]
    public async Task TextIndent_AppliedToFirstLine()
    {
        var html = "<p style='text-indent: 40px'>This is a paragraph with indented first line</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf);
        PdfAssert.TextPosition(text, "This is a paragraph with indented first line").X.Should().BeApproximately(36f, 0.01f,
            "the first line starts 40px in from the body's 8px margin: 48px = 36pt");
    }

    [Fact]
    public async Task LetterSpacing_PdfOperatorEmitted()
    {
        var html = "<p style='letter-spacing: 2px'>Spaced text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Spaced text");
        text.Should().Contain("1.50 Tc", "letter-spacing: 2px is a 1.5pt character spacing");
    }

    [Fact]
    public async Task WordSpacing_PdfOperatorEmitted()
    {
        var html = "<p style='word-spacing: 5px'>Word spaced text</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Word spaced text");
        text.Should().Contain("3.75 Tw", "word-spacing: 5px is a 3.75pt word spacing");
    }

    [Fact]
    public async Task WhiteSpacePre_NewlinesPreserved()
    {
        var html = "<pre>Line 1\nLine 2\nLine 3</pre>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf);
        float line1 = PdfAssert.TextPosition(text, "Line 1").Y;
        float line2 = PdfAssert.TextPosition(text, "Line 2").Y;
        float line3 = PdfAssert.TextPosition(text, "Line 3").Y;
        line2.Should().BeLessThan(line1, "each newline in <pre> starts a new line");
        line3.Should().BeLessThan(line2, "each newline in <pre> starts a new line");
    }

    [Fact]
    public async Task OverflowHidden_ContentRendered()
    {
        var html = "<div style='overflow: hidden; width: 100px; height: 50px; background-color: #eee'>Clipped</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "Clipped");
        text.Should().Contain("75.00 37.50 re W n", "overflow:hidden clips to the 100x50px box");
    }

    [Fact]
    public async Task PageBreakBefore_CreatesNewPage()
    {
        var html = @"
            <p>Page 1 content</p>
            <p style='page-break-before: always'>Page 2 content</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "Page 1 content", "Page 2 content");
        PdfAssert.PageCount(text).Should().Be(2, "page-break-before:always starts the second paragraph on a new page");
    }

    [Fact]
    public async Task PageBreakAfter_CreatesNewPage()
    {
        var html = @"
            <p style='page-break-after: always'>Page 1 content</p>
            <p>Page 2 content</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "Page 1 content", "Page 2 content");
        PdfAssert.PageCount(text).Should().Be(2, "page-break-after:always puts the second paragraph on a new page");
    }

    [Fact]
    public async Task TextTransform_Uppercase_AppliedInPdf()
    {
        var html = "<p style='text-transform: uppercase'>hello world</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("HELLO WORLD");
        text.Should().NotContain("hello world");
    }

    [Fact]
    public async Task TextTransform_Lowercase_AppliedInPdf()
    {
        var html = "<p style='text-transform: lowercase'>HELLO WORLD</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("hello world");
    }

    [Fact]
    public async Task TextTransform_Capitalize_AppliedInPdf()
    {
        var html = "<p style='text-transform: capitalize'>hello world</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Hello World");
    }

    [Fact]
    public async Task VisibilityHidden_TextNotRendered()
    {
        var html = "<p>Visible</p><p style='visibility: hidden'>Hidden</p><p>Also visible</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Visible");
        text.Should().Contain("Also visible");
        text.Should().NotContain("Hidden");
    }

    [Fact]
    public async Task HrElement_Rendered()
    {
        var html = "<p>Above</p><hr><p>Below</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf, "Above", "Below");
        text.Should().MatchRegex(@"-?[\d.]+ -?[\d.]+ l S", "the <hr> is drawn as its border line");
    }

    [Fact]
    public async Task BrElement_CausesLineBreak()
    {
        var html = "<p>Line 1<br>Line 2</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = PdfAssert.ValidPdf(pdf);
        PdfAssert.TextPosition(text, "Line 2").Y.Should().BeLessThan(PdfAssert.TextPosition(text, "Line 1").Y,
            "the <br> moves 'Line 2' onto the next line");
    }

    [Fact]
    public async Task TableBorderAttribute_CellsHaveBorders()
    {
        var html = "<table border='1'><tr><td>Cell</td></tr></table>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Cell");
        text.Should().Contain("re S", "the table's border attribute propagates to its cells, which are stroked");
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

    [Fact]
    public void SupElement_RendersSuperscript()
    {
        var html = "<p>E=mc<sup>2</sup></p>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "E=mc");

        FontSizeOf(text, "2").Should().BeLessThan(12f, "the superscript is set smaller than the 12pt text");
    }

    [Fact]
    public void SubElement_RendersSubscript()
    {
        var html = "<p>H<sub>2</sub>O</p>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        FontSizeOf(text, "2").Should().BeLessThan(12f, "the subscript is set smaller than the 12pt text");
    }

    [Fact]
    public void OutlineProperty_RendersAroundElement()
    {
        var html = "<p style='outline: 2px solid blue;'>Outlined</p>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Outlined");

        text.Should().Contain("0.00 0.00 1.00 RG", "the blue outline is stroked around the paragraph");
    }

    [Fact]
    public void VerticalAlignMiddle_InTableCell()
    {
        var html = @"<table style='height: 100px;'>
            <tr>
                <td style='vertical-align: middle; height: 100px;'>Center</td>
                <td style='vertical-align: bottom; height: 100px;'>Bottom</td>
            </tr>
        </table>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        PdfAssert.TextPosition(text, "Bottom").Y.Should().BeLessThan(PdfAssert.TextPosition(text, "Center").Y,
            "in the 100px cells, bottom-aligned text sits lower (a smaller PDF y) than middle-aligned text");
    }

    [Fact]
    public void BorderCollapse_Table()
    {
        var html = @"<table style='border-collapse: collapse;'>
            <tr>
                <td style='border: 1px solid black; padding: 4px;'>A</td>
                <td style='border: 1px solid black; padding: 4px;'>B</td>
            </tr>
        </table>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "(A) Tj", "(B) Tj");

        text.Should().Contain("0.00 0.00 0.00 RG", "the collapsed black cell borders are stroked");
    }

    [Fact]
    public void ZIndex_PositionedElements()
    {
        var html = @"<div style='position: relative; width: 200px; height: 200px;'>
            <div style='position: absolute; z-index: 2; top: 10px; left: 10px; width: 50px; height: 50px; background: red;'>Top</div>
            <div style='position: absolute; z-index: 1; top: 20px; left: 20px; width: 50px; height: 50px; background: blue;'>Below</div>
        </div>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Top", "Below");

        text.IndexOf(Blue + "\n", System.StringComparison.Ordinal).Should().BeLessThan(text.IndexOf(Red + "\n", System.StringComparison.Ordinal),
            "the z-index:1 blue box is painted before the z-index:2 red box, though it comes later in the source");
    }

    [Fact]
    public void OverflowWrap_BreakWord_LongWord()
    {
        var html = @"<div style='width: 100px; overflow-wrap: break-word;'>Superlongwordthatwillnotfitinasmallcontainer</div>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Super");

        text.Should().NotContain("(Superlongwordthatwillnotfitinasmallcontainer) Tj",
            "overflow-wrap: break-word breaks the word across lines");
    }

    [Fact]
    public void WordBreak_BreakAll()
    {
        var html = @"<div style='width: 80px; word-break: break-all;'>ABCDEFGHIJKLMNOP</div>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "ABCDE");

        text.Should().NotContain("(ABCDEFGHIJKLMNOP) Tj", "word-break: break-all breaks between any two letters");
    }

    [Fact]
    public void TextOverflow_Ellipsis()
    {
        var html = @"<div style='width: 80px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;'>This is a very long text that should be truncated</div>";
        var pdf = HtmlToPdf.Render(html);
        var text = System.Text.Encoding.Latin1.GetString(pdf);

        text.Should().Contain("...");
    }

    [Fact]
    public void TextDecoration_Overline()
    {
        var html = @"<p style='text-decoration: overline;'>Overlined text</p>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Overlined text");

        PdfAssert.Count(text, "l S").Should().Be(1, "the overline is one stroked line");
    }

    [Fact]
    public void TextDecoration_Multiple()
    {
        var html = @"<p style='text-decoration: underline overline;'>Both decorations</p>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Both decorations");

        PdfAssert.Count(text, "l S").Should().Be(2, "underline and overline are each one stroked line");
    }

    [Fact]
    public void RgbaColor_InPdf()
    {
        var html = @"<p style='color: rgba(255, 0, 0, 0.5);'>Semi-transparent</p>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Semi-transparent");

        text.Should().Contain(Red, "the rgb part is painted as the fill color");
        text.Should().Contain("/GS50 gs", "the 0.5 alpha is applied through a graphics state");
    }

    [Fact]
    public void HslColor_InPdf()
    {
        var html = @"<p style='color: hsl(0, 100%, 50%);'>HSL Red</p>";
        var pdf = HtmlToPdf.Render(html);
        var text = System.Text.Encoding.Latin1.GetString(pdf);

        text.Should().Contain("HSL Red");
        text.Should().Contain(Red);
    }

    [Fact]
    public void FontShorthand_AppliesAllProperties()
    {
        var html = @"<style>p { font: bold 20px serif; }</style><p>Bold serif</p>";
        var pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Bold serif");

        text.Should().Contain("Times-Bold", "bold + serif");
        FontSizeOf(text, "Bold serif").Should().Be(15f, "20px is 15pt");
    }
}
