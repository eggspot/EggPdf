using System.Collections.Generic;
using EggPdf.Core;
using EggPdf.Css.Cascade;
using EggPdf.Html.Dom;
using EggPdf.Layout;
using EggPdf.Pdf;

namespace EggPdf;

public static partial class HtmlToPdf
{
    /// <summary>Consecutive top-level blocks that share one used page name.</summary>
    private sealed class PageGroup
    {
        public string? Name;
        public readonly List<HtmlNode> Nodes = new();
    }

    /// <summary>A unit of body content with the page name it is laid out under and the wrapper elements it sits in.</summary>
    private sealed class PageLeaf
    {
        public HtmlNode Node = null!;
        public string? Name;
        public readonly List<HtmlElement> Wrappers = new();
    }

    /// <summary>
    /// Split the body into page groups for <c>page: &lt;name&gt;</c>: a change of used page name between
    /// blocks forces a page break, and each group gets the size, margins and margin boxes of its
    /// <c>@page &lt;name&gt;</c> rules. Returns null -- leaving the ordinary single-size path untouched --
    /// when the document declares no named @page rule or no element uses one. A named element may sit
    /// anywhere: wrapper elements around it are fragmented (cloned per page, keeping their attributes and
    /// their original parent for selector matching) so the split lands between the right siblings.
    /// </summary>
    private static List<PageGroup>? FindNamedPageGroups(HtmlDocument document, CascadeResolver resolver, PageSettings baseSettings)
    {
        if (baseSettings.NamedRules.Count == 0) return null;
        var html = document.DocumentElement;
        var body = document.Body;
        if (html == null || body == null) return null;

        var bodyStyle = resolver.Resolve(body, resolver.Resolve(html, null));
        var leaves = new List<PageLeaf>();
        var namedBelow = new Dictionary<HtmlElement, bool>();
        CollectLeaves(body.ChildNodes, bodyStyle, resolver, new List<HtmlElement>(), namedBelow, leaves);
        if (!leaves.Exists(l => l.Name != null)) return null;

        var groups = new List<PageGroup>();
        PageGroup? current = null;
        var wrapperClones = new Dictionary<HtmlElement, HtmlElement>();
        foreach (var leaf in leaves)
        {
            bool isElement = leaf.Node is HtmlElement;
            if (current == null || (isElement && leaf.Name != current.Name))
            {
                current = new PageGroup { Name = leaf.Name };
                groups.Add(current);
                wrapperClones.Clear();
            }
            AddToGroup(current, leaf, wrapperClones);
        }

        // A group holding only whitespace/comments (e.g. the newline before the first block)
        // would otherwise render as an empty page.
        groups.RemoveAll(g => !g.Nodes.Exists(n => n is HtmlElement));
        return groups;
    }

    /// <summary>
    /// Flatten the body's children into leaves. An element that declares no page name but contains one deeper
    /// becomes a transparent wrapper (its children are collected instead); everything else is a leaf.
    /// </summary>
    private static void CollectLeaves(IEnumerable<HtmlNode> nodes, Css.ComputedStyle parentStyle, CascadeResolver resolver,
        List<HtmlElement> wrappers, Dictionary<HtmlElement, bool> namedBelow, List<PageLeaf> leaves)
    {
        string? lastName = null; // text and comments stay with the group they sit in
        foreach (var node in nodes)
        {
            var leaf = new PageLeaf { Node = node, Name = lastName };
            leaf.Wrappers.AddRange(wrappers);

            if (node is HtmlElement element)
            {
                var style = resolver.Resolve(element, parentStyle);
                var value = style.Get("page")?.Trim();
                string? name = string.IsNullOrEmpty(value) || value == "auto" ? null : value;

                if (name == null && HasNamedBelow(element, namedBelow, style, resolver))
                {
                    wrappers.Add(element);
                    CollectLeaves(element.ChildNodes, style, resolver, wrappers, namedBelow, leaves);
                    wrappers.RemoveAt(wrappers.Count - 1);
                    lastName = null;
                    continue;
                }
                leaf.Name = name;
                lastName = name;
            }
            leaves.Add(leaf);
        }
    }

    /// <summary>Whether any descendant element declares a page name (memoized per element).</summary>
    private static bool HasNamedBelow(HtmlElement element, Dictionary<HtmlElement, bool> memo, Css.ComputedStyle style, CascadeResolver resolver)
    {
        if (memo.TryGetValue(element, out var known)) return known;

        bool found = false;
        foreach (var child in element.ChildNodes)
        {
            if (!(child is HtmlElement childElement)) continue;
            var childStyle = resolver.Resolve(childElement, style);
            var value = childStyle.Get("page")?.Trim();
            if ((!string.IsNullOrEmpty(value) && value != "auto") || HasNamedBelow(childElement, memo, childStyle, resolver))
            {
                found = true;
                break;
            }
        }
        memo[element] = found;
        return found;
    }

