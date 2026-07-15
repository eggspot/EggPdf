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

        // 1b. Decode Cloudflare email obfuscation (browsers do this via script,
        // which a PDF engine does not run).
        if (document.Body != null)
            DecodeCloudflareEmails(document.Body);

        // 2. Extract <style> tags, <link> stylesheets, and resolve @imports
        var stylesheets = ExtractStyleSheets(document, basePath);

        // 3. Resolve @page rules for page size and margins
        var pageSettings = PageRuleResolver.Resolve(stylesheets);
        float pageWidthPx = pageSettings.PageWidthPx;
        float pageHeightPx = pageSettings.PageHeightPx;
        float contentWidthPx = pageSettings.ContentWidthPx;
        float contentHeightPx = pageSettings.ContentHeightPx;

        // 4. Create CascadeResolver with parsed stylesheets (replaces BasicStyleResolver)
        var cascadeResolver = new CascadeResolver(stylesheets, mediaType: "print", pageWidth: pageWidthPx);

        // 4b. When @font-face webfonts are declared, measure text with the real
        // font metrics so layout matches the glyphs the PDF paints.
        var fontFaces = BuildFontFaceMap(stylesheets);
        if (fontFaces.Count > 0)
        {
            var measureResolver = new Text.FontResolver();
            var measureCache = new Dictionary<string, Text.TrueType.FontData?>(StringComparer.Ordinal);
            TextMeasurer.FontDataProvider = (family, weight, fontStyle) =>
            {
                if (string.IsNullOrEmpty(family)) return null;
                var key = family + "|" + weight + "|" + fontStyle;
                if (measureCache.TryGetValue(key, out var cached)) return cached;

                bool italic = fontStyle == "italic" || fontStyle == "oblique";
                var data = TryResolveFontFace(family, fontFaces, ParseFontWeight(weight), italic, measureResolver);
                measureCache[key] = data;
                return data;
            };
        }

        try
        {
            // 5. Layout (uses cascade resolver for full CSS support)
            // Layout uses content area (page minus margins) for body width
            var layoutRoot = BlockLayout.LayoutDocument(document, contentWidthPx, contentHeightPx, cascadeResolver);

            // 6. Resolve images (load data from src attributes)
            var pdfDoc = new PdfDocument();
            ResolveImages(layoutRoot, pdfDoc);

            // 6b. Subset and embed TrueType fonts for non-standard fonts
            SubsetAndEmbedFonts(layoutRoot, pdfDoc, fontFaces);

            // 7. Render to PDF
            float pageWidthPt = pageWidthPx * PdfCoordinates.PxToPt;
            float pageHeightPt = pageHeightPx * PdfCoordinates.PxToPt;

            PdfRenderer.Render(layoutRoot, pdfDoc, pageWidthPt, pageHeightPt, pageHeightPx,
                pageSettings.MarginLeft, pageSettings.MarginTop);

            return pdfDoc.ToByteArray();
        }
        finally
        {
            TextMeasurer.FontDataProvider = null;
        }
    }

    /// <summary>
    /// Decode Cloudflare-obfuscated emails: data-cfemail holds hex bytes where
    /// the first byte is an XOR key for the rest. The placeholder child text
    /// ("[email protected]") is replaced with the decoded address.
    /// </summary>
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
            return null; // malformed hex — leave the placeholder text as-is
        }
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

    /// <summary>Load CSS text from a URL (data: URI, http(s) URL, or file path).</summary>
    internal static string? LoadCssText(string url, string? basePath)
    {
        // Data URI: data:text/css;base64,...  or  data:text/css,...
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeDataUri(url);
        }

        // Remote stylesheet (e.g. Google Fonts <link>): fetch over HTTP(S) so
        // @font-face webfont declarations are available for embedding.
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = Text.FontUrlFetcher.Fetch(url);
            return bytes != null && bytes.Length > 0 ? Encoding.UTF8.GetString(bytes) : null;
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
    /// Walk the layout tree, find text needing a real font program, subset it,
    /// and register as CIDFont Type 2 in the PDF document.
    /// A font is embedded when the text's font-family list references a declared
    /// @font-face (webfont), or when its codepoints exceed WinAnsiEncoding
    /// (e.g. Vietnamese) so the non-embedded Type1 built-ins cannot encode them.
    /// </summary>
    private static void SubsetAndEmbedFonts(LayoutBox root, PdfDocument pdfDoc,
        Dictionary<string, List<FontFaceCandidate>> fontFaces)
    {
        // Collect (fontName, codepoints) plus the raw font-family list per font name
        var fontCodepoints = new Dictionary<string, HashSet<int>>();
        var fontFamilyLists = new Dictionary<string, string>();
        CollectTextCodepoints(root, fontCodepoints, fontFamilyLists);

        if (fontCodepoints.Count == 0) return;

        var fontResolver = new Text.FontResolver();

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

            // 1. Webfont: first family in the list with a declared @font-face wins,
            //    mirroring the browser's font selection.
            Text.TrueType.FontData? fontData =
                TryResolveFontFace(familyList, fontFaces, targetWeight, italic, fontResolver);

            // 2. Standard built-in Type1 fonts (WinAnsiEncoding) stay non-embedded
            //    while every codepoint is WinAnsi-encodable and no webfont applies.
            bool isStandard = IsStandardPdfFont(pdfFontName);
            if (fontData == null && isStandard && AllWinAnsiEncodable(codepoints))
                continue;

            // 3. System fonts: real families from the list, then metric-compatible
            //    substitutes for the standard font class.
            if (fontData == null)
                fontData = TryResolveSystemFont(familyList, fontResolver, bold, italic);

            if (fontData == null)
            {
                foreach (var candidate in GetSystemFontCandidates(pdfFontName))
                {
                    fontData = fontResolver.Resolve(candidate, bold, italic);
                    if (fontData != null) break;
                }
            }

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

            // Codepoints the chosen font cannot shape (e.g. ⚠ in text fonts):
            // embed a symbol-capable fallback the renderer can switch to mid-run.
            HashSet<int>? missing = null;
            foreach (var cp in codepoints)
            {
                if (cp > 0x20 && fontData.GetGlyphId(cp) == 0)
                    (missing ?? (missing = new HashSet<int>())).Add(cp);
            }
            if (missing != null)
                EmbedFallbackFont(pdfDoc, pdfFontName, missing, fontResolver);
        }
    }

    /// <summary>
    /// Embed a symbol-capable system font covering codepoints the main font
    /// lacks, registered as "&lt;fontName&gt;-FB" for mid-run font switching.
    /// </summary>
    private static void EmbedFallbackFont(PdfDocument pdfDoc, string pdfFontName,
        HashSet<int> missing, Text.FontResolver fontResolver)
    {
        string[] symbolFonts =
        {
            "Segoe UI Symbol", "seguisym", "Apple Symbols", "DejaVuSans",
            "NotoSansSymbols2", "NotoSansSymbols", "Segoe UI Emoji", "seguiemj"
        };

        foreach (var candidate in symbolFonts)
        {
            var fb = fontResolver.Resolve(candidate);
            if (fb == null || fb.RawData == null || fb.RawData.Length == 0) continue;

            bool covers = false;
            foreach (var cp in missing)
            {
                if (fb.GetGlyphId(cp) > 0) { covers = true; break; }
            }
            if (!covers) continue;

            var fbSubset = Text.TrueType.TtfSubsetter.Subset(fb, missing);
            if (fbSubset == null || fbSubset.FontData.Length == 0) continue;

            pdfDoc.AddEmbeddedFont(
                pdfFontName + "-FB",
                fbSubset.FontData,
                fbSubset.CodepointToNewGlyphId,
                fbSubset.AdvanceWidths,
                fb.UnitsPerEm,
                fb.Ascent,
                fb.Descent);
            return;
        }
    }

    /// <summary>
    /// Walk a CSS font-family list and load the first family that has a declared
    /// @font-face, picking the variant closest to the requested weight/style.
    /// </summary>
    private static Text.TrueType.FontData? TryResolveFontFace(string? familyList,
        Dictionary<string, List<FontFaceCandidate>> fontFaces, int targetWeight, bool italic,
        Text.FontResolver fontResolver)
    {
        if (string.IsNullOrEmpty(familyList) || fontFaces.Count == 0) return null;

        var families = familyList!.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawFamily in families)
        {
            var family = rawFamily.Trim().Trim('"', '\'').ToLowerInvariant();
            if (family.Length == 0) continue;
            if (!fontFaces.TryGetValue(family, out var candidates)) continue;

            var face = SelectFontFace(candidates, targetWeight, italic);
            if (face == null) continue;

            // src may list alternatives: url(...) format(...), local(...), ...
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
                    var rawBytes = Text.FontUrlFetcher.Fetch(url);
                    if (rawBytes != null && rawBytes.Length > 0)
                    {
                        try { fontData = Text.TrueType.TtfParser.Parse(rawBytes); }
                        catch { fontData = null; }
                    }
                }

                if (fontData != null) return fontData;
            }
        }

        return null;
    }

    /// <summary>Pick the @font-face variant best matching the requested weight/style.</summary>
    private static FontFaceCandidate? SelectFontFace(List<FontFaceCandidate> candidates, int targetWeight, bool italic)
    {
        if (candidates.Count == 0) return null;

        FontFaceCandidate? best = null;
        int bestScore = int.MaxValue;

        foreach (var c in candidates)
        {
            // Style mismatch is worse than any weight distance
            int score = Math.Abs(c.Weight - targetWeight) + (c.Italic != italic ? 10000 : 0);
            if (score < bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        return best;
    }

    /// <summary>Resolve the first family in a CSS font-family list to an installed system font.</summary>
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

    /// <summary>
    /// Metric-compatible system font candidates for a standard PDF font name,
    /// covering Windows, macOS, and Linux (Liberation/DejaVu/Noto) installs.
    /// </summary>
    private static string[] GetSystemFontCandidates(string pdfFontName)
    {
        if (pdfFontName.StartsWith("Times", StringComparison.OrdinalIgnoreCase))
            return new[] { "Times New Roman", "Times", "LiberationSerif", "DejaVuSerif", "NotoSerif" };
        if (pdfFontName.StartsWith("Courier", StringComparison.OrdinalIgnoreCase))
            return new[] { "Courier New", "Courier", "LiberationMono", "DejaVuSansMono", "NotoSansMono" };
        if (pdfFontName.StartsWith("Symbol", StringComparison.OrdinalIgnoreCase) ||
            pdfFontName.StartsWith("ZapfDingbats", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();
        // Helvetica / Arial / anything else sans-like
        return new[] { "Arial", "Helvetica", "LiberationSans", "DejaVuSans", "NotoSans" };
    }

    /// <summary>True when every codepoint can be encoded in WinAnsiEncoding.</summary>
    private static bool AllWinAnsiEncodable(HashSet<int> codepoints)
    {
        foreach (var cp in codepoints)
            if (!IsWinAnsiEncodable(cp)) return false;
        return true;
    }

    /// <summary>Mirror of PdfPage.MapToWinAnsi: codepoints representable in WinAnsiEncoding.</summary>
    private static bool IsWinAnsiEncodable(int cp)
    {
        if (cp < 0x80) return true;               // ASCII
        if (cp >= 0xA0 && cp <= 0xFF) return true; // Latin-1 Supplement
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

    /// <summary>
    /// Build a map from @font-face family name (lowercase) to its declared face
    /// variants (weight, style, src). Scans all font-face rules in all stylesheets.
    /// </summary>
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

    /// <summary>Extract the numeric weight from a "-W###" PDF font name suffix, if present.</summary>
    private static int? ParseWeightSuffix(string pdfFontName)
    {
        int idx = pdfFontName.LastIndexOf("-W", StringComparison.Ordinal);
        if (idx <= 0 || idx + 2 >= pdfFontName.Length) return null;
        for (int i = idx + 2; i < pdfFontName.Length; i++)
            if (!char.IsDigit(pdfFontName[i])) return null;
        return int.Parse(pdfFontName.Substring(idx + 2),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Parse a font-weight declaration ("400", "bold", "300 700") to a numeric weight.</summary>
    private static int ParseFontWeight(string? weight)
    {
        if (string.IsNullOrEmpty(weight)) return 400;
        var w = weight!.Trim();

        if (w.Equals("bold", StringComparison.OrdinalIgnoreCase)) return 700;
        if (w.Equals("normal", StringComparison.OrdinalIgnoreCase)) return 400;

        // Range like "300 700" — use the first bound
        int space = w.IndexOf(' ');
        if (space > 0) w = w.Substring(0, space);

        return int.TryParse(w, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : 400;
    }

    private static void CollectTextCodepoints(LayoutBox box, Dictionary<string, HashSet<int>> fontCodepoints,
        Dictionary<string, string> fontFamilyLists)
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

            // Remember the raw font-family list so embedding can honor @font-face
            // webfonts and real system families, not just the standard mapping.
            var familyList = box.Style?.FontFamily;
            if (!string.IsNullOrEmpty(familyList) && !fontFamilyLists.ContainsKey(fontName))
                fontFamilyLists[fontName] = familyList!;

            AddCodepoints(codepoints, box.Text!);

            // The renderer applies text-transform / small-caps at paint time, so the
            // subset must also cover the transformed characters.
            var textTransform = box.Style?.Get("text-transform");
            var fontVariant = box.Style?.Get("font-variant");
            bool transforms = (!string.IsNullOrEmpty(textTransform) && textTransform != "none") ||
                              (fontVariant != null && fontVariant.IndexOf("small-caps", StringComparison.OrdinalIgnoreCase) >= 0);
            if (transforms)
            {
                AddCodepoints(codepoints, box.Text!.ToUpperInvariant());
                AddCodepoints(codepoints, box.Text!.ToLowerInvariant());
            }

            // text-overflow: ellipsis may append "..." at paint time
            if (box.Style?.Get("text-overflow") == "ellipsis")
                codepoints.Add('.');
        }

        foreach (var child in box.Children)
            CollectTextCodepoints(child, fontCodepoints, fontFamilyLists);
    }

    /// <summary>Add every codepoint of a string (surrogate-pair aware) to the set.</summary>
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
            // Arial variants are metric-compatible with Helvetica and available on all major
            // operating systems. Reference without embedding; PDF viewers load system Arial.
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
