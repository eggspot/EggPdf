using System.Collections.Generic;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private static List<HtmlNode> TableChildrenInRenderOrder(HtmlElement element, ComputedStyle style) =>
        style.Display == "table" ? TableRowGroupsInRenderOrder(element) : element.ChildNodes;

    private static List<HtmlNode> TableRowGroupsInRenderOrder(HtmlElement table)
    {
        var children = table.ChildNodes;
        if (!HasMisplacedHeaderOrFooter(children)) return children;

        var ordered = new List<HtmlNode>(children.Count);
        HtmlElement? header = null;
        HtmlElement? footer = null;
        foreach (var child in children)
        {
            if (child is HtmlElement group)
            {
                if (group.TagName == "thead" && header == null) { header = group; continue; }
                if (group.TagName == "tfoot" && footer == null) { footer = group; continue; }
            }
            ordered.Add(child);
        }

        if (header != null)
            ordered.Insert(FirstRowContentIndex(ordered), header);
        if (footer != null)
            ordered.Add(footer);
        return ordered;
    }

    private static bool HasMisplacedHeaderOrFooter(List<HtmlNode> children)
    {
        bool sawBodyContent = false;
        bool sawFooter = false;
        foreach (var child in children)
        {
            if (child is not HtmlElement element) continue;
            switch (element.TagName)
            {
                case "thead":
                    if (sawBodyContent || sawFooter) return true;
                    break;
                case "tfoot":
                    sawFooter = true;
                    break;
                case "tbody":
                case "tr":
                    if (sawFooter) return true;
                    sawBodyContent = true;
                    break;
            }
        }
        return false;
    }

    private static int FirstRowContentIndex(List<HtmlNode> children)
    {
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is HtmlElement element &&
                (element.TagName == "tbody" || element.TagName == "tr" || element.TagName == "tfoot"))
                return i;
        }
        return children.Count;
    }
}
