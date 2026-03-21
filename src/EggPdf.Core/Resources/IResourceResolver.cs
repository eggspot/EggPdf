using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Core.Resources;

/// <summary>
/// Resolves URLs to resource data (images, fonts, stylesheets).
/// </summary>
public interface IResourceResolver
{
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
