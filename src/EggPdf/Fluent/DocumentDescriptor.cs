using System;
using EggPdf.Html.Dom;
using EggPdf.Pdf;

namespace EggPdf.Fluent;

/// <summary>
/// Root of the fluent builder: a document is one or more page groups, plus document-wide PDF options.
/// Build it either declaratively inside <see cref="Document.Create"/>'s callback, or step by step:
/// <c>var doc = Document.New(); var page = doc.AddPage(); page.Header().Text("..."); ... doc.Render();</c>
/// Both produce the same document. Once built or rendered a document is frozen -- further changes throw.
/// </summary>
public sealed class DocumentDescriptor
{
    private readonly BuilderContext _ctx;
    private readonly HtmlDocument _document;
    private readonly HtmlElement _head;
    private readonly HtmlElement _body;
    private readonly System.Collections.Generic.List<(PageDescriptor Page, HtmlElement Style)> _pages = new();
    private DocumentBuilder? _built;

    internal string? BasePathValue { get; private set; }
    internal PdfEncryption? EncryptionValue { get; private set; }
    internal PdfAConformance? ConformanceValue { get; private set; }
    internal FacturXInvoice? InvoiceValue { get; private set; }
    internal bool TaggedValue { get; private set; }
    internal PdfUaVersion UaVersionValue { get; private set; } = PdfUaVersion.Ua1;

    internal DocumentDescriptor(BuilderContext ctx, HtmlDocument document, HtmlElement head, HtmlElement body)
    {
        _ctx = ctx;
        _document = document;
        _head = head;
        _body = body;
    }

    /// <summary>
    /// Adds a page group and returns it to configure imperatively (size, margins, header, footer,
    /// content) at any later point before the document is built. The first group is the document's
    /// base (unnamed) page size; every group after that gets its own named <c>@page</c> rule and a
    /// matching <c>page: &lt;name&gt;</c> on its content root, so later groups can use a different
    /// size/orientation (e.g. a landscape appendix) -- the same CSS Paged Media named-page mechanism
    /// HTML documents use for mixed page sizes.
    /// </summary>
    public PageDescriptor AddPage()
    {
        var page = new PageDescriptor(_ctx);

        // The <style> element is created (and ordered among any Css() calls) now; its @page text is
        // filled in by Build(), because size/margins may still be set after AddPage() returns.
        var styleElement = _ctx.CreateElement("style");
        _head.AppendChild(styleElement);
        _pages.Add((page, styleElement));

        if (_pages.Count > 1)
            _ctx.SetStyle(page.RootElement, CssProp.Page, PageName(_pages.Count));

        _body.AppendChild(page.RootElement);
        return page;
    }

    /// <summary>Adds a page group configured inside <paramref name="build"/> (see <see cref="AddPage"/>).</summary>
    public DocumentDescriptor Page(Action<PageDescriptor> build)
    {
        if (build == null) throw new ArgumentNullException(nameof(build));
        build(AddPage());
        return this;
    }

    private static string PageName(int oneBasedIndex)
        => "eggpdf-page-" + oneBasedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Finishes the document: serializes every accumulated style and page rule, and freezes it. Called
    /// automatically by <see cref="Render"/>, <see cref="RenderAsync"/> and <see cref="RenderToFileAsync"/>;
    /// calling it again returns the same result. After this, changing the document throws.
    /// </summary>
    public DocumentBuilder Build()
    {
        if (_built != null) return _built;

        for (int i = 0; i < _pages.Count; i++)
            _pages[i].Style.AppendChild(new HtmlTextNode(_pages[i].Page.BuildPageRuleCss(i == 0 ? null : PageName(i + 1))));

        var globalCss = _ctx.BuildGlobalCss();
        if (globalCss != null)
        {
            var globalStyle = _ctx.CreateElement("style");
            globalStyle.AppendChild(new HtmlTextNode(globalCss));
            _head.AppendChild(globalStyle);
        }

        _ctx.FinalizeStyles(_document);
        _ctx.Freeze();
        _built = new DocumentBuilder(_document, BasePathValue, EncryptionValue, ConformanceValue,
            InvoiceValue, TaggedValue, UaVersionValue);
        return _built;
    }

    /// <summary>Builds the document (see <see cref="Build"/>) and renders it to PDF bytes.</summary>
    public byte[] Render() => Build().Render();

