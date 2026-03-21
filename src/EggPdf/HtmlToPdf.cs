using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EggPdf.Core;
using EggPdf.Html;
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

        // 2. Layout
        var layoutRoot = BlockLayout.LayoutDocument(document, DefaultPageWidthPx, DefaultPageHeightPx);

        // 3. Render to PDF
        var pdfDoc = new PdfDocument();

        float pageWidthPt = DefaultPageWidthPx * PdfCoordinates.PxToPt;
        float pageHeightPt = DefaultPageHeightPx * PdfCoordinates.PxToPt;

        PdfRenderer.Render(layoutRoot, pdfDoc, pageWidthPt, pageHeightPt);

        return pdfDoc.ToByteArray();
    }
}
