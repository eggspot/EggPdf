using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EggPdf.Core;
using EggPdf.Core.Resources;

namespace EggPdf;

/// <summary>
/// Loads one render's external resources -- stylesheets, @import, @font-face sources, images -- under that
/// render's policy. data: URIs are always decoded in-process. With a caller's <see cref="IResourceResolver"/>
/// every other URL goes only through it, memoized for this render and never through the process-wide caches,
/// so a restrictive resolver can't be bypassed by a URL some other render already fetched. Without one, the
/// built-in behaviour applies: http(s) stylesheets and fonts through <see cref="Text.FontUrlFetcher"/>, http(s)
/// images not loaded, everything else read from disk. When local file access is off, anything but data: and
/// http(s) is refused before it reaches either. Thread-safe (web fonts are prefetched in parallel).
/// </summary>
internal sealed class ResourceLoader
{
    private readonly IResourceResolver? _resolver;
    private readonly bool _allowLocalFileAccess;
    private readonly string? _basePath;
    private readonly WarningCollector? _warnings;
    // Created on first use: most renders load nothing external, and every render builds a loader
    private ConcurrentDictionary<string, byte[]?>? _loaded;
    private ConcurrentDictionary<string, bool>? _warned;
    private ConcurrentDictionary<string, Text.TrueType.FontData?>? _parsedFonts;

    internal ResourceLoader(IResourceResolver? resolver, bool allowLocalFileAccess, string? basePath,
        WarningCollector? warnings, CancellationToken ct)
    {
        _resolver = resolver;
        _allowLocalFileAccess = allowLocalFileAccess;
        _basePath = basePath;
        _warnings = warnings;
        CancellationToken = ct;
    }

    /// <summary>The built-in loading used when the caller set no resource options.</summary>
    internal static ResourceLoader BuiltIn(string? basePath) => new(null, true, basePath, null, CancellationToken.None);

    internal CancellationToken CancellationToken { get; }

    /// <summary>True when a caller's resolver is in charge (no process-wide caches may be consulted).</summary>
    internal bool UsesResolver => _resolver != null;

    /// <summary>Parsed web fonts for this render, keyed by source URL (resolver path only).</summary>
    internal ConcurrentDictionary<string, Text.TrueType.FontData?> ParsedFonts =>
        LazyInitializer.EnsureInitialized(ref _parsedFonts, () => new(StringComparer.Ordinal))!;

    internal static bool IsRemote(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    internal static bool IsDataUri(string url) => url.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Load <paramref name="url"/>, resolving a relative file path against the render's base path.</summary>
    internal byte[]? Load(string url, ResourceType type) => Load(url, type, _basePath);

    /// <summary>Load <paramref name="url"/>, resolving a relative file path against <paramref name="basePath"/>.</summary>
    internal byte[]? Load(string url, ResourceType type, string? basePath)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (IsDataUri(url)) return DecodeDataUri(url);

        bool remote = IsRemote(url);
        if (!remote)
        {
            if (!_allowLocalFileAccess)
            {
                WarnOnce(url, WarningCodes.ResourceBlocked,
                    $"Not loading '{url}': local file access is disabled (only data: and http(s) URLs are allowed)");
                return null;
            }
            url = ResolveAgainstBasePath(url, basePath);
        }

        var loaded = LazyInitializer.EnsureInitialized(ref _loaded, () => new(StringComparer.Ordinal))!;
        if (loaded.TryGetValue(url, out var cached)) return cached;
        var data = _resolver != null ? ResolveThroughResolver(url, type) : LoadBuiltIn(url, type, remote);
        loaded.TryAdd(url, data);
        return data;
    }

    private static byte[]? LoadBuiltIn(string url, ResourceType type, bool remote)
    {
        if (remote)
            return type == ResourceType.Image ? null : Text.FontUrlFetcher.Fetch(url);

        string path;
        try
        {
            path = url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(url).LocalPath : url;
        }
        catch (UriFormatException)
        {
            return null;
        }
        return LocalFileReader.TryReadAllBytes(path, LocalFileReader.DefaultMaxBytes);
    }

