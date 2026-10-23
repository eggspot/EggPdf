using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Core.Resources;

/// <summary>
/// Resolves file paths to local filesystem resources, confined to one base directory: path traversal
/// (<c>../</c>), absolute paths outside it, and (on .NET 6+) symbolic links whose target lies outside it
/// are refused. Files larger than <see cref="MaxFileSizeBytes"/> are refused.
/// </summary>
public class FileResourceResolver : IResourceResolver
{
    private readonly string _baseDirectory;

    /// <summary>Creates a resolver confined to <paramref name="baseDirectory"/> (default: the current directory).</summary>
    public FileResourceResolver(string? baseDirectory = null)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory ?? Directory.GetCurrentDirectory());
    }

    /// <summary>Largest file read, in bytes; larger files (and endless ones such as devices) resolve to null.</summary>
    public long MaxFileSizeBytes { get; set; } = LocalFileReader.DefaultMaxBytes;

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

        // Security: ensure resolved path, and any link on the way to it, stays within base directory
        if (!IsInsideBase(fullPath) || !LinksStayInsideBase(fullPath))
            return null;

        var bytes = await LocalFileReader.TryReadAllBytesAsync(fullPath, MaxFileSizeBytes, ct);
        if (bytes == null)
            return null;

        return new ResourceResult(bytes, DetectMimeType(fullPath), fullPath);
    }

    private bool IsInsideBase(string fullPath)
    {
        // Append separator to prevent "C:\docs" matching "C:\docs-evil\file"
        string baseWithSep = _baseDirectory.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? _baseDirectory
            : _baseDirectory + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(baseWithSep, LocalFileReader.PathComparison);
    }

    /// <summary>
    /// Every symbolic link between the base directory and the file (the file itself included) must point
    /// inside the base directory. Links in the base directory's own path are the operator's choice and
    /// are not checked. .NET Standard has no API to read link targets, so there links are not checked.
    /// </summary>
    private bool LinksStayInsideBase(string fullPath)
    {
#if NET6_0_OR_GREATER
        try
        {
            for (string? path = fullPath; path != null && IsInsideBase(path); path = Path.GetDirectoryName(path))
            {
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                if (info.LinkTarget == null) continue;
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target == null || !IsInsideBase(Path.GetFullPath(target.FullName))) return false;
            }
            return true;
        }
        catch (Exception)
        {
            return false; // unreadable link: refuse rather than guess
        }
#else
        return true;
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
