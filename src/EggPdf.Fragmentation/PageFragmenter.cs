using System;
using System.Collections.Generic;
using EggPdf.Layout;

namespace EggPdf.Fragmentation;

/// <summary>
/// Pure page-break/pagination computation over a laid-out box tree: which
/// boxes are paintable, where forced/avoided breaks fall, which boxes are
/// fixed-positioned or pinned to a page bottom, orphan/widow line tracking,
/// and heading collection for bookmarks. Takes no dependency on how a page
/// is painted -- PdfRenderer supplies the PdfPage side separately via
/// EggPdf.Paint. Split out of PdfRenderer.cs, which mixed this with paint
/// logic in one file.
/// </summary>
public static class PageFragmenter
{
    private static readonly string[] BorderStyleKeys = { "border-top-style", "border-right-style", "border-bottom-style", "border-left-style" };

    /// <summary>A table's &lt;thead&gt; box and the table's overall bottom Y, for repeat-on-continuation-page purposes.</summary>
    public struct TableHeaderInfo
    {
        public LayoutBox TheadBox;
        public float TableBottom;
        public float FirstBodyRowBottom;
    }

    /// <summary>
    /// Find every &lt;table&gt; that has a &lt;thead&gt; child, so its header row(s)
    /// can be repainted at the top of each page the table's body continues onto.
    /// </summary>
    public static void CollectRepeatingTableHeaders(LayoutBox box, List<TableHeaderInfo> result)
    {
        if (box.Element?.TagName == "table")
        {
            for (int i = 0; i < box.Children.Count; i++)
            {
                if (box.Children[i].Element?.TagName == "thead")
                {
                    var thead = box.Children[i];
                    result.Add(new TableHeaderInfo
                    {
                        TheadBox = thead,
                        TableBottom = box.Y + box.Height,
                        FirstBodyRowBottom = FirstBodyRowBottom(box, thead) ?? thead.Y + thead.Height,
                    });
                    break;
                }
            }
        }

        for (int i = 0; i < box.Children.Count; i++)
            CollectRepeatingTableHeaders(box.Children[i], result);
    }

    private static float? FirstBodyRowBottom(LayoutBox table, LayoutBox thead)
    {
        float? best = null;
        foreach (var child in table.Children)
        {
            if (child == thead) continue;
            var tag = child.Element?.TagName;
            LayoutBox? row = tag == "tr" ? child : tag == "tbody" && child.Children.Count > 0 ? child.Children[0] : null;
            if (row == null || row.Y < thead.Y) continue;
            if (best == null || row.Y + row.Height < best.Value) best = row.Y + row.Height;
        }
        return best;
    }

    /// <summary>
    /// True when a page break must not cut through the box: text lines, images and other
    /// replaced content, table rows and cells, and boxes with no children. Containers with
    /// children split between their descendants instead.
    /// </summary>
    public static bool IsUnsplittable(LayoutBox box)
    {
        if (!string.IsNullOrEmpty(box.Text) || !string.IsNullOrEmpty(box.ImageSource) || box.ImageSvgRoot != null) return true;
        var tag = box.Element?.TagName;
        if (tag == "tr" || tag == "td" || tag == "th" || tag == "img" || tag == "svg" || tag == "canvas" || tag == "iframe")
            return true;
        return box.Children.Count == 0;
    }

    public static void CollectPaintableBoxes(LayoutBox box, List<LayoutBox> result)
    {
        bool hasBorder = false;
        var borderStyleFallback = box.Style.Get("border-style");
        for (int bsi = 0; bsi < 4; bsi++)
        {
            var sideStyle = box.Style.Get(BorderStyleKeys[bsi]) ?? borderStyleFallback;
            if (!string.IsNullOrEmpty(sideStyle) && sideStyle != "none" && sideStyle != "hidden")
            { hasBorder = true; break; }
        }

        var bgImageStyle = box.Style.Get("background-image");
        bool hasBgImage = !string.IsNullOrEmpty(bgImageStyle) && bgImageStyle != "none";

        var colRuleShorthand = box.Style.Get("column-rule");
        var colRuleStyle = box.Style.Get("column-rule-style");
        bool hasColumnRule = (!string.IsNullOrEmpty(colRuleStyle) && colRuleStyle != "none") ||
            (!string.IsNullOrEmpty(colRuleShorthand) && colRuleShorthand != "none" &&
             colRuleShorthand.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0);

        bool hasPaint = !string.IsNullOrEmpty(box.Text) ||
                        !string.IsNullOrEmpty(box.ImageSource) ||
                        hasBorder || hasBgImage || hasColumnRule ||
                        !string.IsNullOrEmpty(box.Style.BackgroundColor) &&
                        box.Style.BackgroundColor != "transparent" ||
                        box.Element?.TagName == "a" ||
                        box.Element?.TagName == "svg" ||
                        box.Element?.HasAttribute("id") == true;

        if (hasPaint)
            result.Add(box);

        foreach (var child in box.Children)
            CollectPaintableBoxes(child, result);
    }

