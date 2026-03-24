using System;
using System.Collections.Generic;
using EggPdf.Css;

namespace EggPdf.Layout;

/// <summary>
/// CSS Multi-column layout engine (column-count, column-width, column-gap, column-rule).
/// Splits child content across multiple columns within a container.
/// </summary>
public static class MultiColumnLayout
{
    /// <summary>
    /// Check if a box uses multi-column layout.
    /// </summary>
    public static bool IsMultiColumn(ComputedStyle style)
    {
        var columnCount = style?.Get("column-count");
        var columnWidth = style?.Get("column-width");
        return (!string.IsNullOrEmpty(columnCount) && columnCount != "auto") ||
               (!string.IsNullOrEmpty(columnWidth) && columnWidth != "auto");
    }

    /// <summary>
    /// Resolve column count and width from CSS properties.
    /// </summary>
    public static (int columnCount, float columnWidth, float columnGap) ResolveColumns(
        ComputedStyle style, float containerWidth, float fontSize)
    {
        var countStr = style.Get("column-count");
        var widthStr = style.Get("column-width");
        var gapStr = style.Get("column-gap") ?? style.Get("gap");

        float columnGap = 16; // default 1em
        if (!string.IsNullOrEmpty(gapStr) && gapStr != "normal")
            columnGap = BlockLayout.ResolveLength(gapStr, containerWidth, fontSize);

        int columnCount = 1;
        float columnWidth = containerWidth;

        if (!string.IsNullOrEmpty(countStr) && countStr != "auto" &&
            int.TryParse(countStr, out int cc) && cc > 0)
        {
            columnCount = cc;
        }

        if (!string.IsNullOrEmpty(widthStr) && widthStr != "auto")
        {
            float cw = BlockLayout.ResolveLength(widthStr, containerWidth, fontSize);
            if (cw > 0)
            {
                // If both count and width specified, count wins but width constrains
                int maxCols = (int)Math.Floor((containerWidth + columnGap) / (cw + columnGap));
                if (maxCols < 1) maxCols = 1;
                if (columnCount == 1 || maxCols < columnCount)
                    columnCount = maxCols;
            }
        }

        if (columnCount < 1) columnCount = 1;

        // Calculate actual column width
        columnWidth = (containerWidth - (columnCount - 1) * columnGap) / columnCount;
        if (columnWidth < 0) columnWidth = containerWidth;

        return (columnCount, columnWidth, columnGap);
    }

    /// <summary>
    /// Distribute children across columns. Returns column boxes positioned side by side.
    /// Each column contains a subset of children that fit within it.
    /// </summary>
    public static List<LayoutBox> DistributeIntoColumns(
        List<LayoutBox> children, int columnCount, float columnWidth, float columnGap,
        float containerX, float containerY)
    {
        if (columnCount <= 1 || children.Count == 0)
            return children;

        // Calculate total content height
        float totalHeight = 0;
        foreach (var child in children)
            totalHeight += child.Height + child.MarginTop + child.MarginBottom;

        // Target height per column (balanced distribution)
        float targetHeight = totalHeight / columnCount;

        var columns = new List<LayoutBox>();
        int childIdx = 0;

        for (int col = 0; col < columnCount && childIdx < children.Count; col++)
        {
            float colX = containerX + col * (columnWidth + columnGap);
            float colY = containerY;
            float currentHeight = 0;

            var columnBox = new LayoutBox
            {
                X = colX,
                Y = colY,
                Width = columnWidth,
                ContentWidth = columnWidth,
            };

            // Add children to this column until it's "full"
            while (childIdx < children.Count)
            {
                var child = children[childIdx];
                float childHeight = child.Height + child.MarginTop + child.MarginBottom;

                // Always add at least one child per column
                if (currentHeight > 0 && currentHeight + childHeight > targetHeight &&
                    col < columnCount - 1) // last column gets everything remaining
                    break;

                // Reposition child within this column
                var repositioned = child;
                repositioned.X = colX + child.MarginLeft;
                repositioned.Y = colY + currentHeight + child.MarginTop;

                columnBox.Children.Add(repositioned);
                currentHeight += childHeight;
                childIdx++;
            }

            columnBox.Height = currentHeight;
            columnBox.ContentHeight = currentHeight;
            columns.Add(columnBox);
        }

        return columns;
    }
}
