using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private static bool IsTableRow(string display)
        => display == "table-row";

    private static bool IsTableCell(string display)
        => display == "table-cell";

    /// <summary>Check if a table row (tr) is the first row in its table.</summary>
    private static bool IsFirstRowInTable(HtmlElement row)
    {
        var parent = row.Parent as HtmlElement;
        if (parent == null) return true;

        // If parent is thead/tbody/tfoot, check if this is the first row in it AND the group is first
        if (parent.TagName == "thead" || parent.TagName == "tbody" || parent.TagName == "tfoot")
        {
            // Not the first row in this group
            for (int i = 0; i < parent.ChildNodes.Count; i++)
            {
                if (parent.ChildNodes[i] is HtmlElement elem && elem.TagName == "tr")
                    return elem == row;
            }

            // Check if this group is the first group in the table
            var table = parent.Parent as HtmlElement;
            if (table == null) return true;
            for (int i = 0; i < table.ChildNodes.Count; i++)
            {
                if (table.ChildNodes[i] is HtmlElement elem && (elem.TagName == "thead" || elem.TagName == "tbody" || elem.TagName == "tfoot" || elem.TagName == "tr"))
                {
                    if (elem == parent) return true;
                    if (elem.TagName == "tr") return false;
                    // Non-empty group before this one
                    for (int j = 0; j < elem.ChildNodes.Count; j++)
                        if (elem.ChildNodes[j] is HtmlElement) return false;
                }
            }
            return true;
        }

        // Direct child of table
        if (parent.TagName == "table")
        {
            for (int i = 0; i < parent.ChildNodes.Count; i++)
            {
                if (parent.ChildNodes[i] is HtmlElement elem)
                {
                    if (elem == row) return true;
                    if (elem.TagName == "tr") return false;
                    if (elem.TagName == "thead" || elem.TagName == "tbody" || elem.TagName == "tfoot")
                    {
                        for (int j = 0; j < elem.ChildNodes.Count; j++)
                            if (elem.ChildNodes[j] is HtmlElement) return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Compute auto column widths by measuring content across all rows in the table.
    /// Walks up from the row to find the table, scans all rows, measures text width
    /// per column, then distributes available width proportionally.
    /// Results are cached per table element to avoid re-computation for each row.
    /// </summary>
    [ThreadStatic]
    private static System.Collections.Generic.Dictionary<HtmlElement, float[]>? _tableColumnWidthCache;

    private static float[] ComputeAutoColumnWidths(HtmlElement row, int totalColumns, float availableWidth,
        float fontSize, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        if (totalColumns <= 0) return new float[] { availableWidth };

        // Find the table element (walk up through thead/tbody/tfoot)
        HtmlElement? tableElement = row.Parent as HtmlElement;
        while (tableElement != null && tableElement.TagName != "table")
            tableElement = tableElement.Parent as HtmlElement;

        if (tableElement == null)
        {
            // Fallback: equal distribution
            float eq = availableWidth / totalColumns;
            var eqWidths = new float[totalColumns];
            for (int i = 0; i < totalColumns; i++) eqWidths[i] = eq;
            return eqWidths;
        }

        // Check cache
        if (_tableColumnWidthCache == null)
            _tableColumnWidthCache = new System.Collections.Generic.Dictionary<HtmlElement, float[]>();

        if (_tableColumnWidthCache.TryGetValue(tableElement, out var cached))
            return cached;

        // table-layout: fixed — widths determined by first row only (no content scanning)
        var tableStyle = resolver(tableElement, parentStyle);
        if (tableStyle.Get("table-layout") == "fixed")
        {
            var fixedWidths = ComputeFixedColumnWidths(tableElement, totalColumns, availableWidth, fontSize, resolver, parentStyle);
            _tableColumnWidthCache[tableElement] = fixedWidths;
            return fixedWidths;
        }

        // Scan all rows in the table to find max content width per column
        var maxContentWidths = new float[totalColumns];
        var hasExplicitWidth = new bool[totalColumns];
        var explicitWidths = new float[totalColumns];

        ScanTableForColumnWidths(tableElement, totalColumns, maxContentWidths, hasExplicitWidth, explicitWidths,
            fontSize, availableWidth, resolver, parentStyle);

        // Distribute available width proportionally based on content
        float totalContentWidth = 0;
        float totalExplicitWidth = 0;
        int flexColumns = 0;

        for (int i = 0; i < totalColumns; i++)
        {
            if (hasExplicitWidth[i])
            {
                totalExplicitWidth += explicitWidths[i];
            }
            else
            {
                totalContentWidth += Math.Max(maxContentWidths[i], 20); // minimum 20px per column
                flexColumns++;
            }
        }

        float remainingWidth = availableWidth - totalExplicitWidth;
        if (remainingWidth < 0) remainingWidth = availableWidth;

        var result = new float[totalColumns];
        for (int i = 0; i < totalColumns; i++)
        {
            if (hasExplicitWidth[i])
            {
                result[i] = explicitWidths[i];
            }
            else if (totalContentWidth > 0)
            {
                float contentW = Math.Max(maxContentWidths[i], 20);
                result[i] = remainingWidth * contentW / totalContentWidth;
            }
            else
            {
                result[i] = remainingWidth / Math.Max(flexColumns, 1);
            }
        }

        _tableColumnWidthCache[tableElement] = result;
        return result;
    }

    /// <summary>
    /// table-layout:fixed — determine column widths from the first row only.
    /// Explicit cell widths (from width attribute/style on first-row cells or &lt;col&gt;) are used;
    /// remaining columns share the leftover width equally.
    /// </summary>
    private static float[] ComputeFixedColumnWidths(HtmlElement tableElement, int totalColumns,
        float availableWidth, float fontSize,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        var explicitWidths = new float[totalColumns];
        var hasExplicit = new bool[totalColumns];

        // 1. Check <colgroup>/<col> elements first
        foreach (var child in tableElement.ChildNodes)
        {
            var group = child as HtmlElement;
            if (group == null || group.TagName != "colgroup") continue;
            int colIdx = 0;
            foreach (var colNode in group.ChildNodes)
            {
                var col = colNode as HtmlElement;
                if (col == null || col.TagName != "col") continue;
                var colStyle = resolver(col, parentStyle);
                var wStr = colStyle.Width ?? col.GetAttribute("width");
                if (!string.IsNullOrEmpty(wStr) && colIdx < totalColumns)
                {
                    float w = ResolveLength(wStr, availableWidth, fontSize);
                    if (w > 0) { explicitWidths[colIdx] = w; hasExplicit[colIdx] = true; }
                }
                colIdx++;
            }
        }

        // 2. Scan only the first row for explicit cell widths
        HtmlElement? firstRow = null;
        foreach (var child in tableElement.ChildNodes)
        {
            var group = child as HtmlElement;
            if (group == null) continue;
            if (group.TagName == "tr") { firstRow = group; break; }
            // thead/tbody/tfoot
            if (group.TagName == "thead" || group.TagName == "tbody" || group.TagName == "tfoot")
            {
                foreach (var rowNode in group.ChildNodes)
                {
                    var row = rowNode as HtmlElement;
                    if (row != null && row.TagName == "tr") { firstRow = row; break; }
                }
                if (firstRow != null) break;
            }
        }

        if (firstRow != null)
        {
            int colIdx = 0;
            foreach (var cellNode in firstRow.ChildNodes)
            {
                var cell = cellNode as HtmlElement;
                if (cell == null || (cell.TagName != "td" && cell.TagName != "th")) continue;
                if (colIdx >= totalColumns) break;
                if (!hasExplicit[colIdx])
                {
                    var cellStyle = resolver(cell, parentStyle);
                    var wStr = cellStyle.Width ?? cell.GetAttribute("width");
                    if (!string.IsNullOrEmpty(wStr) && wStr != "auto")
                    {
                        float w = ResolveLength(wStr, availableWidth, fontSize);
                        if (w > 0) { explicitWidths[colIdx] = w; hasExplicit[colIdx] = true; }
                    }
                }
                colIdx++;
            }
        }

        // 3. Distribute remaining width equally among columns with no explicit width
        float usedWidth = 0;
        int flexCols = 0;
        for (int i = 0; i < totalColumns; i++)
        {
            if (hasExplicit[i]) usedWidth += explicitWidths[i];
            else flexCols++;
        }

        float remaining = availableWidth - usedWidth;
        if (remaining < 0) remaining = 0;
        float flexWidth = flexCols > 0 ? remaining / flexCols : 0;

        var result = new float[totalColumns];
        for (int i = 0; i < totalColumns; i++)
            result[i] = hasExplicit[i] ? explicitWidths[i] : flexWidth;

        return result;
    }

    private static void ScanTableForColumnWidths(HtmlElement tableElement, int totalColumns,
        float[] maxContentWidths, bool[] hasExplicitWidth, float[] explicitWidths,
        float fontSize, float availableWidth,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        // Walk: table -> thead/tbody/tfoot -> tr -> td/th
        // Also process <colgroup>/<col> elements which define explicit column widths.
        foreach (var child in tableElement.ChildNodes)
        {
            var group = child as HtmlElement;
            if (group == null) continue;

            // <colgroup> may contain <col> children or carry a span attribute itself
            if (group.TagName == "colgroup")
            {
                int colIdx = 0;
                bool hasColChildren = false;
                foreach (var colNode in group.ChildNodes)
                {
                    var col = colNode as HtmlElement;
                    if (col == null || col.TagName != "col") continue;
                    hasColChildren = true;

                    int span = 1;
                    var spanAttr = col.GetAttribute("span");
                    if (!string.IsNullOrEmpty(spanAttr) && int.TryParse(spanAttr,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int spanVal) && spanVal > 0)
                        span = spanVal;

                    // Resolve width from style or attribute
                    var colStyle = resolver(col, parentStyle);
                    var widthStr = colStyle?.Width ?? col.GetAttribute("width");
                    if (!string.IsNullOrEmpty(widthStr) && widthStr != "auto")
                    {
                        float? w = ResolveOptionalLength(widthStr, availableWidth, fontSize);
                        if (w.HasValue)
                        {
                            for (int s = 0; s < span && colIdx + s < totalColumns; s++)
                            {
                                hasExplicitWidth[colIdx + s] = true;
                                explicitWidths[colIdx + s] = w.Value;
                            }
                        }
                    }
                    colIdx += span;
                    if (colIdx >= totalColumns) break;
                }

                // Colgroup without <col> children: apply its own width to spanned columns
                if (!hasColChildren)
                {
                    int span = 1;
                    var spanAttr = group.GetAttribute("span");
                    if (!string.IsNullOrEmpty(spanAttr) && int.TryParse(spanAttr,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int spanVal) && spanVal > 0)
                        span = spanVal;

                    var cgStyle = resolver(group, parentStyle);
                    var widthStr = cgStyle?.Width ?? group.GetAttribute("width");
                    if (!string.IsNullOrEmpty(widthStr) && widthStr != "auto")
                    {
                        float? w = ResolveOptionalLength(widthStr, availableWidth, fontSize);
                        if (w.HasValue)
                        {
                            for (int s = 0; s < span && s < totalColumns; s++)
                            {
                                if (!hasExplicitWidth[s])
                                {
                                    hasExplicitWidth[s] = true;
                                    explicitWidths[s] = w.Value;
                                }
                            }
                        }
                    }
                }
            }
            // Direct <tr> children of <table>
            else if (group.TagName == "tr")
            {
                ScanRowForColumnWidths(group, totalColumns, maxContentWidths, hasExplicitWidth, explicitWidths,
                    fontSize, availableWidth, resolver, parentStyle);
            }
            // <thead>, <tbody>, <tfoot> contain <tr>
            else if (group.TagName == "thead" || group.TagName == "tbody" || group.TagName == "tfoot")
            {
                foreach (var trNode in group.ChildNodes)
                {
                    var tr = trNode as HtmlElement;
                    if (tr != null && tr.TagName == "tr")
                    {
                        ScanRowForColumnWidths(tr, totalColumns, maxContentWidths, hasExplicitWidth, explicitWidths,
                            fontSize, availableWidth, resolver, parentStyle);
                    }
                }
            }
        }
    }

    private static void ScanRowForColumnWidths(HtmlElement row, int totalColumns,
        float[] maxContentWidths, bool[] hasExplicitWidth, float[] explicitWidths,
        float fontSize, float availableWidth,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        int colIdx = 0;
        foreach (var cellNode in row.ChildNodes)
        {
            var cell = cellNode as HtmlElement;
            if (cell == null || (cell.TagName != "td" && cell.TagName != "th"))
                continue;

            if (colIdx >= totalColumns) break;
            int colspan = GetColspan(cell);

            // Check for explicit width on cell
            var cellStyle = resolver(cell, parentStyle);
            var widthStr = cellStyle?.Width;
            if (!string.IsNullOrEmpty(widthStr) && widthStr != "auto")
            {
                float? w = ResolveOptionalLength(widthStr, availableWidth, fontSize);
                if (w.HasValue && colspan == 1)
                {
                    hasExplicitWidth[colIdx] = true;
                    explicitWidths[colIdx] = Math.Max(explicitWidths[colIdx], w.Value);
                }
            }

            // Measure text content width
            if (colspan == 1)
            {
                float textWidth = MeasureElementTextWidth(cell, fontSize);
                // Add padding estimate (10px each side default for cells with padding:10px)
                float padding = 0;
                if (cellStyle != null)
                {
                    padding += ResolveLength(cellStyle.PaddingLeft ?? "0", 0, fontSize);
                    padding += ResolveLength(cellStyle.PaddingRight ?? "0", 0, fontSize);
                }
                textWidth += padding;

                if (textWidth > maxContentWidths[colIdx])
                    maxContentWidths[colIdx] = textWidth;
            }

            colIdx += colspan;
        }
    }

    /// <summary>Measure the total text width of an element and its children.</summary>
    private static float MeasureElementTextWidth(HtmlElement element, float fontSize)
    {
        float totalWidth = 0;
        foreach (var node in element.ChildNodes)
        {
            if (node is Html.Dom.HtmlTextNode textNode)
            {
                var text = TrimHtmlText(textNode.Data ?? "");
                if (!string.IsNullOrEmpty(text))
                    totalWidth += TextMeasurer.MeasureWidth(text, fontSize, "Helvetica");
            }
            else if (node is HtmlElement child)
            {
                totalWidth += MeasureElementTextWidth(child, fontSize);
            }
        }
        return totalWidth;
    }

    /// <summary>Count total column slots in a row (respecting colspan).</summary>
    /// <summary>Get colspan attribute value (default 1).</summary>
    private static int GetColspan(HtmlElement cell)
    {
        var attr = cell.GetAttribute("colspan");
        if (!string.IsNullOrEmpty(attr) && int.TryParse(attr, out int colspan) && colspan > 0)
            return colspan;
        return 1;
    }

}
