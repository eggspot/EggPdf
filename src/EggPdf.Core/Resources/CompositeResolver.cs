using System;
using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Core.Resources;

/// <summary>
/// Default resolver that dispatches to the appropriate resolver based on URL scheme.
/// </summary>
public class CompositeResolver : IResourceResolver
{
    private readonly DataUriResolver _dataUri = new();
    private readonly HttpResourceResolver _http;
    private readonly FileResourceResolver _file;

    public CompositeResolver(string? baseDirectory = null, HttpResourceOptions? httpOptions = null)
    {
        _http = new HttpResourceResolver(httpOptions);
        _file = new FileResourceResolver(baseDirectory);
    }

    public async Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return await _dataUri.ResolveAsync(url, type, ct);

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return await _http.ResolveAsync(url, type, ct);

        // Default: treat as file path
        return await _file.ResolveAsync(url, type, ct);
    }
}
