using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Core.Resources;

/// <summary>
/// Resolves URLs to resource data (images, fonts, stylesheets). Pass one as
/// <c>PdfRenderOptions.ResourceResolver</c> and every external resource a document references goes
/// through it. Implementations must be thread-safe: a render may call it concurrently (e.g. to fetch
/// several web fonts at once).
/// </summary>
public interface IResourceResolver
{
    /// <summary>
    /// Fetch <paramref name="url"/> (as written in the document; relative file paths are first resolved
    /// against the render's base path when one is given). Return null when the URL isn't handled, is
    /// refused, or fails. Honour <paramref name="ct"/>: it is cancelled when the caller cancels the render
    /// or the per-resource time limit passes.
    /// </summary>
    Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default);
}

/// <summary>Result of resolving a resource URL.</summary>
public class ResourceResult
{
    public byte[] Data { get; }
    public string? MimeType { get; }
    public string? ResolvedUrl { get; }

    public ResourceResult(byte[] data, string? mimeType = null, string? resolvedUrl = null)
    {
        Data = data;
        MimeType = mimeType;
        ResolvedUrl = resolvedUrl;
    }
}

public enum ResourceType
{
    Image,
    Font,
    StyleSheet,
    Other
}
