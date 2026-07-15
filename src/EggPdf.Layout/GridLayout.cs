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
public static class GridLayout
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

        // Parse grid container properties
        string? templateColumns = style.Get("grid-template-columns");
        string? templateRows = style.Get("grid-template-rows");
        string? templateAreas = style.Get("grid-template-areas");
        string autoFlow = style.Get("grid-auto-flow") ?? "row";
        bool flowColumn = autoFlow.IndexOf("column", StringComparison.OrdinalIgnoreCase) >= 0;

        // Resolve gap
        float columnGap = ResolveGap(style, true, container.ContentWidth, fontSize);
        float rowGap = ResolveGap(style, false, container.ContentWidth, fontSize);

        // Collect grid items
        var items = CollectGridItems(element, style, container, containingWidth, resolver, fontSize);
        if (items.Count == 0) return;

        // Parse template areas if specified
        var areaMap = ParseTemplateAreas(templateAreas);

        // Expand auto-fill / auto-fit repeat() before full track parsing
        string? resolvedColumns = templateColumns;
        if (!string.IsNullOrEmpty(resolvedColumns) && HasAutoRepeat(resolvedColumns))
            resolvedColumns = ExpandAutoRepeat(resolvedColumns, container.ContentWidth, items.Count);
        string? resolvedRows = templateRows;
        if (!string.IsNullOrEmpty(resolvedRows) && HasAutoRepeat(resolvedRows))
            resolvedRows = ExpandAutoRepeat(resolvedRows, container.ContentWidth, items.Count);

        // Parse column and row track definitions
        var columnTracks = ParseTrackList(resolvedColumns, container.ContentWidth, fontSize);
        var rowTracks = ParseTrackList(resolvedRows, container.ContentWidth, fontSize);

        // If no explicit columns defined, determine from items
        if (columnTracks.Count == 0)
        {
            // Create implicit single column (all items in one column)
            if (flowColumn)
            {
                // For column flow with no template, create as many columns as explicitly placed items need,
                // default to 1
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

        // Resolve explicit placement from item properties and areas
        ResolveExplicitPlacement(items, areaMap, numColumns);

        // Auto-place remaining items
        AutoPlaceItems(items, numColumns, flowColumn, rowTracks.Count);

        // Determine number of rows needed
        int numRows = rowTracks.Count;
        for (int i = 0; i < items.Count; i++)
        {
            int neededRows = items[i].RowStart + items[i].RowSpan;
            if (neededRows > numRows) numRows = neededRows;
        }

        // Add implicit row tracks if needed
        while (rowTracks.Count < numRows)
        {
            rowTracks.Add(new TrackDefinition { Type = TrackType.Auto, Value = 0 });
        }

        // Resolve track sizes
        float[] columnSizes = ResolveTrackSizes(columnTracks, container.ContentWidth, columnGap, fontSize);
        float[] rowSizes = ResolveTrackSizes(rowTracks, 0, rowGap, fontSize); // rows don't have a fixed containing size initially

        // Create child boxes and measure for auto rows
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            // Calculate available width for this item
            float itemWidth = 0;
            for (int c = item.ColumnStart; c < item.ColumnStart + item.ColumnSpan && c < numColumns; c++)
            {
                itemWidth += columnSizes[c];
                if (c > item.ColumnStart) itemWidth += columnGap;
            }

            // subgrid: if child has display:grid and grid-template-columns:subgrid,
            // override its column template with the parent's resolved column sizes for its span.
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
                    // Build explicit column widths from parent's resolved sizes for this item's span
                    var sb = new System.Text.StringBuilder();
                    for (int c = item.ColumnStart; c < item.ColumnStart + item.ColumnSpan && c < numColumns; c++)
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(columnSizes[c].ToString("F2", CultureInfo.InvariantCulture));
                        sb.Append("px");
                    }
                    // Clone style with overridden column template
                    var overriddenStyle = new ComputedStyle();
                    foreach (var kv in childStyle.All)
                        overriddenStyle.Set(kv.Key, kv.Value);
                    overriddenStyle.Set("grid-template-columns", sb.ToString());
                    childStyle = overriddenStyle;
                    item.Style = childStyle;
                }

                if (rowSubgrid)
                {
                    // Build explicit row heights from parent's resolved row sizes for this item's span
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
                    childStyle = overriddenStyle;
                    item.Style = childStyle;
                }
            }

            // Create child box with the calculated width
            var childBox = BlockLayout.CreateBox(item.Element, item.Style, container, itemWidth, resolver, style);

            // Override width to match grid cell
            childBox.Width = itemWidth;
            childBox.ContentWidth = itemWidth - childBox.PaddingLeft - childBox.PaddingRight;
            if (childBox.ContentWidth < 0) childBox.ContentWidth = 0;

            item.Box = childBox;

            // Update auto row heights based on content
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
                // For spanning items, distribute height across auto rows if needed
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

        // Position items
        // Compute column start positions
        float[] columnPositions = new float[numColumns];
        float colPos = 0;
        for (int c = 0; c < numColumns; c++)
        {
            columnPositions[c] = colPos;
            colPos += columnSizes[c];
            if (c < numColumns - 1) colPos += columnGap;
        }

        // Compute row start positions
        float[] rowPositions = new float[numRows];
        float rowPos = 0;
        for (int r = 0; r < numRows; r++)
        {
            rowPositions[r] = rowPos;
            rowPos += rowSizes[r];
            if (r < numRows - 1) rowPos += rowGap;
        }

        // Place each item
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

            // Calculate cell dimensions (spanning)
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

            // Move the item box to its cell. X coordinates are absolute, so the
            // already-laid-out descendants shift with the box; Y coordinates are
            // parent-relative and get resolved in BlockLayout's post-layout pass.
            float deltaX = cellX - item.Box.X;
            item.Box.X = cellX;
            item.Box.Y = cellY;
            if (Math.Abs(deltaX) > 0.01f)
                FlexLayout.OffsetChildren(item.Box, deltaX, 0);
            item.Box.Width = cellWidth;
            item.Box.ContentWidth = cellWidth - item.Box.PaddingLeft - item.Box.PaddingRight;
            if (item.Box.ContentWidth < 0) item.Box.ContentWidth = 0;

            // Stretch height to fill cell if no explicit height
            if (string.IsNullOrEmpty(item.Style.Height) || item.Style.Height == "auto")
            {
                item.Box.Height = cellHeight;
                item.Box.ContentHeight = cellHeight - item.Box.PaddingTop - item.Box.PaddingBottom;
                if (item.Box.ContentHeight < 0) item.Box.ContentHeight = 0;
            }

            container.Children.Add(item.Box);
        }
    }

    /// <summary>Resolve gap value for column or row axis.</summary>
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
                    // gap: row-gap column-gap
                    gapValue = isColumn ? parts[1] : parts[0];
                }
            }
        }

        return BlockLayout.ResolveLength(gapValue, containingWidth, fontSize);
    }

    /// <summary>Collect grid items from child elements.</summary>
    private static List<GridItem> CollectGridItems(HtmlElement element, ComputedStyle containerStyle,
        LayoutBox container, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        float fontSize)
    {
        var items = new List<GridItem>();

        for (int i = 0; i < element.ChildNodes.Count; i++)
        {
            var childNode = element.ChildNodes[i];
            if (!(childNode is HtmlElement childElem))
                continue;

            var childStyle = resolver(childElem, containerStyle);
            if (childStyle.Display == "none")
                continue;

            // Absolutely/fixed positioned children are out-of-flow, not grid items.
            var childPosition = childStyle.Get("position");
            if (childPosition == "absolute" || childPosition == "fixed")
                continue;

            var item = new GridItem
            {
                Element = childElem,
                Style = childStyle,
                SourceIndex = i,
                ColumnStart = -1,  // -1 = auto placement
                RowStart = -1,
                ColumnSpan = 1,
                RowSpan = 1
            };

            // Parse grid-column-start / grid-column-end
            ParseGridPlacement(childStyle.Get("grid-column-start"), childStyle.Get("grid-column-end"),
                childStyle.Get("grid-column"),
                out item.ColumnStart, out item.ColumnSpan);

            // Parse grid-row-start / grid-row-end
            ParseGridPlacement(childStyle.Get("grid-row-start"), childStyle.Get("grid-row-end"),
                childStyle.Get("grid-row"),
                out item.RowStart, out item.RowSpan);

            // Parse grid-area (maps to named area)
            var gridArea = childStyle.Get("grid-area");
            if (!string.IsNullOrEmpty(gridArea))
            {
                item.AreaName = gridArea.Trim();
            }

            items.Add(item);
        }

        return items;
    }

    /// <summary>Parse grid-column/row placement properties.</summary>
    private static void ParseGridPlacement(string? start, string? end, string? shorthand,
        out int startLine, out int span)
    {
        startLine = -1; // auto
        span = 1;

        // Shorthand: grid-column: 1 / 3  or  grid-column: span 2
        if (!string.IsNullOrEmpty(shorthand))
        {
            var trimmed = shorthand.Trim();
            if (trimmed.StartsWith("span", StringComparison.OrdinalIgnoreCase))
            {
                // grid-column: span N
                span = ParseSpanValue(trimmed);
                return;
            }

            int slashIdx = trimmed.IndexOf('/');
            if (slashIdx >= 0)
            {
                var startPart = trimmed.Substring(0, slashIdx).Trim();
                var endPart = trimmed.Substring(slashIdx + 1).Trim();

                if (int.TryParse(startPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
                    startLine = s - 1; // Convert 1-based to 0-based

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

            // Single number
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int singleVal))
            {
                startLine = singleVal - 1;
                return;
            }
        }

        // Individual properties
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

    /// <summary>Parse "span N" value, returning N.</summary>
    private static int ParseSpanValue(string value)
    {
        // "span 2" or "span2"
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            return Math.Max(1, n);
        return 1;
    }

    /// <summary>Parse grid-template-areas into a map of area-name to grid cell positions.</summary>
    private static Dictionary<string, GridArea> ParseTemplateAreas(string? templateAreas)
    {
        var areas = new Dictionary<string, GridArea>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(templateAreas))
            return areas;

        // Template areas: "'header header' 'sidebar main' 'footer footer'"
        // Each quoted string is a row
        var rows = new List<string[]>();
        int pos = 0;
        while (pos < templateAreas.Length)
        {
            // Find next quoted string
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

        // Build area map
        for (int r = 0; r < rows.Count; r++)
        {
            var cells = rows[r];
            for (int c = 0; c < cells.Length; c++)
            {
                var name = cells[c];
                if (name == ".") continue; // empty cell

                if (areas.TryGetValue(name, out var existing))
                {
                    // Expand existing area
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

    /// <summary>Resolve explicit placement from grid-area names.</summary>
    private static void ResolveExplicitPlacement(List<GridItem> items, Dictionary<string, GridArea> areaMap, int numColumns)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            // If item has an area name, resolve from area map
            if (!string.IsNullOrEmpty(item.AreaName) && areaMap.TryGetValue(item.AreaName, out var area))
            {
                item.ColumnStart = area.ColumnStart;
                item.RowStart = area.RowStart;
                item.ColumnSpan = area.ColumnSpan;
                item.RowSpan = area.RowSpan;
            }

            // Clamp column placement to grid bounds
            if (item.ColumnStart >= numColumns)
                item.ColumnStart = numColumns - 1;
            if (item.ColumnStart >= 0 && item.ColumnStart + item.ColumnSpan > numColumns)
                item.ColumnSpan = numColumns - item.ColumnStart;
        }
    }

    /// <summary>Auto-place items that don't have explicit placement.</summary>
    private static void AutoPlaceItems(List<GridItem> items, int numColumns, bool flowColumn, int explicitRowCount)
    {
        // Build an occupancy grid
        // First pass: determine grid size from explicitly placed items
        int maxRow = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].RowStart >= 0)
            {
                int rowEnd = items[i].RowStart + items[i].RowSpan;
                if (rowEnd > maxRow) maxRow = rowEnd;
            }
        }

        // Estimate enough rows for auto-placed items
        int estimatedRows = maxRow + items.Count; // generous estimate
        if (estimatedRows < 1) estimatedRows = 1;

        // Occupancy grid: true = occupied
        var grid = new bool[estimatedRows, numColumns];

        // Mark explicitly placed items
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.ColumnStart >= 0 && item.RowStart >= 0)
            {
                MarkOccupied(grid, item.RowStart, item.ColumnStart, item.RowSpan, item.ColumnSpan, numColumns, estimatedRows);
            }
        }

        // Auto-place remaining items
        int cursorRow = 0;
        int cursorCol = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            // Skip already fully placed items
            if (item.ColumnStart >= 0 && item.RowStart >= 0)
                continue;

            // Item has explicit column but auto row
            if (item.ColumnStart >= 0 && item.RowStart < 0)
            {
                // Find first available row at this column
                for (int r = 0; r < estimatedRows; r++)
                {
                    if (CanPlace(grid, r, item.ColumnStart, item.RowSpan, item.ColumnSpan, numColumns, estimatedRows))
                    {
                        item.RowStart = r;
                        MarkOccupied(grid, r, item.ColumnStart, item.RowSpan, item.ColumnSpan, numColumns, estimatedRows);
                        break;
                    }
                }
                if (item.RowStart < 0)
                {
                    item.RowStart = estimatedRows - 1;
                }
                continue;
            }

            // Item has explicit row but auto column
            if (item.RowStart >= 0 && item.ColumnStart < 0)
            {
                for (int c = 0; c < numColumns; c++)
                {
                    if (CanPlace(grid, item.RowStart, c, item.RowSpan, item.ColumnSpan, numColumns, estimatedRows))
                    {
                        item.ColumnStart = c;
                        MarkOccupied(grid, item.RowStart, c, item.RowSpan, item.ColumnSpan, numColumns, estimatedRows);
                        break;
                    }
                }
                if (item.ColumnStart < 0) item.ColumnStart = 0;
                continue;
            }

            // Fully auto placement
            if (flowColumn)
            {
                // Column-wise: fill rows in a column, then advance to next column
                // When explicit template rows exist, limit rows per column
                int maxRowsPerCol = explicitRowCount > 0 ? explicitRowCount : estimatedRows;
                bool placed = false;
                for (int c = cursorCol; c < numColumns && !placed; c++)
                {
                    for (int r = (c == cursorCol ? cursorRow : 0); r < maxRowsPerCol; r++)
                    {
                        if (CanPlace(grid, r, c, item.RowSpan, item.ColumnSpan, numColumns, estimatedRows))
                        {
                            item.RowStart = r;
                            item.ColumnStart = c;
                            MarkOccupied(grid, r, c, item.RowSpan, item.ColumnSpan, numColumns, estimatedRows);
                            cursorRow = r + item.RowSpan;
                            cursorCol = c;
                            if (cursorRow >= maxRowsPerCol)
                            {
                                cursorRow = 0;
                                cursorCol = c + 1;
                            }
                            placed = true;
                            break;
                        }
                    }
                }
                if (!placed)
                {
                    // Fallback: place at end
                    item.RowStart = estimatedRows - 1;
                    item.ColumnStart = 0;
                }
            }
            else
            {
                // Row-wise (default): advance column, then wrap to next row
                bool placed = false;
                for (int r = cursorRow; r < estimatedRows && !placed; r++)
                {
                    for (int c = (r == cursorRow ? cursorCol : 0); c <= numColumns - item.ColumnSpan; c++)
                    {
                        if (CanPlace(grid, r, c, item.RowSpan, item.ColumnSpan, numColumns, estimatedRows))
                        {
                            item.RowStart = r;
                            item.ColumnStart = c;
                            MarkOccupied(grid, r, c, item.RowSpan, item.ColumnSpan, numColumns, estimatedRows);
                            cursorRow = r;
                            cursorCol = c + item.ColumnSpan;
                            if (cursorCol >= numColumns)
                            {
                                cursorCol = 0;
                                cursorRow = r + 1;
                            }
                            placed = true;
                            break;
                        }
                    }
                }
                if (!placed)
                {
                    item.RowStart = estimatedRows - 1;
                    item.ColumnStart = 0;
                }
            }
        }
    }

    /// <summary>Check if a span can be placed at the given position.</summary>
    private static bool CanPlace(bool[,] grid, int row, int col, int rowSpan, int colSpan, int numCols, int numRows)
    {
        if (col + colSpan > numCols) return false;
        if (row + rowSpan > numRows) return false;
        for (int r = row; r < row + rowSpan; r++)
        {
            for (int c = col; c < col + colSpan; c++)
            {
                if (grid[r, c]) return false;
            }
        }
        return true;
    }

    /// <summary>Mark cells as occupied.</summary>
    private static void MarkOccupied(bool[,] grid, int row, int col, int rowSpan, int colSpan, int numCols, int numRows)
    {
        for (int r = row; r < row + rowSpan && r < numRows; r++)
        {
            for (int c = col; c < col + colSpan && c < numCols; c++)
            {
                grid[r, c] = true;
            }
        }
    }

    /// <summary>Parse a track list like "200px 1fr 1fr" or "repeat(3, 1fr)" into track definitions.</summary>
    private static List<TrackDefinition> ParseTrackList(string? trackList, float containingSize, float fontSize)
    {
        var tracks = new List<TrackDefinition>();
        if (string.IsNullOrEmpty(trackList))
            return tracks;

        var input = trackList.Trim();

        // Handle repeat() function
        int repeatIdx = input.IndexOf("repeat(", StringComparison.OrdinalIgnoreCase);
        if (repeatIdx >= 0)
        {
            input = ExpandRepeat(input);
        }

        // Handle minmax() - we need to handle this before splitting
        input = ExpandMinmax(input);

        // Split on whitespace
        var parts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (string.IsNullOrEmpty(part)) continue;

            tracks.Add(ParseSingleTrack(part, containingSize, fontSize));
        }

        return tracks;
    }

    /// <summary>Returns true if the track list contains an auto-fill or auto-fit repeat.</summary>
    private static bool HasAutoRepeat(string input)
    {
        return input.IndexOf("auto-fill", StringComparison.OrdinalIgnoreCase) >= 0 ||
               input.IndexOf("auto-fit", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Expand repeat(auto-fill, ...) and repeat(auto-fit, ...) into concrete track lists.
    /// auto-fill: creates as many tracks as fit (floor(containerWidth / minTrackSize)).
    /// auto-fit: same count but capped to itemCount so empty tracks collapse.
    /// </summary>
    private static string ExpandAutoRepeat(string input, float containingSize, int itemCount)
    {
        int idx = 0;
        while (idx < input.Length)
        {
            int repeatStart = input.IndexOf("repeat(", idx, StringComparison.OrdinalIgnoreCase);
            if (repeatStart < 0) break;

            // Find matching close paren
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

            // Extract minimum size from minmax(min, max) to compute how many columns fit
            float minSize = ExtractMinSizeFromTrack(trackStr);
            if (minSize <= 0) minSize = 1;

            int count = (int)(containingSize / minSize);
            if (count < 1) count = 1;

            // auto-fit: collapse empty tracks — only create tracks for actual items
            if (isAutoFit)
                count = Math.Min(count, Math.Max(1, itemCount));

            // Use the max value (or full track) for the expanded track definition
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

    /// <summary>Extracts the minimum (first) size value from a minmax() track or returns the raw value.</summary>
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

    /// <summary>Extracts the maximum (second) value from a minmax() track, or returns the raw value.</summary>
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

    /// <summary>Parse a simple CSS length value (px only) for auto-repeat minimum size calculation.</summary>
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

    /// <summary>Expand repeat(N, track) into N copies of track.</summary>
    private static string ExpandRepeat(string input)
    {
        // Simple repeat expansion: repeat(3, 1fr) -> 1fr 1fr 1fr
        int idx = 0;
        while (idx < input.Length)
        {
            int repeatStart = input.IndexOf("repeat(", idx, StringComparison.OrdinalIgnoreCase);
            if (repeatStart < 0) break;

            // Find the matching closing paren
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

            // Build expansion
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

    /// <summary>Simplify minmax(min, max) to just the max value for layout purposes.</summary>
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

            // Use the max value for track sizing
            string maxValue = inner.Substring(commaIdx + 1).Trim();
            // If max is "1fr", use it; otherwise use the max
            input = input.Substring(0, minmaxStart) + maxValue + input.Substring(closeIdx + 1);
            idx = minmaxStart + maxValue.Length;
        }

        return input;
    }

    /// <summary>Parse a single track definition (e.g., "200px", "1fr", "auto").</summary>
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

        // Fixed unit (px, %, em, etc.)
        float resolved = BlockLayout.ResolveLength(value, containingSize, fontSize);
        return new TrackDefinition { Type = TrackType.Fixed, Value = resolved };
    }

    /// <summary>Resolve track sizes: fixed tracks get their size, fr tracks share remaining space.</summary>
    private static float[] ResolveTrackSizes(List<TrackDefinition> tracks, float availableSize, float gap, float fontSize)
    {
        float[] sizes = new float[tracks.Count];
        float totalGap = tracks.Count > 1 ? gap * (tracks.Count - 1) : 0;
        float usedSpace = totalGap;
        float totalFr = 0;
        int autoCount = 0;

        // First pass: resolve fixed tracks
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
                    break;
            }
        }

        // Second pass: distribute remaining space to fr tracks
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
            remainingSpace = 0; // all space consumed
        }

        // Third pass: auto tracks get equal share of what's left (or a minimum)
        if (autoCount > 0)
        {
            float autoSize = remainingSpace > 0 ? remainingSpace / autoCount : 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i].Type == TrackType.Auto)
                {
                    sizes[i] = autoSize;
                }
            }
        }

        return sizes;
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

    /// <summary>A grid item with placement information.</summary>
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
    }
}
