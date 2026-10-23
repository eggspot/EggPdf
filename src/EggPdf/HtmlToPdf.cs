using System;
using System.Collections.Generic;
using System.Globalization;
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
using EggPdf.Text.Fallback;

namespace EggPdf;

/// <summary>
/// Static class for HTML-to-PDF conversion. Page size, margins, orientation, and running
/// headers/footers are configured with CSS inside the HTML itself (<c>@page</c> for size/margin,
/// <c>position: fixed</c> for repeating headers/footers) rather than through an options object.
/// </summary>
public static partial class HtmlToPdf
{
    private const float DefaultPageWidthPx = 595.28f;
    private const float DefaultPageHeightPx = 841.89f;
    private const int MaxImportDepth = 10;

    private static readonly Text.FontResolver SharedFontResolver = new Text.FontResolver();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Text.TrueType.FontData?>
        WebFontParseCache = new System.Collections.Concurrent.ConcurrentDictionary<string, Text.TrueType.FontData?>(StringComparer.Ordinal);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Text.TrueType.TtfSubsetter.SubsetResult>
        SubsetCache = new System.Collections.Concurrent.ConcurrentDictionary<string, Text.TrueType.TtfSubsetter.SubsetResult>(StringComparer.Ordinal);

    private const int SubsetCacheMaxEntries = 512;

    internal static int SubsetCacheHits;

    private static Text.TrueType.TtfSubsetter.SubsetResult? SubsetCached(
        Text.TrueType.FontData font, System.Collections.Generic.HashSet<int> codepoints,
        List<string>? activeFeatures = null, HashSet<ushort>? extraGlyphs = null)
    {
        if (font.RawData == null) return null;

        var sorted = new int[codepoints.Count];
        codepoints.CopyTo(sorted);
        Array.Sort(sorted);

        ulong h = 14695981039346656037UL;
        for (int i = 0; i < sorted.Length; i++)
        {
            h ^= (uint)sorted[i];
            h *= 1099511628211UL;
        }
        if (activeFeatures != null)
        {
            for (int i = 0; i < activeFeatures.Count; i++)
            {
                foreach (char c in activeFeatures[i]) { h ^= c; h *= 1099511628211UL; }
                h ^= '|'; h *= 1099511628211UL;
            }
        }
        if (extraGlyphs != null && extraGlyphs.Count > 0)
        {
            ulong sum = 0, xor = 0;
            foreach (var g in extraGlyphs) { sum += g; xor ^= (ulong)g * 0x9E3779B97F4A7C15UL; }
            h ^= sum; h *= 1099511628211UL;
            h ^= xor; h *= 1099511628211UL;
            h ^= (ulong)extraGlyphs.Count; h *= 1099511628211UL;
        }
        var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(font.RawData).ToString("x8")
            + ":" + font.RawData.Length + ":" + h.ToString("x16");

        if (SubsetCache.TryGetValue(key, out var cached))
        {
            System.Threading.Interlocked.Increment(ref SubsetCacheHits);
            return cached;
        }

        var subset = Text.TrueType.TtfSubsetter.Subset(font, sorted, activeFeatures, extraGlyphs);
        if (subset == null || subset.FontData.Length == 0) return subset;

        if (SubsetCache.Count >= SubsetCacheMaxEntries) SubsetCache.Clear();
        SubsetCache.TryAdd(key, subset);
        return subset;
    }

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

    /// <summary>
    /// Render HTML to an RC4-encrypted PDF (view-only protection / permission
    /// flags). All streams and strings are encrypted per the PDF 1.7 Standard
    /// security handler.
    /// </summary>
    public static byte[] Render(string? html, Pdf.PdfEncryption encryption)
    {
        return RenderInternal(html ?? "", null, encryption);
    }

    /// <summary>Render HTML to an RC4-encrypted PDF asynchronously.</summary>
    public static Task<byte[]> RenderAsync(string? html, Pdf.PdfEncryption encryption, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(RenderInternal(html ?? "", null, encryption));
    }

    /// <summary>
    /// Render HTML to a PDF/A-conformant PDF: embeds an ICC output intent and XMP conformance
    /// metadata, and forces every font (including the standard 14) to be embedded.
    /// </summary>
    public static byte[] Render(string? html, Pdf.PdfAConformance conformance)
    {
        return RenderInternal(html ?? "", null, null, conformance);
    }

    /// <summary>Render HTML to a PDF/A-conformant PDF asynchronously.</summary>
    public static Task<byte[]> RenderAsync(string? html, Pdf.PdfAConformance conformance, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(RenderInternal(html ?? "", null, null, conformance));
    }

    /// <summary>
    /// Render HTML to a Factur-X/ZUGFeRD e-invoice: a PDF/A-3 PDF with the invoice's CII XML
    /// (MINIMUM profile) embedded as a file attachment. <paramref name="conformance"/> must be
    /// <see cref="Pdf.PdfAConformance.PdfA3b"/> or <see cref="Pdf.PdfAConformance.PdfA3u"/>.
    /// </summary>
    public static byte[] Render(string? html, Pdf.PdfAConformance conformance, Pdf.FacturXInvoice invoice)
    {
        return RenderInternal(html ?? "", null, null, conformance, invoice);
    }

    /// <summary>Render HTML to a Factur-X/ZUGFeRD e-invoice asynchronously.</summary>
    public static Task<byte[]> RenderAsync(string? html, Pdf.PdfAConformance conformance, Pdf.FacturXInvoice invoice, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(RenderInternal(html ?? "", null, null, conformance, invoice));
    }

