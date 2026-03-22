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

        // 3. Create CascadeResolver with parsed stylesheets (replaces BasicStyleResolver)
        var cascadeResolver = new CascadeResolver(stylesheets, mediaType: "print");

        // 4. Layout (uses cascade resolver for full CSS support)
        var layoutRoot = BlockLayout.LayoutDocument(document, DefaultPageWidthPx, DefaultPageHeightPx, cascadeResolver);

        // 5. Resolve images (load data from src attributes)
        var pdfDoc = new PdfDocument();
        ResolveImages(layoutRoot, pdfDoc);

        // 6. Render to PDF
        float pageWidthPt = DefaultPageWidthPx * PdfCoordinates.PxToPt;
        float pageHeightPt = DefaultPageHeightPx * PdfCoordinates.PxToPt;

        PdfRenderer.Render(layoutRoot, pdfDoc, pageWidthPt, pageHeightPt);

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
}
