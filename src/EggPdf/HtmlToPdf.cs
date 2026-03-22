using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        return RenderInternal(html ?? "");
    }

    private static byte[] RenderInternal(string html)
    {
        // 1. Parse HTML -> DOM
        var document = HtmlParser.Parse(html);

        // 2. Extract <style> tags and parse CSS stylesheets
        var stylesheets = ExtractStyleSheets(document);

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
    /// Extract CSS from all &lt;style&gt; tags in the document and parse into stylesheets.
    /// </summary>
    private static List<CssStyleSheet> ExtractStyleSheets(HtmlDocument document)
    {
        var sheets = new List<CssStyleSheet>();

        // Find <style> elements in <head>
        var head = document.Head;
        if (head != null)
        {
            foreach (var node in head.ChildNodes)
            {
                if (node is HtmlElement elem && elem.TagName == "style")
                {
                    var cssText = GetElementText(elem);
                    if (!string.IsNullOrWhiteSpace(cssText))
                    {
                        var sheet = CssStyleSheetParser.Parse(cssText);
                        sheets.Add(sheet);
                    }
                }
            }
        }

        // Also find <style> elements in <body> (common in email HTML, CMS output)
        var body = document.Body;
        if (body != null)
            FindStyleElements(body, sheets);

        return sheets;
    }

    private static void FindStyleElements(HtmlNode node, List<CssStyleSheet> sheets)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is HtmlElement elem && elem.TagName == "style")
            {
                var cssText = GetElementText(elem);
                if (!string.IsNullOrWhiteSpace(cssText))
                    sheets.Add(CssStyleSheetParser.Parse(cssText));
            }
            else if (child is HtmlElement container)
            {
                FindStyleElements(container, sheets);
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
                var pdfImage = PdfImage.FromJpeg(imgName, data);
                if (pdfImage != null)
                {
                    pdfDoc.AddImage(pdfImage);
                }
                else
                {
                    // Try as raw RGB (for PNG, we'd need a PNG decoder)
                    // For now, only JPEG pass-through is supported
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