    /// <summary>Builds the document and renders it to PDF bytes asynchronously.</summary>
    public System.Threading.Tasks.Task<byte[]> RenderAsync(System.Threading.CancellationToken ct = default) => Build().RenderAsync(ct);

    /// <summary>Builds the document and renders it to a PDF file.</summary>
    public System.Threading.Tasks.Task RenderToFileAsync(string filePath, System.Threading.CancellationToken ct = default) => Build().RenderToFileAsync(filePath, ct);

    /// <summary>The PDF's title metadata (HTML &lt;title&gt;).</summary>
    public DocumentDescriptor Title(string title)
    {
        var el = _ctx.CreateElement("title");
        el.AppendChild(new HtmlTextNode(title ?? ""));
        _head.AppendChild(el);
        return this;
    }

    /// <summary>The PDF's author metadata (HTML &lt;meta name="author"&gt;).</summary>
    public DocumentDescriptor Author(string author)
    {
        var el = _ctx.CreateElement("meta");
        el.SetAttribute("name", "author");
        el.SetAttribute("content", author ?? "");
        _head.AppendChild(el);
        return this;
    }

    /// <summary>The document language (<c>lang</c> on &lt;html&gt;), e.g. "en"; used by tagged (PDF/UA) output.</summary>
    public DocumentDescriptor Language(string lang)
    {
        _ctx.EnsureMutable();
        if (_head.Parent is HtmlElement html) html.SetAttribute("lang", lang ?? "");
        return this;
    }

    /// <summary>
    /// Adds a raw stylesheet to the document's &lt;head&gt; -- the head-level counterpart to
    /// <see cref="Container.Style"/>/<see cref="Container.Raw"/>: selectors, @media print, @import, anything.
    /// Pair with <see cref="Container.Attribute"/> to give elements classes/ids to target.
    /// </summary>
    public DocumentDescriptor Css(string css)
    {
        var el = _ctx.CreateElement("style");
        el.AppendChild(new HtmlTextNode(css ?? ""));
        _head.AppendChild(el);
        return this;
    }

    /// <summary>Declares an <c>@font-face</c> webfont; <paramref name="src"/> is a URL, file path or data: URI.</summary>
    public DocumentDescriptor FontFace(string family, string src, int weight = 400, bool italic = false)
    {
        if (weight < 1 || weight > 1000)
            throw new ArgumentOutOfRangeException(nameof(weight), weight, "Font weight must be 1-1000.");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return Css("@font-face{font-family:\"" + family + "\";src:url(\"" + src + "\");font-weight:" +
                   weight.ToString(inv) + ";font-style:" + (italic ? "italic" : "normal") + ";}");
    }

    /// <summary>Base directory for resolving relative resource paths (images, stylesheets, fonts).</summary>
    public DocumentDescriptor BasePath(string basePath)
    {
        _ctx.EnsureMutable();
        BasePathValue = basePath;
        return this;
    }

    /// <summary>Encrypts the PDF (RC4 view-only protection / permission flags).</summary>
    public DocumentDescriptor Encrypt(PdfEncryption encryption)
    {
        _ctx.EnsureMutable();
        EncryptionValue = encryption ?? throw new ArgumentNullException(nameof(encryption));
        return this;
    }

    /// <summary>Renders a PDF/A-conformant PDF (ICC output intent, XMP conformance metadata, forced font embedding).</summary>
    public DocumentDescriptor Conformance(PdfAConformance conformance)
    {
        _ctx.EnsureMutable();
        ConformanceValue = conformance;
        return this;
    }

    /// <summary>
    /// Attaches a Factur-X/ZUGFeRD e-invoice XML. <see cref="Conformance"/> must be set to
    /// <see cref="PdfAConformance.PdfA3b"/> or <see cref="PdfAConformance.PdfA3u"/> first.
    /// </summary>
    public DocumentDescriptor Invoice(FacturXInvoice invoice)
    {
        _ctx.EnsureMutable();
        InvoiceValue = invoice ?? throw new ArgumentNullException(nameof(invoice));
        return this;
    }

    /// <summary>Renders a PDF/UA tagged (accessible) PDF: structure tree, headings, landmarks, alt text, tagged links.</summary>
    public DocumentDescriptor Tagged(PdfUaVersion uaVersion = PdfUaVersion.Ua1)
    {
        _ctx.EnsureMutable();
        TaggedValue = true;
        UaVersionValue = uaVersion;
        return this;
    }
}
