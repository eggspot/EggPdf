using System;
using System.Threading;
using System.Threading.Tasks;
using EggPdf.Html.Dom;

namespace EggPdf.Fluent;

/// <summary>
/// Entry point for defining PDF content in C# instead of HTML. It builds a DOM directly from the
/// fluent calls and hands it to <see cref="HtmlToPdf"/> unchanged -- same cascade/layout/paint/
/// PDF-write pipeline as the HTML API, just without ever going through an HTML string. Two styles,
/// same result: declarative (<see cref="Create"/>, one callback) or step by step (<see cref="New"/>).
/// </summary>
public static class Document
{
    /// <summary>Builds a document from <paramref name="build"/>'s fluent calls, ready to render.</summary>
    public static DocumentBuilder Create(Action<DocumentDescriptor> build)
    {
        if (build == null) throw new ArgumentNullException(nameof(build));

        var descriptor = New();
        build(descriptor);
        return descriptor.Build();
    }

    /// <summary>
    /// Starts an empty document to fill in step by step -- keep the returned descriptor, add pages,
    /// headers, footers and content in separate statements (or separate methods), then call
    /// <see cref="DocumentDescriptor.Render"/>:
    /// <code>
    /// var doc = Document.New().Title("Report");
    /// var page = doc.AddPage().Size(PageSize.A4);
    /// page.Header().Text("Acme");
    /// page.Footer().PageNumberOfTotal();
    /// var body = page.Content();
    /// body.Item().Text("Hello");
    /// byte[] pdf = doc.Render();
    /// </code>
    /// </summary>
    public static DocumentDescriptor New()
    {
        var ctx = new BuilderContext();
        var htmlDocument = new HtmlDocument();
        var html = ctx.CreateElement("html");
        var head = ctx.CreateElement("head");
        ctx.Head = head;
        var body = ctx.CreateElement("body");
        html.AppendChild(head);
        html.AppendChild(body);
        htmlDocument.AppendChild(html);
        return new DocumentDescriptor(ctx, htmlDocument, head, body);
    }
}

/// <summary>The finished document, ready to render. Returned by <see cref="Document.Create"/>.</summary>
public sealed class DocumentBuilder
{
    private readonly HtmlDocument _document;
    private readonly string? _basePath;
    private readonly PdfRenderOptions _options;

    /// <param name="options">Writer, warning and resource settings only (no HTML-injected page/metadata options).</param>
    internal DocumentBuilder(HtmlDocument document, string? basePath, PdfRenderOptions options)
    {
        _document = document;
        _basePath = basePath;
        _options = options;
    }

    /// <summary>Renders the document to PDF bytes. May be called repeatedly; the output is identical each time.</summary>
    public byte[] Render() => HtmlToPdf.RenderDocument(_document, _basePath, _options);

    /// <summary>Renders the document to PDF bytes asynchronously.</summary>
    public Task<byte[]> RenderAsync(CancellationToken ct = default) =>
        HtmlToPdf.RenderDocumentAsync(_document, _basePath, _options, ct);

    /// <summary>Renders the document and writes the PDF to <paramref name="filePath"/>.</summary>
    public Task RenderToFileAsync(string filePath, CancellationToken ct = default) =>
        HtmlToPdf.RenderDocumentToFileAsync(_document, filePath, _basePath, _options, ct);
}
