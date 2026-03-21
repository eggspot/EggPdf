using System;
using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Core.Resources;

/// <summary>
/// Resolves data: URIs (base64 or URL-encoded inline data).
/// </summary>
public class DataUriResolver : IResourceResolver
{
    public Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default)
    {
        if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<ResourceResult?>(null);

        // Format: data:[<mediatype>][;base64],<data>
        int commaIndex = url.IndexOf(',');
        if (commaIndex < 0)
            return Task.FromResult<ResourceResult?>(null);

        string header = url.Substring(5, commaIndex - 5); // after "data:" before ","
        string data = url.Substring(commaIndex + 1);

        bool isBase64 = header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        string mimeType = isBase64
            ? header.Substring(0, header.Length - 7) // remove ";base64"
            : header;

        if (string.IsNullOrEmpty(mimeType))
            mimeType = "application/octet-stream";

        byte[] bytes;
        if (isBase64)
        {
            bytes = string.IsNullOrEmpty(data) ? Array.Empty<byte>() : Convert.FromBase64String(data);
        }
        else
        {
            // URL-encoded
            var decoded = Uri.UnescapeDataString(data);
            bytes = System.Text.Encoding.UTF8.GetBytes(decoded);
        }

        return Task.FromResult<ResourceResult?>(new ResourceResult(bytes, mimeType, url));
    }
}
