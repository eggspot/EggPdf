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
    /// <summary>HTML tag name -> PDF standard structure type, for the tags this MVP tags. Everything else passes through to its nearest tagged ancestor untouched.</summary>
    private static readonly Dictionary<string, string> TagToType = new()
    {
        ["h1"] = "H1", ["h2"] = "H2", ["h3"] = "H3", ["h4"] = "H4", ["h5"] = "H5", ["h6"] = "H6",
        ["p"] = "P",
        ["table"] = "Table", ["tr"] = "TR", ["th"] = "TH", ["td"] = "TD",
        ["ul"] = "L", ["ol"] = "L", ["li"] = "LI",
        ["img"] = "Figure",
        ["a"] = "Link",
    };

    /// <summary>
    /// Walk a laid-out box tree (before pagination splits it into per-page paint calls -- see
    /// PageFragmenter.CollectPaintableBoxes, which this mirrors), building a parallel structure
    /// tree rooted at a synthetic "Document" element and a map from every box to the element its
    /// content belongs under.
    /// </summary>
    public static (PdfStructureElement root, Dictionary<LayoutBox, PdfStructureElement> boxToElement) Build(LayoutBox root)
    {
        var rootElem = new PdfStructureElement("Document");
        var map = new Dictionary<LayoutBox, PdfStructureElement>();
        BuildRecursive(root, rootElem, map);
        return (rootElem, map);
    }

    private static void BuildRecursive(LayoutBox box, PdfStructureElement currentAncestor, Dictionary<LayoutBox, PdfStructureElement> map)
    {
        var tag = box.TagName;
        var target = currentAncestor;

        if (tag != null && TagToType.TryGetValue(tag, out var type))
        {
            var elem = new PdfStructureElement(type);
            ApplyAttributes(elem, box, tag);
            currentAncestor.AddChild(elem);
            target = elem;
        }

        // LI's only allowed children are Lbl/LBody (ISO 14289-1 7.2-17..20) -- wrap its content
        // in LBody so the tree stays structurally valid without modeling list-item labels separately.
        if (tag == "li")
        {
            var lbody = new PdfStructureElement("LBody");
            target.AddChild(lbody);
            target = lbody;
        }

        map[box] = target;

        foreach (var child in box.Children)
            BuildRecursive(child, target, map);
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
