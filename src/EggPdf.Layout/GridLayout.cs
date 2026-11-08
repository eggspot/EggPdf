using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

/// <summary>
/// CSS Grid Layout Level 1 engine.
/// Handles grid-template-columns, grid-template-rows, grid-template-areas,
/// gap, grid-auto-flow, grid-column/row placement, and spanning.
/// </summary>
public static partial class GridLayout
{
    private const float DefaultFontSize = 16f;

    /// <summary>
    /// Lay out children of a grid container according to the CSS Grid algorithm.
    /// The container's box model (Width, ContentWidth, Padding, Margin) must already be computed.
    /// This method populates container.Children with properly positioned child LayoutBoxes.
    /// </summary>
    public static void LayoutGrid(LayoutBox container, HtmlElement element, ComputedStyle style,
        float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        float parentFontSize = parentStyle != null ? BlockLayout.ResolveFontSize(parentStyle.FontSize, DefaultFontSize) : DefaultFontSize;
        float fontSize = BlockLayout.ResolveFontSize(style.FontSize, parentFontSize);

        string? templateColumns = style.Get("grid-template-columns");
        string? templateRows = style.Get("grid-template-rows");
        string? templateAreas = style.Get("grid-template-areas");
        string autoFlow = style.Get("grid-auto-flow") ?? "row";
        bool flowColumn = autoFlow.IndexOf("column", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isDense = autoFlow.IndexOf("dense", StringComparison.OrdinalIgnoreCase) >= 0;

        float columnGap = ResolveGap(style, true, container.ContentWidth, fontSize);
        float rowGap = ResolveGap(style, false, container.ContentWidth, fontSize);

        var items = CollectGridItems(element, style, container, containingWidth, resolver, fontSize);
        if (items.Count == 0) return;

        var areaMap = ParseTemplateAreas(templateAreas);

        string? resolvedColumns = templateColumns;
        if (!string.IsNullOrEmpty(resolvedColumns) && HasAutoRepeat(resolvedColumns))
            resolvedColumns = ExpandAutoRepeat(resolvedColumns, container.ContentWidth, items.Count);
        string? resolvedRows = templateRows;
        if (!string.IsNullOrEmpty(resolvedRows) && HasAutoRepeat(resolvedRows))
            resolvedRows = ExpandAutoRepeat(resolvedRows, container.ContentWidth, items.Count);

        var columnTracks = ParseTrackList(resolvedColumns, container.ContentWidth, fontSize);
        var rowTracks = ParseTrackList(resolvedRows, container.ContentWidth, fontSize);

        var autoRowTracks = ParseTrackList(style.Get("grid-auto-rows"), 0, fontSize);
        var autoColumnTracks = ParseTrackList(style.Get("grid-auto-columns"), container.ContentWidth, fontSize);
        bool columnsAreImplicit = string.IsNullOrEmpty(resolvedColumns);

        if (columnTracks.Count == 0)
        {
            if (flowColumn)
            {
                int maxCol = 1;
                for (int i = 0; i < items.Count; i++)
                {
                    int end = items[i].ColumnStart + items[i].ColumnSpan;
                    if (end > maxCol) maxCol = end;
                }
                for (int c = 0; c < maxCol; c++)
                    columnTracks.Add(new TrackDefinition { Type = TrackType.Fr, Value = 1 });
            }
            else
            {
                columnTracks.Add(new TrackDefinition { Type = TrackType.Fr, Value = 1 });
            }
        }

        int numColumns = columnTracks.Count;

        ResolveExplicitPlacement(items, areaMap, numColumns);

        numColumns = AutoPlaceItems(items, numColumns, flowColumn, rowTracks.Count, isDense,
            allowColumnGrowth: flowColumn && columnsAreImplicit);

        int newColumnIndex = 0;
        while (columnTracks.Count < numColumns)
        {
            columnTracks.Add(autoColumnTracks.Count > 0
                ? autoColumnTracks[newColumnIndex % autoColumnTracks.Count]
                : new TrackDefinition { Type = TrackType.Fr, Value = 1 });
            newColumnIndex++;
        }

        int numRows = rowTracks.Count;
        for (int i = 0; i < items.Count; i++)
        {
            int neededRows = items[i].RowStart + items[i].RowSpan;
            if (neededRows > numRows) numRows = neededRows;
        }

        int newRowIndex = 0;
        while (rowTracks.Count < numRows)
        {
            rowTracks.Add(autoRowTracks.Count > 0
                ? autoRowTracks[newRowIndex % autoRowTracks.Count]
                : new TrackDefinition { Type = TrackType.Auto, Value = 0 });
            newRowIndex++;
        }

        var autoColumnWidths = AutoColumnContentWidths(columnTracks, items, resolver, fontSize);
        float[] columnSizes = ResolveTrackSizes(columnTracks, container.ContentWidth, columnGap, fontSize,
            autoColumnWidths?.MaxContent, autoColumnWidths?.MinContent);
        float[] rowSizes = ResolveTrackSizes(rowTracks, 0, rowGap, fontSize, null);

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            float itemWidth = 0;
            for (int c = item.ColumnStart; c < item.ColumnStart + item.ColumnSpan && c < numColumns; c++)
            {
                itemWidth += columnSizes[c];
                if (c > item.ColumnStart) itemWidth += columnGap;
            }

            var childStyle = item.Style;
            if (childStyle.Display == "grid" || childStyle.Display == "inline-grid")
            {
                var childColTemplate = childStyle.Get("grid-template-columns");
                var childRowTemplate = childStyle.Get("grid-template-rows");
                bool colSubgrid = !string.IsNullOrEmpty(childColTemplate) &&
                    childColTemplate.Trim().Equals("subgrid", StringComparison.OrdinalIgnoreCase);
                bool rowSubgrid = !string.IsNullOrEmpty(childRowTemplate) &&
                    childRowTemplate.Trim().Equals("subgrid", StringComparison.OrdinalIgnoreCase);

                if (colSubgrid)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int c = item.ColumnStart; c < item.ColumnStart + item.ColumnSpan && c < numColumns; c++)
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(columnSizes[c].ToString("F2", CultureInfo.InvariantCulture));
                        sb.Append("px");
                    }
                    var overriddenStyle = new ComputedStyle();
                    foreach (var kv in childStyle.All)
                        overriddenStyle.Set(kv.Key, kv.Value);
                    overriddenStyle.Set("grid-template-columns", sb.ToString());
                    if (string.IsNullOrEmpty(childStyle.Get("column-gap")) && string.IsNullOrEmpty(childStyle.Get("gap")))
                        overriddenStyle.Set("column-gap", columnGap.ToString("F2", CultureInfo.InvariantCulture) + "px");
                    childStyle = overriddenStyle;
                    item.Style = childStyle;
                }

