using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EggPdf.Core;
using EggPdf.Html.Dom;

namespace EggPdf;

public static partial class HtmlToPdf
{
    // Named RenderDocument*, not an overload of Render/RenderAsync/RenderToFileAsync: a same-arity
    // overload taking an unrelated reference type (HtmlDocument vs string) makes any existing
    // Render(null) call site ambiguous, since the compiler can't tell which reference type a null
    // literal was meant for. Distinct names sidestep that permanently.

    /// <summary>
    /// Render an already-built DOM to PDF, skipping HTML parsing entirely. Used by document
    /// builders (e.g. EggPdf.Fluent's document builder) that construct the DOM directly instead of
    /// generating and re-parsing an HTML string. Mirrors every option the HTML-string Render(string)
    /// overloads expose (encryption, PDF/A conformance, Factur-X invoice, PDF/UA tagging).
    /// </summary>
    public static byte[] RenderDocument(HtmlDocument document, string? basePath = null,
        Pdf.PdfEncryption? encryption = null, Pdf.PdfAConformance? conformance = null,
        Pdf.FacturXInvoice? invoice = null, bool tagged = false, Pdf.PdfUaVersion uaVersion = Pdf.PdfUaVersion.Ua1,
        WarningCollector? warnings = null)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        return RenderInternal(document, basePath, encryption, conformance, invoice, tagged, uaVersion, warnings);
    }

    /// <summary>Render an already-built DOM to PDF as byte array, asynchronously.</summary>
    public static Task<byte[]> RenderDocumentAsync(HtmlDocument document, string? basePath = null,
        Pdf.PdfEncryption? encryption = null, Pdf.PdfAConformance? conformance = null,
        Pdf.FacturXInvoice? invoice = null, bool tagged = false, Pdf.PdfUaVersion uaVersion = Pdf.PdfUaVersion.Ua1,
        CancellationToken ct = default, WarningCollector? warnings = null)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(RenderDocument(document, basePath, encryption, conformance, invoice, tagged, uaVersion, warnings));
    }

    /// <summary>Render an already-built DOM to PDF and write to a stream.</summary>
    public static Task RenderDocumentAsync(HtmlDocument document, Stream output, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var bytes = RenderDocument(document);
        return output.WriteAsync(bytes, 0, bytes.Length, ct);
    }

    /// <summary>Render an already-built DOM to PDF and save to a file.</summary>
    public static async Task RenderDocumentToFileAsync(HtmlDocument document, string filePath, string? basePath = null,
        Pdf.PdfEncryption? encryption = null, Pdf.PdfAConformance? conformance = null,
        Pdf.FacturXInvoice? invoice = null, bool tagged = false, Pdf.PdfUaVersion uaVersion = Pdf.PdfUaVersion.Ua1,
        CancellationToken ct = default, WarningCollector? warnings = null)
    {
        var bytes = RenderDocument(document, basePath, encryption, conformance, invoice, tagged, uaVersion, warnings);
        await WriteFileAsync(filePath, bytes, ct);
    }

    /// <summary>
    /// Render an already-built DOM to PDF with <see cref="PdfRenderOptions"/>: the PDF-writer settings
    /// (Encryption, Conformance, Invoice, Tagged, UaVersion), Warnings, and resource loading
    /// (<see cref="PdfRenderOptions.ResourceResolver"/>, <see cref="PdfRenderOptions.AllowLocalFileAccess"/>).
    /// The page/metadata properties (PageSize, Orientation, Margin*, Title, Author, UserStyleSheet) work by
    /// injecting HTML into the source text, which a DOM doesn't have: setting any of them throws
    /// <see cref="ArgumentException"/> -- put the equivalent <c>@page</c> rule, &lt;title&gt; or &lt;style&gt;
    /// into the document instead. A null <paramref name="options"/> renders with defaults: C# binds the
    /// pre-existing call <c>RenderDocument(doc, basePath, null)</c> to this overload, which must keep working.
    /// </summary>
    public static byte[] RenderDocument(HtmlDocument document, string? basePath, PdfRenderOptions? options) =>
        RenderDocumentWithOptions(document, basePath, options, default);

    /// <summary>Render an already-built DOM to PDF with <see cref="PdfRenderOptions"/>, asynchronously; <paramref name="ct"/> also cancels resource fetches.</summary>
    public static Task<byte[]> RenderDocumentAsync(HtmlDocument document, string? basePath, PdfRenderOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(RenderDocumentWithOptions(document, basePath, options, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<byte[]>(ct);
        }
    }

    /// <summary>Render an already-built DOM to a PDF file with <see cref="PdfRenderOptions"/>.</summary>
    public static async Task RenderDocumentToFileAsync(HtmlDocument document, string filePath, string? basePath,
        PdfRenderOptions? options, CancellationToken ct = default)
    {
        var bytes = await RenderDocumentAsync(document, basePath, options, ct);
        await WriteFileAsync(filePath, bytes, ct);
    }

    private static byte[] RenderDocumentWithOptions(HtmlDocument document, string? basePath, PdfRenderOptions? options, CancellationToken ct)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        options ??= new PdfRenderOptions();
        ThrowIfHtmlOnlyOption(options);
        return RenderInternal(document, basePath, options.Encryption, options.Conformance, options.Invoice, options.Tagged,
            options.UaVersion, options.Warnings, CreateResourceLoader(options, basePath, ct));
    }

    private static void ThrowIfHtmlOnlyOption(PdfRenderOptions options)
    {
        string? name =
            !string.IsNullOrEmpty(options.PageSize) ? nameof(options.PageSize) :
            !string.IsNullOrEmpty(options.Orientation) ? nameof(options.Orientation) :
            options.Margin.HasValue ? nameof(options.Margin) :
            options.MarginTop.HasValue ? nameof(options.MarginTop) :
            options.MarginRight.HasValue ? nameof(options.MarginRight) :
            options.MarginBottom.HasValue ? nameof(options.MarginBottom) :
            options.MarginLeft.HasValue ? nameof(options.MarginLeft) :
            !string.IsNullOrEmpty(options.Title) ? nameof(options.Title) :
            !string.IsNullOrEmpty(options.Author) ? nameof(options.Author) :
            !string.IsNullOrEmpty(options.UserStyleSheet) ? nameof(options.UserStyleSheet) : null;
        if (name != null)
            throw new ArgumentException(
                $"PdfRenderOptions.{name} is applied by injecting HTML and can't be used when rendering a DOM; " +
                "put the equivalent @page rule, <title> or <style> into the document instead.", nameof(options));
    }

    private static async Task WriteFileAsync(string filePath, byte[] bytes, CancellationToken ct)
    {
#if NET6_0_OR_GREATER
        await File.WriteAllBytesAsync(filePath, bytes, ct);
#else
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await fs.WriteAsync(bytes, 0, bytes.Length, ct);
#endif
    }
}
