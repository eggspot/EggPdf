using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Core.Resources;

/// <summary>
/// Resolves http:// and https:// URLs via HttpClient.
/// </summary>
public class HttpResourceResolver : IResourceResolver
{
    private readonly HttpClient _httpClient;
    private readonly HttpResourceOptions _options;

    public HttpResourceResolver(HttpResourceOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? new HttpResourceOptions();
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds)
        };
    }

    public async Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        // Domain allowlist
        if (_options.AllowedDomains != null && _options.AllowedDomains.Length > 0)
        {
            var uri = new Uri(url);
            if (!_options.AllowedDomains.Any(d =>
                uri.Host.Equals(d, StringComparison.OrdinalIgnoreCase)))
                return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", _options.UserAgent);

        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        // Check content length
        if (response.Content.Headers.ContentLength > _options.MaxResponseSizeBytes)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync();

        if (bytes.Length > _options.MaxResponseSizeBytes)
            return null;

        var mimeType = response.Content.Headers.ContentType?.MediaType;

        return new ResourceResult(bytes, mimeType, url);
    }
}

public class HttpResourceOptions
{
    public string[]? AllowedDomains { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
    public long MaxResponseSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB
    public int MaxRedirects { get; set; } = 5;
    public string UserAgent { get; set; } = "EggPdf/0.1";
}