    /// <summary>Append a leaf to a group, recreating (once per group) the wrapper chain it came from.</summary>
    private static void AddToGroup(PageGroup group, PageLeaf leaf, Dictionary<HtmlElement, HtmlElement> clones)
    {
        List<HtmlNode> container = group.Nodes;
        foreach (var wrapper in leaf.Wrappers)
        {
            if (!clones.TryGetValue(wrapper, out var clone))
            {
                clone = CloneWrapper(wrapper);
                clones[wrapper] = clone;
                container.Add(clone);
            }
            container = clone.ChildNodes;
        }
        container.Add(leaf.Node);
    }

    /// <summary>
    /// A childless copy of an element with the same attributes. Its Parent points at the original's parent (set by
    /// briefly appending it there and detaching it from the child list again) so selectors like <c>body &gt; main</c>
    /// still match; its future children keep their original parents for the same reason.
    /// </summary>
    private static HtmlElement CloneWrapper(HtmlElement original)
    {
        var clone = new HtmlElement(original.TagName);
        foreach (var attribute in original.Attributes) clone.SetAttribute(attribute.Key, attribute.Value);
        if (original.Parent != null)
        {
            original.Parent.AppendChild(clone);
            original.Parent.ChildNodes.Remove(clone);
        }
        return clone;
    }

    /// <summary>Lay out each page group at its own page size (the body temporarily holds only that group's nodes).</summary>
    private static List<(LayoutBox root, PageSettings settings)> LayoutPageGroups(HtmlDocument document,
        List<PageGroup> groups, PageSettings baseSettings, CascadeResolver cascade)
    {
        var body = document.Body!;
        var original = new List<HtmlNode>(body.ChildNodes);
        var result = new List<(LayoutBox, PageSettings)>(groups.Count);
        try
        {
            foreach (var group in groups)
            {
                var settings = group.Name == null ? baseSettings : PageRuleResolver.ForNamedPage(baseSettings, group.Name);
                body.ChildNodes.Clear();
                body.ChildNodes.AddRange(group.Nodes);
                var root = BlockLayout.LayoutDocument(document, settings.ContentWidthPx, settings.ContentHeightPx, cascade,
                    fullPageWidth: settings.PageWidthPx, fullPageHeight: settings.PageHeightPx);
                result.Add((root, settings));
            }
        }
        finally
        {
            body.ChildNodes.Clear();
            body.ChildNodes.AddRange(original);
        }
        return result;
    }

    /// <summary>
    /// Render page groups one after another with continuous page numbering. Each group's pages are
    /// counted first (in a throwaway document) so counter(pages) and margin boxes see the document total.
    /// </summary>
    private static void RenderPageGroups(List<(LayoutBox root, PageSettings settings)> layouts, PdfDocument pdfDoc)
    {
        var counts = new int[layouts.Count];
        int total = 0;

        // The precount pass exists only to learn each group's page count for counter(pages) and
        // margin boxes; its pages are thrown away. If PDF/UA-1 tagging is active, BoxPainter.StructureMap
        // is already set (by the caller, before RenderPageGroups runs) and would register real (page,
        // MCID) pairs from these discarded pages against the one shared structure tree -- doubling up
        // every element's content refs, once for the discarded scratch pages and once for the real
        // ones. Suppress it here and restore it for the real pass below.
        var savedStructureMap = Paint.BoxPainter.StructureMap;
        Paint.BoxPainter.StructureMap = null;
        try
        {
            for (int i = 0; i < layouts.Count; i++)
            {
                var scratch = new PdfDocument();
                RenderOneGroup(layouts[i].root, layouts[i].settings, scratch, 0, null);
                counts[i] = scratch.PageCount;
                total += counts[i];
            }
        }
        finally
        {
            Paint.BoxPainter.StructureMap = savedStructureMap;
        }

        int offset = 0;
        for (int i = 0; i < layouts.Count; i++)
        {
            RenderOneGroup(layouts[i].root, layouts[i].settings, pdfDoc, offset, total);
            offset += counts[i];
        }
    }

    private static void RenderOneGroup(LayoutBox root, PageSettings settings, PdfDocument target, int pageOffset, int? totalPages)
    {
        float pageWidthPt = settings.PageWidthPx * PdfCoordinates.PxToPt;
        float pageHeightPt = settings.PageHeightPx * PdfCoordinates.PxToPt;
        var marginBoxes = MarginBoxRenderer.Build(settings, settings.PageWidthPx, settings.PageHeightPx);
        PdfRenderer.Render(root, target, pageWidthPt, pageHeightPt, settings.PageHeightPx,
            settings.MarginLeft, settings.MarginTop, settings.MarginBottom, marginBoxes, pageOffset, totalPages);
    }
}
