using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private static readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> _failedFetches = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient _httpClient = CreateClient();

    /// <summary>
    /// How long a failed fetch suppresses retries for that URL. Bounded so a
    /// transient network failure cannot poison the URL for the process
    /// lifetime, but long enough that a dead URL does not add a network
    /// timeout to every render. Internal-mutable for tests.
    /// </summary>
    internal static TimeSpan FailureRetryInterval = TimeSpan.FromSeconds(30);

    /// <summary>Test seam: replaces the actual fetch when set.</summary>
    internal static Func<string, byte[]?>? FetchOverride;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Add("User-Agent", "EggPdf/0.1");
        return client;
    }

    /// <summary>
    /// Fetch font data from a URL. Returns null on failure.
    /// Results are cached by URL. A failed fetch is not retried for a short interval; unlike a cache entry that
    /// expires, so the URL recovers after a transient outage.
    /// </summary>
    public static byte[]? Fetch(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        if (_cache.TryGetValue(url, out var cached)) return cached;

        if (_failedFetches.TryGetValue(url, out var failedAt) &&
            DateTime.UtcNow - failedAt < FailureRetryInterval)
            return null;

        byte[]? data;
        try
        {
            var fetch = FetchOverride;
            data = fetch != null ? fetch(url) : FetchInternal(url);
        }
        catch
        {
            data = null;
        }

        if (data != null && data.Length > 0)
        {
            _cache.TryAdd(url, data);
            _failedFetches.TryRemove(url, out _);
            return data;
        }

        _failedFetches[url] = DateTime.UtcNow;
        return null;
    }

    private static byte[]? FetchInternal(string url)
    {
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

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

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
    /// Handles: url("https://..."), url('...'), url(data:...), local("FontName") (returned as "local:FontName"),
    /// and a bare URL, as left by a CSS parser that already unwrapped url(...), whose first token is the URL.
    /// Returns the URL string or null.
    /// </summary>
    public static string? ParseFontSrcUrl(string srcValue)
    {
        if (string.IsNullOrEmpty(srcValue)) return null;

        var trimmed = srcValue.Trim();

        if (trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            int start = 4;
            int end = trimmed.IndexOf(')', start);
            if (end <= start) return null;

            var url = trimmed.Substring(start, end - start).Trim();
            if (url.Length >= 2 && ((url[0] == '"' && url[url.Length - 1] == '"') ||
                (url[0] == '\'' && url[url.Length - 1] == '\'')))
                url = url.Substring(1, url.Length - 2);

            return url.Length > 0 ? url : null;
        }

        if (trimmed.StartsWith("local(", StringComparison.OrdinalIgnoreCase))
        {
            int start = 6;
            int end = trimmed.IndexOf(')', start);
            if (end <= start) return null;
            var name = trimmed.Substring(start, end - start).Trim().Trim('"', '\'');
            return name.Length > 0 ? "local:" + name : null;
        }

        int ws = trimmed.IndexOfAny(new[] { ' ', '\t' });
        var bare = (ws > 0 ? trimmed.Substring(0, ws) : trimmed).Trim('"', '\'');
        if (bare.Length > 0 &&
            !bare.StartsWith("format(", StringComparison.OrdinalIgnoreCase) &&
            !bare.StartsWith("tech(", StringComparison.OrdinalIgnoreCase))
            return bare;

        return null;
    }

    /// <summary>
    /// Split an @font-face src list into its sources at the commas between them; commas inside url(), local(),
    /// format() or quoted strings (a data: URI's "base64,") stay part of their source.
    /// </summary>
    public static List<string> SplitFontSrcList(string src)
    {
        var sources = new List<string>();
        if (string.IsNullOrEmpty(src)) return sources;

        int depth = 0, start = 0;
        char quote = '\0';
        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];
            if (quote != '\0')
            {
                if (c == '\\') i++;
                else if (c == quote) quote = '\0';
            }
            else if (c == '"' || c == '\'') quote = c;
            else if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (c == ',' && depth == 0)
            {
                AddSource(sources, src, start, i);
                start = i + 1;
            }
        }
        AddSource(sources, src, start, src.Length);
        return sources;
    }

    private static void AddSource(List<string> sources, string src, int start, int end)
    {
        var source = src.Substring(start, end - start).Trim();
        if (source.Length > 0) sources.Add(source);
    }

    /// <summary>Clear the font cache, including recorded fetch failures.</summary>
    public static void ClearCache()
    {
        _cache.Clear();
        _failedFetches.Clear();
    }
}
