using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private const int MaxColspan = 1000;
    private const int MaxRowspan = 65534;
    private const float MinimumColumnWidthPx = 1f;
    private const float FitTolerancePx = 0.05f;

    private sealed class TableCellSlot
    {
        public int Column;
        public int RowSpan;
        public int ColSpan;
    }

    private sealed class TableGrid
    {
        public int ColumnCount;
        public readonly Dictionary<HtmlElement, TableCellSlot> Cells = new();
    }

    private sealed class TableColumnMeasure
    {
        public TableColumnMeasure(int columns)
        {
            MinContent = new float[columns];
            MaxContent = new float[columns];
            Explicit = new bool[columns];
        }

        public float[] MinContent { get; }
        public float[] MaxContent { get; }
        public bool[] Explicit { get; }
    }

    [ThreadStatic]
    private static Dictionary<HtmlElement, TableGrid>? _tableGridCache;

    [ThreadStatic]
    private static Dictionary<HtmlElement, TableColumnMeasure>? _tableColumnMeasureCache;

    private static HtmlElement? TableOf(HtmlElement row)
    {
        var parent = row.Parent as HtmlElement;
        if (parent?.TagName == "table") return parent;
        if (parent != null && (parent.TagName == "thead" || parent.TagName == "tbody" || parent.TagName == "tfoot"))
            return parent.Parent as HtmlElement is { TagName: "table" } table ? table : null;
        return null;
    }

    private static TableCellSlot? CellSlot(HtmlElement row, HtmlElement cell, out int columnCount)
    {
        columnCount = 0;
        var table = TableOf(row);
        if (table == null) return null;
        var grid = GridFor(table);
        columnCount = grid.ColumnCount;
        return grid.Cells.TryGetValue(cell, out var slot) ? slot : null;
    }

    private static int RowSpanOf(HtmlElement? cell)
    {
        if (cell?.Parent is not HtmlElement row) return 1;
        var table = TableOf(row);
        if (table == null) return 1;
        return GridFor(table).Cells.TryGetValue(cell, out var slot) ? slot.RowSpan : 1;
    }

    private static TableGrid GridFor(HtmlElement table)
    {
        _tableGridCache ??= new Dictionary<HtmlElement, TableGrid>();
        if (_tableGridCache.TryGetValue(table, out var cached)) return cached;

        var grid = new TableGrid();
        var occupiedUntilRow = new List<int>();
        var directRows = new List<HtmlElement>();
        int rowIndex = 0;
        foreach (var node in TableRowGroupsInRenderOrder(table))
        {
            if (node is not HtmlElement group) continue;
            if (group.TagName == "tr")
            {
                directRows.Add(group);
                continue;
            }
            if (group.TagName != "thead" && group.TagName != "tbody" && group.TagName != "tfoot") continue;
            rowIndex = PlaceRowGroup(directRows, grid, occupiedUntilRow, rowIndex);
            directRows.Clear();
            var groupRows = new List<HtmlElement>();
            foreach (var child in group.ChildNodes)
                if (child is HtmlElement { TagName: "tr" } tr) groupRows.Add(tr);
            rowIndex = PlaceRowGroup(groupRows, grid, occupiedUntilRow, rowIndex);
        }
        PlaceRowGroup(directRows, grid, occupiedUntilRow, rowIndex);
        grid.ColumnCount = Math.Max(grid.ColumnCount, occupiedUntilRow.Count);
        _tableGridCache[table] = grid;
        return grid;
    }

    private static int PlaceRowGroup(List<HtmlElement> rows, TableGrid grid, List<int> occupiedUntilRow, int firstRow)
    {
        int lastRow = firstRow + rows.Count - 1;
        for (int r = 0; r < rows.Count; r++)
        {
            int row = firstRow + r;
            int column = 0;
            foreach (var node in rows[r].ChildNodes)
            {
                if (node is not HtmlElement cell || (cell.TagName != "td" && cell.TagName != "th")) continue;
                while (column < occupiedUntilRow.Count && occupiedUntilRow[column] > row) column++;
                int colspan = Math.Min(GetColspan(cell), MaxColspan);
                int requested = GetRowspan(cell);
                int rowspan = requested == 0 ? lastRow - row + 1 : Math.Min(Math.Min(requested, MaxRowspan), lastRow - row + 1);
                for (int k = column; k < column + colspan; k++)
                {
                    while (occupiedUntilRow.Count <= k) occupiedUntilRow.Add(0);
                    occupiedUntilRow[k] = row + rowspan;
                }
                grid.Cells[cell] = new TableCellSlot { Column = column, RowSpan = Math.Max(1, rowspan), ColSpan = colspan };
                column += colspan;
            }
        }
        for (int k = 0; k < occupiedUntilRow.Count; k++)
            if (occupiedUntilRow[k] > lastRow + 1) occupiedUntilRow[k] = lastRow + 1;
        return firstRow + rows.Count;
    }

    private static int GetRowspan(HtmlElement cell)
    {
        var attr = cell.GetAttribute("rowspan");
        if (!string.IsNullOrEmpty(attr) && int.TryParse(attr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rowspan) && rowspan >= 0)
            return rowspan;
        return 1;
    }

    private static float ApplyRowSpans(LayoutBox table)
    {
        float added = 0f;
        var tableChildren = new List<LayoutBox>(table.Children);
        var directRows = new List<LayoutBox>();
        foreach (var child in tableChildren)
        {
            var tag = child.Element?.TagName;
            if (tag == "tr") directRows.Add(child);
            else if (tag == "thead" || tag == "tbody" || tag == "tfoot") added += ApplyRowSpans(table, child, RowsOf(child));
        }
        if (directRows.Count > 0) added += ApplyRowSpans(table, table, directRows);
        return added;
    }

    private static List<LayoutBox> RowsOf(LayoutBox group)
    {
        var rows = new List<LayoutBox>();
        foreach (var child in group.Children)
            if (child.Element?.TagName == "tr") rows.Add(child);
        return rows;
    }

    private static float ApplyRowSpans(LayoutBox table, LayoutBox container, List<LayoutBox> rows)
    {
        float added = 0f;
        for (int i = 0; i < rows.Count; i++)
        {
            foreach (var cell in rows[i].Children)
            {
                int span = RowSpanOf(cell.Element);
                if (span <= 1) continue;
                int last = Math.Min(i + span - 1, rows.Count - 1);
                float spanned = rows[last].Y + rows[last].Height - rows[i].Y;
                float extra = cell.Height - spanned;
                if (extra <= 0f) continue;
                GrowRow(rows[last], extra);
                ShiftSiblingsAfter(container, rows[last], extra);
                if (container != table)
                {
                    container.Height += extra;
                    container.ContentHeight += extra;
                    ShiftSiblingsAfter(table, container, extra);
                }
                added += extra;
            }
        }

        for (int i = 0; i < rows.Count; i++)
        {
            foreach (var cell in rows[i].Children)
            {
                int span = RowSpanOf(cell.Element);
                if (span <= 1) continue;
                int last = Math.Min(i + span - 1, rows.Count - 1);
                StretchCell(cell, rows[last].Y + rows[last].Height - rows[i].Y);
            }
        }
        return added;
    }

    private static void GrowRow(LayoutBox row, float extra)
    {
        row.Height += extra;
        row.ContentHeight += extra;
        foreach (var cell in row.Children)
            if (cell.Element != null && RowSpanOf(cell.Element) <= 1) cell.Height += extra;
    }

    private static void ShiftSiblingsAfter(LayoutBox parent, LayoutBox after, float dy)
    {
        bool found = false;
        foreach (var child in parent.Children)
        {
            if (found) child.Y += dy;
            else if (child == after) found = true;
        }
    }

    private static void StretchCell(LayoutBox cell, float height)
    {
        float contentHeight = cell.ContentHeight;
        float offset = 0f;
        var verticalAlign = cell.Style.Get("vertical-align") ?? "top";
        if (verticalAlign == "middle")
            offset = (height - cell.PaddingTop - cell.PaddingBottom - contentHeight) / 2f;
        else if (verticalAlign == "bottom")
            offset = height - cell.PaddingTop - cell.PaddingBottom - contentHeight;
        cell.Height = Math.Max(cell.Height, height);
        if (offset > 0f)
            foreach (var child in cell.Children) child.Y += offset;
    }

    private static TableColumnMeasure MeasureTableColumns(HtmlElement table, int columnCount, float availableWidth, float fontSize,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        _tableColumnMeasureCache ??= new Dictionary<HtmlElement, TableColumnMeasure>();
        if (_tableColumnMeasureCache.TryGetValue(table, out var cached) && cached.MaxContent.Length == columnCount) return cached;

        var measure = new TableColumnMeasure(columnCount);
        var grid = GridFor(table);
        var tableStyle = resolver(table, parentStyle);
        bool collapse = tableStyle.Get("border-collapse") == "collapse";
        ApplyColumnElementWidths(table, measure, availableWidth, fontSize, resolver, parentStyle);

        var spanning = new List<(TableCellSlot Slot, float Min, float Max)>();
        foreach (var entry in grid.Cells)
        {
            var cell = entry.Key;
            var slot = entry.Value;
            if (slot.Column >= columnCount) continue;
            var cellStyle = resolver(cell, tableStyle);
            float cellFontSize = ResolveFontSize(cellStyle.FontSize, fontSize);
            float insets = Math.Max(0f, ResolveLength(cellStyle.PaddingLeft, 0, cellFontSize))
                           + Math.Max(0f, ResolveLength(cellStyle.PaddingRight, 0, cellFontSize));
            if (!collapse) insets += VisibleBorderWidth(cellStyle, "left", cellFontSize) + VisibleBorderWidth(cellStyle, "right", cellFontSize);

            float min = FlexLayout.MeasureMinContentWidth(cell, cellStyle, resolver, cellFontSize, 0) + insets + FitTolerancePx;
            float max = Math.Max(min, FlexLayout.MeasureMaxContentWidth(cell, cellStyle, resolver, cellFontSize) + insets + FitTolerancePx);
            var specified = cellStyle.Width;
            if (!string.IsNullOrEmpty(specified) && specified != "auto" && slot.ColSpan == 1 &&
                ResolveOptionalLength(specified, availableWidth, cellFontSize) is float explicitWidth)
            {
                max = Math.Max(min, explicitWidth + (cellStyle.Get("box-sizing") == "border-box" ? 0f : insets));
                measure.Explicit[slot.Column] = true;
            }

            if (slot.ColSpan == 1)
            {
                measure.MinContent[slot.Column] = Math.Max(measure.MinContent[slot.Column], min);
                measure.MaxContent[slot.Column] = Math.Max(measure.MaxContent[slot.Column], max);
            }
            else
            {
                spanning.Add((slot, min, max));
            }
        }

        foreach (var (slot, min, max) in spanning)
        {
            int end = Math.Min(columnCount, slot.Column + slot.ColSpan);
            int span = end - slot.Column;
            if (span <= 0) continue;
            float currentMin = 0f, currentMax = 0f;
            for (int c = slot.Column; c < end; c++)
            {
                currentMin += measure.MinContent[c];
                currentMax += measure.MaxContent[c];
            }
            for (int c = slot.Column; c < end; c++)
            {
                if (min > currentMin) measure.MinContent[c] += (min - currentMin) / span;
                if (max > currentMax) measure.MaxContent[c] += (max - currentMax) / span;
            }
        }

        for (int c = 0; c < columnCount; c++)
        {
            measure.MinContent[c] = Math.Max(measure.MinContent[c], MinimumColumnWidthPx);
            measure.MaxContent[c] = Math.Max(measure.MaxContent[c], measure.MinContent[c]);
        }
        _tableColumnMeasureCache[table] = measure;
        return measure;
    }

    private static void ApplyColumnElementWidths(HtmlElement table, TableColumnMeasure measure, float availableWidth, float fontSize,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        int column = 0;
        foreach (var node in table.ChildNodes)
        {
            if (node is not HtmlElement { TagName: "colgroup" } group) continue;
            bool hasCols = false;
            foreach (var colNode in group.ChildNodes)
            {
                if (colNode is not HtmlElement { TagName: "col" } col) continue;
                hasCols = true;
                int span = SpanAttribute(col);
                var width = resolver(col, parentStyle).Width ?? col.GetAttribute("width");
                SetExplicitColumns(measure, column, span, width, availableWidth, fontSize);
                column += span;
            }
            if (!hasCols)
            {
                int span = SpanAttribute(group);
                var width = resolver(group, parentStyle).Width ?? group.GetAttribute("width");
                SetExplicitColumns(measure, column, span, width, availableWidth, fontSize);
                column += span;
            }
        }
    }

    private static void SetExplicitColumns(TableColumnMeasure measure, int start, int span, string? width, float availableWidth, float fontSize)
    {
        if (string.IsNullOrEmpty(width) || width == "auto") return;
        if (ResolveOptionalLength(width, availableWidth, fontSize) is not float w) return;
        for (int c = start; c < start + span && c < measure.MaxContent.Length; c++)
        {
            measure.Explicit[c] = true;
            measure.MaxContent[c] = Math.Max(measure.MaxContent[c], w);
        }
    }

    private static int SpanAttribute(HtmlElement element)
    {
        var attr = element.GetAttribute("span");
        return !string.IsNullOrEmpty(attr) && int.TryParse(attr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int span) && span > 0
            ? Math.Min(span, MaxColspan)
            : 1;
    }

    private static float[] DistributeColumnWidths(TableColumnMeasure measure, float availableWidth, bool tableHasWidth)
    {
        int columns = measure.MaxContent.Length;
        var widths = new float[columns];
        float sumMin = 0f, sumMax = 0f;
        for (int c = 0; c < columns; c++)
        {
            sumMin += measure.MinContent[c];
            sumMax += measure.MaxContent[c];
        }

        if (sumMax <= availableWidth)
        {
            float extra = tableHasWidth ? availableWidth - sumMax : 0f;
            float flexibleMax = 0f;
            for (int c = 0; c < columns; c++)
                if (!measure.Explicit[c]) flexibleMax += measure.MaxContent[c];
            for (int c = 0; c < columns; c++)
            {
                widths[c] = measure.MaxContent[c];
                if (extra <= 0f) continue;
                if (flexibleMax > 0f)
                    widths[c] += measure.Explicit[c] ? 0f : extra * measure.MaxContent[c] / flexibleMax;
                else
                    widths[c] += extra * measure.MaxContent[c] / sumMax;
            }
            return widths;
        }

        if (sumMin >= availableWidth)
        {
            Array.Copy(measure.MinContent, widths, columns);
            return widths;
        }

        float share = (availableWidth - sumMin) / (sumMax - sumMin);
        for (int c = 0; c < columns; c++)
            widths[c] = measure.MinContent[c] + (measure.MaxContent[c] - measure.MinContent[c]) * share;
        return widths;
    }

    private static float CaptionMinContentWidth(HtmlElement table, ComputedStyle tableStyle, float fontSize,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver)
    {
        float widest = 0f;
        foreach (var node in table.ChildNodes)
        {
            if (node is not HtmlElement { TagName: "caption" } caption) continue;
            var captionStyle = resolver(caption, tableStyle);
            float captionFontSize = ResolveFontSize(captionStyle.FontSize, fontSize);
            float insets = Math.Max(0f, ResolveLength(captionStyle.PaddingLeft, 0, captionFontSize))
                           + Math.Max(0f, ResolveLength(captionStyle.PaddingRight, 0, captionFontSize));
            widest = Math.Max(widest, FlexLayout.MeasureMinContentWidth(caption, captionStyle, resolver, captionFontSize, 0) + insets + FitTolerancePx);
        }
        return widest;
    }

    private static void ShrinkTableToContent(LayoutBox box, HtmlElement table, ComputedStyle style, float fontSize,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        var width = style.Width;
        if (!string.IsNullOrEmpty(width) && width != "auto") return;
        if (style.Get("table-layout") == "fixed") return;
        var grid = GridFor(table);
        if (grid.ColumnCount == 0) return;

        float spacing = 0f;
        if (style.Get("border-collapse") != "collapse")
        {
            var spacingValue = style.Get("border-spacing");
            spacing = !string.IsNullOrEmpty(spacingValue) ? ResolveLength(spacingValue, 0, fontSize) : 2f;
        }
        float spacingTotal = grid.ColumnCount > 1 ? spacing * (grid.ColumnCount - 1) : 0f;
        var measure = MeasureTableColumns(table, grid.ColumnCount, box.ContentWidth - spacingTotal, fontSize, resolver, parentStyle);
        float preferred = spacingTotal;
        for (int c = 0; c < grid.ColumnCount; c++) preferred += measure.MaxContent[c];
        preferred = Math.Max(preferred, CaptionMinContentWidth(table, style, fontSize, resolver));
        float preferredBox = preferred + box.PaddingLeft + box.PaddingRight;
        if (preferredBox >= box.Width) return;

        float freed = box.Width - preferredBox;
        box.Width = preferredBox;
        box.ContentWidth = preferred;
        bool leftAuto = style.MarginLeft == "auto";
        bool rightAuto = style.MarginRight == "auto";
        float shift = leftAuto && rightAuto ? freed / 2f : leftAuto ? freed : 0f;
        if (shift > 0f)
        {
            box.MarginLeft += shift;
            box.X += shift;
        }
    }
}
