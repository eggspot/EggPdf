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
    public async Task PageMarginBox_BottomCenter_DoesNotCrash()
    {
        // @bottom-center is the most common margin box for page numbers
        var html = @"
            <html><head><style>
                @page {
                    @bottom-center { content: ""Page "" counter(page); }
                }
            </style></head>
            <body><p>Content</p></body></html>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("@page margin boxes should not crash the renderer");
    }

    [Fact]
    public async Task PageMarginBox_TopRight_DoesNotCrash()
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
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("multiple @page margin boxes should not crash");
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
}
