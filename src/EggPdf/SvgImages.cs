using System;
using System.Text;
using EggPdf.Html;
using EggPdf.Html.Dom;

namespace EggPdf;

internal static class SvgImages
{
    private const int MaxSvgImageBytes = 16 * 1024 * 1024;
    private const int SniffedPrefixBytes = 4096;
    private const char ByteOrderMark = (char)0xFEFF;

    internal static HtmlElement? ParseRoot(byte[] data)
    {
        if (data.Length == 0 || data.Length > MaxSvgImageBytes || !StartsLikeSvg(data)) return null;
        var document = HtmlParser.Parse(Encoding.UTF8.GetString(data));
        return FindFirstSvg(document.DocumentElement);
    }

    private static bool StartsLikeSvg(byte[] data)
    {
        var prefix = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, SniffedPrefixBytes)).TrimStart(ByteOrderMark, ' ', '\t', '\r', '\n');
        return prefix.Length > 0 && prefix[0] == '<' && prefix.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static HtmlElement? FindFirstSvg(HtmlElement? root)
    {
        if (root == null) return null;
        if (root.TagName == "svg") return root;
        foreach (var child in root.ChildNodes)
        {
            if (child is HtmlElement element)
            {
                var found = FindFirstSvg(element);
                if (found != null) return found;
            }
        }
        return null;
    }
}
