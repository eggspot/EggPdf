using System;
using EggPdf.Core;
using EggPdf.Layout;
using EggPdf.Pdf;

namespace EggPdf.Paint;

public static partial class BoxPainter
{
    /// <summary>
    /// Resolve a CSS Images Level 4 <c>image(...)</c> value: <c>image([ltr|rtl] &lt;image-src&gt;,
    /// ...)</c> optionally followed by a trailing <c>&lt;color&gt;</c> fallback. Picks the
    /// candidate tagged for <paramref name="isRTL"/>'s direction over an untagged candidate,
    /// over the opposite-direction tag, over whichever came first. Unlike <c>image-set()</c>
    /// (resolution candidates), <c>image()</c>'s tags select by writing direction, not density.
    /// </summary>
    private static (string? url, string? fallbackColor) ResolveImageFunction(string value, bool isRTL)
    {
        int open = value.IndexOf('(');
        int close = value.LastIndexOf(')');
        if (open < 0 || close <= open) return (null, null);
        string inner = value.Substring(open + 1, close - open - 1);

        string? ltrUrl = null, rtlUrl = null, untaggedUrl = null, firstUrl = null;
        string? fallbackColor = null;

        foreach (var rawPart in SplitTopLevel(inner, ','))
        {
            var part = rawPart.Trim();
            if (part.Length == 0) continue;

            string? tag = null;
            string rest = part;
            if (rest.StartsWith("ltr ", StringComparison.OrdinalIgnoreCase)) { tag = "ltr"; rest = rest.Substring(4).Trim(); }
            else if (rest.StartsWith("rtl ", StringComparison.OrdinalIgnoreCase)) { tag = "rtl"; rest = rest.Substring(4).Trim(); }
            else if (rest.Equals("ltr", StringComparison.OrdinalIgnoreCase)) { tag = "ltr"; rest = ""; }
            else if (rest.Equals("rtl", StringComparison.OrdinalIgnoreCase)) { tag = "rtl"; rest = ""; }

            bool looksLikeImageSrc = rest.StartsWith("url(", StringComparison.OrdinalIgnoreCase) ||
                (rest.Length >= 2 && (rest[0] == '\'' || rest[0] == '"'));

            if (looksLikeImageSrc)
            {
                string url = ExtractUrlToken(rest);
                if (url.Length == 0) continue;
                if (firstUrl == null) firstUrl = url;
                if (tag == "ltr") ltrUrl = url;
                else if (tag == "rtl") rtlUrl = url;
                else untaggedUrl = url;
            }
            else if (tag == null && rest.Length > 0)
            {
                // Not an image-src and not a bare direction tag -- the trailing <color> fallback.
                fallbackColor = rest;
            }
        }

        string? chosen = isRTL
            ? (rtlUrl ?? untaggedUrl ?? ltrUrl ?? firstUrl)
            : (ltrUrl ?? untaggedUrl ?? rtlUrl ?? firstUrl);

        return (chosen, fallbackColor);
    }

    /// <summary>
    /// Paint image()'s trailing &lt;color&gt; fallback as a solid fill when no image-src was
    /// given, or the one that was given failed to resolve/load. No-op (paints nothing) when
    /// there is no fallback color -- matching a browser leaving the background empty.
    /// </summary>
    private static void PaintImageFunctionFallback(PdfPage page, LayoutBox box, string? fallbackColor,
        float effectiveX, float pageHeightPx, float adjustedY)
    {
        if (string.IsNullOrEmpty(fallbackColor)) return;
        var color = ParseColor(fallbackColor);
        if (!color.HasValue) return;

        float pdfX = effectiveX * PdfCoordinates.PxToPt;
        float pdfY = (pageHeightPx - adjustedY - box.Height) * PdfCoordinates.PxToPt;
        float pdfW = box.Width * PdfCoordinates.PxToPt;
        float pdfH = box.Height * PdfCoordinates.PxToPt;
        page.AddRectangle(pdfX, pdfY, pdfW, pdfH, color.Value.R / 255f, color.Value.G / 255f, color.Value.B / 255f);
    }
}
