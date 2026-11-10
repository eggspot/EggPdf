using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class PageRulesE2ETests
{
    private const float PdfPointsPerCssPixel = 0.75f;

    [Fact]
    public async Task PageSizeLetter_ChangesDimensions()
    {
        var html = @"
            <html><head><style>
                @page { size: letter; }
            </style></head>
            <body><p>Letter sized page</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/MediaBox [0 0 612.00 792.00]", "Letter is 816 x 1056 CSS px, 612 x 792 pt");
    }

    [Fact]
    public async Task PageSizeA4Landscape_SwapsDimensions()
    {
        var html = @"
            <html><head><style>
                @page { size: A4 landscape; }
            </style></head>
            <body><p>Landscape A4</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/MediaBox [0 0 841.89 595.28]",
            "landscape swaps A4's width and height: 1122.52 x 793.70 CSS px, 841.89 x 595.28 pt");
    }

    [Fact]
    public async Task PageMargin_AffectsContentArea()
    {
        var html = @"
            <html><head><style>
                @page { margin: 100px; }
                body { margin: 0; padding: 0; }
            </style></head>
            <body><p>Content with page margins</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Content with page margins", "text still renders inside large page margins");
        text.Should().Contain("/MediaBox [0 0 595.28 841.89]", "page margins don't change the MediaBox, which stays full A4");
    }

    [Fact]
    public async Task PageMargin_Asymmetric_ReservesRealBottomMargin()
    {
        var sb = new StringBuilder();
        sb.Append("<html><head><style>@page { margin-top: 20px; margin-bottom: 200px; } body{margin:0}</style></head><body>");
        for (int i = 0; i < 60; i++)
            sb.Append($"<p>Paragraph {i}: filler text to force this document across multiple pages.</p>");
        sb.Append("</body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var text = Encoding.ASCII.GetString(pdf);

        float marginBottomPt = 200f * PdfPointsPerCssPixel;

        foreach (Match m in Regex.Matches(text, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \("))
        {
            float y = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            y.Should().BeGreaterOrEqualTo(marginBottomPt - 1f,
                "the 200px margin-bottom is reserved on every page; it must not be mistaken for the 20px margin-top");
        }
    }

    [Fact]
    public async Task PageSizeCustom_WorksWithPixels()
    {
        var html = @"
            <html><head><style>
                @page { size: 500px 700px; }
            </style></head>
            <body><p>Custom sized page</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/MediaBox [0 0 375.00 525.00]", "500 x 700 CSS px is 375 x 525 pt");
    }

    [Fact]
    public async Task NoPageRule_DefaultA4()
    {
        var html = @"<html><body><p>Default A4</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/MediaBox [0 0 595.28 841.89]", "the default A4 is 793.70 x 1122.52 CSS px, 595.28 x 841.89 pt");
    }

    [Fact]
    public async Task MultiplePageRules_LastWins()
    {
        var html = @"
            <html><head><style>
                @page { size: letter; }
                @page { size: A5; }
            </style></head>
            <body><p>Last rule wins</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/MediaBox [0 0 419.53 595.28]", "A5 is 559.37 x 793.70 CSS px, 419.53 x 595.28 pt");
    }

    [Fact]
    public async Task PageMarginBox_BottomCenter_TextAppearsOnPage()
    {
        var html = @"
            <html><head><style>
                @page {
                    margin: 20mm;
                    @bottom-center { content: ""Page "" counter(page); }
                }
            </style></head>
            <body><p>Content</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("(Page 1) Tj",
            "the margin box's counter(page) sentinel must be substituted with the real page number");
    }

    [Fact]
    public async Task PageMarginBox_TopRightAndBottomLeft_BothAppear()
    {
        var html = @"
            <html><head><style>
                @page {
                    size: A4;
                    margin: 25mm;
                    @top-right { content: ""Chapter 1""; }
                    @bottom-left { content: counter(page) "" of "" counter(pages); }
                }
            </style></head>
            <body><p>Body text here</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("(Chapter 1) Tj", "the @top-right margin box must render its static content");
        text.Should().Contain("(1 of 1) Tj",
            "the @bottom-left margin box must resolve counter(page) and counter(pages) on a single-page document");
    }

    [Fact]
    public async Task PageMarginBox_CounterPage_ResolvesDifferentlyPerPhysicalPage()
    {
        var sb = new StringBuilder(@"
            <html><head><style>
                @page {
                    margin: 15mm;
                    @bottom-center { content: counter(page); }
                }
            </style></head><body>");
        for (int i = 0; i < 100; i++)
            sb.Append($"<p>Paragraph {i}: enough repeated content to force pagination across pages.</p>");
        sb.Append("</body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var text = Encoding.ASCII.GetString(pdf);

        var pageCount = Regex.Matches(text, @"/Type /Page[^s]").Count;
        pageCount.Should().BeGreaterThan(1, "100 paragraphs must actually paginate for this test to be meaningful");

        text.Should().Contain("(1) Tj", "page 1's margin box must show 1, not the same literal text on every page");
        text.Should().Contain("(2) Tj", "page 2's margin box must show 2, not the same literal text on every page");
    }

    [Fact]
    public async Task PageMarginBox_FontSizeAndColor_AreApplied()
    {
        var html = @"
            <html><head><style>
                @page {
                    margin: 20mm;
                    @bottom-center { content: ""Draft""; font-size: 8pt; color: red; }
                }
            </style></head>
            <body><p>Content</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("(Draft) Tj");
        text.Should().Contain("8.00 Tf",
            "the margin box's font-size:8pt must select an 8pt font size");
        text.Should().Contain("1.00 0.00 0.00 rg",
            "the margin box's color:red must set a red non-stroking fill color");
    }

    [Fact]
    public async Task PageMarginBox_ContentDoesNotLeakIntoBodyFlow()
    {
        var html = @"
            <html><head><style>
                @page {
                    margin: 20mm;
                    @bottom-center { content: ""Confidential""; }
                }
            </style></head>
            <body><p>Ordinary paragraph text</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("(Confidential) Tj");
        text.Should().Contain("(Ordinary paragraph text) Tj");
        Regex.Matches(text, Regex.Escape("(Confidential) Tj")).Count.Should().Be(1,
            "on a one-page document the margin box text appears once, not again duplicated into the body flow");
    }

    [Fact]
    public async Task PageMarginBoxFirst_SuppressesHeaderOnFirstPageOnly()
    {
        var sb = new StringBuilder(@"
            <html><head><style>
                @page {
                    margin: 15mm;
                    @top-center { content: ""Running Title""; }
                }
                @page :first {
                    @top-center { content: none; }
                }
            </style></head><body>");
        for (int i = 0; i < 100; i++)
            sb.Append($"<p>Paragraph {i}: enough content to force a second physical page.</p>");
        sb.Append("</body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var text = Encoding.ASCII.GetString(pdf);

        var pageCount = Regex.Matches(text, @"/Type /Page[^s]").Count;
        pageCount.Should().BeGreaterThan(1, "100 paragraphs must actually paginate for this test to be meaningful");

        Regex.Matches(text, Regex.Escape("(Running Title) Tj")).Count.Should().Be(pageCount - 1,
            "the header must be suppressed on page 1 by :first, but still appear on every other page");
    }

    [Fact]
    public async Task PageMarginBoxFirst_OverridesContentOnFirstPageOnly()
    {
        var sb = new StringBuilder(@"
            <html><head><style>
                @page {
                    margin: 15mm;
                    @bottom-center { content: counter(page); }
                }
                @page :first {
                    @bottom-center { content: ""Cover""; }
                }
            </style></head><body>");
        for (int i = 0; i < 100; i++)
            sb.Append($"<p>Paragraph {i}: enough content to force a second physical page.</p>");
        sb.Append("</body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("(Cover) Tj", "page 1 must use the :first override instead of the page-number base rule");
        text.Should().Contain("(2) Tj", "page 2 must fall back to the base rule's counter(page) since :first only applies to page 1");
        text.Should().NotContain("(1) Tj", "the base rule's content must not also render on page 1 alongside the :first override");
    }

    [Fact]
    public async Task PageMarginBoxLeftRight_AlternateAcrossPages()
    {
        var sb = new StringBuilder(@"
            <html><head><style>
                @page { margin: 15mm; }
                @page :left  { @top-center { content: ""Even Page""; } }
                @page :right { @top-center { content: ""Odd Page""; } }
            </style></head><body>");
        for (int i = 0; i < 200; i++)
            sb.Append($"<p>Paragraph {i}: enough content to force several physical pages.</p>");
        sb.Append("</body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var text = Encoding.ASCII.GetString(pdf);

        var pageCount = Regex.Matches(text, @"/Type /Page[^s]").Count;
        pageCount.Should().BeGreaterThanOrEqualTo(3, "need at least pages 1-3 for this test to exercise :right (page 1), :left (page 2) and :right (page 3)");

        text.Should().Contain("(Odd Page) Tj", ":right must apply on odd pages (1, 3, ...)");
        text.Should().Contain("(Even Page) Tj", ":left must apply on even pages (2, 4, ...)");
    }

    [Fact]
    public async Task PageMarginBoxFirst_TakesPriorityOverRightOnPageOne()
    {
        var html = @"
            <html><head><style>
                @page { margin: 15mm; }
                @page :first { @top-center { content: ""First""; } }
                @page :right { @top-center { content: ""Right""; } }
            </style></head>
            <body><p>Single page content</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("(First) Tj", "page 1 is an odd page and matches :right too, but :first must win there");
        text.Should().NotContain("(Right) Tj", ":right must not apply to page 1 when :first also targets it");
    }

    [Fact]
    public async Task PageMarginBox_WithPageSizeAndMargin_ParsesBoth()
    {
        var html = @"
            <html><head><style>
                @page {
                    size: letter;
                    margin: 1in;
                    @bottom-center { content: counter(page); }
                }
            </style></head>
            <body><p>Letter page</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/MediaBox [0 0 612.00 792.00]",
            "regular @page declarations such as size: letter should still be parsed when margin boxes are present");
    }

    [Fact]
    public async Task PinBottom_BoxFitsOnPage_MovesNearPageBottom()
    {
        string BuildHtml(bool pinned) => $@"
            <html><head><style>
                @page {{ size: 400px 800px; margin: 20px; }}
                body {{ margin: 0; }}
                .filler {{ height: 100px; }}
                .accept {{ {(pinned ? "-eggpdf-pin-bottom: page;" : "")} }}
            </style></head><body>
                <div class=""filler""></div>
                <div class=""accept"">PINMARK</div>
            </body></html>";

        byte[] pinnedPdf = await HtmlToPdf.RenderAsync(BuildHtml(pinned: true));
        byte[] unpinnedPdf = await HtmlToPdf.RenderAsync(BuildHtml(pinned: false));

        float pinnedY = ExtractMarkerY(pinnedPdf, "PINMARK");
        float unpinnedY = ExtractMarkerY(unpinnedPdf, "PINMARK");

        pinnedY.Should().BeLessThan(unpinnedY - 100f,
            "-eggpdf-pin-bottom: page moves a box that fits on one page to that page's bottom content edge, " +
            "and PDF y grows upward, so it ends up much lower than its natural flow position");
        pinnedY.Should().BeLessThan(60f,
            "pinned box's text should sit near the 20px (=15pt) bottom margin, not mid-page");
    }

    [Fact]
    public async Task PinBottom_BoxTallerThanOnePage_LeftInNaturalFlow()
    {
        var html = @"
            <html><head><style>
                @page { size: 400px 800px; margin: 20px; }
                body { margin: 0; }
                .accept { -eggpdf-pin-bottom: page; height: 2000px; }
            </style></head><body>
                <div class=""accept"">PINMARK</div>
                <p>AFTERMARK</p>
            </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("PINMARK");
        text.Should().Contain("AFTERMARK");
        Regex.Matches(text, @"/MediaBox").Count.Should().BeGreaterThan(1,
            "a pinned box taller than a page has no single page bottom to align to, so it stays in normal flow " +
            "and still paginates across several pages");
    }

    [Fact]
    public async Task PositionFixed_RepeatsOnEveryPhysicalPage()
    {
        var html = @"
            <html><head><style>
                @page { size: 400px 800px; }
                body { margin: 0; }
                .hdr { position: fixed; top: 0; left: 0; }
                .filler { height: 1650px; background: #eee; }
            </style></head><body>
                <div class=""hdr"">HEADERMARK</div>
                <div class=""filler""></div>
            </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        int pageCount = Regex.Matches(text, "/MediaBox").Count;
        pageCount.Should().BeGreaterThan(1, "the filler must force multiple physical pages");
        Regex.Matches(text, "HEADERMARK").Count.Should().Be(pageCount,
            "a position:fixed box's containing block is the page, so it repeats on every physical page");
    }

    [Fact]
    public async Task PositionFixed_CounterPage_ResolvesPerPhysicalPage()
    {
        var html = @"
            <html><head><style>
                @page { size: 400px 800px; }
                body { margin: 0; }
                .ftr { position: fixed; bottom: 0; left: 0; }
                .ftr::after { content: ""P"" counter(page) ""of"" counter(pages); }
                .filler { height: 1650px; background: #eee; }
            </style></head><body>
                <div class=""filler""></div>
                <div class=""ftr""></div>
            </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var pageContents = ExtractPageContentStreams(pdf);
        pageContents.Count.Should().Be(3, "1650px over an 800px page should span 3 pages");

        var footerRuns = pageContents.ConvertAll(ExtractLastTextShowOperand);
        footerRuns.Should().OnlyHaveUniqueItems(
            "each page's counter(page) resolves to its own page number, not 0 or one repeated value; the runs are " +
            "compared as glyph-id hex because the counter sentinel puts the footer on the embedded CID font");
    }

    /// <summary>Map each PDF page object (in document order) to its decompressed content stream text.</summary>
    private static System.Collections.Generic.List<string> ExtractPageContentStreams(byte[] pdf)
    {
        string doc = Encoding.Latin1.GetString(pdf);
        var result = new System.Collections.Generic.List<string>();

        foreach (Match pageMatch in Regex.Matches(doc, @"\d+ 0 obj\s*<<(?:(?!>>).)*?/Type\s*/Page[^s](?:(?!>>).)*?>>", RegexOptions.Singleline))
        {
            var contentsMatch = Regex.Match(pageMatch.Value, @"/Contents\s+(\d+)\s+0\s+R");
            if (!contentsMatch.Success) continue;

            var streamObjMatch = Regex.Match(doc, contentsMatch.Groups[1].Value + @" 0 obj\s*<<.*?>>\s*stream\r?\n", RegexOptions.Singleline);
            if (!streamObjMatch.Success) continue;

            int start = streamObjMatch.Index + streamObjMatch.Length;
            int end = doc.IndexOf("endstream", start, System.StringComparison.Ordinal);
            string raw = doc.Substring(start, end - start).TrimEnd('\r', '\n');

            result.Add(InflateOrKeepRaw(raw));
        }

        return result;
    }

    private static string InflateOrKeepRaw(string raw)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(Encoding.Latin1.GetBytes(raw));
            using var zlib = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress);
            using var outMs = new System.IO.MemoryStream();
            zlib.CopyTo(outMs);
            return Encoding.Latin1.GetString(outMs.ToArray());
        }
        catch
        {
            return raw;
        }
    }

    /// <summary>Extract the operand of the last text-show operator (Tj, in "(...)" or "&lt;...&gt;" form) in a content stream.</summary>
    private static string ExtractLastTextShowOperand(string contentStream)
    {
        var matches = Regex.Matches(contentStream, @"[(<]([^)>]*)[)>] Tj");
        matches.Count.Should().BeGreaterThan(0, "expected at least one text-show operator in the page content stream");
        return matches[matches.Count - 1].Groups[1].Value;
    }

    private static float ExtractMarkerY(byte[] pdf, string marker)
    {
        string text = Encoding.ASCII.GetString(pdf);
        var m = Regex.Match(text, $@"(-?\d+\.\d+) (-?\d+\.\d+) Td \([^)]*{marker}");
        m.Success.Should().BeTrue("expected to find the marker text's positioning operator");
        return float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task TablePagination_SectionTallerThanOnePage_AllRowsPaintedWithinPageBounds()
    {
        var sb = new StringBuilder();
        sb.Append(@"<html><head><style>
            @page { size: 400px 800px; margin: 0; }
            body { margin: 0; }
            .page { page-break-after: always; }
            table { border-collapse: collapse; width: 100%; }
            td { height: 40px; }
        </style></head><body>
        <section class=""page""><p>SECTIONA</p></section>
        <section class=""page""><table><tbody>");
        for (int i = 0; i < 40; i++)
            sb.Append($@"<tr><td>ROW{i}</td></tr>");
        sb.Append(@"</tbody></table></section>
        <section class=""page""><p>SECTIONC</p></section>
        </body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var pageContents = ExtractPageContentStreams(pdf);

        pageContents.Count.Should().BeGreaterThan(3,
            "sections A and C take a page each, and the table section's 40 rows of 40px need more than one 800px page: " +
            "a section between forced breaks spills onto extra pages instead of being squeezed into one");

        const float pageHeightPt = 800f * PdfPointsPerCssPixel;
        foreach (var content in pageContents)
        {
            foreach (Match m in Regex.Matches(content, @"(-?\d+\.\d+) (-?\d+\.\d+) Td"))
            {
                float y = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                y.Should().BeInRange(-1f, pageHeightPt + 1f,
                    "text must never be painted outside the physical page it was assigned to, where no viewer shows it");
            }
        }

        string allText = string.Join("\n", pageContents);
        for (int i = 0; i < 40; i++)
            allText.Should().Contain($"ROW{i}", "no row may be lost");
    }

    [Fact]
    public async Task Background_BoxSpanningMultiplePages_IsPaintedWholeAndClippedToEachPage()
    {
        var html = @"
            <html><head><style>
                @page { size: 400px 800px; margin-bottom: 200px; }
                body { margin: 0; }
                .box { width: 100%; min-height: 800px; background: #336699; box-sizing: border-box; }
            </style></head><body>
                <div class=""box"">Content</div>
            </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var pageContents = ExtractPageContentStreams(pdf);
        pageContents.Count.Should().BeGreaterThan(1, "an 800px box over a 600px content band must spill onto a second page");

        const float pageHeightPt = 800f * PdfPointsPerCssPixel;
        foreach (var content in pageContents)
        {
            var clip = Regex.Match(content, @"(-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) re W n\n0\.20 0\.40 0\.60 rg\n(-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) re f");
            clip.Success.Should().BeTrue(
                "on every page the box is painted whole and clipped to the page's content band, not drawn at its full " +
                "height from a continuation page's origin");
            float clipY = float.Parse(clip.Groups[2].Value, CultureInfo.InvariantCulture);
            float clipTop = clipY + float.Parse(clip.Groups[4].Value, CultureInfo.InvariantCulture);
            float fillY = float.Parse(clip.Groups[6].Value, CultureInfo.InvariantCulture);
            float fillTop = fillY + float.Parse(clip.Groups[8].Value, CultureInfo.InvariantCulture);
            clipY.Should().BeGreaterOrEqualTo(-1f);
            clipTop.Should().BeLessOrEqualTo(pageHeightPt + 1f, "the clip never extends beyond the physical page");
            fillY.Should().BeLessOrEqualTo(clipY + 0.5f, "the fill covers the whole visible band");
            fillTop.Should().BeGreaterOrEqualTo(clipTop - 0.5f);
        }
    }

    [Fact]
    public async Task PositionFixed_BottomZero_SitsOnThePageAreaBottomEdge()
    {
        var html = @"
            <html><head><style>
                @page { size: 400px 800px; margin-bottom: 100px; }
                body { margin: 0; }
                .footer { position: fixed; bottom: 0; left: 0; right: 0; height: 50px; background: #f00; }
            </style></head><body>
                <div class=""footer""></div>
                <p>Hello</p>
            </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var pageContents = ExtractPageContentStreams(pdf);
        pageContents.Count.Should().Be(1);

        const float expectedFooterHeightPt = 50f * PdfPointsPerCssPixel;
        const float pageAreaBottomPt = 100f * PdfPointsPerCssPixel;
        bool foundFooterOnPageAreaBottom = false;
        foreach (Match m in Regex.Matches(pageContents[0], @"([\d.]+) ([\d.]+) ([\d.]+) ([\d.]+) re f"))
        {
            float y = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            float h = float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (Math.Abs(y - pageAreaBottomPt) < 0.5f && Math.Abs(h - expectedFooterHeightPt) < 0.5f)
                foundFooterOnPageAreaBottom = true;
        }
        foundFooterOnPageAreaBottom.Should().BeTrue(
            "like Chrome, a position:fixed box's containing block is the page area inside the @page margins (CSS 2.1 §10.1)");
    }

    [Fact]
    public async Task FluentFooter_StaysInThePageMargin()
    {
        var pdf = await Fluent.Document.Create(doc =>
        {
            var page = doc.AddPage();
            page.Margin(Fluent.Length.Px(100));
            page.Footer(f => f.RawStyle("height", "50px").Background(Fluent.Colors.Red));
            page.Content(c => c.Item().Text("Hello"));
        }).RenderAsync();
        var pageContents = ExtractPageContentStreams(pdf);

        bool footerAtSheetBottom = false;
        foreach (Match m in Regex.Matches(pageContents[0], @"([\d.]+) ([\d.]+) ([\d.]+) ([\d.]+) re f"))
        {
            float y = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            float h = float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (Math.Abs(y) < 0.5f && Math.Abs(h - 50f * PdfPointsPerCssPixel) < 0.5f) footerAtSheetBottom = true;
        }
        footerAtSheetBottom.Should().BeTrue("the fluent footer is drawn in the page margin, against the whole sheet");
    }
}
