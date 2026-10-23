using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Core.Resources;

/// <summary>
/// Reads local files for resource loading with a size cap, so a document can't exhaust memory by
/// pointing at something endless such as <c>/dev/zero</c> or a huge file.
/// </summary>
internal static class LocalFileReader
{
    /// <summary>Default cap for one local resource, matching <see cref="HttpResourceOptions.MaxResponseSizeBytes"/>.</summary>
    public const long DefaultMaxBytes = 50 * 1024 * 1024;

    /// <summary>Path comparison matching the file system: case-insensitive on Windows only.</summary>
    public static StringComparison PathComparison { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The file's bytes, or null when it doesn't exist, can't be read, or is larger than <paramref name="maxBytes"/>.</summary>
    public static byte[]? TryReadAllBytes(string path, long maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ReadCapped(stream, maxBytes);
        }
        catch
        {
            return null; // permission denied, malformed path, etc.
        }
    }

    /// <inheritdoc cref="TryReadAllBytes"/>
    public static Task<byte[]?> TryReadAllBytesAsync(string path, long maxBytes, CancellationToken ct) =>
        Task.Run(() => TryReadAllBytes(path, maxBytes), ct);

    // Reads to EOF rather than trusting Length: devices and pipes report 0
    private static byte[]? ReadCapped(Stream stream, long maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
