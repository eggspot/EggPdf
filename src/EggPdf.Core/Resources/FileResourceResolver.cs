using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Core.Resources;

/// <summary>
/// Resolves file paths to local filesystem resources. Prevents path traversal attacks.
/// </summary>
public class FileResourceResolver : IResourceResolver
{
    private readonly string _baseDirectory;

    public FileResourceResolver(string? baseDirectory = null)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory ?? Directory.GetCurrentDirectory());
    }

    public async Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default)
    {
        // Don't handle non-file URLs
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        // Strip file:// prefix if present
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            url = url.Substring(7);

        // Resolve to absolute path
        string fullPath;
        if (Path.IsPathRooted(url))
            fullPath = Path.GetFullPath(url);
        else
            fullPath = Path.GetFullPath(Path.Combine(_baseDirectory, url));

        // Security: ensure resolved path is within base directory.
        // Append separator to prevent "C:\docs" matching "C:\docs-evil\file".
        string baseWithSep = _baseDirectory.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? _baseDirectory
            : _baseDirectory + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!File.Exists(fullPath))
            return null;

        var bytes = await ReadAllBytesAsync(fullPath, ct);
        var mimeType = DetectMimeType(fullPath);

        return new ResourceResult(bytes, mimeType, fullPath);
    }

    private static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct)
    {
#if NET6_0_OR_GREATER
        return await File.ReadAllBytesAsync(path, ct);
#else
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var bytes = new byte[stream.Length];
        await stream.ReadAsync(bytes, 0, bytes.Length, ct);
        return bytes;
#endif
    }

    private static string? DetectMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".css" => "text/css",
            ".html" or ".htm" => "text/html",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => null
        };
    }
}