    /// <summary>
    /// Render a PDF/UA-1 tagged PDF: a structure tree (headings, paragraphs, tables, lists,
    /// landmark regions (nav/header/footer/aside/main/article/section), figures with alt text,
    /// links cross-referenced to their annotation via OBJR) linked to the page content via marked
    /// content, plus the <c>/MarkInfo</c>, <c>/Lang</c> and XMP metadata PDF/UA-1 requires.
    /// Combinable with named page groups (<c>page: &lt;name&gt;</c> + <c>@page &lt;name&gt;</c>) --
    /// every group contributes to the same single Document structure tree root.
    /// </summary>
    public static byte[] Render(string? html, bool tagged)
    {
        return RenderInternal(html ?? "", null, tagged: tagged);
    }

    /// <summary>Render a PDF/UA-1 tagged PDF asynchronously.</summary>
    public static Task<byte[]> RenderAsync(string? html, bool tagged, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(RenderInternal(html ?? "", null, tagged: tagged));
    }

    /// <summary>
    /// Render a tagged PDF targeting a specific PDF/UA version. <see cref="Pdf.PdfUaVersion.Ua2"/>
    /// (ISO 14289-2:2024, PDF 2.0-based) cannot be combined with PDF/A conformance -- there is no
    /// defined joint standard for that combination, unlike PDF/UA-1's level-A conformance.
    /// </summary>
    public static byte[] Render(string? html, bool tagged, Pdf.PdfUaVersion uaVersion)
    {
        return RenderInternal(html ?? "", null, tagged: tagged, uaVersion: uaVersion);
    }

    /// <summary>Render a tagged PDF targeting a specific PDF/UA version, asynchronously.</summary>
    public static Task<byte[]> RenderAsync(string? html, bool tagged, Pdf.PdfUaVersion uaVersion, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(RenderInternal(html ?? "", null, tagged: tagged, uaVersion: uaVersion));
    }

    /// <summary>Render a PDF/UA-1 tagged, PDF/A-conformant PDF in one call (see the single-argument overloads of each).</summary>
    public static byte[] Render(string? html, Pdf.PdfAConformance conformance, bool tagged)
    {
        return RenderInternal(html ?? "", null, null, conformance, tagged: tagged);
    }

    /// <summary>Render a PDF/UA-1 tagged, PDF/A-conformant PDF asynchronously.</summary>
    public static Task<byte[]> RenderAsync(string? html, Pdf.PdfAConformance conformance, bool tagged, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(RenderInternal(html ?? "", null, null, conformance, tagged: tagged));
    }

    /// <summary>
    /// Render a tagged, PDF/A-conformant PDF, explicit about targeting PDF/UA-1 (the only UA
    /// version definable jointly with PDF/A -- see the single-argument <see cref="Pdf.PdfUaVersion"/>
    /// overload's doc comment). Throws if <paramref name="uaVersion"/> is <see cref="Pdf.PdfUaVersion.Ua2"/>.
    /// </summary>
    public static byte[] Render(string? html, Pdf.PdfAConformance conformance, bool tagged, Pdf.PdfUaVersion uaVersion)
    {
        return RenderInternal(html ?? "", null, null, conformance, tagged: tagged, uaVersion: uaVersion);
    }

    /// <summary>Render a tagged, PDF/A-conformant PDF targeting a specific PDF/UA version, asynchronously.</summary>
    public static Task<byte[]> RenderAsync(string? html, Pdf.PdfAConformance conformance, bool tagged, Pdf.PdfUaVersion uaVersion, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(RenderInternal(html ?? "", null, null, conformance, tagged: tagged, uaVersion: uaVersion));
    }

    /// <summary>
    /// Render HTML to PDF, applying <see cref="PdfRenderOptions"/>. Page size, margins,
    /// orientation, title/author metadata, and extra CSS are translated into an injected
    /// <c>@page</c> rule and &lt;head&gt; tags before parsing (the generated rule is appended
    /// last in the HTML's &lt;head&gt;, so it wins the CSS cascade over any conflicting
    /// <c>@page</c> rule already in the document). <see cref="PdfRenderOptions.Encryption"/>,
    /// <see cref="PdfRenderOptions.Conformance"/> and <see cref="PdfRenderOptions.Invoice"/> are
    /// applied directly to the PDF writer, same as the dedicated overloads.
    /// <see cref="PdfRenderOptions.ResourceResolver"/> and <see cref="PdfRenderOptions.AllowLocalFileAccess"/>
    /// govern how external resources are loaded.
    /// </summary>
    public static byte[] Render(string? html, PdfRenderOptions options) => RenderWithOptions(html, options, default);