    private byte[]? ResolveThroughResolver(string url, ResourceType type)
    {
        var resolver = _resolver!;
        var limit = HtmlToPdf.ResourceTimeout;
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(limit);

        // Off the calling thread, so a resolver that awaits without ConfigureAwait(false) can't deadlock
        // against a caller's synchronization context while this thread blocks
        var task = Task.Run(() => resolver.ResolveAsync(url, type, timeout.Token));
        // Disposed only once the resolver is done with its token, even if this render stops waiting first
        task.ContinueWith(_ => timeout.Dispose(), TaskScheduler.Default);
        try
        {
            // Bounded wait even if the resolver ignores its token; throws if the caller cancels
            if (!task.Wait((int)Math.Min(limit.TotalMilliseconds, int.MaxValue), CancellationToken))
            {
                WarnOnce(url, WarningCodes.ResourceTimeout, $"Resource '{url}' timed out after {FormatSeconds(limit)}s");
                return null;
            }
            return task.Result?.Data;
        }
        catch (AggregateException ex)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var inner = ex.InnerException ?? ex;
            if (inner is OperationCanceledException)
                WarnOnce(url, WarningCodes.ResourceTimeout, $"Resource '{url}' timed out after {FormatSeconds(limit)}s");
            else
                WarnOnce(url, FailureCode(type), $"Loading '{url}' failed: {inner.Message}");
            return null;
        }
    }

    private static string FormatSeconds(TimeSpan span) =>
        span.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private static string FailureCode(ResourceType type) => type switch
    {
        ResourceType.Image => WarningCodes.ImageLoadFailed,
        ResourceType.Font => WarningCodes.FontLoadFailed,
        ResourceType.StyleSheet => WarningCodes.StylesheetLoadFailed,
        _ => WarningCodes.ImageLoadFailed,
    };

    private void WarnOnce(string url, string code, string message)
    {
        if (_warnings == null) return;
        var warned = LazyInitializer.EnsureInitialized(ref _warned, () => new(StringComparer.Ordinal))!;
        if (!warned.TryAdd(code + "|" + url, true)) return;
        lock (_warnings) _warnings.Add(code, message);
    }

    /// <summary>
    /// A relative file path joined to <paramref name="basePath"/>; URLs with a scheme (file:, ftp:, ...)
    /// and absolute paths pass through unchanged.
    /// </summary>
    private static string ResolveAgainstBasePath(string url, string? basePath)
    {
        if (string.IsNullOrEmpty(basePath) || HasScheme(url) || Path.IsPathRooted(url)) return url;
        try
        {
            return Path.GetFullPath(Path.Combine(basePath, url));
        }
        catch
        {
            return url;
        }
    }

    // A one-letter "scheme" is a Windows drive letter (C:\...), not a URL scheme
    private static bool HasScheme(string url)
    {
        int colon = url.IndexOf(':');
        if (colon < 2) return false;
        for (int i = 0; i < colon; i++)
        {
            char c = url[i];
            if (!(char.IsLetterOrDigit(c) || c == '+' || c == '-' || c == '.')) return false;
        }
        return char.IsLetter(url[0]);
    }

    /// <summary>Decode data:[&lt;mediatype&gt;][;base64],&lt;data&gt; to bytes (percent-decoded UTF-8 when not base64).</summary>
    internal static byte[]? DecodeDataUri(string dataUri)
    {
        int comma = dataUri.IndexOf(',');
        if (comma < 0) return null;

        var header = dataUri.Substring(5, comma - 5);
        var data = dataUri.Substring(comma + 1);
        if (header.IndexOf("base64", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            try { return Convert.FromBase64String(data); }
            catch { return null; }
        }

        try { return Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data)); }
        catch { return Encoding.UTF8.GetBytes(data); }
    }
}
