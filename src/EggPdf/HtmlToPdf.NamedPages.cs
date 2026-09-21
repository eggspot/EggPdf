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

    /// <summary>
    /// Split the body into page groups for <c>page: &lt;name&gt;</c>: a change of used page name between
    /// top-level blocks forces a page break, and each group gets the size, margins and margin boxes of
    /// its <c>@page &lt;name&gt;</c> rules. Returns null -- leaving the ordinary single-size path
    /// untouched -- when the document declares no named @page rule or no block uses one.
    /// Only direct children of &lt;body&gt; select a page name (the common section-level use).
    /// </summary>
    private static List<PageGroup>? FindNamedPageGroups(HtmlDocument document, CascadeResolver resolver, PageSettings baseSettings)
    {
        if (baseSettings.NamedRules.Count == 0) return null;
        var html = document.DocumentElement;
        var body = document.Body;
        if (html == null || body == null) return null;

        var bodyStyle = resolver.Resolve(body, resolver.Resolve(html, null));
        var groups = new List<PageGroup>();
        PageGroup? current = null;
        bool anyNamed = false;

        foreach (var node in body.ChildNodes)
        {
            string? name = current?.Name; // text and comments stay with the group they sit in
            bool isElement = node is HtmlElement;
            if (node is HtmlElement element)
            {
                var value = resolver.Resolve(element, bodyStyle).Get("page")?.Trim();
                name = string.IsNullOrEmpty(value) || value == "auto" ? null : value;
                if (name != null) anyNamed = true;
            }

            if (current == null || (isElement && name != current.Name))
            {
                current = new PageGroup { Name = name };
                groups.Add(current);
            }
            current.Nodes.Add(node);
        }

        if (!anyNamed) return null;

        // A group holding only whitespace/comments (e.g. the newline before the first block)
        // would otherwise render as an empty page.
        groups.RemoveAll(g => !g.Nodes.Exists(n => n is HtmlElement));
        return groups;
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
        for (int i = 0; i < layouts.Count; i++)
        {
            var scratch = new PdfDocument();
            RenderOneGroup(layouts[i].root, layouts[i].settings, scratch, 0, null);
            counts[i] = scratch.PageCount;
            total += counts[i];
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