    /// <summary>
    /// Render HTML to PDF asynchronously, applying <see cref="PdfRenderOptions"/>. <paramref name="ct"/> also
    /// cancels in-flight <see cref="PdfRenderOptions.ResourceResolver"/> fetches.
    /// </summary>
    public static Task<byte[]> RenderAsync(string? html, PdfRenderOptions options, CancellationToken ct = default)
    {
        options?.Statistics?.Reset();
        ct.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(RenderWithOptions(html, options, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<byte[]>(ct);
        }
    }

    private static byte[] RenderWithOptions(string? html, PdfRenderOptions? options, CancellationToken ct)
    {
        options?.Statistics?.Reset();
        return RenderInternal(ApplyRenderOptions(html ?? "", options), null,
            options?.Encryption, options?.Conformance, options?.Invoice, options?.Tagged ?? false,
            options?.UaVersion ?? Pdf.PdfUaVersion.Ua1, options?.Warnings, CreateResourceLoader(options, null, ct),
            options?.Statistics);
    }

    /// <summary>
    /// Translate <see cref="PdfRenderOptions"/> into CSS/HTML and inject it into the document's
    /// &lt;head&gt; (creating a &lt;head&gt;/&lt;html&gt; wrapper if the input has none). Public so
    /// other packages (EggPdf.Razor, EggPdf.Service) can apply the same options shape to HTML
    /// they've already assembled by other means, rather than re-implementing this translation.
    /// </summary>
    public static string ApplyRenderOptions(string html, PdfRenderOptions? options)
    {
        if (options == null) return html;

        var css = new StringBuilder();
        bool hasSize = !string.IsNullOrEmpty(options.PageSize) || !string.IsNullOrEmpty(options.Orientation);
        bool hasMargin = options.Margin.HasValue || options.MarginTop.HasValue || options.MarginRight.HasValue
            || options.MarginBottom.HasValue || options.MarginLeft.HasValue;

        if (hasSize || hasMargin)
        {
            css.Append("@page{");
            if (hasSize)
            {
                css.Append("size:").Append(string.IsNullOrEmpty(options.PageSize) ? "A4" : options.PageSize);
                if (!string.IsNullOrEmpty(options.Orientation)) css.Append(' ').Append(options.Orientation);
                css.Append(';');
            }
            if (options.Margin.HasValue)
                css.Append("margin:").Append(options.Margin.Value.ToString(CultureInfo.InvariantCulture)).Append("px;");
            if (options.MarginTop.HasValue)
                css.Append("margin-top:").Append(options.MarginTop.Value.ToString(CultureInfo.InvariantCulture)).Append("px;");
            if (options.MarginRight.HasValue)
                css.Append("margin-right:").Append(options.MarginRight.Value.ToString(CultureInfo.InvariantCulture)).Append("px;");
            if (options.MarginBottom.HasValue)
                css.Append("margin-bottom:").Append(options.MarginBottom.Value.ToString(CultureInfo.InvariantCulture)).Append("px;");
            if (options.MarginLeft.HasValue)
                css.Append("margin-left:").Append(options.MarginLeft.Value.ToString(CultureInfo.InvariantCulture)).Append("px;");
            css.Append('}');
        }
        if (!string.IsNullOrEmpty(options.UserStyleSheet))
            css.Append(options.UserStyleSheet);

        string styleTag = css.Length > 0 ? $"<style>{css}</style>" : "";
        string titleTag = !string.IsNullOrEmpty(options.Title)
            ? $"<title>{System.Net.WebUtility.HtmlEncode(options.Title)}</title>" : "";
        string authorTag = !string.IsNullOrEmpty(options.Author)
            ? $"<meta name=\"author\" content=\"{System.Net.WebUtility.HtmlEncode(options.Author)}\">" : "";
        string inject = titleTag + authorTag + styleTag;
        if (inject.Length == 0) return html;

        int headClose = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headClose >= 0) return html.Insert(headClose, inject);

        int htmlOpen = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (htmlOpen >= 0)
        {
            int htmlTagEnd = html.IndexOf('>', htmlOpen);
            if (htmlTagEnd >= 0) return html.Insert(htmlTagEnd + 1, $"<head>{inject}</head>");
        }
        return $"<head>{inject}</head>{html}";
    }

    private static byte[] RenderInternal(string html, string? basePath, Pdf.PdfEncryption? encryption = null, Pdf.PdfAConformance? conformance = null, Pdf.FacturXInvoice? invoice = null, bool tagged = false, Pdf.PdfUaVersion uaVersion = Pdf.PdfUaVersion.Ua1, WarningCollector? warnings = null, ResourceLoader? resources = null, RenderStatistics? statistics = null)
    {
        var document = HtmlParser.Parse(html);
        return RenderInternal(document, basePath, encryption, conformance, invoice, tagged, uaVersion, warnings, resources, statistics);
    }

