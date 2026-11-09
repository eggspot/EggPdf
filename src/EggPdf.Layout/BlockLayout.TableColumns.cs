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

    private static bool IsFirstRowInTable(HtmlElement row)
    {
        var parent = row.Parent as HtmlElement;
        if (parent == null) return true;

        if (parent.TagName == "thead" || parent.TagName == "tbody" || parent.TagName == "tfoot")
        {
            for (int i = 0; i < parent.ChildNodes.Count; i++)
            {
                if (parent.ChildNodes[i] is HtmlElement elem && elem.TagName == "tr")
                {
                    if (elem != row) return false;
                    break;
                }
            }

            var table = parent.Parent as HtmlElement;
            if (table == null) return true;
            var groups = TableRowGroupsInRenderOrder(table);
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] is HtmlElement elem && (elem.TagName == "thead" || elem.TagName == "tbody" || elem.TagName == "tfoot" || elem.TagName == "tr"))
                {
                    if (elem == parent) return true;
                    if (elem.TagName == "tr") return false;
                    for (int j = 0; j < elem.ChildNodes.Count; j++)
                        if (elem.ChildNodes[j] is HtmlElement) return false;
                }
            }
            return true;
        }

        if (parent.TagName == "table")
        {
            var children = TableRowGroupsInRenderOrder(parent);
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is HtmlElement elem)
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

    [ThreadStatic]
    private static System.Collections.Generic.Dictionary<HtmlElement, float[]>? _tableColumnWidthCache;

    private static float[] ComputeAutoColumnWidths(HtmlElement row, int totalColumns, float availableWidth,
        float fontSize, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        if (totalColumns <= 0) return new float[] { availableWidth };

        HtmlElement? tableElement = row.Parent as HtmlElement;
        while (tableElement != null && tableElement.TagName != "table")
            tableElement = tableElement.Parent as HtmlElement;

        if (tableElement == null)
        {
            float eq = availableWidth / totalColumns;
            var eqWidths = new float[totalColumns];
            for (int i = 0; i < totalColumns; i++) eqWidths[i] = eq;
            return eqWidths;
        }

        if (_tableColumnWidthCache == null)
            _tableColumnWidthCache = new System.Collections.Generic.Dictionary<HtmlElement, float[]>();

        if (_tableColumnWidthCache.TryGetValue(tableElement, out var cached))
            return cached;

        var tableStyle = resolver(tableElement, parentStyle);
        if (tableStyle.Get("table-layout") == "fixed")
        {
            var fixedWidths = ComputeFixedColumnWidths(tableElement, totalColumns, availableWidth, fontSize, resolver, parentStyle);
            _tableColumnWidthCache[tableElement] = fixedWidths;
            return fixedWidths;
        }

        var measure = MeasureTableColumns(tableElement, totalColumns, availableWidth, fontSize, resolver, parentStyle, includeMinContent: false);
        if (measure.TotalMaxContent() > availableWidth)
            measure = MeasureTableColumns(tableElement, totalColumns, availableWidth, fontSize, resolver, parentStyle, includeMinContent: true);
        var tableWidth = tableStyle.Width;
        bool tableHasWidth = !string.IsNullOrEmpty(tableWidth) && tableWidth != "auto";
        var result = DistributeColumnWidths(measure, availableWidth, tableHasWidth);
        _tableColumnWidthCache[tableElement] = result;
        return result;
    }

    private static float[] ComputeFixedColumnWidths(HtmlElement tableElement, int totalColumns,
        float availableWidth, float fontSize,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        var explicitWidths = new float[totalColumns];
        var hasExplicit = new bool[totalColumns];

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

        HtmlElement? firstRow = null;
        foreach (var child in TableRowGroupsInRenderOrder(tableElement))
        {
            var group = child as HtmlElement;
            if (group == null) continue;
            if (group.TagName == "tr") { firstRow = group; break; }
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
            var grid = GridFor(tableElement);
            foreach (var cellNode in firstRow.ChildNodes)
            {
                var cell = cellNode as HtmlElement;
                if (cell == null || (cell.TagName != "td" && cell.TagName != "th")) continue;
                if (!grid.Cells.TryGetValue(cell, out var slot) || slot.ColSpan != 1 || slot.Column >= totalColumns) continue;
                int colIdx = slot.Column;
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
            }
        }

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

    private static int GetColspan(HtmlElement cell)
    {
        var attr = cell.GetAttribute("colspan");
        if (!string.IsNullOrEmpty(attr) && int.TryParse(attr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int colspan) && colspan > 0)
            return colspan;
        return 1;
    }

}
