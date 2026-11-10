using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EggPdf.Css.Cascade;
using EggPdf.Html;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class TableCellWithBrokenImageTests
{
    [Fact]
    public async Task DisplayTableCell_SiblingHasEmptyBase64Image_SiblingCellStillRenders()
    {
        var html = @"
            <html><body>
                <div style=""display:table; width:400px"">
                    <div style=""display:table-cell""><img src=""data:image/png;base64,"" /></div>
                    <div style=""display:table-cell""><span>MARKERTEXT</span></div>
                </div>
            </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var pageContents = ExtractPageContentStreams(pdf);

        string allText = string.Join("\n", pageContents);
        allText.Should().Contain("MARKERTEXT",
            "an empty base64 image, a common placeholder before a real QR code or photo is filled in, must degrade " +
            "gracefully and not remove content from a sibling cell");
    }

    [Fact]
    public async Task SignGrid_TwoCellsWithVietnameseText_BothCellsRenderSideBySideWithinPageBounds()
    {
        var html = @"
            <html><head><style>
                @page { size: 400px 800px; margin: 0; }
                body { margin: 0; }
                .sign-grid { display: table; width: 100%; table-layout: fixed; }
                .sign-cell { display: table-cell; padding: 14px 12px; }
                .qr-frame { padding: 6px; background: white; border: 2px solid #0E1322; display: inline-block; }
                .qr-frame img { display: block; width: 60px; height: 60px; }
                .place-date { font-size: 12px; }
                .stamp-space { height: 40px; }
                .signer-name { font-weight: 900; font-size: 11.5px; }
                .signer-title { font-size: 9.5px; }
            </style></head>
            <body>
                <div class=""sign-grid"">
                    <div class=""sign-cell""><div class=""qr-frame""><img src=""data:image/png;base64,"" alt=""M&#227;"" /></div></div>
                    <div class=""sign-cell""><div class=""place-date"">TP. H&#7891; Ch&#237; Minh, ng&#224;y 15</div>
                        <div class=""stamp-space""></div>
                        <div class=""signer-name"">Gi&#225;m &#273;&#7889;c VCRRM</div>
                        <div class=""signer-title"">Ch&#7913;c danh</div></div>
                </div>
            </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var pageContents = ExtractPageContentStreams(pdf);
        pageContents.Should().HaveCount(1);

        var xs = new System.Collections.Generic.List<float>();
        foreach (Match m in Regex.Matches(pageContents[0], @"(-?\d+\.\d+) (-?\d+\.\d+) Td"))
            xs.Add(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

        xs.Should().Contain(x => x > 150f,
            "the second sign-cell's content starts near the middle of the two equal 400px columns and must be painted " +
            "there, not dropped; positions are checked because the Vietnamese text is painted as glyph-id hex, not literal text");
    }

    [Fact]
    public void SignGrid_LayoutDocumentDirectly_NestedGrandchildTextShiftsWithCell()
    {
        var html = @"
            <html><head><style>
                @page { size: 400px 800px; margin: 0; }
                body { margin: 0; }
                .sign-grid { display: table; width: 100%; table-layout: fixed; }
                .sign-cell { display: table-cell; padding: 14px 12px; }
            </style></head>
            <body>
                <div class=""sign-grid"">
                    <div class=""sign-cell""><span>LEFTCELL</span></div>
                    <div class=""sign-cell""><div class=""wrap""><span class=""marker"">RIGHTTEXT</span></div></div>
                </div>
            </body></html>";

        var document = HtmlParser.Parse(html);
        var stylesheets = HtmlToPdf.ExtractStyleSheets(document, null);
        var pageSettings = PageRuleResolver.Resolve(stylesheets);
        var cascadeResolver = new CascadeResolver(stylesheets, mediaType: "print", pageWidth: pageSettings.PageWidthPx);

        var root = BlockLayout.LayoutDocument(document, pageSettings.ContentWidthPx, pageSettings.ContentHeightPx,
            cascadeResolver, fullPageWidth: pageSettings.PageWidthPx, fullPageHeight: pageSettings.PageHeightPx);

        var cells = root.FindAll(b => b.Element?.GetAttribute("class") == "sign-cell");
        cells.Should().HaveCount(2);
        var marker = root.FindAll(b => b.Element?.GetAttribute("class") == "marker").Single();

        marker.X.Should().BeApproximately(cells[1].X, 20f,
            "moving a cell into its column must shift its whole subtree, so a grandchild two levels deep moves with " +
            "it; comparing with the second cell's own X keeps padding from passing a grandchild stuck in the left column");
    }

    /// <summary>Map each PDF page object (in document order) to its decompressed content stream text.</summary>
    private static System.Collections.Generic.List<string> ExtractPageContentStreams(byte[] pdf)
    {
        string doc = Encoding.Latin1.GetString(pdf);
        var result = new System.Collections.Generic.List<string>();

        var kidsMatch = Regex.Match(doc, @"/Type\s*/Pages.*?/Kids\s*\[([^\]]*)\]", RegexOptions.Singleline);
        var kids = kidsMatch.Success
            ? Regex.Matches(kidsMatch.Groups[1].Value, @"(\d+)\s+0\s+R")
            : Regex.Matches(string.Empty, "x");

        foreach (Match kid in kids)
        {
            string pageNum = kid.Groups[1].Value;
            var pageObjMatch = Regex.Match(doc, @"\b" + pageNum + @" 0 obj\s*<<(.*?)>>\s*(?:endobj|stream)", RegexOptions.Singleline);
            if (!pageObjMatch.Success) continue;

            var contentsMatch = Regex.Match(pageObjMatch.Groups[1].Value, @"/Contents\s+(\d+)\s+0\s+R");
            if (!contentsMatch.Success) continue;

            var streamObjMatch = Regex.Match(doc, @"\b" + contentsMatch.Groups[1].Value + @" 0 obj\s*<<.*?>>\s*stream\r?\n", RegexOptions.Singleline);
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
            catch (System.IO.InvalidDataException)
            {
                decoded = raw;
            }

            result.Add(decoded);
        }

        return result;
    }
}