    private static byte[] RenderInternal(HtmlDocument document, string? basePath, Pdf.PdfEncryption? encryption = null, Pdf.PdfAConformance? conformance = null, Pdf.FacturXInvoice? invoice = null, bool tagged = false, Pdf.PdfUaVersion uaVersion = Pdf.PdfUaVersion.Ua1, WarningCollector? warnings = null, ResourceLoader? resources = null, RenderStatistics? statistics = null)
    {
        resources ??= ResourceLoader.BuiltIn(basePath);

        if (document.Body != null)
            DecodeCloudflareEmails(document.Body);

        var stylesheets = ExtractStyleSheets(document, basePath, resources);

        var pageSettings = PageRuleResolver.Resolve(stylesheets);
        float pageWidthPx = pageSettings.PageWidthPx;
        float pageHeightPx = pageSettings.PageHeightPx;
        float contentWidthPx = pageSettings.ContentWidthPx;
        float contentHeightPx = pageSettings.ContentHeightPx;

        var cascadeResolver = new CascadeResolver(stylesheets, mediaType: "print", pageWidth: pageWidthPx);

        var fontFaces = BuildFontFaceMap(stylesheets);
        bool usesVariations = UsesFontVariations(document, stylesheets);
        if (fontFaces.Count > 0 || usesVariations)
        {
            PrefetchWebFonts(fontFaces, resources);
            var measureResolver = SharedFontResolver;
            var measureCache = new Dictionary<string, Text.TrueType.FontData?>(StringComparer.Ordinal);
            TextMeasurer.FontDataProvider = (family, weight, fontStyle) =>
            {
                if (string.IsNullOrEmpty(family)) return null;
                var key = family + "|" + weight + "|" + fontStyle;
                if (measureCache.TryGetValue(key, out var cached)) return cached;

                bool italic = fontStyle == "italic" || fontStyle == "oblique";
                int numericWeight = ParseFontWeight(weight);
                var data = TryResolveFontFace(family, fontFaces, numericWeight, italic, measureResolver);
                if (data == null && family.IndexOf("__vf:", StringComparison.Ordinal) >= 0)
                {
                    var cleanFamily = Css.FontVariationMarker.Split(family, out var axes);
                    var system = TryResolveSystemFont(cleanFamily, measureResolver, numericWeight >= 600, italic);
                    if (system != null) data = Text.TrueType.VariableFontInstancer.InstanceFor(system, numericWeight, axes);
                }
                measureCache[key] = data;
                return data;
            };
        }

        CurrentResources = resources;
        Paint.BoxPainter.ImageLoader = url => resources.Load(url, Core.Resources.ResourceType.Image);

        BlockLayout.ShapeImageLoader = source => LoadShapeImageAlpha(source, resources);

        TextMeasurer.ComplexFontProvider = CreateComplexFontProvider(fontFaces);

        var han = DetectHanVariant(document);
        TextMeasurer.GlyphAdvanceProvider = CreateGlyphAdvanceProvider(fontFaces, han);

        try
        {
            var namedGroups = FindNamedPageGroups(document, cascadeResolver, pageSettings);
            var layouts = namedGroups == null
                ? new List<(LayoutBox root, PageSettings settings)>
                {
                    (BlockLayout.LayoutDocument(document, contentWidthPx, contentHeightPx, cascadeResolver,
                        fullPageWidth: pageWidthPx, fullPageHeight: pageHeightPx), pageSettings)
                }
                : LayoutPageGroups(document, namedGroups, pageSettings, cascadeResolver);

            var pdfDoc = new PdfDocument { Encryption = encryption, Conformance = conformance, Invoice = invoice };
            pdfDoc.Title = FindTitleTagText(document);
            pdfDoc.Author = FindMetaContent(document, "author");
            var layoutRoots = new List<LayoutBox>(layouts.Count);
            foreach (var layout in layouts)
            {
                ResolveImages(layout.root, pdfDoc, resources);
                layoutRoots.Add(layout.root);
            }

            SubsetAndEmbedFonts(layoutRoots, pdfDoc, fontFaces, han, warnings);

            if (tagged)
            {
                var (structRoot, boxToElement) = StructureTreeBuilder.Build(layoutRoots, uaVersion);
                pdfDoc.StructureTree = structRoot;
                pdfDoc.UaVersion = uaVersion;
                pdfDoc.DocumentLanguage = document.DocumentElement?.GetAttribute("lang");
                Paint.BoxPainter.StructureMap = boxToElement;
            }

            try
            {
                if (layouts.Count == 1)
                    RenderOneGroup(layouts[0].root, layouts[0].settings, pdfDoc, 0, null);
                else
                    RenderPageGroups(layouts, pdfDoc);
            }
            finally
            {
                Paint.BoxPainter.StructureMap = null;
            }

            var pdfBytes = pdfDoc.ToByteArray();
            if (statistics != null) statistics.PageCount = pdfDoc.PageCount;
            return pdfBytes;
        }
        finally
        {
            TextMeasurer.FontDataProvider = null;
            TextMeasurer.ComplexFontProvider = null;
            TextMeasurer.GlyphAdvanceProvider = null;
            BlockLayout.ShapeImageLoader = null;
            Paint.BoxPainter.ImageLoader = null;
            CurrentResources = null;
        }
    }

    private static void PrefetchWebFonts(Dictionary<string, List<FontFaceCandidate>> fontFaces, ResourceLoader resources)
    {
        var tasks = new List<System.Threading.Tasks.Task>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var faces in fontFaces.Values)
        {
            foreach (var face in faces)
            {
                foreach (var srcPart in face.Src.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var url = Text.FontUrlFetcher.ParseFontSrcUrl(srcPart.Trim());
                    if (url == null || !ResourceLoader.IsRemote(url) || !seen.Add(url))
                        continue;

                    var u = url;
                    tasks.Add(System.Threading.Tasks.Task.Run(() => resources.Load(u, Core.Resources.ResourceType.Font)));
                }
            }
        }

