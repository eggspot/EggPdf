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

    // Internal on purpose: a public same-name overload taking PdfRenderOptions makes existing
    // RenderDocument*(doc, basePath, null) calls ambiguous or rebinds them (see the note at the top), and a
    // differently named public method is a naming decision for its own change. EggPdf.Fluent uses these;
    // other DOM callers get resource options through HTML: Render(string, PdfRenderOptions).

    /// <summary>
    /// Render an already-built DOM with <paramref name="options"/>' PDF-writer settings (Encryption, Conformance,
    /// Invoice, Tagged, UaVersion), Warnings, Statistics and resource loading (ResourceResolver, AllowLocalFileAccess). Its
    /// HTML-injected page/metadata properties are not applied: a DOM has no source text to inject into.
    /// </summary>
    internal static byte[] RenderDocumentWithOptions(HtmlDocument document, string? basePath, PdfRenderOptions options,
        CancellationToken ct = default)
    {
        options.Statistics?.Reset();
        return RenderInternal(document, basePath, options.Encryption, options.Conformance, options.Invoice, options.Tagged,
            options.UaVersion, options.Warnings, CreateResourceLoader(options, basePath, ct), options.Statistics);
    }

    /// <summary><see cref="RenderDocumentWithOptions"/> as a task; <paramref name="ct"/> also cancels resource fetches.</summary>
    internal static Task<byte[]> RenderDocumentWithOptionsAsync(HtmlDocument document, string? basePath, PdfRenderOptions options,
        CancellationToken ct)
    {
        options.Statistics?.Reset();
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

    internal static async Task WriteFileAsync(string filePath, byte[] bytes, CancellationToken ct)
    {
#if NET6_0_OR_GREATER
        await File.WriteAllBytesAsync(filePath, bytes, ct);
#else
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await fs.WriteAsync(bytes, 0, bytes.Length, ct);
#endif
    }
}
