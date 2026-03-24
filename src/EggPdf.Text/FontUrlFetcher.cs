using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;

namespace EggPdf.Text;

/// <summary>
/// Fetches font files from HTTP/HTTPS URLs for @font-face declarations.
/// Required for web fonts from Google Fonts, CDNs, etc.
/// Caches downloaded fonts to avoid re-fetching across renders.
/// </summary>
public static class FontUrlFetcher
{
    private static readonly ConcurrentDictionary<string, byte[]?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient _httpClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Add("User-Agent", "EggPdf/0.1");
        return client;
    }

    /// <summary>
    /// Fetch font data from a URL. Returns null on failure.
    /// Results are cached by URL.
    /// </summary>
    public static byte[]? Fetch(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        return _cache.GetOrAdd(url, u =>
        {
            try
            {
                return FetchInternal(u);
            }
            catch
            {
                return null;
            }
        });
    }

    private static byte[]? FetchInternal(string url)
    {
        // Handle data: URIs
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = url.IndexOf(',');
            if (comma > 0)
            {
                try { return Convert.FromBase64String(url.Substring(comma + 1)); }
                catch { return null; }
            }
            return null;
        }

        // Only fetch HTTP/HTTPS
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        // Synchronous fetch (layout is single-threaded)
        try
        {
            var task = _httpClient.GetByteArrayAsync(url);
            if (task.Wait(TimeSpan.FromSeconds(10)))
                return task.Result;
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Parse a CSS @font-face src value and extract the URL.
    /// Handles: url("https://..."), url('...'), url(data:...), local("FontName")
    /// Returns the URL string or null.
    /// </summary>
    public static string? ParseFontSrcUrl(string srcValue)
    {
        if (string.IsNullOrEmpty(srcValue)) return null;

        var trimmed = srcValue.Trim();

        // url("...") or url('...') or url(...)
        if (trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            int start = 4;
            int end = trimmed.Length - 1;
            if (end <= start) return null;

            var url = trimmed.Substring(start, end - start).Trim();
            // Remove quotes
            if (url.Length >= 2 && ((url[0] == '"' && url[url.Length - 1] == '"') ||
                (url[0] == '\'' && url[url.Length - 1] == '\'')))
                url = url.Substring(1, url.Length - 2);

            return url;
        }

        // local("FontName") — return as-is for system font resolution
        if (trimmed.StartsWith("local(", StringComparison.OrdinalIgnoreCase))
        {
            int start = 6;
            int end = trimmed.Length - 1;
            if (end <= start) return null;
            var name = trimmed.Substring(start, end - start).Trim().Trim('"', '\'');
            return "local:" + name;
        }

        return null;
    }

    /// <summary>Clear the font cache.</summary>
    public static void ClearCache() => _cache.Clear();
}
