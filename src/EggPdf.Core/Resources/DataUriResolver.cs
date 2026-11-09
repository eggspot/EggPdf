using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Core.Resources;

/// <summary>
/// Resolves data: URIs (base64 or URL-encoded inline data). A data: URI whose base64 body is malformed
/// resolves to null.
/// </summary>
public class DataUriResolver : IResourceResolver
{
    /// <inheritdoc />
    public Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default) =>
        Task.FromResult<ResourceResult?>(DataUri.TryDecode(url, out var bytes, out var mimeType)
            ? new ResourceResult(bytes, mimeType, url)
            : null);
}
