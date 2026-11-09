using System;
using System.Text;

namespace EggPdf.Core.Resources;

internal static class DataUri
{
    private const string Scheme = "data:";
    private const string Base64Marker = ";base64";
    private const string DefaultMimeType = "application/octet-stream";

    public static bool TryDecode(string url, out byte[] bytes, out string mimeType)
    {
        bytes = Array.Empty<byte>();
        mimeType = DefaultMimeType;
        if (url == null || !url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        int comma = url.IndexOf(',');
        if (comma < 0) return false;

        var header = url.Substring(Scheme.Length, comma - Scheme.Length).Trim();
        var data = url.Substring(comma + 1);
        bool base64 = header.EndsWith(Base64Marker, StringComparison.OrdinalIgnoreCase);
        var type = base64 ? header.Substring(0, header.Length - Base64Marker.Length) : header;
        if (type.Length > 0) mimeType = type;

        var body = PercentDecoded(data);
        if (!base64)
        {
            bytes = Encoding.UTF8.GetBytes(body);
            return true;
        }
        try
        {
            bytes = Convert.FromBase64String(body);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string PercentDecoded(string data)
    {
        if (data.IndexOf('%') < 0) return data;
        try { return Uri.UnescapeDataString(data); }
        catch (UriFormatException) { return data; }
    }
}
