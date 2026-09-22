using System.Text;
using System.Text.RegularExpressions;
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
    public async Task FormElements_RenderValuesAndControlBoxes()
    {
        var html = @"
            <form>
                <input type='text' value='John Doe'>
                <input type='checkbox' checked>
                <select><option selected>Option 1</option></select>
                <textarea>Notes here</textarea>
                <button>Submit</button>
            </form>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "John Doe", "Option 1", "Notes here", "Submit");
        text.Should().Contain("re S", "form controls draw stroked borders");
        text.Should().Contain("0.26 0.55 0.96 rg", "the checked checkbox is filled with the default accent color");
    }

    [Fact]
    public async Task LargeTable_500Rows_PaginatesAndRendersFirstAndLastRow()
    {
        var sb = new StringBuilder("<table>");
        sb.Append("<thead><tr><th>ID</th><th>Name</th><th>Value</th></tr></thead><tbody>");
        for (int i = 0; i < 500; i++)
            sb.Append($"<tr><td>{i}</td><td>Item {i}</td><td>${i * 10}.00</td></tr>");
        sb.Append("</tbody></table>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var text = PdfAssert.ValidPdf(pdf, "(Item 0) Tj", "(Item 499) Tj", "$4990.00");
        PdfAssert.PageCount(text).Should().BeGreaterThan(5, "500 rows cannot fit on a few pages");
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
    public async Task VeryLargeHtml_1000Sections_RendersFirstAndLastAcrossPages()
    {
        var sb = new StringBuilder("<html><body>");
        for (int i = 0; i < 1000; i++)
            sb.Append($"<div><p>Section {i} with content.</p></div>");
        sb.Append("</body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var text = PdfAssert.ValidPdf(pdf, "(Section 0 with content.) Tj", "(Section 999 with content.) Tj");
        PdfAssert.PageCount(text).Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task DeeplyNestedHtml_50Levels_RendersInnermostText()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 50; i++) sb.Append("<div>");
        sb.Append("Deep content");
        for (int i = 0; i < 50; i++) sb.Append("</div>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var text = PdfAssert.ValidPdf(pdf, "(Deep content) Tj");
        PdfAssert.Count(text, "(Deep content) Tj").Should().Be(1);
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
    public async Task UnicodeContent_MultiScriptParagraphs_EachPaintedAsTextRun()
    {
        var html = "<p>Vietnamese: Xin chào thế giới</p><p>Thai: สวัสดีชาวโลก</p><p>Arabic: مرحبا بالعالم</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf);

        // Non-Latin-1 runs are emitted as glyph-id (hex) strings, so count the text objects instead.
        Regex.Matches(text, @"BT [^\n]*(Tj|TJ)[^\n]* ET").Count.Should().BeGreaterThanOrEqualTo(3,
            "each of the three paragraphs is painted");
        text.Should().Contain("/ToUnicode", "glyph-id text must stay extractable");
        PdfAssert.PageCount(text).Should().Be(1);
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
    public void FontFace_WithLocalSrc_FallsThroughToSystemFontAndPaintsText()
    {
        // @font-face with local() src should fall through to system font resolution
        // without crashing; the key requirement is graceful handling.
        var html = "<style>" +
                   "@font-face { font-family: 'MyFont'; src: local('Arial'); }" +
                   "</style>" +
                   "<p style='font-family: MyFont'>Hello from @font-face</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        // Either a standard-font literal run or an embedded (glyph-id) run, but exactly one paragraph line.
        text.Should().MatchRegex(@"BT [^\n]*(\) Tj|> Tj|\] TJ) ET", "the paragraph text must be painted");
        PdfAssert.PageCount(text).Should().Be(1);
    }

    [Fact]
    public void BreakInsideAvoid_ShortContent_StaysTogetherOnOnePage()
    {
        // A box with break-inside:avoid should keep both paragraphs together
        var html = "<div style='break-inside: avoid; page-break-inside: avoid'>" +
                   "<p>Content that should not be split across pages.</p>" +
                   "<p>More content in the same avoid-break container.</p>" +
                   "</div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Content that should not be split across pages.",
            "More content in the same avoid-break container.");
        PdfAssert.PageCount(text).Should().Be(1);
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
    public void ObjectPosition_WithObjectFit_LeftAlignsImageInBox()
    {
        var html = "<img style='width:100px;height:100px;object-fit:contain;object-position:left top' src='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=='>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("/Subtype /Image");
        var cm = Regex.Match(text, @"q\n(\d+\.\d+) 0 0 (\d+\.\d+) (\d+\.\d+) (\d+\.\d+) cm\n/Img\w+ Do");
        cm.Success.Should().BeTrue("the image is drawn with a scale+translate matrix");
        float.Parse(cm.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(6.00f, 0.01f, "object-position:left puts the image at the left edge of the box");
    }

    [Fact]
    public void ObjectPosition_Center_IsDefault()
    {
        var html = "<img style='width:200px;height:100px;object-fit:cover' src='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=='>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("/Subtype /Image");
        var cm = Regex.Match(text, @"q\n(\d+\.\d+) 0 0 (\d+\.\d+) (\d+\.\d+) (\d+\.\d+) cm\n/Img\w+ Do");
        cm.Success.Should().BeTrue("the image is drawn with a scale+translate matrix");
        float.Parse(cm.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(6.5f, "without object-position the image is centered, not flush left");
    }

    // A real 4x2 (2:1) red PNG, so object-fit:contain in a square box leaves a vertical gap
    // for object-position's vertical keyword to place the image within.
    private const string Wide2x1Png =
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAIAAADwyuo0AAAAEElEQVR4nGP8z4AATEhsBgAZNQEDGy9LEgAAAABJRU5ErkJggg==";

    private static float ImageCmY(string pdfText)
    {
        var cm = Regex.Match(pdfText, @"q\n(\d+\.\d+) 0 0 (\d+\.\d+) (\d+\.\d+) (\d+\.\d+) cm\n/Img\w+ Do");
        cm.Success.Should().BeTrue("the image is drawn with a scale+translate matrix");
        return float.Parse(cm.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void ObjectPosition_Top_PlacesImageHigherOnPageThanBottom()
    {
        // PDF's y-axis points up, the opposite of CSS's top-down layout, so the vertical object-position
        // fraction must be mirrored when applied to the already-flipped PDF y coordinate.
        string topHtml = $"<img style='width:200px;height:200px;object-fit:contain;object-position:center top' src='data:image/png;base64,{Wide2x1Png}'>";
        string bottomHtml = $"<img style='width:200px;height:200px;object-fit:contain;object-position:center bottom' src='data:image/png;base64,{Wide2x1Png}'>";

        var topText = PdfAssert.ValidPdf(HtmlToPdf.Render(topHtml));
        var bottomText = PdfAssert.ValidPdf(HtmlToPdf.Render(bottomHtml));

        ImageCmY(topText).Should().BeGreaterThan(ImageCmY(bottomText),
            "object-position:top must sit higher on the (bottom-up) PDF page than object-position:bottom");
    }

    [Fact]
    public void ObjectPosition_TopAndBottom_AreSymmetricAroundCenter()
    {
        string centerHtml = $"<img style='width:200px;height:200px;object-fit:contain;object-position:center' src='data:image/png;base64,{Wide2x1Png}'>";
        string topHtml = $"<img style='width:200px;height:200px;object-fit:contain;object-position:center top' src='data:image/png;base64,{Wide2x1Png}'>";
        string bottomHtml = $"<img style='width:200px;height:200px;object-fit:contain;object-position:center bottom' src='data:image/png;base64,{Wide2x1Png}'>";

        float centerY = ImageCmY(PdfAssert.ValidPdf(HtmlToPdf.Render(centerHtml)));
        float topY = ImageCmY(PdfAssert.ValidPdf(HtmlToPdf.Render(topHtml)));
        float bottomY = ImageCmY(PdfAssert.ValidPdf(HtmlToPdf.Render(bottomHtml)));

        (topY - centerY).Should().BeApproximately(centerY - bottomY, 0.05f,
            "top and bottom should be equidistant from the centered position");
    }

    [Fact]
    public void Filter_Invert_AppliesToDefaultBlackTextColor()
    {
        // No explicit `color`: the element's text color comes from the cascade's initial value
        // ("canvastext"), which must still resolve to a real color so a filter can affect it.
        var html = "<p style='filter: invert(1)'>Inverted</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "(Inverted) Tj");

        PdfAssert.TextPaintedWith(text, "Inverted", "1.00 1.00 1.00 rg")
            .Should().BeTrue("invert(1) on default black text must paint it white");
        PdfAssert.TextPaintedWith(text, "Inverted", "0.00 0.00 0.00 rg")
            .Should().BeFalse("the filter must not be silently skipped because color:canvastext failed to parse");
    }

    [Fact]
    public void TextDecoration_ColorAndStyle_StrokesRedDashedTwoPixelLine()
    {
        var html = "<p style='text-decoration: underline dashed red 2px'>Decorated text</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Decorated text");

        text.Should().Contain("1.50 w", "2px thickness");
        text.Should().Contain("1.00 0.00 0.00 RG", "red decoration color");
        text.Should().Contain("[6.00 6.00] 0 d", "dashed pattern scaled to the thickness");
    }

    [Fact]
    public void Supports_Rule_Applies()
    {
        var html = "<style>@supports (display: flex) { p { color: red; } }</style><p>Test</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Test");

        PdfAssert.TextPaintedWith(text, "Test", "1.00 0.00 0.00 rg").Should().BeTrue("the supported @supports block applies its rule");
    }

    [Fact]
    public void BorderRadius_Shorthand_PaintsCurvedPathInsteadOfRect()
    {
        var html = "<div style='width:100px;height:100px;border-radius:10px;background-color:blue'></div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain(" c\n", "rounded corners are Bezier curves");
        text.Should().Contain("h f");
        text.Should().NotContain("75.00 75.00 re f");
    }

    [Fact]
    public void AspectRatio_ComputesHeight()
    {
        var html = "<div style='width:160px;aspect-ratio:16/9;background:red'></div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        // 160px x 90px = 120pt x 67.5pt
        text.Should().Contain("120.00 67.50 re f");
    }

    [Fact]
    public void OrphansWidows_ShortParagraph_RendersUnbroken()
    {
        // orphans and widows are pagination properties; a one-line paragraph stays intact
        var html = @"
            <style>p { orphans: 3; widows: 2; }</style>
            <p>Paragraph with orphans and widows set.</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "(Paragraph with orphans and widows set.) Tj");
        PdfAssert.PageCount(text).Should().Be(1);
    }

    [Fact]
    public void Hyphens_Auto_BreaksNarrowWordWithHyphen()
    {
        var html = "<p style='hyphens: auto; width: 100px; font-size: 12px'>hyphenation demonstration</p>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "(hyphenation de-) Tj", "(monstration) Tj");
    }

    [Fact]
    public void OrphansWidows_ExplicitValues_PaginatesAllParagraphsInOrder()
    {
        // Long content that will span multiple pages to exercise the pagination logic
        var sb = new System.Text.StringBuilder("<style>p { orphans: 2; widows: 2; }</style>");
        for (int i = 0; i < 200; i++)
            sb.Append($"<p>Paragraph {i}: The quick brown fox jumps over the lazy dog.</p>");
        byte[] pdf = HtmlToPdf.Render(sb.ToString());
        var text = PdfAssert.ValidPdf(pdf, "(Paragraph 0: The quick brown fox jumps over the lazy dog.) Tj",
            "(Paragraph 199: The quick brown fox jumps over the lazy dog.) Tj");
        PdfAssert.PageCount(text).Should().BeGreaterThan(3);
    }

    // === Tier 1 wire-ups ===

    [Fact]
    public void WritingMode_Vertical_RotatesLinesNinetyDegrees()
    {
        var html = "<div style='writing-mode: vertical-rl; width:60px; height:200px'>Vertical text</div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "(Vertical) Tj", "(text) Tj");

        PdfAssert.Count(text, "0.00 1.00 -1.00 0.00").Should().Be(2, "each of the two lines is rotated 90 degrees");
    }

    [Fact]
    public void TextWrap_Balance_BreaksHeadingIntoMoreEvenLines()
    {
        var html = "<h2 style='text-wrap: balance; width: 300px'>A moderately long heading that should be balanced</h2>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "(A moderately) Tj");

        // Greedy wrapping would fit "A moderately long" on the first line; balancing shortens it.
        text.Should().NotContain("(A moderately long) Tj");
    }

    [Fact]
    public void Filter_Grayscale_PaintsRedBoxAsGray()
    {
        var html = "<div style='filter: grayscale(100%); background-color: red; width:100px; height:50px'>Gray box</div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Gray box");

        text.Should().Contain("0.21 0.21 0.21 rg", "grayscale(100%) maps red to its luminance");
        text.Should().NotContain("1.00 0.00 0.00 rg");
    }

    [Fact]
    public void Filter_Brightness_ChangesColor()
    {
        // filter:brightness(0.5) halves the channels of the box background
        var html = "<div style='filter: brightness(0.5); background-color: red; width:100px; height:50px'>Dimmed</div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Dimmed");

        text.Should().Contain("0.50 0.00 0.00 rg");
        text.Should().NotContain("1.00 0.00 0.00 rg");
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
    public void ClipPath_Circle_ClipsWithFourBezierArcsBeforeFill()
    {
        var html = "<div style='clip-path: circle(50%); width:100px; height:100px; background:blue'></div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().MatchRegex(@"(?s)q\n[^\n]* m [^\n]* c [^\n]* c [^\n]* c [^\n]* c h\nW n\n0\.00 0\.00 1\.00 rg\n6\.00 766\.89 75\.00 75\.00 re f\nQ",
            "the circle path is a closed 4-arc curve installed as clip before the blue fill");
    }

    [Fact]
    public void ClipPath_Polygon_ClipsToTriangleBeforeFill()
    {
        var html = "<div style='clip-path: polygon(50% 0%, 100% 100%, 0% 100%); width:100px; height:100px; background:red'></div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("43.50 766.89 m 81.00 841.89 l 6.00 841.89 l h\nW n", "triangle vertices are the box's bottom-center, top-right and top-left");
        text.Should().Contain("1.00 0.00 0.00 rg");
    }

    [Fact]
    public void ColumnRule_BlackRuleSeparatesColumns()
    {
        var html = "<div style='column-count:2; column-rule: 1px solid black; width:400px'>" +
                   "<p>Column one content here.</p><p>Column two content here.</p>" +
                   "</div>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "Column one content here.", "Column two content here.");

        text.Should().Contain("0.75 w\n0.00 0.00 0.00 RG\n156.00 803.49 m 156.00 841.89 l S", "a vertical black rule is stroked between the columns");
        PdfAssert.TextPosition(text, "Column two content here.").X.Should().BeGreaterThan(156f);
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
    public void TableCaption_RendersCaptionAboveCellText()
    {
        var html = "<table><caption>Table Title</caption>" +
                   "<tr><td>Cell</td></tr></table>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf, "(Table Title) Tj", "(Cell) Tj");

        PdfAssert.TextPosition(text, "Table Title").Y.Should().BeGreaterThan(PdfAssert.TextPosition(text, "Cell").Y,
            "the caption is placed above the table rows (PDF y grows upward)");
    }

    // ── <progress> / <meter> rendering ────────────────────────────────────

    [Fact]
    public void Progress_WithValue_FillsSixtyPercentOfTrack()
    {
        var html = "<progress value='60' max='100' style='width:200px; height:20px'></progress>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("6.00 826.89 150.00 15.00 re S", "the 200x20px track outline");
        text.Should().Contain("88.20 12.00 re f", "60% of the inner track width is filled");
    }

    [Fact]
    public void Progress_Indeterminate_PaintsPartialBar()
    {
        var html = "<progress max='100' style='width:200px; height:20px'></progress>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("6.00 826.89 150.00 15.00 re S", "the track outline");
        text.Should().Contain("58.80 12.00 re f", "the indeterminate bar covers a fixed portion of the track");
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
    public void Meter_WithValue_FillsThreeQuartersInGreen()
    {
        var html = "<meter min='0' max='100' value='75'></meter>";
        byte[] pdf = HtmlToPdf.Render(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("0.20 0.70 0.20 rg", "an in-range meter is green");
        text.Should().Contain("42.75 9.00 re f", "75% of the inner track width is filled");
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
