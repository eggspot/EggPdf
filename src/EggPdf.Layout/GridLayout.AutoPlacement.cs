using System;
using System.Collections.Generic;

namespace EggPdf.Layout;

public static partial class GridLayout
{
    /// <summary>
    /// The CSS Grid auto-placement algorithm (css-grid-1 §8.5). Items are placed along the flow's minor axis (columns
    /// for <c>grid-auto-flow: row</c>, rows for <c>column</c>) and the grid grows along its major axis as needed;
    /// column flow therefore adds implicit columns. Occupancy is stored per major line, so memory grows with the cells
    /// used, never with the square of the item count. Returns the column count, which grows past
    /// <paramref name="numColumns"/> when items need implicit columns.
    /// </summary>
    private static int AutoPlaceItems(List<GridItem> items, int numColumns, bool flowColumn, int explicitRowCount, bool dense)
    {
        var occupancy = new GridOccupancy();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.ColumnStart >= 0 && item.RowStart >= 0)
                occupancy.Mark(MajorStart(item, flowColumn), MajorSpan(item, flowColumn), MinorStart(item, flowColumn), MinorSpan(item, flowColumn));
        }

        PlaceItemsLockedToMajorLine(items, flowColumn, dense, occupancy);

        int minorCount = Math.Max(1, flowColumn ? explicitRowCount : numColumns);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            int minorStart = MinorStart(item, flowColumn);
            int minorEnd = (minorStart >= 0 ? minorStart : 0) + MinorSpan(item, flowColumn);
            if (minorEnd > minorCount) minorCount = minorEnd;
        }

        int cursorMajor = 0, cursorMinor = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (MajorStart(item, flowColumn) >= 0) continue;

            int majorSpan = MajorSpan(item, flowColumn);
            int minorSpan = MinorSpan(item, flowColumn);
            int major, minor = MinorStart(item, flowColumn);

            if (minor >= 0)
            {
                if (dense) major = occupancy.FirstLineWithFreeCell(minorCount);
                else major = minor < cursorMinor ? cursorMajor + 1 : cursorMajor;
                while (!occupancy.IsFree(major, majorSpan, minor, minorSpan)) major++;
            }
            else
            {
                major = dense ? occupancy.FirstLineWithFreeCell(minorCount) : cursorMajor;
                minor = dense ? 0 : cursorMinor;
                while (minor + minorSpan > minorCount || !occupancy.IsFree(major, majorSpan, minor, minorSpan))
                {
                    if (minor + minorSpan >= minorCount) { major++; minor = 0; }
                    else minor++;
                }
            }

            SetStarts(item, flowColumn, major, minor);
            occupancy.Mark(major, majorSpan, minor, minorSpan);
            if (!dense)
            {
                cursorMajor = major;
                cursorMinor = minor + minorSpan;
            }
        }

        int resolvedColumns = numColumns;
        for (int i = 0; i < items.Count; i++)
        {
            int columnEnd = items[i].ColumnStart + items[i].ColumnSpan;
            if (columnEnd > resolvedColumns) resolvedColumns = columnEnd;
        }
        return resolvedColumns;
    }

    /// <summary>
    /// Step 2 of auto-placement: an item with a definite major-axis line (a row in row flow) and an auto minor
    /// position takes the earliest free minor position in that line; in sparse mode, past the items this step already
    /// put in the same line.
    /// </summary>
    private static void PlaceItemsLockedToMajorLine(List<GridItem> items, bool flowColumn, bool dense, GridOccupancy occupancy)
    {
        Dictionary<int, int>? nextMinorByLine = dense ? null : new Dictionary<int, int>();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            int major = MajorStart(item, flowColumn);
            if (major < 0 || MinorStart(item, flowColumn) >= 0) continue;

            int majorSpan = MajorSpan(item, flowColumn);
            int minorSpan = MinorSpan(item, flowColumn);
            int minor = 0;
            if (nextMinorByLine != null && nextMinorByLine.TryGetValue(major, out var next)) minor = next;
            while (!occupancy.IsFree(major, majorSpan, minor, minorSpan)) minor++;

            SetStarts(item, flowColumn, major, minor);
            occupancy.Mark(major, majorSpan, minor, minorSpan);
            if (nextMinorByLine != null) nextMinorByLine[major] = minor + minorSpan;
        }
    }

    /// <summary>The track for the <paramref name="implicitIndex"/>-th implicit column: <c>grid-auto-columns</c> repeated, or 1fr when it is unset.</summary>
    private static TrackDefinition ImplicitColumnTrack(List<TrackDefinition> autoColumnTracks, int implicitIndex) =>
        autoColumnTracks.Count > 0
            ? autoColumnTracks[implicitIndex % autoColumnTracks.Count]
            : new TrackDefinition { Type = TrackType.Fr, Value = 1 };

    private static int MajorStart(GridItem item, bool flowColumn) => flowColumn ? item.ColumnStart : item.RowStart;

    private static int MinorStart(GridItem item, bool flowColumn) => flowColumn ? item.RowStart : item.ColumnStart;

    private static int MajorSpan(GridItem item, bool flowColumn) => Math.Max(1, flowColumn ? item.ColumnSpan : item.RowSpan);

    private static int MinorSpan(GridItem item, bool flowColumn) => Math.Max(1, flowColumn ? item.RowSpan : item.ColumnSpan);

    private static void SetStarts(GridItem item, bool flowColumn, int major, int minor)
    {
        if (flowColumn)
        {
            item.ColumnStart = major;
            item.RowStart = minor;
        }
        else
        {
            item.RowStart = major;
            item.ColumnStart = minor;
        }
    }

    /// <summary>
    /// Which grid cells are taken, stored as one growable array per major-axis line (a row in row flow, a column in
    /// column flow); cells past an array's end are free.
    /// </summary>
    private sealed class GridOccupancy
    {
        private readonly List<bool[]?> _lines = new List<bool[]?>();
        private int _firstLineWithFreeCell;

        public bool IsFree(int major, int majorSpan, int minor, int minorSpan)
        {
            int majorEnd = Math.Min(major + majorSpan, _lines.Count);
            for (int m = major; m < majorEnd; m++)
            {
                var line = _lines[m];
                if (line == null) continue;
                int minorEnd = Math.Min(minor + minorSpan, line.Length);
                for (int c = minor; c < minorEnd; c++)
                {
                    if (line[c]) return false;
                }
            }
            return true;
        }

        public void Mark(int major, int majorSpan, int minor, int minorSpan)
        {
            if (major < 0 || minor < 0) return;
            int minorEnd = minor + minorSpan;
            for (int m = major; m < major + majorSpan; m++)
            {
                while (_lines.Count <= m) _lines.Add(null);
                var line = _lines[m];
                if (line == null) line = new bool[Math.Max(minorEnd, 4)];
                else if (line.Length < minorEnd) Array.Resize(ref line, Math.Max(minorEnd, line.Length * 2));
                for (int c = minor; c < minorEnd; c++) line[c] = true;
                _lines[m] = line;
            }
        }

        /// <summary>
        /// The first major line with a free cell among the first <paramref name="minorCount"/> minor positions. Lines
        /// only fill up, so the search resumes where the last one stopped.
        /// </summary>
        public int FirstLineWithFreeCell(int minorCount)
        {
            while (_firstLineWithFreeCell < _lines.Count && IsFull(_lines[_firstLineWithFreeCell], minorCount))
                _firstLineWithFreeCell++;
            return _firstLineWithFreeCell;
        }

        private static bool IsFull(bool[]? line, int minorCount)
        {
            if (line == null || line.Length < minorCount) return false;
            for (int c = 0; c < minorCount; c++)
            {
                if (!line[c]) return false;
            }
            return true;
        }
    }
}
