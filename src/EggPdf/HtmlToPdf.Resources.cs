using System;
using System.Threading;
using EggPdf.Core.Resources;

namespace EggPdf;

public static partial class HtmlToPdf
{
    /// <summary>
    /// Longest a <see cref="PdfRenderOptions.ResourceResolver"/> call may take before the render gives up on
    /// that resource (RESOURCE_TIMEOUT warning) -- a backstop for resolvers that ignore their token.
    /// Internal-mutable for tests.
    /// </summary>
    internal static TimeSpan ResourceTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The resource loader of the render running on this thread (web fonts read it during layout).</summary>
    [ThreadStatic]
    private static ResourceLoader? CurrentResources;

    private static ResourceLoader CreateResourceLoader(PdfRenderOptions? options, string? basePath, CancellationToken ct) =>
        new(options?.ResourceResolver, options?.AllowLocalFileAccess ?? true, basePath, options?.Warnings, ct);

    /// <summary>
    /// Fetch and parse an @font-face source through the current render's resource loader. Parsing costs tens
    /// of ms per face, so results are cached: process-wide for data: URIs and for remote fonts fetched by the
    /// built-in loader (the bytes behind such a URL are treated as immutable), per render otherwise -- a
    /// caller's resolver decides on every render, so its results are never shared with another render.
    /// </summary>
    private static Text.TrueType.FontData? LoadWebFont(string url)
    {
        var resources = CurrentResources ?? ResourceLoader.BuiltIn(null);
        bool shared = ResourceLoader.IsDataUri(url) || (!resources.UsesResolver && ResourceLoader.IsRemote(url));
        var cache = shared ? WebFontParseCache : resources.ParsedFonts;
        if (cache.TryGetValue(url, out var cached)) return cached;

        var rawBytes = resources.Load(url, ResourceType.Font);
        // A FAILED fetch is not cached here: FontUrlFetcher's retry window decides when a shared URL may be
        // tried again, and the loader already remembers failures for the rest of this render
        if (rawBytes == null || rawBytes.Length == 0) return null;

        Text.TrueType.FontData? fontData;
        try { fontData = Text.TrueType.TtfParser.Parse(rawBytes); }
        catch { fontData = null; }
        // Even a parse failure is safe to cache: the bytes behind the key don't change
        cache.TryAdd(url, fontData);
        return fontData;
    }
}
