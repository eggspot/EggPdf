using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    /// <summary>
    /// Check if an element has any inline element children (not just text nodes or br).
    /// A sibling explicitly styled as block-level (e.g. a &lt;span&gt; with CSS
    /// <c>display: block</c>) is excluded even though its tag name defaults to inline --
    /// the tag-name fallback alone previously routed such text through the word-by-word
    /// inline-flow path, which has no text-align centering support (each word box's
    /// Width equals its own ContentWidth, so PdfRenderer's centering never triggers).
    /// A real block-level sibling belongs on its own line regardless, so excluding it here
    /// lets the text instead take the "regular text with line wrapping" path, which does.
    /// </summary>
    private static bool HasInlineElementSiblings(HtmlElement parent, ComputedStyle parentStyle,
        Func<HtmlElement, ComputedStyle?, ComputedStyle>? resolver)
    {
        foreach (var child in parent.ChildNodes)
        {
            if (child is HtmlElement e && e.TagName != "br" && e.TagName != "img")
            {
                var tag = e.TagName;
                // Only count true inline elements (not block-level)
                if (tag != "div" && tag != "p" && tag != "h1" && tag != "h2" && tag != "h3" &&
                    tag != "h4" && tag != "h5" && tag != "h6" && tag != "ul" && tag != "ol" &&
                    tag != "li" && tag != "table" && tag != "blockquote" && tag != "pre" &&
                    tag != "hr" && tag != "section" && tag != "article" && tag != "nav" &&
                    tag != "header" && tag != "footer" && tag != "main" && tag != "aside" &&
                    tag != "figure" && tag != "figcaption" && tag != "details" && tag != "summary")
                {
                    if (resolver != null)
                    {
                        var childStyle = resolver(e, parentStyle);
                        if (IsBlockLevel(childStyle.Display)) continue;
                    }
                    return true;
                }
            }
        }
        return false;
    }

    private static float ResolveImgDimension(string? cssValue, string? htmlAttr, float containingSize, float fontSize, float defaultValue)
    {
        // CSS takes priority
        var resolved = ResolveOptionalLength(cssValue, containingSize, fontSize);
        if (resolved.HasValue) return resolved.Value;

        // HTML attribute
        if (!string.IsNullOrEmpty(htmlAttr))
        {
            var attrResolved = ResolveOptionalLength(htmlAttr.EndsWith("%") ? htmlAttr : htmlAttr + "px", containingSize, fontSize);
            if (attrResolved.HasValue) return attrResolved.Value;
        }

        return defaultValue;
    }

    private static bool IsBlockLevel(string display)
    {
        return display == "block" || display == "list-item" ||
               display == "table" || display == "flex" || display == "grid" ||
               display == "table-row-group" || display == "table-header-group" ||
               display == "table-footer-group" || display == "table-row" ||
               display == "table-cell" || display == "table-caption";
    }

    /// <summary>
    /// Parse a CSS srcset attribute and return the best URL for PDF rendering.
    /// PDF is treated as a 1x print context: prefer the 1x density or smallest-width candidate.
    /// Returns null if srcset is empty/null so the caller can fall back to src.
    /// </summary>
    private static string? ResolveSrcset(string? srcset, float elementWidthPx)
    {
        if (string.IsNullOrWhiteSpace(srcset)) return null;

        string? bestUrl = null;
        float bestWidth = float.MaxValue;
        bool foundDensity = false;

        // Each candidate: "url [descriptor]" separated by commas
        var candidates = srcset.Split(',');
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i].Trim();
            if (string.IsNullOrEmpty(candidate)) continue;

            // Split into URL and descriptor (last token that is a descriptor)
            int lastSpace = candidate.LastIndexOf(' ');
            if (lastSpace < 0)
            {
                // No descriptor — treat as 1x
                if (!foundDensity && bestUrl == null)
                    bestUrl = candidate;
                continue;
            }

            string url = candidate.Substring(0, lastSpace).Trim();
            string descriptor = candidate.Substring(lastSpace + 1).Trim().ToLowerInvariant();

            if (descriptor.EndsWith("x"))
            {
                // Density descriptor: prefer 1x
                float.TryParse(descriptor.Substring(0, descriptor.Length - 1), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float density);
                if (!foundDensity || Math.Abs(density - 1f) < Math.Abs(bestWidth - 1f))
                {
                    if (!foundDensity || density <= 1f)
                    { bestUrl = url; bestWidth = density; foundDensity = true; }
                }
            }
            else if (descriptor.EndsWith("w"))
            {
                // Width descriptor: pick closest to element width from below, or smallest
                float.TryParse(descriptor.Substring(0, descriptor.Length - 1), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float w);
                if (!foundDensity && w < bestWidth)
                { bestUrl = url; bestWidth = w; }
            }
        }

        return bestUrl;
    }

    /// <summary>
    /// Find the best image source from a &lt;picture&gt; element.
    /// Returns (imageUrl, imgElement) from a &lt;source&gt; or the fallback &lt;img&gt;.
    /// </summary>
    private static (string? src, Html.Dom.HtmlElement? imgElem) ResolvePicture(Html.Dom.HtmlElement picture)
    {
        Html.Dom.HtmlElement? fallbackImg = null;
        Html.Dom.HtmlElement? selectedSource = null;

        foreach (var child in picture.ChildNodes)
        {
            if (!(child is Html.Dom.HtmlElement elem)) continue;
            if (elem.TagName == "img")
            { fallbackImg = elem; }
            else if (elem.TagName == "source" && selectedSource == null)
            {
                // Prefer <source> with media="print" or no media (first wins)
                var media = elem.GetAttribute("media");
                if (string.IsNullOrEmpty(media) ||
                    media.IndexOf("print", StringComparison.OrdinalIgnoreCase) >= 0)
                    selectedSource = elem;
            }
        }

        if (selectedSource != null)
        {
            // Use the source's srcset, or fall back to img dimensions
            var srcset = selectedSource.GetAttribute("srcset");
            var src = ResolveSrcset(srcset, 0) ?? FirstSrcsetUrl(srcset);
            // Return img element for dimension resolution; source provides the URL
            return (src, fallbackImg ?? selectedSource);
        }

        if (fallbackImg != null)
        {
            var srcset = fallbackImg.GetAttribute("srcset");
            var src = ResolveSrcset(srcset, 0) ?? fallbackImg.GetAttribute("src");
            return (src, fallbackImg);
        }

        return (null, null);
    }


    private static string? GetTextContent(HtmlElement element)
    {
        var sb = new System.Text.StringBuilder();
        CollectTextRecursive(element, sb);
        var result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    private static void CollectTextRecursive(HtmlNode node, System.Text.StringBuilder sb)
    {
        if (node is HtmlTextNode text)
        {
            sb.Append(text.Data);
            return;
        }
        if (node is HtmlElement elem)
        {
            foreach (var child in elem.ChildNodes)
                CollectTextRecursive(child, sb);
        }
    }

}