    /// <summary>
    /// Set every descendant's <see cref="LayoutBox.AncestorOpacity"/> to the product of its ancestors'
    /// <c>opacity</c>, so the painter can fade a translucent element's whole subtree.
    /// </summary>
    public static void PropagateAncestorOpacity(LayoutBox root)
    {
        var pending = new Stack<LayoutBox>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var box = pending.Pop();
            float childOpacity = box.AncestorOpacity * OwnOpacity(box.Style.Get("opacity")) * FilterOpacity(box.Style.Get("filter"));
            foreach (var child in box.Children)
            {
                child.AncestorOpacity = childOpacity;
                pending.Push(child);
            }
        }
    }

    private static float OwnOpacity(string? opacity)
    {
        if (string.IsNullOrEmpty(opacity)) return 1f;
        var value = opacity!.Trim();
        bool percent = value.EndsWith("%", StringComparison.Ordinal);
        if (percent) value = value.Substring(0, value.Length - 1);
        if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) ||
            float.IsNaN(parsed))
            return 1f;
        if (percent) parsed /= 100f;
        return Math.Max(0f, Math.Min(1f, parsed));
    }

    private static float FilterOpacity(string? filter)
    {
        if (string.IsNullOrEmpty(filter)) return 1f;
        float product = 1f;
        int searchFrom = 0;
        while (true)
        {
            int start = filter!.IndexOf("opacity(", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return product;
            int argumentStart = start + "opacity(".Length;
            int end = filter.IndexOf(')', argumentStart);
            if (end < 0) return product;
            var argument = filter.Substring(argumentStart, end - argumentStart);
            if (argument.Trim().Length > 0) product *= OwnOpacity(argument);
            searchFrom = end + 1;
        }
    }

    public static void CollectPageBreaks(LayoutBox box, List<float> breakYs)
    {
        var breakBefore = box.Style.Get("page-break-before") ?? box.Style.Get("break-before");
        if (breakBefore == "always" || breakBefore == "page")
        {
            breakYs.Add(box.Y);
        }

        var breakAfter = box.Style.Get("page-break-after") ?? box.Style.Get("break-after");
        if (breakAfter == "always" || breakAfter == "page")
        {
            breakYs.Add(box.Y + box.Height);
        }

        foreach (var child in box.Children)
            CollectPageBreaks(child, breakYs);
    }

    /// <summary>
    /// Collect all boxes that have break-inside: avoid (or page-break-inside: avoid).
    /// These boxes must not be split across pages; if they would straddle a page boundary
    /// a page break is forced before their top edge.
    /// </summary>
    public static void CollectBreakInsideAvoid(LayoutBox box, List<(float y, float height)> result)
    {
        var breakInside = box.Style.Get("break-inside") ?? box.Style.Get("page-break-inside");
        if (breakInside == "avoid" || breakInside == "avoid-page")
            result.Add((box.Y, box.Height));

        foreach (var child in box.Children)
            CollectBreakInsideAvoid(child, result);
    }

    /// <summary>
    /// Collect every paintable box that lives inside a <c>position: fixed</c> subtree
    /// (the fixed element itself and all its descendants). Their Y/X are already page-local
    /// (see BlockLayout's <c>LayoutAbsoluteChildren</c>, which anchors a fixed box's containing
    /// block to page origin), so they must be excluded from normal document-flow pagination
    /// and repainted per page instead.
    /// </summary>
    public static void CollectFixedPositionedBoxes(LayoutBox box, bool insideFixed, HashSet<LayoutBox> result) =>
        CollectFixedPositionedBoxes(box, insideFixed, false, result);

    private static void CollectFixedPositionedBoxes(LayoutBox box, bool insideFixed, bool pageArea, HashSet<LayoutBox> result)
    {
        bool startsFixed = !insideFixed && box.Style?.Get("position") == "fixed";
        if (startsFixed) pageArea = !BlockLayout.IsSheetScopedFixed(box.Style!);
        bool nowInsideFixed = insideFixed || startsFixed;
        if (nowInsideFixed)
        {
            box.FixedToPageArea = pageArea;
            result.Add(box);
        }

        foreach (var child in box.Children)
            CollectFixedPositionedBoxes(child, nowInsideFixed, pageArea, result);
    }

    /// <summary>
    /// Collect boxes carrying the <c>-eggpdf-pin-bottom: page</c> extension property —
    /// an opt-in EggPdf-specific hook for pinning trailing content (e.g. a signature/acceptance
    /// block) to the bottom of whichever physical page it lands on, once auto-pagination of any
    /// preceding dynamic content has already been resolved.
    /// </summary>
    public static void CollectPinBottomBoxes(LayoutBox box, List<LayoutBox> result)
    {
        if (box.Style?.Get("-eggpdf-pin-bottom") == "page")
            result.Add(box);

        foreach (var child in box.Children)
            CollectPinBottomBoxes(child, result);
    }

    /// <summary>Text block with orphans/widows constraints for pagination.</summary>
    public struct OrphanWidowBlock
    {
        public float Y;
        public float Height;
        public int OrphansValue;
        public int WidowsValue;
        public float[] LineYs;
    }

    /// <summary>Walk the layout tree and collect blocks with explicit orphans/widows CSS values.</summary>
    public static void CollectOrphansWidowsBlocks(LayoutBox box, List<OrphanWidowBlock> result)
    {
        if (box.Style != null)
        {
            var orphansStr = box.Style.Get("orphans");
            var widowsStr  = box.Style.Get("widows");

            if (!string.IsNullOrEmpty(orphansStr) || !string.IsNullOrEmpty(widowsStr))
            {
                int orphans = ParsePaginationInt(orphansStr, 2);
                int widows  = ParsePaginationInt(widowsStr,  2);

                var lineYList = new List<float>();
                CollectTextLineYs(box, lineYList);

                if (lineYList.Count > 1)
                {
                    lineYList.Sort();
                    var uniqueYList = new List<float>();
                    for (int i = 0; i < lineYList.Count; i++)
                    {
                        if (uniqueYList.Count == 0 || lineYList[i] - uniqueYList[uniqueYList.Count - 1] > 0.5f)
                            uniqueYList.Add(lineYList[i]);
                    }

                    if (uniqueYList.Count > 1)
                    {
                        var block = new OrphanWidowBlock();
                        block.Y = box.Y;
                        block.Height = box.Height;
                        block.OrphansValue = orphans;
                        block.WidowsValue = widows;
                        block.LineYs = uniqueYList.ToArray();
                        result.Add(block);
                    }
                }
            }
        }

        for (int i = 0; i < box.Children.Count; i++)
            CollectOrphansWidowsBlocks(box.Children[i], result);
    }

    private static void CollectTextLineYs(LayoutBox box, List<float> lineYs)
    {
        if (!string.IsNullOrEmpty(box.Text))
            lineYs.Add(box.Y);
        for (int i = 0; i < box.Children.Count; i++)
            CollectTextLineYs(box.Children[i], lineYs);
    }

    private static int ParsePaginationInt(string? value, int defaultValue)
    {
        if (string.IsNullOrEmpty(value)) return defaultValue;
        if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int result))
            return result;
        return defaultValue;
    }

    public static void CollectHeadings(LayoutBox box, List<(string title, int level, float yPx)> headings)
    {
        if (box.Element != null && box.Element.TagName.Length == 2 &&
            box.Element.TagName[0] == 'h' &&
            box.Element.TagName[1] >= '1' && box.Element.TagName[1] <= '6')
        {
            var text = box.Text ?? GetChildText(box);
            if (!string.IsNullOrEmpty(text))
                headings.Add((text, box.Element.TagName[1] - '0', box.Y));
        }

        foreach (var child in box.Children)
            CollectHeadings(child, headings);
    }

    private static string? GetChildText(LayoutBox box)
    {
        foreach (var child in box.Children)
        {
            if (!string.IsNullOrEmpty(child.Text)) return child.Text;
            var childText = GetChildText(child);
            if (childText != null) return childText;
        }
        return null;
    }

}
