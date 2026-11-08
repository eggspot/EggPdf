using System;
using System.Collections.Generic;
using System.Globalization;
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
            int.TryParse(countStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cc) && cc > 0)
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
        float containerX, float containerY) =>
        DistributeIntoColumns(children, columnCount, columnWidth, columnGap, containerX, containerY, float.PositiveInfinity);

    /// <summary>
    /// Distribute children across columns no taller than <paramref name="maxColumnHeight"/> (a page's content
    /// height): when balanced columns would be taller, columns are filled to that height in sets stacked below each
    /// other, and only the last set is balanced. A child taller than the limit stays whole in its column.
    /// </summary>
    public static List<LayoutBox> DistributeIntoColumns(
        List<LayoutBox> children, int columnCount, float columnWidth, float columnGap,
        float containerX, float containerY, float maxColumnHeight)
    {
        if (columnCount <= 1 || children.Count == 0)
            return children;

        var columns = new List<LayoutBox>();
        int childIdx = 0;
        float setY = containerY;
        while (childIdx < children.Count)
        {
            float remaining = 0;
            for (int i = childIdx; i < children.Count; i++)
                remaining += children[i].Height + children[i].MarginTop + children[i].MarginBottom;
            bool lastSet = remaining / columnCount <= maxColumnHeight;
            float targetHeight = lastSet ? remaining / columnCount : maxColumnHeight;
            float setHeight = FillColumnSet(children, ref childIdx, columns, columnCount, columnWidth, columnGap,
                containerX, setY, targetHeight, lastSet);
            setY += setHeight;
        }
        return columns;
    }

    private static float FillColumnSet(List<LayoutBox> children, ref int childIdx, List<LayoutBox> columns,
        int columnCount, float columnWidth, float columnGap, float containerX, float setY, float targetHeight, bool lastSet)
    {
        float setHeight = 0;
        for (int col = 0; col < columnCount && childIdx < children.Count; col++)
        {
            float colX = containerX + col * (columnWidth + columnGap);
            float colY = setY;
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

                if (currentHeight > 0 && currentHeight + childHeight > targetHeight &&
                    (col < columnCount - 1 || !lastSet))
                    break;

                ShiftSubtreeX(child, colX + child.MarginLeft - child.X);
                child.Y = currentHeight + child.MarginTop;

                columnBox.Children.Add(child);
                currentHeight += childHeight;
                childIdx++;
            }

            columnBox.Height = currentHeight;
            columnBox.ContentHeight = currentHeight;
            columns.Add(columnBox);
            setHeight = Math.Max(setHeight, currentHeight);
        }

        return setHeight;
    }

    /// <summary>
    /// Shift a box and its descendants horizontally by dx. An absolutely positioned
    /// descendant moves only if its containing block is inside the shifted subtree
    /// (a positioned ancestor at or below <paramref name="box"/>); one anchored to the
    /// page root keeps its coordinates. fixed-positioned boxes never move.
    /// </summary>
    internal static void ShiftSubtreeX(LayoutBox box, float dx)
    {
        if (Math.Abs(dx) > 0.01f)
            ShiftSubtreeX(box, dx, insidePositioned: false);
    }

    private static void ShiftSubtreeX(LayoutBox box, float dx, bool insidePositioned)
    {
        box.X += dx;
        var position = box.Style.Get("position");
        bool positioned = insidePositioned ||
            (position != null && position != "static");

        foreach (var child in box.Children)
        {
            if (child.IsAbsolutelyPositioned)
            {
                if (!positioned || child.Style.Get("position") == "fixed")
                    continue;
                ShiftSubtreeX(child, dx, insidePositioned: true);
            }
            else
            {
                ShiftSubtreeX(child, dx, positioned);
            }
        }
    }
}