                if (rowSubgrid)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int r = item.RowStart; r < item.RowStart + item.RowSpan && r < numRows; r++)
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(rowSizes[r].ToString("F2", CultureInfo.InvariantCulture));
                        sb.Append("px");
                    }
                    var overriddenStyle = new ComputedStyle();
                    foreach (var kv in childStyle.All)
                        overriddenStyle.Set(kv.Key, kv.Value);
                    overriddenStyle.Set("grid-template-rows", sb.ToString());
                    if (string.IsNullOrEmpty(childStyle.Get("row-gap")) && string.IsNullOrEmpty(childStyle.Get("gap")))
                        overriddenStyle.Set("row-gap", rowGap.ToString("F2", CultureInfo.InvariantCulture) + "px");
                    childStyle = overriddenStyle;
                    item.Style = childStyle;
                }
            }

            LayoutBox childBox;
            if (item.PseudoContent != null)
            {
                float pFontSize = BlockLayout.ResolveFontSize(item.Style.FontSize, fontSize);
                float pLineHeight = TextMeasurer.GetLineHeight(pFontSize, item.Style.Get("line-height"));
                childBox = new LayoutBox
                {
                    Style = item.Style,
                    Width = itemWidth,
                    Height = pLineHeight,
                    ContentWidth = itemWidth,
                    ContentHeight = pLineHeight,
                    Text = item.PseudoContent
                };
            }
            else
            {
                childBox = BlockLayout.CreateBox(item.Element, item.Style, container, itemWidth, resolver, style);
            }

            if (!IsReplacedElement(item.Element))
            {
                childBox.Width = itemWidth;
                childBox.ContentWidth = itemWidth - childBox.PaddingLeft - childBox.PaddingRight;
                if (childBox.ContentWidth < 0) childBox.ContentWidth = 0;
            }

            item.Box = childBox;

            float itemHeight = childBox.Height;
            int rowEnd = item.RowStart + item.RowSpan;
            if (item.RowSpan == 1 && item.RowStart < numRows)
            {
                if (rowTracks[item.RowStart].Type == TrackType.Auto)
                {
                    if (itemHeight > rowSizes[item.RowStart])
                        rowSizes[item.RowStart] = itemHeight;
                }
            }
            else
            {
                float existingHeight = 0;
                int autoRowCount = 0;
                for (int r = item.RowStart; r < rowEnd && r < numRows; r++)
                {
                    existingHeight += rowSizes[r];
                    if (r > item.RowStart) existingHeight += rowGap;
                    if (rowTracks[r].Type == TrackType.Auto) autoRowCount++;
                }
                if (itemHeight > existingHeight && autoRowCount > 0)
                {
                    float extra = (itemHeight - existingHeight) / autoRowCount;
                    for (int r = item.RowStart; r < rowEnd && r < numRows; r++)
                    {
                        if (rowTracks[r].Type == TrackType.Auto)
                            rowSizes[r] += extra;
                    }
                }
            }
        }

        float[] columnPositions = new float[numColumns];
        float colPos = 0;
        for (int c = 0; c < numColumns; c++)
        {
            columnPositions[c] = colPos;
            colPos += columnSizes[c];
            if (c < numColumns - 1) colPos += columnGap;
        }

        float[] rowPositions = new float[numRows];
        float rowPos = 0;
        for (int r = 0; r < numRows; r++)
        {
            rowPositions[r] = rowPos;
            rowPos += rowSizes[r];
            if (r < numRows - 1) rowPos += rowGap;
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Box == null) continue;

            float cellX = container.X + container.PaddingLeft;
            if (item.ColumnStart < numColumns)
                cellX += columnPositions[item.ColumnStart];

            float cellY = container.Y + container.PaddingTop;
            if (item.RowStart < numRows)
                cellY += rowPositions[item.RowStart];

            float cellWidth = 0;
            for (int c = item.ColumnStart; c < item.ColumnStart + item.ColumnSpan && c < numColumns; c++)
            {
                cellWidth += columnSizes[c];
                if (c > item.ColumnStart) cellWidth += columnGap;
            }

            float cellHeight = 0;
            for (int r = item.RowStart; r < item.RowStart + item.RowSpan && r < numRows; r++)
            {
                cellHeight += rowSizes[r];
                if (r > item.RowStart) cellHeight += rowGap;
            }

            float deltaX = cellX - item.Box.X;
            item.Box.X = cellX;
            item.Box.Y = cellY;
            if (Math.Abs(deltaX) > 0.01f)
                FlexLayout.OffsetChildren(item.Box, deltaX, 0);
            bool replaced = IsReplacedElement(item.Element);
            if (!replaced)
            {
                item.Box.Width = cellWidth;
                item.Box.ContentWidth = cellWidth - item.Box.PaddingLeft - item.Box.PaddingRight;
                if (item.Box.ContentWidth < 0) item.Box.ContentWidth = 0;
            }

            if (!replaced && (string.IsNullOrEmpty(item.Style.Height) || item.Style.Height == "auto"))
            {
                item.Box.Height = cellHeight;
                item.Box.ContentHeight = cellHeight - item.Box.PaddingTop - item.Box.PaddingBottom;
                if (item.Box.ContentHeight < 0) item.Box.ContentHeight = 0;
            }

            container.Children.Add(item.Box);
        }
    }

    private static float ResolveGap(ComputedStyle style, bool isColumn, float containingWidth, float fontSize)
    {
        string? gapValue = isColumn ? style.Get("column-gap") : style.Get("row-gap");

        if (string.IsNullOrEmpty(gapValue))
        {
            var shorthand = style.Get("gap");
            if (!string.IsNullOrEmpty(shorthand))
            {
                var parts = shorthand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                {
                    gapValue = parts[0];
                }
                else if (parts.Length >= 2)
                {
                    gapValue = isColumn ? parts[1] : parts[0];
                }
            }
        }

        return BlockLayout.ResolveLength(gapValue, containingWidth, fontSize);
    }

    private static List<GridItem> CollectGridItems(HtmlElement element, ComputedStyle containerStyle,
        LayoutBox container, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        float fontSize)
    {
        var items = new List<GridItem>();

        var beforeItem = TryCreatePseudoItem(element, containerStyle, "before");
        if (beforeItem != null) items.Add(beforeItem);

        for (int i = 0; i < element.ChildNodes.Count; i++)
        {
            var childNode = element.ChildNodes[i];
            if (!(childNode is HtmlElement childElem))
                continue;

            var childStyle = resolver(childElem, containerStyle);
            if (childStyle.Display == "none")
                continue;

            var childPosition = childStyle.Get("position");
            if (childPosition == "absolute" || childPosition == "fixed")
                continue;

            var item = new GridItem
            {
                Element = childElem,
                Style = childStyle,
                SourceIndex = i,
                ColumnStart = AutoPlaced,
                RowStart = AutoPlaced,
                ColumnSpan = 1,
                RowSpan = 1
            };

            ParseGridPlacement(childStyle.Get("grid-column-start"), childStyle.Get("grid-column-end"),
                childStyle.Get("grid-column"),
                out item.ColumnStart, out item.ColumnSpan);

            ParseGridPlacement(childStyle.Get("grid-row-start"), childStyle.Get("grid-row-end"),
                childStyle.Get("grid-row"),
                out item.RowStart, out item.RowSpan);

            var gridArea = childStyle.Get("grid-area");
            if (!string.IsNullOrEmpty(gridArea))
            {
                item.AreaName = gridArea.Trim();
            }

            items.Add(item);
        }

        var afterItem = TryCreatePseudoItem(element, containerStyle, "after");
        if (afterItem != null) items.Add(afterItem);

        return items;
    }

    private const int AutoPlaced = -1;
    private const string EmptyAreaCell = ".";

    private static GridItem? TryCreatePseudoItem(HtmlElement element, ComputedStyle containerStyle, string pseudo)
    {
        if (!BlockLayout.TryResolvePseudoContent(element, pseudo, containerStyle, out var pseudoStyle, out var content))
            return null;

        return new GridItem
        {
            Element = element,
            Style = pseudoStyle!,
            ColumnStart = AutoPlaced,
            RowStart = AutoPlaced,
            ColumnSpan = 1,
            RowSpan = 1,
            PseudoContent = content
        };
    }

    private static void ParseGridPlacement(string? start, string? end, string? shorthand,
        out int startLine, out int span)
    {
        startLine = AutoPlaced;
        span = 1;

        if (!string.IsNullOrEmpty(shorthand))
        {
            var trimmed = shorthand.Trim();
            if (trimmed.StartsWith("span", StringComparison.OrdinalIgnoreCase))
            {
                span = ParseSpanValue(trimmed);
                return;
            }

            int slashIdx = trimmed.IndexOf('/');
            if (slashIdx >= 0)
            {
                var startPart = trimmed.Substring(0, slashIdx).Trim();
                var endPart = trimmed.Substring(slashIdx + 1).Trim();

                if (int.TryParse(startPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
                    startLine = s - 1;

                if (endPart.StartsWith("span", StringComparison.OrdinalIgnoreCase))
                {
                    span = ParseSpanValue(endPart);
                }
                else if (int.TryParse(endPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int e))
                {
                    if (startLine >= 0)
                        span = e - (startLine + 1);
                    if (span < 1) span = 1;
                }
                return;
            }

            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int singleVal))
            {
                startLine = singleVal - 1;
                return;
            }
        }

        if (!string.IsNullOrEmpty(start))
        {
            var trimmedStart = start.Trim();
            if (trimmedStart.StartsWith("span", StringComparison.OrdinalIgnoreCase))
            {
                span = ParseSpanValue(trimmedStart);
            }
            else if (int.TryParse(trimmedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
            {
                startLine = s - 1;
            }
        }

        if (!string.IsNullOrEmpty(end))
        {
            var trimmedEnd = end.Trim();
            if (trimmedEnd.StartsWith("span", StringComparison.OrdinalIgnoreCase))
            {
                span = ParseSpanValue(trimmedEnd);
            }
            else if (int.TryParse(trimmedEnd, NumberStyles.Integer, CultureInfo.InvariantCulture, out int e))
            {
                if (startLine >= 0)
                {
                    span = e - (startLine + 1);
                    if (span < 1) span = 1;
                }
            }
        }
    }

    private static int ParseSpanValue(string value)
    {
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            return Math.Max(1, n);
        return 1;
    }

    private static Dictionary<string, GridArea> ParseTemplateAreas(string? templateAreas)
    {
        var areas = new Dictionary<string, GridArea>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(templateAreas))
            return areas;

        var rows = new List<string[]>();
        int pos = 0;
        while (pos < templateAreas.Length)
        {
            int quoteStart = templateAreas.IndexOf('"', pos);
            if (quoteStart < 0)
            {
                quoteStart = templateAreas.IndexOf('\'', pos);
            }
            if (quoteStart < 0) break;

            char quoteChar = templateAreas[quoteStart];
            int quoteEnd = templateAreas.IndexOf(quoteChar, quoteStart + 1);
            if (quoteEnd < 0) break;

            string rowStr = templateAreas.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Trim();
            var cells = rowStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            rows.Add(cells);
            pos = quoteEnd + 1;
        }

        for (int r = 0; r < rows.Count; r++)
        {
            var cells = rows[r];
            for (int c = 0; c < cells.Length; c++)
            {
                var name = cells[c];
                if (name == EmptyAreaCell) continue;

                if (areas.TryGetValue(name, out var existing))
                {
                    if (c < existing.ColumnStart) existing.ColumnStart = c;
                    if (r < existing.RowStart) existing.RowStart = r;
                    int colEnd = c + 1;
                    int rowEnd = r + 1;
                    if (colEnd > existing.ColumnStart + existing.ColumnSpan)
                        existing.ColumnSpan = colEnd - existing.ColumnStart;
                    if (rowEnd > existing.RowStart + existing.RowSpan)
                        existing.RowSpan = rowEnd - existing.RowStart;
                    areas[name] = existing;
                }
                else
                {
                    areas[name] = new GridArea
                    {
                        ColumnStart = c,
                        RowStart = r,
                        ColumnSpan = 1,
                        RowSpan = 1
                    };
                }
            }
        }

        return areas;
    }

    private static void ResolveExplicitPlacement(List<GridItem> items, Dictionary<string, GridArea> areaMap, int numColumns)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (!string.IsNullOrEmpty(item.AreaName) && areaMap.TryGetValue(item.AreaName, out var area))
            {
                item.ColumnStart = area.ColumnStart;
                item.RowStart = area.RowStart;
                item.ColumnSpan = area.ColumnSpan;
                item.RowSpan = area.RowSpan;
            }

            if (item.ColumnStart >= numColumns)
                item.ColumnStart = numColumns - 1;
            if (item.ColumnStart >= 0 && item.ColumnStart + item.ColumnSpan > numColumns)
                item.ColumnSpan = numColumns - item.ColumnStart;
        }
    }

    private static List<TrackDefinition> ParseTrackList(string? trackList, float containingSize, float fontSize)
    {
        var tracks = new List<TrackDefinition>();
        if (string.IsNullOrEmpty(trackList))
            return tracks;

        var input = trackList.Trim();

        int repeatIdx = input.IndexOf("repeat(", StringComparison.OrdinalIgnoreCase);
        if (repeatIdx >= 0)
        {
            input = ExpandRepeat(input);
        }

        input = ExpandMinmax(input);

        var parts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (string.IsNullOrEmpty(part)) continue;

            tracks.Add(ParseSingleTrack(part, containingSize, fontSize));
        }

        return tracks;
    }

    private static bool HasAutoRepeat(string input)
    {
        return input.IndexOf("auto-fill", StringComparison.OrdinalIgnoreCase) >= 0 ||
               input.IndexOf("auto-fit", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ExpandAutoRepeat(string input, float containingSize, int itemCount)
    {
        int idx = 0;
        while (idx < input.Length)
        {
            int repeatStart = input.IndexOf("repeat(", idx, StringComparison.OrdinalIgnoreCase);
            if (repeatStart < 0) break;

            int parenDepth = 0;
            int closeIdx = -1;
            for (int i = repeatStart + 7; i < input.Length; i++)
            {
                if (input[i] == '(') parenDepth++;
                else if (input[i] == ')')
                {
                    if (parenDepth == 0) { closeIdx = i; break; }
                    parenDepth--;
                }
            }
            if (closeIdx < 0) break;

            string inner = input.Substring(repeatStart + 7, closeIdx - repeatStart - 7);
            int commaIdx = inner.IndexOf(',');
            if (commaIdx < 0) { idx = closeIdx + 1; continue; }

            string countStr = inner.Substring(0, commaIdx).Trim();
            bool isAutoFill = string.Equals(countStr, "auto-fill", StringComparison.OrdinalIgnoreCase);
            bool isAutoFit = string.Equals(countStr, "auto-fit", StringComparison.OrdinalIgnoreCase);

            if (!isAutoFill && !isAutoFit) { idx = closeIdx + 1; continue; }

            string trackStr = inner.Substring(commaIdx + 1).Trim();

            float minSize = ExtractMinSizeFromTrack(trackStr);
            if (minSize <= 0) minSize = 1;

            int count = (int)(containingSize / minSize);
            if (count < 1) count = 1;

            if (isAutoFit)
                count = Math.Min(count, Math.Max(1, itemCount));

            string expandedTrack = ExtractMaxTrackValue(trackStr);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(expandedTrack);
            }

            string expanded = sb.ToString();
            input = input.Substring(0, repeatStart) + expanded + input.Substring(closeIdx + 1);
            idx = repeatStart + expanded.Length;
        }

        return input;
    }

    private static float ExtractMinSizeFromTrack(string trackStr)
    {
        if (trackStr.StartsWith("minmax(", StringComparison.OrdinalIgnoreCase))
        {
            int closeIdx = trackStr.IndexOf(')');
            if (closeIdx > 7)
            {
                string inner = trackStr.Substring(7, closeIdx - 7);
                int commaIdx = inner.IndexOf(',');
                if (commaIdx >= 0)
                    return ParseSimpleLength(inner.Substring(0, commaIdx).Trim());
            }
        }
        return ParseSimpleLength(trackStr);
    }

    private static string ExtractMaxTrackValue(string trackStr)
    {
        if (trackStr.StartsWith("minmax(", StringComparison.OrdinalIgnoreCase))
        {
            int closeIdx = trackStr.IndexOf(')');
            if (closeIdx > 7)
            {
                string inner = trackStr.Substring(7, closeIdx - 7);
                int commaIdx = inner.IndexOf(',');
                if (commaIdx >= 0)
                    return inner.Substring(commaIdx + 1).Trim();
            }
        }
        return trackStr;
    }

    private static float ParseSimpleLength(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            var numStr = value.Substring(0, value.Length - 2);
            if (float.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float px))
                return px;
        }
        return 0;
    }

    private static string ExpandRepeat(string input)
    {
        int idx = 0;
        while (idx < input.Length)
        {
            int repeatStart = input.IndexOf("repeat(", idx, StringComparison.OrdinalIgnoreCase);
            if (repeatStart < 0) break;

            int parenDepth = 0;
            int closeIdx = -1;
            for (int i = repeatStart + 7; i < input.Length; i++)
            {
                if (input[i] == '(') parenDepth++;
                else if (input[i] == ')')
                {
                    if (parenDepth == 0)
                    {
                        closeIdx = i;
                        break;
                    }
                    parenDepth--;
                }
            }

            if (closeIdx < 0) break;

            string inner = input.Substring(repeatStart + 7, closeIdx - repeatStart - 7);
            int commaIdx = inner.IndexOf(',');
            if (commaIdx < 0) { idx = closeIdx + 1; continue; }

            string countStr = inner.Substring(0, commaIdx).Trim();
            string trackStr = inner.Substring(commaIdx + 1).Trim();

            if (!int.TryParse(countStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                idx = closeIdx + 1;
                continue;
            }

            var expanded = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) expanded.Append(' ');
                expanded.Append(trackStr);
            }

            input = input.Substring(0, repeatStart) + expanded.ToString() + input.Substring(closeIdx + 1);
            idx = repeatStart + expanded.Length;
        }

        return input;
    }

    private static string ExpandMinmax(string input)
    {
        int idx = 0;
        while (idx < input.Length)
        {
            int minmaxStart = input.IndexOf("minmax(", idx, StringComparison.OrdinalIgnoreCase);
            if (minmaxStart < 0) break;

            int parenDepth = 0;
            int closeIdx = -1;
            for (int i = minmaxStart + 7; i < input.Length; i++)
            {
                if (input[i] == '(') parenDepth++;
                else if (input[i] == ')')
                {
                    if (parenDepth == 0)
                    {
                        closeIdx = i;
                        break;
                    }
                    parenDepth--;
                }
            }

            if (closeIdx < 0) break;

            string inner = input.Substring(minmaxStart + 7, closeIdx - minmaxStart - 7);
            int commaIdx = inner.IndexOf(',');
            if (commaIdx < 0) { idx = closeIdx + 1; continue; }

            string maxValue = inner.Substring(commaIdx + 1).Trim();
            input = input.Substring(0, minmaxStart) + maxValue + input.Substring(closeIdx + 1);
            idx = minmaxStart + maxValue.Length;
        }

        return input;
    }

    private static TrackDefinition ParseSingleTrack(string value, float containingSize, float fontSize)
    {
        if (value == "auto")
        {
            return new TrackDefinition { Type = TrackType.Auto, Value = 0 };
        }

        if (value.EndsWith("fr"))
        {
            var numStr = value.Substring(0, value.Length - 2);
            if (float.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float fr))
            {
                return new TrackDefinition { Type = TrackType.Fr, Value = fr };
            }
            return new TrackDefinition { Type = TrackType.Fr, Value = 1 };
        }

        float resolved = BlockLayout.ResolveLength(value, containingSize, fontSize);
        return new TrackDefinition { Type = TrackType.Fixed, Value = resolved };
    }

    private static float[] ResolveTrackSizes(List<TrackDefinition> tracks, float availableSize, float gap, float fontSize,
        float[]? autoContentSizes, float[]? autoMinContentSizes = null)
    {
        float[] sizes = new float[tracks.Count];
        float totalGap = tracks.Count > 1 ? gap * (tracks.Count - 1) : 0;
        float usedSpace = totalGap;
        float totalFr = 0;
        int autoCount = 0;

        for (int i = 0; i < tracks.Count; i++)
        {
            switch (tracks[i].Type)
            {
                case TrackType.Fixed:
                    sizes[i] = tracks[i].Value;
                    usedSpace += sizes[i];
                    break;
                case TrackType.Fr:
                    totalFr += tracks[i].Value;
                    break;
                case TrackType.Auto:
                    autoCount++;
                    if (autoContentSizes != null)
                        sizes[i] = autoContentSizes[i];
                    break;
            }
        }

        usedSpace += FitAutoTracks(tracks, sizes, autoMinContentSizes, availableSize - usedSpace);

        float remainingSpace = availableSize - usedSpace;
        if (remainingSpace < 0) remainingSpace = 0;

        if (totalFr > 0)
        {
            float frUnit = remainingSpace / totalFr;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i].Type == TrackType.Fr)
                {
                    sizes[i] = frUnit * tracks[i].Value;
                }
            }
            remainingSpace = 0;
        }

        if (autoCount > 0)
        {
            float autoShare = remainingSpace > 0 ? remainingSpace / autoCount : 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i].Type == TrackType.Auto)
                    sizes[i] += autoShare;
            }
        }

        return sizes;
    }

    private static float FitAutoTracks(List<TrackDefinition> tracks, float[] sizes, float[]? minContentSizes, float spaceForAutoTracks)
    {
        float totalMax = 0f, totalMin = 0f;
        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].Type != TrackType.Auto) continue;
            totalMax += sizes[i];
            totalMin += MinContentOf(minContentSizes, i, sizes[i]);
        }
        if (totalMax <= 0f || totalMax <= spaceForAutoTracks) return totalMax;

        float space = Math.Max(0f, spaceForAutoTracks);
        if (totalMin >= space)
        {
            for (int i = 0; i < tracks.Count; i++)
                if (tracks[i].Type == TrackType.Auto) sizes[i] = MinContentOf(minContentSizes, i, sizes[i]);
            return totalMin;
        }

        float share = (space - totalMin) / (totalMax - totalMin);
        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].Type != TrackType.Auto) continue;
            float min = MinContentOf(minContentSizes, i, sizes[i]);
            sizes[i] = min + (sizes[i] - min) * share;
        }
        return space;
    }

    private static float MinContentOf(float[]? minContentSizes, int track, float maxContent) =>
        minContentSizes == null ? 0f : Math.Min(minContentSizes[track], maxContent);

    private sealed class AutoTrackWidths
    {
        public AutoTrackWidths(int trackCount)
        {
            MaxContent = new float[trackCount];
            MinContent = new float[trackCount];
        }

        public float[] MaxContent { get; }
        public float[] MinContent { get; }
    }

    private static bool IsReplacedElement(HtmlElement? element) =>
        element != null && (element.TagName == "img" || element.TagName == "svg");

    private static AutoTrackWidths? AutoColumnContentWidths(List<TrackDefinition> tracks, List<GridItem> items,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, float fontSize)
    {
        AutoTrackWidths? widths = null;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.ColumnSpan != 1 || item.ColumnStart >= tracks.Count || tracks[item.ColumnStart].Type != TrackType.Auto)
                continue;

            float itemFontSize = BlockLayout.ResolveFontSize(item.Style.FontSize, fontSize);
            var specifiedWidth = item.Style.Width;
            bool definiteWidth = !string.IsNullOrEmpty(specifiedWidth) && !specifiedWidth!.EndsWith("%", StringComparison.Ordinal);
            float? definite = definiteWidth ? BlockLayout.ResolveOptionalLength(specifiedWidth, 0, itemFontSize) : null;
            float maxContent = definite ?? FlexLayout.MeasureMaxContentWidth(item.Element, item.Style, resolver, itemFontSize);
            float minContent = definite ?? FlexLayout.MeasureMinContentWidth(item.Element, item.Style, resolver, itemFontSize, 0);
            widths ??= new AutoTrackWidths(tracks.Count);
            int column = item.ColumnStart;
            if (maxContent > widths.MaxContent[column]) widths.MaxContent[column] = maxContent;
            if (minContent > widths.MinContent[column]) widths.MinContent[column] = minContent;
        }
        return widths;
    }

    private enum TrackType
    {
        Fixed,
        Fr,
        Auto
    }

    private struct TrackDefinition
    {
        public TrackType Type;
        public float Value;
    }

    private struct GridArea
    {
        public int ColumnStart;
        public int RowStart;
        public int ColumnSpan;
        public int RowSpan;
    }

    private sealed class GridItem
    {
        public HtmlElement Element = null!;
        public ComputedStyle Style = null!;
        public LayoutBox? Box;
        public int SourceIndex;
        public int ColumnStart;
        public int RowStart;
        public int ColumnSpan;
        public int RowSpan;
        public string? AreaName;
        public string? PseudoContent;
    }
}
