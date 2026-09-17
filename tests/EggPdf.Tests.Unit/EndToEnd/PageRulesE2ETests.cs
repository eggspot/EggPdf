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
        // Letter = 816px × 1056px at 96dpi → ×0.75 = 612pt × 792pt
        text.Should().Contain("/MediaBox [0 0 612.00 792.00]");
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
        // A4 landscape: swap width/height → 1122.52px × 793.70px at 96dpi
        // In PDF pt: 1122.52 × 0.75 = 841.89pt, 793.70 × 0.75 = 595.28pt
        text.Should().Contain("/MediaBox [0 0 841.89 595.28]");
    }

    [Fact]
    public async Task PageMargin_AffectsContentArea()
    {
        // With large margins, text should still be rendered but within the margin area
        var html = @"
            <html><head><style>
                @page { margin: 100px; }
                body { margin: 0; padding: 0; }
            </style></head>
            <body><p>Content with page margins</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Content with page margins");
        // MediaBox should remain full A4 size (margins don't change MediaBox)
        text.Should().Contain("/MediaBox [0 0 595.28 841.89]");
    }

    [Fact]
    public async Task PageMargin_Asymmetric_ReservesRealBottomMargin()
    {
        // A small top margin with a large bottom margin. The renderer used to derive
        // its per-page content band as `pageHeight - marginTop*2`, treating marginTop
        // as a stand-in for marginBottom — so the true, much larger margin-bottom was
        // silently ignored (it wasn't even forwarded from @page to the renderer).
        // With that bug, the reserved dead zone at the bottom of each page equals
        // marginTop (20px), not the real marginBottom (200px), so text paints deep
        // inside where the margin should be.
        var sb = new StringBuilder();
        sb.Append("<html><head><style>@page { margin-top: 20px; margin-bottom: 200px; } body{margin:0}</style></head><body>");
        for (int i = 0; i < 60; i++)
            sb.Append($"<p>Paragraph {i}: filler text to force this document across multiple pages.</p>");
        sb.Append("</body></html>");

        byte[] pdf = await HtmlToPdf.RenderAsync(sb.ToString());
        var text = Encoding.ASCII.GetString(pdf);

        float marginBottomPt = 200f * 0.75f; // CSS px (96dpi) -> PDF pt

        foreach (Match m in Regex.Matches(text, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \("))
        {
            float y = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            y.Should().BeGreaterOrEqualTo(marginBottomPt - 1f,
                "no text should be painted inside the reserved @page margin-bottom band");
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
        // 500px * 0.75 = 375.00pt, 700px * 0.75 = 525.00pt
        text.Should().Contain("/MediaBox [0 0 375.00 525.00]");
    }

    [Fact]
    public async Task NoPageRule_DefaultA4()
    {
        var html = @"<html><body><p>Default A4</p></body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        // A4 default: 793.70px × 1122.52px at 96dpi → ×0.75 = 595.28pt × 841.89pt
        text.Should().Contain("/MediaBox [0 0 595.28 841.89]");
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
        // A5: 559.37px × 793.70px at 96dpi → ×0.75 = 419.53pt × 595.28pt
        text.Should().Contain("/MediaBox [0 0 419.53 595.28]");
    }

    // ── @page margin boxes ────────────────────────────────────────────────────

    [Fact]
    public async Task PageMarginBox_BottomCenter_TextAppearsOnPage()
    {
        // @bottom-center is the most common margin box for page numbers
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
        // A document spanning multiple pages: the same @bottom-center rule
        // must paint a different resolved page number on each physical page,
        // not the same literal text repeated.
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

        text.Should().Contain("(1) Tj", "page 1's margin box must show 1");
        text.Should().Contain("(2) Tj", "page 2's margin box must show 2");
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
        // 8pt font size selection and a red (1 0 0) fill color must both appear
        // near the margin-box text, confirming the declared styling was read.
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
        // Exactly one page, so the margin box text must appear exactly once —
        // not once for the margin box and again duplicated into body flow.
        Regex.Matches(text, Regex.Escape("(Confidential) Tj")).Count.Should().Be(1);
    }

    [Fact]
    public async Task PageMarginBox_WithPageSizeAndMargin_ParsesBoth()
    {
        // Ensure margin box coexists with regular @page declarations
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
        // Letter: 612pt × 792pt
        text.Should().Contain("/MediaBox [0 0 612.00 792.00]",
            "regular @page declarations should still be parsed when margin boxes are present");
    }

    [Fact]
    public async Task PinBottom_BoxFitsOnPage_MovesNearPageBottom()
    {
        // -eggpdf-pin-bottom: page pins a box (that fits within a single physical page)
        // to that page's bottom content edge, regardless of how much dynamic content
        // precedes it — this is what makes an "acceptance box" trailing a variable-length
        // terms section land flush against the bottom of whichever page it ends up on.
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

        // PDF Y grows upward from the page bottom, so pinning to the bottom edge must
        // produce a substantially SMALLER y than the box's natural (unpinned) flow position.
        pinnedY.Should().BeLessThan(unpinnedY - 100f,
            "pinning should move the box substantially closer to the physical page bottom");
        pinnedY.Should().BeLessThan(60f,
            "pinned box's text should sit near the 20px (=15pt) bottom margin, not mid-page");
    }

    [Fact]
    public async Task PinBottom_BoxTallerThanOnePage_LeftInNaturalFlow()
    {
        // If the marked box itself doesn't fit within a single physical page, pinning it
        // would corrupt layout (there's no single "page bottom" to align to), so it must be
        // left exactly where normal flow placed it rather than forced/clipped.
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
            "an oversized pinned box must still paginate across multiple pages, not collapse to one");
    }

    [Fact]
    public async Task PositionFixed_RepeatsOnEveryPhysicalPage()
    {
        // position:fixed content's containing block is the page origin, not the document
        // flow, so it must be repainted on every physical page rather than assigned to
        // whichever single page its (page-local, near-zero) Y coordinate happens to fall in.
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
            "position:fixed content must repeat on every physical page, not just the first");
    }

    [Fact]
    public async Task PositionFixed_CounterPage_ResolvesPerPhysicalPage()
    {
        // counter(page)/counter(pages) inside a position:fixed footer's ::after content
        // must resolve to each page's real, distinct page number — not "0" (no counter-reset
        // ever defines a counter named "page") and not the same value repeated on every page.
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

        // The counter sentinel forces the "of"/digits onto the embedded CID-font path (any
        // non-WinAnsi character in a run makes the whole font embedded), so the page number
        // shows up as a hex glyph-id run rather than literal ASCII — assert the three pages'
        // footer runs are pairwise distinct instead of string-matching literal digits.
        var footerRuns = pageContents.ConvertAll(ExtractLastTextShowOperand);
        footerRuns.Should().OnlyHaveUniqueItems("each page's counter(page) substitution must differ");
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
            byte[] rawBytes = Encoding.Latin1.GetBytes(raw);

            string decoded;
            try
            {
                using var ms = new System.IO.MemoryStream(rawBytes);
                using var zlib = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress);
                using var outMs = new System.IO.MemoryStream();
                zlib.CopyTo(outMs);
                decoded = Encoding.Latin1.GetString(outMs.ToArray());
            }
            catch
            {
                decoded = raw; // not compressed
            }

            result.Add(decoded);
        }

        return result;
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
        // A <section> bounded by page-break-after: always (a common "one logical chunk per
        // forced break" document structure) can itself be taller than one physical page — e.g.
        // a long table. The old pagination logic treated every forced-break-bounded section as
        // exactly one physical page, so content past the first page's-worth of an oversized
        // section got painted at Y coordinates beyond that single page's canvas: present in the
        // PDF's text objects, but never visible in any viewer. Rows must instead spill onto
        // additional physical pages, same as unbounded flowing content already does.
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

        // Section A + Section C are each one page on their own; if the table section were
        // (buggily) treated as a single physical page regardless of its content height, the
        // whole document would be exactly 3 pages. 40 rows of ~40px+ each need more than one
        // 800px page, so the real count must exceed that.
        pageContents.Count.Should().BeGreaterThan(3,
            "an oversized forced-break section must spill onto extra physical pages, not collapse into one");

        const float pageHeightPt = 800f * 0.75f;
        foreach (var content in pageContents)
        {
            foreach (Match m in Regex.Matches(content, @"(-?\d+\.\d+) (-?\d+\.\d+) Td"))
            {
                float y = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                y.Should().BeInRange(-1f, pageHeightPt + 1f,
                    "text must never be painted outside the physical page it was assigned to");
            }
        }

        // And every row must actually be present — no silent data loss.
        string allText = string.Join("\n", pageContents);
        for (int i = 0; i < 40; i++)
            allText.Should().Contain($"ROW{i}");
    }

    [Fact]
    public async Task Background_BoxSpanningMultiplePages_ClampsToVisiblePortionPerPage()
    {
        // A box taller than one physical page (already proven possible by the pagination fix
        // above) used to have its background/border rectangle painted using its full,
        // unclamped height on every page it touches — correct on the page it starts on, but
        // wildly mispositioned on every continuation page, since box.Height there vastly
        // exceeds what's actually visible. The visible symptom: instead of a full-page fill,
        // only a small, misplaced sliver of color shows and the rest of the page stays blank.
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

        const float pageHeightPt = 800f * 0.75f;
        foreach (var content in pageContents)
        {
            foreach (Match m in Regex.Matches(content, @"([\d.]+) ([\d.]+) ([\d.]+) ([\d.]+) re f"))
            {
                float y = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                float h = float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                y.Should().BeGreaterOrEqualTo(-1f, "a background rect must never start above the physical page it's painted on");
                (y + h).Should().BeLessOrEqualTo(pageHeightPt + 1f,
                    "a background rect must never extend beyond the physical page it's painted on");
            }
        }
    }

    [Fact]
    public async Task PositionFixed_BottomZero_ReachesPhysicalPageBottomEdge()
    {
        // position:fixed's containing block must be the full physical page, not the content
        // area (which is already shrunk by @page margin-bottom) — otherwise "bottom: 0" lands
        // short of the true page edge, inside the content area instead of the margin area
        // reserved for it, and collides with ordinary flowing body text placed near the bottom
        // of the page.
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

        const float expectedFooterHeightPt = 50f * 0.75f;
        bool foundFooterAtPhysicalBottom = false;
        foreach (Match m in Regex.Matches(pageContents[0], @"([\d.]+) ([\d.]+) ([\d.]+) ([\d.]+) re f"))
        {
            float y = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            float h = float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (Math.Abs(y) < 0.5f && Math.Abs(h - expectedFooterHeightPt) < 0.5f)
                foundFooterAtPhysicalBottom = true;
        }
        foundFooterAtPhysicalBottom.Should().BeTrue(
            "a position:fixed footer with bottom:0 must reach the literal physical page bottom edge (y=0), not stop at the content area's own bottom edge");
    }
}
