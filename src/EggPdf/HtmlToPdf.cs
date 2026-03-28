using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EggPdf.Core;
using EggPdf.Css.Cascade;
using EggPdf.Css.Parser;
using EggPdf.Html;
using EggPdf.Html.Dom;
using EggPdf.Layout;
using EggPdf.Pdf;

namespace EggPdf;

/// <summary>
/// Static convenience class for HTML-to-PDF conversion.
/// For advanced usage, use HtmlToPdfConverter with PdfOptions.
/// </summary>
public static class HtmlToPdf
{
    private const float DefaultPageWidthPx = 595.28f;   // A4 width
    private const float DefaultPageHeightPx = 841.89f;  // A4 height
    private const int MaxImportDepth = 10;

    /// <summary>Render HTML to PDF as byte array.</summary>
    public static Task<byte[]> RenderAsync(string? html, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Render(html ?? ""));
    }

    /// <summary>Render HTML to PDF and write to a stream.</summary>
    public static Task RenderAsync(string? html, Stream output, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var bytes = Render(html ?? "");
        return output.WriteAsync(bytes, 0, bytes.Length, ct);
    }

    /// <summary>Render HTML to PDF and save to a file.</summary>
    public static async Task RenderToFileAsync(string? html, string filePath, CancellationToken ct = default)
    {
        var bytes = Render(html ?? "");
#if NET6_0_OR_GREATER
        await File.WriteAllBytesAsync(filePath, bytes, ct);
#else
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await fs.WriteAsync(bytes, 0, bytes.Length, ct);
#endif
    }

    /// <summary>Render HTML to PDF synchronously.</summary>
    public static byte[] Render(string? html)
    {
        return RenderInternal(html ?? "", null);
    }

    /// <summary>Render HTML to PDF synchronously with a base path for resolving external resources.</summary>
    public static byte[] Render(string? html, string? basePath)
    {
        return RenderInternal(html ?? "", basePath);
    }

    private static byte[] RenderInternal(string html, string? basePath)
    {
        // 1. Parse HTML -> DOM
        var document = HtmlParser.Parse(html);

        // 2. Extract <style> tags, <link> stylesheets, and resolve @imports
        var stylesheets = ExtractStyleSheets(document, basePath);

        // 3. Resolve @page rules for page size and margins
        var pageSettings = PageRuleResolver.Resolve(stylesheets);
        float pageWidthPx = pageSettings.PageWidthPx;
        float pageHeightPx = pageSettings.PageHeightPx;
        float contentWidthPx = pageSettings.ContentWidthPx;
        float contentHeightPx = pageSettings.ContentHeightPx;

        // 4. Create CascadeResolver with parsed stylesheets (replaces BasicStyleResolver)
        var cascadeResolver = new CascadeResolver(stylesheets, mediaType: "print");

        // 5. Layout (uses cascade resolver for full CSS support)
        // Layout uses content area (page minus margins) for body width
        var layoutRoot = BlockLayout.LayoutDocument(document, contentWidthPx, contentHeightPx, cascadeResolver);

        // 6. Resolve images (load data from src attributes)
        var pdfDoc = new PdfDocument();
        ResolveImages(layoutRoot, pdfDoc);

        // 6b. Subset and embed TrueType fonts for non-standard fonts
        SubsetAndEmbedFonts(layoutRoot, pdfDoc);

        // 7. Render to PDF
        float pageWidthPt = pageWidthPx * PdfCoordinates.PxToPt;
        float pageHeightPt = pageHeightPx * PdfCoordinates.PxToPt;

        PdfRenderer.Render(layoutRoot, pdfDoc, pageWidthPt, pageHeightPt, pageHeightPx,
            pageSettings.MarginLeft, pageSettings.MarginTop);

        return pdfDoc.ToByteArray();
    }

    /// <summary>
    /// Extract CSS from &lt;style&gt; tags, &lt;link&gt; stylesheets, and resolve @import rules.
    /// </summary>
    internal static List<CssStyleSheet> ExtractStyleSheets(HtmlDocument document, string? basePath = null)
    {
        var sheets = new List<CssStyleSheet>();
        var visitedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Find <link> and <style> elements in <head>
        var head = document.Head;
        if (head != null)
        {
            foreach (var node in head.ChildNodes)
            {
                if (node is HtmlElement elem)
                {
                    if (elem.TagName == "link")
                    {
                        var linkSheet = LoadLinkStylesheet(elem, basePath, visitedUrls);
                        if (linkSheet != null)
                            sheets.Add(linkSheet);
                    }
                    else if (elem.TagName == "style")
                    {
                        var cssText = GetElementText(elem);
                        if (!string.IsNullOrWhiteSpace(cssText))
                        {
                            var sheet = CssStyleSheetParser.Parse(cssText);
                            ResolveImports(sheet, sheets, basePath, visitedUrls, 0);
                            sheets.Add(sheet);
                        }
                    }
                }
            }
        }

        // Also find <style> elements in <body> (common in email HTML, CMS output)
        var body = document.Body;
        if (body != null)
            FindStyleElements(body, sheets, basePath, visitedUrls);

        return sheets;
    }

    /// <summary>Load a stylesheet from a &lt;link rel="stylesheet"&gt; element.</summary>
    private static CssStyleSheet? LoadLinkStylesheet(HtmlElement linkElement, string? basePath, HashSet<string> visitedUrls)
    {
        // Must have rel="stylesheet"
        var rel = linkElement.GetAttribute("rel");
        if (rel == null || rel.IndexOf("stylesheet", StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        var href = linkElement.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
            return null;

        // Check media attribute: load if absent, "all", or "print"
        var media = linkElement.GetAttribute("media");
        if (media != null)
        {
            var mediaTrimmed = media.Trim().ToLowerInvariant();
            if (mediaTrimmed.Length > 0 &&
                mediaTrimmed != "all" &&
                mediaTrimmed != "print")
                return null;
        }

        var cssText = LoadCssText(href, basePath);
        if (string.IsNullOrWhiteSpace(cssText))
            return null;

        // Track visited URLs to prevent circular references
        var resolvedUrl = ResolveUrl(href, basePath) ?? href;
        if (!visitedUrls.Add(resolvedUrl))
            return null;

        var sheet = CssStyleSheetParser.Parse(cssText);
        // Determine base path for this stylesheet's @import rules
        var sheetBasePath = GetBasePathForUrl(href, basePath);
        ResolveImports(sheet, new List<CssStyleSheet>(), sheetBasePath, visitedUrls, 0);
        return sheet;
    }

    /// <summary>Resolve @import rules in a stylesheet, inserting imported sheets before the importing sheet.</summary>
    private static void ResolveImports(CssStyleSheet sheet, List<CssStyleSheet> targetList, string? basePath, HashSet<string> visitedUrls, int depth)
    {
        if (depth >= MaxImportDepth) return;
        if (sheet.ImportRules.Count == 0) return;

        // Insert position: imported sheets go before the current sheet in the target list
        int insertIndex = targetList.Count;

        for (int i = 0; i < sheet.ImportRules.Count; i++)
        {
            var importRule = sheet.ImportRules[i];
            var url = importRule.Url;
            if (string.IsNullOrWhiteSpace(url)) continue;

            // Check media query: load if absent, "all", or "print"
            if (importRule.MediaQuery != null)
            {
                var mq = importRule.MediaQuery.Trim().ToLowerInvariant();
                if (mq.Length > 0 && mq != "all" && mq != "print")
                    continue;
            }

            var resolvedUrl = ResolveUrl(url, basePath) ?? url;
            if (!visitedUrls.Add(resolvedUrl))
                continue; // already loaded or circular

            var cssText = LoadCssText(url, basePath);
            if (string.IsNullOrWhiteSpace(cssText))
                continue;

            var importedSheet = CssStyleSheetParser.Parse(cssText);
            var importBasePath = GetBasePathForUrl(url, basePath);

            // Recursively resolve @imports in the imported sheet
            ResolveImports(importedSheet, targetList, importBasePath, visitedUrls, depth + 1);

            // Insert before current sheet's position
            targetList.Insert(insertIndex, importedSheet);
            insertIndex++;
        }

        // Clear import rules after processing
        sheet.ImportRules.Clear();
    }

    /// <summary>Load CSS text from a URL (data: URI or file path).</summary>
    internal static string? LoadCssText(string url, string? basePath)
    {
        // Data URI: data:text/css;base64,...  or  data:text/css,...
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeDataUri(url);
        }

        // Resolve relative path against basePath
        var filePath = ResolveUrl(url, basePath) ?? url;

        try
        {
            if (File.Exists(filePath))
                return File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch
        {
            // Permission denied, etc. -- silently ignore
        }

        return null;
    }

    /// <summary>Decode a data: URI to its text content.</summary>
    private static string? DecodeDataUri(string dataUri)
    {
        // data:[<mediatype>][;base64],<data>
        int commaIndex = dataUri.IndexOf(',');
        if (commaIndex < 0) return null;

        var header = dataUri.Substring(5, commaIndex - 5); // after "data:" before ","
        var data = dataUri.Substring(commaIndex + 1);

        bool isBase64 = header.IndexOf("base64", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isBase64)
        {
            try
            {
                var bytes = Convert.FromBase64String(data);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        // Plain text (possibly percent-encoded)
        try
        {
            return Uri.UnescapeDataString(data);
        }
        catch
        {
            return data;
        }
    }

    /// <summary>Resolve a relative URL against a base path.</summary>
    private static string? ResolveUrl(string url, string? basePath)
    {
        if (string.IsNullOrEmpty(basePath)) return null;
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return url;

        // Already absolute
        if (Path.IsPathRooted(url)) return url;

        try
        {
            return Path.GetFullPath(Path.Combine(basePath, url));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Get the base path for resolving relative URLs within a stylesheet.</summary>
    private static string? GetBasePathForUrl(string url, string? currentBasePath)
    {
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return currentBasePath;

        var resolvedPath = ResolveUrl(url, currentBasePath) ?? url;
        try
        {
            var dir = Path.GetDirectoryName(resolvedPath);
            return string.IsNullOrEmpty(dir) ? currentBasePath : dir;
        }
        catch
        {
            return currentBasePath;
        }
    }

    private static void FindStyleElements(HtmlNode node, List<CssStyleSheet> sheets, string? basePath, HashSet<string> visitedUrls)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is HtmlElement elem && elem.TagName == "style")
            {
                var cssText = GetElementText(elem);
                if (!string.IsNullOrWhiteSpace(cssText))
                {
                    var sheet = CssStyleSheetParser.Parse(cssText);
                    ResolveImports(sheet, sheets, basePath, visitedUrls, 0);
                    sheets.Add(sheet);
                }
            }
            else if (child is HtmlElement container)
            {
                FindStyleElements(container, sheets, basePath, visitedUrls);
            }
        }
    }

    private static string? GetElementText(HtmlElement element)
    {
        foreach (var child in element.ChildNodes)
            if (child is HtmlTextNode text)
                return text.Data;
        return null;
    }

    /// <summary>Walk the layout tree and resolve image sources to byte data.</summary>
    private static void ResolveImages(LayoutBox box, PdfDocument pdfDoc)
    {
        if (!string.IsNullOrEmpty(box.ImageSource))
        {
            var data = LoadImageData(box.ImageSource);
            if (data != null)
            {
                box.ImageData = data;
                string imgName = "Img" + box.ImageSource.GetHashCode().ToString("X8");
                PdfImage? pdfImage = null;

                // Detect format by magic bytes
                if (data.Length >= 8 &&
                    data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71 &&
                    data[4] == 13 && data[5] == 10 && data[6] == 26 && data[7] == 10)
                {
                    // PNG signature
                    pdfImage = PdfImage.FromPng(imgName, data);
                }
                else if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
                {
                    // JPEG SOI marker
                    pdfImage = PdfImage.FromJpeg(imgName, data);
                }
                else if (data.Length >= 4 &&
                    data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
                {
                    // GIF signature ("GIF8")
                    pdfImage = PdfImage.FromGif(imgName, data);
                }
                else if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D)
                {
                    // BMP signature ("BM")
                    pdfImage = PdfImage.FromBmp(imgName, data);
                }

                if (pdfImage != null)
                {
                    pdfDoc.AddImage(pdfImage);
                }
            }
        }

        foreach (var child in box.Children)
            ResolveImages(child, pdfDoc);
    }

    /// <summary>Load image data from a source string (data URI or file path).</summary>
    private static byte[]? LoadImageData(string src)
    {
        // Data URI: data:image/jpeg;base64,...
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int commaIndex = src.IndexOf(',');
            if (commaIndex > 0)
            {
                var base64 = src.Substring(commaIndex + 1);
                try
                {
                    return Convert.FromBase64String(base64);
                }
                catch
                {
                    return null; // invalid base64
                }
            }
        }

        // File path
        try
        {
            if (File.Exists(src))
                return File.ReadAllBytes(src);
        }
        catch
        {
            // Permission denied, etc.
        }

        return null;
    }

    /// <summary>
    /// Walk the layout tree, find text using TrueType system fonts, subset them,
    /// and register as CIDFont Type 2 in the PDF document.
    /// </summary>
    private static void SubsetAndEmbedFonts(LayoutBox root, PdfDocument pdfDoc)
    {
        // Collect all (fontName, codepoints) used in the document
        var fontCodepoints = new Dictionary<string, HashSet<int>>();
        CollectTextCodepoints(root, fontCodepoints);

        if (fontCodepoints.Count == 0) return;

        var fontResolver = new Text.FontResolver();

        foreach (var kv in fontCodepoints)
        {
            var pdfFontName = kv.Key;
            var codepoints = kv.Value;

            // Only embed if this is NOT a standard PDF built-in font
            if (IsStandardPdfFont(pdfFontName)) continue;

            // Try to find a system TrueType font matching this name
            var fontData = fontResolver.Resolve(pdfFontName);
            if (fontData == null || fontData.RawData == null || fontData.RawData.Length == 0)
                continue;

            // Subset the font to only include used glyphs
            var subset = Text.TrueType.TtfSubsetter.Subset(fontData, codepoints);
            if (subset == null || subset.FontData.Length == 0)
                continue;

            // Register with PdfDocument
            pdfDoc.AddEmbeddedFont(
                pdfFontName,
                subset.FontData,
                subset.CodepointToNewGlyphId,
                subset.AdvanceWidths,
                fontData.UnitsPerEm,
                fontData.Ascent,
                fontData.Descent);
        }
    }

    private static void CollectTextCodepoints(LayoutBox box, Dictionary<string, HashSet<int>> fontCodepoints)
    {
        if (!string.IsNullOrEmpty(box.Text))
        {
            string fontName = Layout.StandardFontMetrics.ResolvePdfFontName(
                box.Style?.FontFamily, box.Style?.FontWeight, box.Style?.Get("font-style"));

            if (!fontCodepoints.TryGetValue(fontName, out var codepoints))
            {
                codepoints = new HashSet<int>();
                fontCodepoints[fontName] = codepoints;
            }

            foreach (char c in box.Text)
                codepoints.Add(c);
        }

        foreach (var child in box.Children)
            CollectTextCodepoints(child, fontCodepoints);
    }

    private static bool IsStandardPdfFont(string fontName)
    {
        switch (fontName)
        {
            case "Helvetica":
            case "Helvetica-Bold":
            case "Helvetica-Oblique":
            case "Helvetica-BoldOblique":
            case "Times-Roman":
            case "Times-Bold":
            case "Times-Italic":
            case "Times-BoldItalic":
            case "Courier":
            case "Courier-Bold":
            case "Courier-Oblique":
            case "Courier-BoldOblique":
            case "Symbol":
            case "ZapfDingbats":
                return true;
            default:
                return false;
        }
    }
}