        if (tasks.Count == 0) return;
        try
        {
            System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
        }
        catch (AggregateException)
        {
            resources.CancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    private static void DecodeCloudflareEmails(HtmlElement element)
    {
        var cf = element.GetAttribute("data-cfemail");
        if (!string.IsNullOrEmpty(cf) && cf!.Length >= 4 && cf.Length % 2 == 0)
        {
            var email = TryDecodeCfEmail(cf);
            if (email != null)
            {
                element.ChildNodes.Clear();
                element.ChildNodes.Add(new HtmlTextNode(email));
                return;
            }
        }

        foreach (var child in element.ChildNodes)
        {
            if (child is HtmlElement childElem)
                DecodeCloudflareEmails(childElem);
        }
    }

    private static string? TryDecodeCfEmail(string hex)
    {
        try
        {
            int key = Convert.ToInt32(hex.Substring(0, 2), 16);
            var sb = new StringBuilder((hex.Length - 2) / 2);
            for (int i = 2; i < hex.Length; i += 2)
                sb.Append((char)(Convert.ToInt32(hex.Substring(i, 2), 16) ^ key));
            var email = sb.ToString();
            return email.IndexOf('@') > 0 ? email : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindTitleTagText(HtmlDocument document)
    {
        var head = document.Head;
        if (head == null) return null;

        foreach (var node in head.ChildNodes)
        {
            if (node is HtmlElement elem && elem.TagName == "title")
            {
                var sb = new StringBuilder();
                foreach (var child in elem.ChildNodes)
                    if (child is HtmlTextNode t) sb.Append(t.Data);
                var text = sb.ToString().Trim();
                return text.Length > 0 ? text : null;
            }
        }
        return null;
    }

    private static string? FindMetaContent(HtmlDocument document, string metaName)
    {
        var head = document.Head;
        if (head == null) return null;

        foreach (var node in head.ChildNodes)
        {
            if (node is HtmlElement elem && elem.TagName == "meta" &&
                string.Equals(elem.GetAttribute("name"), metaName, StringComparison.OrdinalIgnoreCase))
            {
                var content = elem.GetAttribute("content");
                return string.IsNullOrEmpty(content) ? null : content;
            }
        }
        return null;
    }

    internal static List<CssStyleSheet> ExtractStyleSheets(HtmlDocument document, string? basePath = null)
        => ExtractStyleSheets(document, basePath, ResourceLoader.BuiltIn(basePath));

    private static List<CssStyleSheet> ExtractStyleSheets(HtmlDocument document, string? basePath, ResourceLoader resources)
    {
        var sheets = new List<CssStyleSheet>();
        var visitedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var head = document.Head;
        if (head != null)
        {
            foreach (var node in head.ChildNodes)
            {
                if (node is HtmlElement elem)
                {
                    if (elem.TagName == "link")
                    {
                        var linkSheet = LoadLinkStylesheet(elem, basePath, visitedUrls, resources);
                        if (linkSheet != null)
                            sheets.Add(linkSheet);
                    }
                    else if (elem.TagName == "style")
                    {
                        var cssText = GetElementText(elem);
                        if (!string.IsNullOrWhiteSpace(cssText))
                        {
                            var sheet = CssStyleSheetParser.Parse(cssText);
                            ResolveImports(sheet, sheets, basePath, visitedUrls, 0, resources);
                            sheets.Add(sheet);
                        }
                    }
                }
            }
        }

        var body = document.Body;
        if (body != null)
            FindStyleElements(body, sheets, basePath, visitedUrls, resources);

        return sheets;
    }

    private static CssStyleSheet? LoadLinkStylesheet(HtmlElement linkElement, string? basePath, HashSet<string> visitedUrls, ResourceLoader resources)
    {
        var rel = linkElement.GetAttribute("rel");
        if (rel == null || rel.IndexOf("stylesheet", StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        var href = linkElement.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
            return null;

        var media = linkElement.GetAttribute("media");
        if (media != null)
        {
            var mediaTrimmed = media.Trim().ToLowerInvariant();
            if (mediaTrimmed.Length > 0 &&
                mediaTrimmed != "all" &&
                mediaTrimmed != "print")
                return null;
        }

        var cssText = LoadCssText(href, basePath, resources);
        if (string.IsNullOrWhiteSpace(cssText))
            return null;

        var resolvedUrl = ResolveUrl(href, basePath) ?? href;
        if (!visitedUrls.Add(resolvedUrl))
            return null;

        var sheet = CssStyleSheetParser.Parse(cssText);
        var sheetBasePath = GetBasePathForUrl(href, basePath);
        ResolveImports(sheet, new List<CssStyleSheet>(), sheetBasePath, visitedUrls, 0, resources);
        return sheet;
    }

    private static void ResolveImports(CssStyleSheet sheet, List<CssStyleSheet> targetList, string? basePath, HashSet<string> visitedUrls, int depth, ResourceLoader resources)
    {
        if (depth >= MaxImportDepth) return;
        if (sheet.ImportRules.Count == 0) return;

        int insertIndex = targetList.Count;

        for (int i = 0; i < sheet.ImportRules.Count; i++)
        {
            var importRule = sheet.ImportRules[i];
            var url = importRule.Url;
            if (string.IsNullOrWhiteSpace(url)) continue;

            if (importRule.MediaQuery != null)
            {
                var mq = importRule.MediaQuery.Trim().ToLowerInvariant();
                if (mq.Length > 0 && mq != "all" && mq != "print")
                    continue;
            }

            var resolvedUrl = ResolveUrl(url, basePath) ?? url;
            if (!visitedUrls.Add(resolvedUrl))
                continue;

            var cssText = LoadCssText(url, basePath, resources);
            if (string.IsNullOrWhiteSpace(cssText))
                continue;

            var importedSheet = CssStyleSheetParser.Parse(cssText);
            var importBasePath = GetBasePathForUrl(url, basePath);

            ResolveImports(importedSheet, targetList, importBasePath, visitedUrls, depth + 1, resources);

            targetList.Insert(insertIndex, importedSheet);
            insertIndex++;
        }

        sheet.ImportRules.Clear();
    }

    private static string? LoadCssText(string url, string? basePath, ResourceLoader resources)
    {
        var bytes = resources.Load(url, Core.Resources.ResourceType.StyleSheet, basePath);
        if (bytes == null || bytes.Length == 0) return null;

        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
    }

    private static string? ResolveUrl(string url, string? basePath)
    {
        if (string.IsNullOrEmpty(basePath)) return null;
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return url;

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

    private static void FindStyleElements(HtmlNode node, List<CssStyleSheet> sheets, string? basePath, HashSet<string> visitedUrls, ResourceLoader resources)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is HtmlElement elem && elem.TagName == "style")
            {
                var cssText = GetElementText(elem);
                if (!string.IsNullOrWhiteSpace(cssText))
                {
                    var sheet = CssStyleSheetParser.Parse(cssText);
                    ResolveImports(sheet, sheets, basePath, visitedUrls, 0, resources);
                    sheets.Add(sheet);
                }
            }
            else if (child is HtmlElement container)
            {
                FindStyleElements(container, sheets, basePath, visitedUrls, resources);
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

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] raw, PdfImage? image)>
        DataUriImageCache = new System.Collections.Concurrent.ConcurrentDictionary<string, (byte[], PdfImage?)>(StringComparer.Ordinal);
    private const int DataUriImageCacheMaxEntries = 32;

    internal static int ImageCacheHits;

    private static void ResolveImages(LayoutBox box, PdfDocument pdfDoc, ResourceLoader resources)
    {
        if (!string.IsNullOrEmpty(box.ImageSource))
        {
            bool cacheable = box.ImageSource.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

            if (cacheable && DataUriImageCache.TryGetValue(box.ImageSource, out var cached))
            {
                System.Threading.Interlocked.Increment(ref ImageCacheHits);
                box.ImageData = cached.raw;
                if (cached.image != null)
                    pdfDoc.AddImage(cached.image);
            }
            else
            {
                var data = resources.Load(box.ImageSource, Core.Resources.ResourceType.Image);
                if (data != null)
                {
                    box.ImageData = data;
                    var pdfImage = DecodeImage(box.ImageSource, data);
                    if (pdfImage != null)
                        pdfDoc.AddImage(pdfImage);

                    if (cacheable)
                    {
                        if (DataUriImageCache.Count >= DataUriImageCacheMaxEntries)
                            DataUriImageCache.Clear();
                        DataUriImageCache.TryAdd(box.ImageSource, (data, pdfImage));
                    }
                }
            }
        }

        foreach (var child in box.Children)
            ResolveImages(child, pdfDoc, resources);
    }

    private static PdfImage? DecodeImage(string source, byte[] data)
    {
        string imgName = "Img" + source.GetHashCode().ToString("X8");

        if (data.Length >= 8 &&
            data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71 &&
            data[4] == 13 && data[5] == 10 && data[6] == 26 && data[7] == 10)
        {
            return PdfImage.FromPng(imgName, data);
        }
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
        {
            return PdfImage.FromJpeg(imgName, data);
        }
        if (data.Length >= 4 &&
            data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
        {
            return PdfImage.FromGif(imgName, data);
        }
        if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D)
        {
            return PdfImage.FromBmp(imgName, data);
        }
        if (WebPDecoder.IsWebP(data))
        {
            return PdfImage.FromWebP(imgName, data);
        }
        return null;
    }

    private static void SubsetAndEmbedFonts(IReadOnlyList<LayoutBox> roots, PdfDocument pdfDoc,
        Dictionary<string, List<FontFaceCandidate>> fontFaces, HanVariant han, WarningCollector? warnings)
    {
        var reportedMissing = new HashSet<int>();

        var fontCodepoints = new Dictionary<string, HashSet<int>>();
        var fontFamilyLists = new Dictionary<string, string>();
        var fontFeatureTags = new Dictionary<string, List<string>>();
        var complexTexts = new Dictionary<string, HashSet<string>>();
        foreach (var root in roots)
            CollectTextCodepoints(root, fontCodepoints, fontFamilyLists, fontFeatureTags, complexTexts);

        if (complexTexts.Count > 0)
            EmbedComplexScriptFonts(complexTexts, fontFamilyLists, pdfDoc, fontFaces, reportedMissing, warnings);

        if (fontCodepoints.Count == 0) return;

        var fontResolver = SharedFontResolver;

        foreach (var kv in fontCodepoints)
        {
            var pdfFontName = kv.Key;
            var codepoints = kv.Value;

            bool bold = pdfFontName.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0;
            bool italic = pdfFontName.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          pdfFontName.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0;
            int targetWeight = ParseWeightSuffix(pdfFontName) ?? (bold ? 700 : 400);
            if (targetWeight >= 600) bold = true;
            fontFamilyLists.TryGetValue(pdfFontName, out var familyList);
            var systemFamilyList = Css.FontVariationMarker.Split(familyList, out var variationAxes);

            Text.TrueType.FontData? fontData =
                TryResolveFontFace(familyList, fontFaces, targetWeight, italic, fontResolver);

            bool mustEmbedAllFonts = pdfDoc.Conformance != null;
            bool isStandard = IsStandardPdfFont(pdfFontName);
            if (fontData == null && isStandard && AllWinAnsiEncodable(codepoints) && !mustEmbedAllFonts)
                continue;

            if (fontData == null)
                fontData = ResolveSystemPrimaryFont(systemFamilyList, pdfFontName, fontResolver, bold, italic);

            if (fontData == null || fontData.RawData == null || fontData.RawData.Length == 0)
            {
                if (mustEmbedAllFonts)
                    throw new InvalidOperationException(
                        $"PDF/A conformance requires every font to be embedded, but no embeddable substitute was found for '{pdfFontName}' on this host.");

                var unencodable = new List<int>();
                foreach (var cp in codepoints)
                    if (!UnicodeScripts.IsInvisible(cp) && !IsWinAnsiEncodable(cp)) unencodable.Add(cp);
                if (unencodable.Count > 0)
                    ReportMissingGlyphs(unencodable, systemFamilyList ?? pdfFontName, reportedMissing, warnings);
                continue;
            }

            fontData = Text.TrueType.VariableFontInstancer.InstanceFor(fontData, targetWeight, variationAxes);

            fontFeatureTags.TryGetValue(pdfFontName, out var activeFeatures);
            var subset = SubsetCached(fontData, codepoints, activeFeatures);
            if (subset == null || subset.FontData.Length == 0)
                continue;

            pdfDoc.AddEmbeddedFont(
                pdfFontName,
                subset.FontData,
                subset.CodepointToNewGlyphId,
                subset.AdvanceWidths,
                fontData.UnitsPerEm,
                fontData.Ascent,
                fontData.Descent,
                subset.ColorLayersByCodepoint);

            HashSet<int>? missing = null;
            foreach (var cp in codepoints)
            {
                if (!UnicodeScripts.IsInvisible(cp) && fontData.GetGlyphId(cp) == 0)
                    (missing ?? (missing = new HashSet<int>())).Add(cp);
            }
            if (missing != null)
            {
                var uncovered = EmbedFallbackFonts(pdfDoc, pdfFontName, missing, bold, italic, han);
                if (uncovered.Count > 0)
                    ReportMissingGlyphs(uncovered, systemFamilyList ?? pdfFontName, reportedMissing, warnings);
            }
        }
    }

    private static Text.TrueType.FontData? TryResolveFontFace(string? familyList,
        Dictionary<string, List<FontFaceCandidate>> fontFaces, int targetWeight, bool italic,
        Text.FontResolver fontResolver)
    {
        if (string.IsNullOrEmpty(familyList) || fontFaces.Count == 0) return null;

        familyList = Css.FontVariationMarker.Split(familyList, out var axes);
        var families = familyList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawFamily in families)
        {
            var family = rawFamily.Trim().Trim('"', '\'').ToLowerInvariant();
            if (family.Length == 0) continue;
            if (!fontFaces.TryGetValue(family, out var candidates)) continue;

            var face = SelectFontFace(candidates, targetWeight, italic);
            if (face == null) continue;

            var srcParts = face.Src.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var srcPart in srcParts)
            {
                var url = Text.FontUrlFetcher.ParseFontSrcUrl(srcPart.Trim());
                if (url == null) continue;

                Text.TrueType.FontData? fontData = null;
                if (url.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
                {
                    fontData = fontResolver.Resolve(url.Substring(6), targetWeight >= 600, italic);
                }
                else
                {
                    fontData = LoadWebFont(url);
                }

                if (fontData != null) return Text.TrueType.VariableFontInstancer.InstanceFor(fontData, targetWeight, axes);
            }
        }

        return null;
    }

    private static FontFaceCandidate? SelectFontFace(List<FontFaceCandidate> candidates, int targetWeight, bool italic)
    {
        if (candidates.Count == 0) return null;

        FontFaceCandidate? best = null;
        int bestScore = int.MaxValue;

        foreach (var c in candidates)
        {
            int score = Math.Abs(c.Weight - targetWeight) + (c.Italic != italic ? 10000 : 0);
            if (score < bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        return best;
    }

    private static Text.TrueType.FontData? TryResolveSystemFont(string? familyList,
        Text.FontResolver fontResolver, bool bold, bool italic)
    {
        if (string.IsNullOrEmpty(familyList)) return null;

        var families = familyList!.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawFamily in families)
        {
            var family = rawFamily.Trim().Trim('"', '\'');
            if (family.Length == 0) continue;

            var fontData = fontResolver.Resolve(family, bold, italic);
            if (fontData != null) return fontData;
        }

        return null;
    }

    private static string[] GetSystemFontCandidates(string pdfFontName)
    {
        if (pdfFontName.StartsWith("Times", StringComparison.OrdinalIgnoreCase))
            return new[] { "Times New Roman", "Times", "LiberationSerif", "DejaVuSerif", "NotoSerif" };
        if (pdfFontName.StartsWith("Courier", StringComparison.OrdinalIgnoreCase))
            return new[] { "Courier New", "Courier", "LiberationMono", "DejaVuSansMono", "NotoSansMono" };
        if (pdfFontName.StartsWith("Symbol", StringComparison.OrdinalIgnoreCase) ||
            pdfFontName.StartsWith("ZapfDingbats", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();
        return new[] { "Arial", "Helvetica", "LiberationSans", "DejaVuSans", "NotoSans" };
    }

    private static bool AllWinAnsiEncodable(HashSet<int> codepoints)
    {
        foreach (var cp in codepoints)
            if (!IsWinAnsiEncodable(cp)) return false;
        return true;
    }

    private static bool IsWinAnsiEncodable(int cp)
    {
        if (cp < 0x80) return true;
        if (cp >= 0xA0 && cp <= 0xFF) return true;
        switch (cp)
        {
            case 0x2022: case 0x2013: case 0x2014: case 0x2018: case 0x2019:
            case 0x201C: case 0x201D: case 0x2026: case 0x2020: case 0x2021:
            case 0x2030: case 0x2039: case 0x203A: case 0x0152: case 0x0153:
            case 0x0160: case 0x0161: case 0x0178: case 0x0192: case 0x02C6:
            case 0x02DC: case 0x2122:
                return true;
            default:
                return false;
        }
    }

    private class FontFaceCandidate
    {
        public int Weight;
        public bool Italic;
        public string Src = "";
    }

    private static Dictionary<string, List<FontFaceCandidate>> BuildFontFaceMap(List<CssStyleSheet>? stylesheets)
    {
        var result = new Dictionary<string, List<FontFaceCandidate>>(StringComparer.Ordinal);
        if (stylesheets == null) return result;

        foreach (var sheet in stylesheets)
        {
            foreach (var rule in sheet.FontFaceRules)
            {
                string? family = null, src = null, weight = null, fontStyle = null;
                foreach (var decl in rule.Declarations)
                {
                    switch (decl.Property)
                    {
                        case "font-family": family = decl.Value.Trim().Trim('"', '\''); break;
                        case "src":         src = decl.Value; break;
                        case "font-weight": weight = decl.Value; break;
                        case "font-style":  fontStyle = decl.Value; break;
                    }
                }

                if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(src)) continue;

                var key = family!.ToLowerInvariant();
                if (!result.TryGetValue(key, out var list))
                {
                    list = new List<FontFaceCandidate>();
                    result[key] = list;
                }

                list.Add(new FontFaceCandidate
                {
                    Weight = ParseFontWeight(weight),
                    Italic = fontStyle != null &&
                             (fontStyle.IndexOf("italic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              fontStyle.IndexOf("oblique", StringComparison.OrdinalIgnoreCase) >= 0),
                    Src = src!
                });
            }
        }

        return result;
    }

    private static int? ParseWeightSuffix(string pdfFontName)
    {
        int idx = pdfFontName.LastIndexOf("-W", StringComparison.Ordinal);
        if (idx <= 0 || idx + 2 >= pdfFontName.Length) return null;
        for (int i = idx + 2; i < pdfFontName.Length; i++)
            if (!char.IsDigit(pdfFontName[i])) return null;
        return int.Parse(pdfFontName.Substring(idx + 2),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int ParseFontWeight(string? weight)
    {
        if (string.IsNullOrEmpty(weight)) return 400;
        var w = weight!.Trim();

        if (w.Equals("bold", StringComparison.OrdinalIgnoreCase)) return 700;
        if (w.Equals("normal", StringComparison.OrdinalIgnoreCase)) return 400;

        int space = w.IndexOf(' ');
        if (space > 0) w = w.Substring(0, space);

        return int.TryParse(w, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : 400;
    }

    private static void CollectTextCodepoints(LayoutBox box, Dictionary<string, HashSet<int>> fontCodepoints,
        Dictionary<string, string> fontFamilyLists, Dictionary<string, List<string>> fontFeatureTags,
        Dictionary<string, HashSet<string>> complexTexts)
    {
        if (!string.IsNullOrEmpty(box.Text))
        {
            var fontFeatureSettings = box.Style?.Get("font-feature-settings");
            string fontName = Layout.StandardFontMetrics.ResolvePdfFontName(
                box.Style?.FontFamily, box.Style?.FontWeight, box.Style?.Get("font-style"), fontFeatureSettings);

            if (RegisterComplexText(box, fontName, fontFamilyLists, complexTexts))
            {
                foreach (var complexChild in box.Children)
                    CollectTextCodepoints(complexChild, fontCodepoints, fontFamilyLists, fontFeatureTags, complexTexts);
                return;
            }

            if (!fontCodepoints.TryGetValue(fontName, out var codepoints))
            {
                codepoints = new HashSet<int>();
                fontCodepoints[fontName] = codepoints;
            }

            var familyList = box.Style?.FontFamily;
            if (!string.IsNullOrEmpty(familyList) && !fontFamilyLists.ContainsKey(fontName))
                fontFamilyLists[fontName] = familyList!;

            var activeFeatures = Text.TrueType.FontFeatureSettings.ParseActiveTags(fontFeatureSettings);
            if (activeFeatures.Count > 0 && !fontFeatureTags.ContainsKey(fontName))
                fontFeatureTags[fontName] = activeFeatures;

            AddCodepoints(codepoints, box.Text!);

            if (Text.ArabicShaper.ContainsArabic(box.Text))
                AddCodepoints(codepoints, Text.ArabicShaper.Shape(box.Text!));

            var textTransform = box.Style?.Get("text-transform");
            var fontVariant = box.Style?.Get("font-variant");
            bool transforms = (!string.IsNullOrEmpty(textTransform) && textTransform != "none") ||
                              (fontVariant != null && fontVariant.IndexOf("small-caps", StringComparison.OrdinalIgnoreCase) >= 0);
            if (transforms)
            {
                AddCodepoints(codepoints, box.Text!.ToUpperInvariant());
                AddCodepoints(codepoints, box.Text!.ToLowerInvariant());
            }

            if (box.Style?.Get("text-overflow") == "ellipsis")
                codepoints.Add('.');

            if (box.Text!.IndexOf(Layout.CssCounterContext.PageCounterSentinel, StringComparison.Ordinal) >= 0 ||
                box.Text!.IndexOf(Layout.CssCounterContext.PagesCounterSentinel, StringComparison.Ordinal) >= 0)
            {
                for (char d = '0'; d <= '9'; d++)
                    codepoints.Add(d);
            }
        }

        foreach (var child in box.Children)
            CollectTextCodepoints(child, fontCodepoints, fontFamilyLists, fontFeatureTags, complexTexts);
    }

    private static void AddCodepoints(HashSet<int> codepoints, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoints.Add(char.ConvertToUtf32(c, text[i + 1]));
                i++;
            }
            else
            {
                codepoints.Add(c);
            }
        }
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
            case "Arial":
            case "Arial-Bold":
            case "Arial-Italic":
            case "Arial-BoldItalic":
                return true;
            default:
                return false;
        }
    }
}
