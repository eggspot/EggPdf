using System.Collections.Generic;

namespace EggPdf.Layout;

public static partial class GridLayout
{
    /// <summary>
    /// Auto-place items that don't have explicit placement. Returns the resolved column
    /// count -- unchanged unless <paramref name="allowColumnGrowth"/> caused the grid to grow
    /// beyond <paramref name="numColumns"/> (grid-auto-flow:column with no explicit
    /// grid-template-columns growing a new implicit column once existing ones fill up).
    /// </summary>
    private static int AutoPlaceItems(List<GridItem> items, int numColumns, bool flowColumn,
        int explicitRowCount, bool dense, bool allowColumnGrowth)
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

        // When columns can grow (column-flow with implicit columns), over-allocate the grid's
        // column dimension the same way estimatedRows already over-allocates rows -- the
        // search loops below naturally spill into these extra columns as needed, and the
        // actual used count is computed from final item placements before returning.
        int gridCols = allowColumnGrowth ? numColumns + items.Count : numColumns;
        if (gridCols < 1) gridCols = 1;

        // Occupancy grid: true = occupied
        var grid = new bool[estimatedRows, gridCols];

        // Mark explicitly placed items
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.ColumnStart >= 0 && item.RowStart >= 0)
            {
                MarkOccupied(grid, item.RowStart, item.ColumnStart, item.RowSpan, item.ColumnSpan, gridCols, estimatedRows);
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
                    if (CanPlace(grid, r, item.ColumnStart, item.RowSpan, item.ColumnSpan, gridCols, estimatedRows))
                    {
                        item.RowStart = r;
                        MarkOccupied(grid, r, item.ColumnStart, item.RowSpan, item.ColumnSpan, gridCols, estimatedRows);
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
                    if (CanPlace(grid, item.RowStart, c, item.RowSpan, item.ColumnSpan, gridCols, estimatedRows))
                    {
                        item.ColumnStart = c;
                        MarkOccupied(grid, item.RowStart, c, item.RowSpan, item.ColumnSpan, gridCols, estimatedRows);
                        break;
                    }
                }
                if (item.ColumnStart < 0) item.ColumnStart = 0;
                continue;
            }

            // Fully auto placement. Dense packing restarts the search from the grid origin
            // for every item instead of continuing from the previous item's cursor, so it
            // backfills gaps left by an earlier item that couldn't fit and skipped ahead;
            // sparse (default) packing always continues forward from the last cursor.
            if (flowColumn)
            {
                // Column-wise: fill rows in a column, then advance to next column.
                // When explicit template rows exist, limit rows per column.
                int maxRowsPerCol = explicitRowCount > 0 ? explicitRowCount : estimatedRows;
                int searchStartCol = dense ? 0 : cursorCol;
                int searchStartRow = dense ? 0 : cursorRow;
                bool placed = false;
                for (int c = searchStartCol; c < gridCols && !placed; c++)
                {
                    for (int r = (c == searchStartCol ? searchStartRow : 0); r < maxRowsPerCol; r++)
                    {
                        if (CanPlace(grid, r, c, item.RowSpan, item.ColumnSpan, gridCols, estimatedRows))
                        {
                            item.RowStart = r;
                            item.ColumnStart = c;
                            MarkOccupied(grid, r, c, item.RowSpan, item.ColumnSpan, gridCols, estimatedRows);
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
                int searchStartRow = dense ? 0 : cursorRow;
                int searchStartCol = dense ? 0 : cursorCol;
                bool placed = false;
                for (int r = searchStartRow; r < estimatedRows && !placed; r++)
                {
                    for (int c = (r == searchStartRow ? searchStartCol : 0); c <= gridCols - item.ColumnSpan; c++)
                    {
                        if (CanPlace(grid, r, c, item.RowSpan, item.ColumnSpan, gridCols, estimatedRows))
                        {
                            item.RowStart = r;
                            item.ColumnStart = c;
                            MarkOccupied(grid, r, c, item.RowSpan, item.ColumnSpan, gridCols, estimatedRows);
                            cursorRow = r;
                            cursorCol = c + item.ColumnSpan;
                            if (cursorCol >= gridCols)
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

        if (!allowColumnGrowth) return numColumns;

        int resolvedColumns = numColumns;
        for (int i = 0; i < items.Count; i++)
        {
            int colEnd = items[i].ColumnStart + items[i].ColumnSpan;
            if (colEnd > resolvedColumns) resolvedColumns = colEnd;
        }
        return resolvedColumns;
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
}
