using System.Collections.Generic;
using EggPdf.Layout;
using EggPdf.Pdf;

namespace EggPdf;

/// <summary>
/// Builds a PDF/UA-1 structure tree (<see cref="PdfStructureElement"/>) that mirrors a laid-out
/// box tree, plus a map from every reachable <see cref="LayoutBox"/> to the structure element its
/// painted content should be attributed to (see <see cref="Build"/>). The paint layer
/// (<c>BoxPainter.StructureMap</c>) consults that map to wrap each box's content in the right
/// marked-content tag.
/// </summary>
internal static class StructureTreeBuilder
{
    /// <summary>
    /// HTML tag name -> PDF structure type, for the tags this MVP tags. Everything else passes
    /// through to its nearest tagged ancestor untouched. The landmark entries (nav/header/footer/
    /// aside/main/article/section) map to custom, non-standard types -- PDF 1.7 (what PDF/UA-1 is
    /// based on) has no native semantic-landmark types -- backed by a RoleMap fallback to a
    /// standard type declared unconditionally on StructTreeRoot (see PdfDocument.Tagging.cs's
    /// LandmarkRoleMap) so a reader that doesn't recognize the custom name still gets valid
    /// fallback semantics, per the RoleMap mechanism ISO 32000-1 14.7.5 defines for exactly this.
    /// </summary>
    private static readonly Dictionary<string, string> TagToType = new()
    {
        ["h1"] = "H1", ["h2"] = "H2", ["h3"] = "H3", ["h4"] = "H4", ["h5"] = "H5", ["h6"] = "H6",
        ["p"] = "P",
        ["table"] = "Table", ["tr"] = "TR", ["th"] = "TH", ["td"] = "TD",
        ["ul"] = "L", ["ol"] = "L", ["li"] = "LI",
        ["img"] = "Figure",
        ["a"] = "Link",
        ["nav"] = "Nav", ["header"] = "Header", ["footer"] = "Footer", ["aside"] = "Aside",
        ["main"] = "Main", ["article"] = "Article", ["section"] = "Section",
    };

    /// <summary>
    /// Walk one or more laid-out box trees (before pagination splits them into per-page paint
    /// calls -- see PageFragmenter.CollectPaintableBoxes, which this mirrors) -- more than one
    /// root only when named page groups (<c>page: &lt;name&gt;</c> + <c>@page &lt;name&gt;</c>)
    /// split the document into several independently laid-out groups -- building one parallel
    /// structure tree rooted at a synthetic "Document" element (ISO 14289-1 8.2.5.2 wants exactly
    /// one Document root even when the content came from multiple layout passes) and a map from
    /// every box, across every group, to the element its content belongs under.
    /// </summary>
    public static (PdfStructureElement root, Dictionary<LayoutBox, PdfStructureElement> boxToElement) Build(IReadOnlyList<LayoutBox> roots)
    {
        var rootElem = new PdfStructureElement("Document");
        var map = new Dictionary<LayoutBox, PdfStructureElement>();
        var spanElements = new Dictionary<InlineElementSpan, PdfStructureElement>();
        foreach (var root in roots)
            BuildRecursive(root, rootElem, map, spanElements);
        return (rootElem, map);
    }

    private static void BuildRecursive(LayoutBox box, PdfStructureElement currentAncestor,
        Dictionary<LayoutBox, PdfStructureElement> map, Dictionary<InlineElementSpan, PdfStructureElement> spanElements)
    {
        var target = currentAncestor;

        if (box.InlineSpan != null)
        {
            // A word-fragment of a multi-word inline element (e.g. <a>Click here</a> -- see
            // InlineElementSpan and LayoutBox.InlineSpan). Element is null on every fragment
            // after the first, so box.TagName alone would silently fall through to the ancestor
            // for those; reuse the ONE structure element every fragment sharing this span (i.e.
            // on the same line) belongs under, created on the first fragment encountered, rather
            // than creating one per word.
            var spanTag = box.InlineSpan.Element.TagName;
            if (TagToType.TryGetValue(spanTag, out var spanType))
            {
                if (!spanElements.TryGetValue(box.InlineSpan, out var spanElem))
                {
                    spanElem = new PdfStructureElement(spanType);
                    ApplyAttributes(spanElem, box, spanTag);
                    currentAncestor.AddChild(spanElem);
                    spanElements[box.InlineSpan] = spanElem;
                }
                target = spanElem;
            }
        }
        else
        {
            var tag = box.TagName;
            if (tag != null && TagToType.TryGetValue(tag, out var type))
            {
                var elem = new PdfStructureElement(type);
                ApplyAttributes(elem, box, tag);
                currentAncestor.AddChild(elem);
                target = elem;
            }

            // LI's only allowed children are Lbl/LBody (ISO 14289-1 7.2-17..20) -- wrap its
            // content in LBody so the tree stays structurally valid without modeling list-item
            // labels separately.
            if (tag == "li")
            {
                var lbody = new PdfStructureElement("LBody");
                target.AddChild(lbody);
                target = lbody;
            }
        }

        map[box] = target;

        foreach (var child in box.Children)
            BuildRecursive(child, target, map, spanElements);
    }

    private static void ApplyAttributes(PdfStructureElement elem, LayoutBox box, string tag)
    {
        if (tag == "img")
            elem.Alt = box.Element?.GetAttribute("alt") ?? "";

        if (tag == "th")
        {
            var scope = box.Element?.GetAttribute("scope")?.ToLowerInvariant();
            elem.TableScope = scope switch
            {
                "col" or "colgroup" => "Column",
                "row" or "rowgroup" => "Row",
                _ => null,
            };
        }

        var lang = box.Element?.GetAttribute("lang");
        if (!string.IsNullOrEmpty(lang))
            elem.Lang = lang;
    }
}
