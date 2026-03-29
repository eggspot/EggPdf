using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

/// <summary>
/// Flexbox layout engine implementing CSS Flexible Box Layout Module Level 1.
/// Handles flex-direction, flex-wrap, flex-grow, flex-shrink, flex-basis,
/// justify-content, align-items, align-self, align-content, gap, and order.
/// </summary>
public static class FlexLayout
{
    private const float DefaultFontSize = 16f;

    /// <summary>
    /// Lay out children of a flex container according to the flexbox algorithm.
    /// The container's box model (Width, ContentWidth, Padding, Margin) must already be computed.
    /// This method populates container.Children with properly positioned child LayoutBoxes.
    /// </summary>
    public static void LayoutFlex(LayoutBox container, HtmlElement element, ComputedStyle style,
        float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        // Read flex container properties
        string flexDirection = style.Get("flex-direction") ?? "row";
        string flexWrap = style.Get("flex-wrap") ?? "nowrap";
        string justifyContent = style.Get("justify-content") ?? "flex-start";
        string alignItems = style.Get("align-items") ?? "stretch";
        string alignContent = style.Get("align-content") ?? "stretch";

        bool isRow = flexDirection == "row" || flexDirection == "row-reverse";
        bool isReverse = flexDirection == "row-reverse" || flexDirection == "column-reverse";
        bool isWrap = flexWrap == "wrap" || flexWrap == "wrap-reverse";
        bool isWrapReverse = flexWrap == "wrap-reverse";

        // Resolve gap
        float parentFontSize = parentStyle != null ? BlockLayout.ResolveFontSize(parentStyle.FontSize, DefaultFontSize) : DefaultFontSize;
        float fontSize = BlockLayout.ResolveFontSize(style.FontSize, parentFontSize);

        float mainGap = ResolveGap(style, isRow, container.ContentWidth, fontSize);
        float crossGap = ResolveGap(style, !isRow, container.ContentWidth, fontSize);

        float mainSize = isRow ? container.ContentWidth : GetContainerCrossSize(container, style, isRow, fontSize);
        float crossSize = isRow ? GetContainerCrossSize(container, style, isRow, fontSize) : container.ContentWidth;

        // Collect flex items
        var items = CollectFlexItems(element, style, container, containingWidth, resolver, isRow, fontSize);

        if (items.Count == 0) return;

        // Sort by order property
        SortByOrder(items);

        // Collect items into flex lines
        var lines = CollectFlexLines(items, mainSize, mainGap, isWrap, isRow);

        // Resolve flexible lengths for each line
        for (int li = 0; li < lines.Count; li++)
        {
            ResolveFlexibleLengths(lines[li], mainSize, mainGap, isRow);
        }

        // Determine cross sizes for each line
        DetermineLineCrossSizes(lines, crossSize, crossGap, alignItems, isRow);

        // Align content (multi-line)
        float totalLinesCross = 0;
        for (int li = 0; li < lines.Count; li++)
        {
            totalLinesCross += lines[li].CrossSize;
            if (li < lines.Count - 1)
                totalLinesCross += crossGap;
        }

        float[] lineOffsets = ComputeAlignContentOffsets(lines, crossSize, crossGap, totalLinesCross, alignContent);

        // Position items
        for (int li = 0; li < lines.Count; li++)
        {
            int lineIdx = isWrapReverse ? lines.Count - 1 - li : li;
            var line = lines[lineIdx];

            float lineCrossOffset = lineOffsets[lineIdx];

            // Main axis alignment
            PositionMainAxis(line, mainSize, mainGap, justifyContent, isRow, isReverse, container);

            // Cross axis alignment
            PositionCrossAxis(line, lineCrossOffset, line.CrossSize, alignItems, isRow, container);
        }

        // Add children to container
        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            for (int i = 0; i < line.Items.Count; i++)
            {
                container.Children.Add(line.Items[i].Box);
            }
        }
    }

    /// <summary>Resolve gap value for main or cross axis.</summary>
    private static float ResolveGap(ComputedStyle style, bool isMainRow, float containingWidth, float fontSize)
    {
        // Check specific gap property first
        string? gapValue = isMainRow ? style.Get("column-gap") : style.Get("row-gap");

        // Fall back to shorthand gap
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
                    gapValue = isMainRow ? parts[1] : parts[0];
                }
            }
        }

        return BlockLayout.ResolveLength(gapValue, containingWidth, fontSize);
    }

    /// <summary>Get container's cross size (height for row, width for column).</summary>
    private static float GetContainerCrossSize(LayoutBox container, ComputedStyle style, bool isRow, float fontSize)
    {
        if (isRow)
        {
            // Cross axis is vertical; check for explicit height
            var h = BlockLayout.ResolveOptionalLength(style.Height, 0, fontSize);
            return h ?? 0; // 0 means auto (will expand to fit content)
        }
        else
        {
            // Cross axis is horizontal; use content width
            return container.ContentWidth;
        }
    }

    /// <summary>Collect flex items from child elements.</summary>
    private static List<FlexItem> CollectFlexItems(HtmlElement element, ComputedStyle containerStyle,
        LayoutBox container, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        bool isRow, float fontSize)
    {
        var items = new List<FlexItem>();

        for (int i = 0; i < element.ChildNodes.Count; i++)
        {
            var childNode = element.ChildNodes[i];
            if (!(childNode is HtmlElement childElem))
                continue;

            var childStyle = resolver(childElem, containerStyle);
            if (childStyle.Display == "none")
                continue;

            // Read flex item properties
            float flexGrow = ParseFloatSafe(childStyle.Get("flex-grow"), 0);
            float flexShrink = ParseFloatSafe(childStyle.Get("flex-shrink"), 1);
            string? flexBasis = childStyle.Get("flex-basis");
            int order = ParseIntSafe(childStyle.Get("order"), 0);
            string? alignSelf = childStyle.Get("align-self");

            // Determine if we need content-based sizing
            bool hasExplicitMainSize;
            if (!string.IsNullOrEmpty(flexBasis) && flexBasis != "auto")
            {
                hasExplicitMainSize = true;
            }
            else if (isRow)
            {
                hasExplicitMainSize = !string.IsNullOrEmpty(childStyle.Width) && childStyle.Width != "auto";
            }
            else
            {
                hasExplicitMainSize = !string.IsNullOrEmpty(childStyle.Height) && childStyle.Height != "auto";
            }

            // Create child box using BlockLayout
            var childBox = BlockLayout.CreateBox(childElem, childStyle, container,
                containingWidth, resolver, containerStyle);

            // Determine base size
            float baseSize;
            if (!string.IsNullOrEmpty(flexBasis) && flexBasis != "auto")
            {
                // flex-basis specified
                float? resolved = BlockLayout.ResolveOptionalLength(flexBasis, isRow ? container.ContentWidth : 0, fontSize);
                baseSize = resolved ?? (isRow ? childBox.Width : childBox.Height);
            }
            else if (hasExplicitMainSize)
            {
                // Explicit width/height
                if (isRow)
                {
                    baseSize = BlockLayout.ResolveOptionalLength(childStyle.Width, container.ContentWidth, fontSize) ?? childBox.Width;
                }
                else
                {
                    baseSize = BlockLayout.ResolveOptionalLength(childStyle.Height, 0, fontSize) ?? childBox.Height;
                }
            }
            else
            {
                // Auto: use content-based size
                if (isRow)
                {
                    // Use the measured content width from child boxes
                    // Text boxes have ContentWidth = measured text width (not full container width)
                    float contentBasedWidth = 0;
                    for (int ci = 0; ci < childBox.Children.Count; ci++)
                    {
                        float cw = childBox.Children[ci].ContentWidth;
                        if (cw > contentBasedWidth) contentBasedWidth = cw;
                    }
                    // If no children, measure the element's own text content
                    if (contentBasedWidth <= 0 && childBox.ContentWidth < childBox.Width)
                        contentBasedWidth = childBox.ContentWidth;
                    // Include padding
                    baseSize = contentBasedWidth + childBox.PaddingLeft + childBox.PaddingRight;
                }
                else
                {
                    baseSize = childBox.Height;
                }
            }

            // Read min/max constraints
            float? minMain, maxMain;
            if (isRow)
            {
                minMain = BlockLayout.ResolveOptionalLength(childStyle.Get("min-width"), container.ContentWidth, fontSize);
                maxMain = BlockLayout.ResolveOptionalLength(childStyle.Get("max-width"), container.ContentWidth, fontSize);
            }
            else
            {
                minMain = BlockLayout.ResolveOptionalLength(childStyle.Get("min-height"), 0, fontSize);
                maxMain = BlockLayout.ResolveOptionalLength(childStyle.Get("max-height"), 0, fontSize);
            }

            // Clamp base size
            if (minMain.HasValue && baseSize < minMain.Value)
                baseSize = minMain.Value;
            if (maxMain.HasValue && baseSize > maxMain.Value)
                baseSize = maxMain.Value;

            items.Add(new FlexItem
            {
                Box = childBox,
                Style = childStyle,
                FlexGrow = flexGrow,
                FlexShrink = flexShrink,
                BaseSize = baseSize,
                HypotheticalMainSize = baseSize,
                Order = order,
                AlignSelf = alignSelf,
                MinMain = minMain ?? 0,
                MaxMain = maxMain ?? float.MaxValue,
                SourceIndex = i
            });
        }

        return items;
    }

    /// <summary>Sort items by order property (stable sort preserving source order for equal orders).</summary>
    private static void SortByOrder(List<FlexItem> items)
    {
        // Simple insertion sort for stability (no LINQ)
        for (int i = 1; i < items.Count; i++)
        {
            var key = items[i];
            int j = i - 1;
            while (j >= 0 && (items[j].Order > key.Order ||
                (items[j].Order == key.Order && items[j].SourceIndex > key.SourceIndex)))
            {
                items[j + 1] = items[j];
                j--;
            }
            items[j + 1] = key;
        }
    }

    /// <summary>Collect items into flex lines.</summary>
    private static List<FlexLine> CollectFlexLines(List<FlexItem> items, float mainSize, float mainGap, bool isWrap, bool isRow)
    {
        var lines = new List<FlexLine>();

        if (!isWrap)
        {
            // Single line: all items
            var line = new FlexLine();
            for (int i = 0; i < items.Count; i++)
                line.Items.Add(items[i]);
            lines.Add(line);
            return lines;
        }

        // Multi-line: wrap when items exceed main size
        var currentLine = new FlexLine();
        float usedMain = 0;

        for (int i = 0; i < items.Count; i++)
        {
            float itemSize = items[i].HypotheticalMainSize;
            float gapBefore = currentLine.Items.Count > 0 ? mainGap : 0;

            if (currentLine.Items.Count > 0 && usedMain + gapBefore + itemSize > mainSize)
            {
                // Start new line
                lines.Add(currentLine);
                currentLine = new FlexLine();
                usedMain = 0;
                gapBefore = 0;
            }

            currentLine.Items.Add(items[i]);
            usedMain += gapBefore + itemSize;
        }

        if (currentLine.Items.Count > 0)
            lines.Add(currentLine);

        return lines;
    }

    /// <summary>Resolve flexible lengths (flex-grow / flex-shrink) for a single line.</summary>
    private static void ResolveFlexibleLengths(FlexLine line, float mainSize, float mainGap, bool isRow)
    {
        float totalGap = line.Items.Count > 1 ? mainGap * (line.Items.Count - 1) : 0;
        float availableMain = mainSize - totalGap;

        // Sum of hypothetical main sizes
        float totalHypothetical = 0;
        for (int i = 0; i < line.Items.Count; i++)
            totalHypothetical += line.Items[i].HypotheticalMainSize;

        float freeSpace = availableMain - totalHypothetical;

        if (freeSpace > 0)
        {
            // Positive free space: distribute via flex-grow
            float totalGrow = 0;
            for (int i = 0; i < line.Items.Count; i++)
                totalGrow += line.Items[i].FlexGrow;

            for (int i = 0; i < line.Items.Count; i++)
            {
                var item = line.Items[i];
                if (totalGrow > 0 && item.FlexGrow > 0)
                {
                    float extraSpace = freeSpace * (item.FlexGrow / totalGrow);
                    item.MainSize = item.HypotheticalMainSize + extraSpace;
                }
                else
                {
                    item.MainSize = item.HypotheticalMainSize;
                }

                // Clamp to min/max
                if (item.MainSize < item.MinMain) item.MainSize = item.MinMain;
                if (item.MainSize > item.MaxMain) item.MainSize = item.MaxMain;
            }
        }
        else if (freeSpace < 0)
        {
            // Negative free space: absorb via flex-shrink
            float totalShrinkWeighted = 0;
            for (int i = 0; i < line.Items.Count; i++)
                totalShrinkWeighted += line.Items[i].FlexShrink * line.Items[i].HypotheticalMainSize;

            for (int i = 0; i < line.Items.Count; i++)
            {
                var item = line.Items[i];
                if (totalShrinkWeighted > 0 && item.FlexShrink > 0)
                {
                    float shrinkRatio = (item.FlexShrink * item.HypotheticalMainSize) / totalShrinkWeighted;
                    float reduction = Math.Abs(freeSpace) * shrinkRatio;
                    item.MainSize = item.HypotheticalMainSize - reduction;
                }
                else
                {
                    item.MainSize = item.HypotheticalMainSize;
                }

                // Clamp to min/max
                if (item.MainSize < item.MinMain) item.MainSize = item.MinMain;
                if (item.MainSize > item.MaxMain) item.MainSize = item.MaxMain;
            }
        }
        else
        {
            // No free space
            for (int i = 0; i < line.Items.Count; i++)
                line.Items[i].MainSize = line.Items[i].HypotheticalMainSize;
        }

        // Apply main sizes to boxes
        for (int i = 0; i < line.Items.Count; i++)
        {
            var item = line.Items[i];
            if (isRow)
            {
                item.Box.Width = item.MainSize;
                item.Box.ContentWidth = item.MainSize - item.Box.PaddingLeft - item.Box.PaddingRight;
                if (item.Box.ContentWidth < 0) item.Box.ContentWidth = 0;
            }
            else
            {
                item.Box.Height = item.MainSize;
                item.Box.ContentHeight = item.MainSize - item.Box.PaddingTop - item.Box.PaddingBottom;
                if (item.Box.ContentHeight < 0) item.Box.ContentHeight = 0;
            }
        }
    }

    /// <summary>Determine cross size of each flex line.</summary>
    private static void DetermineLineCrossSizes(List<FlexLine> lines, float crossSize, float crossGap,
        string alignItems, bool isRow)
    {
        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            float maxCross = 0;

            for (int i = 0; i < line.Items.Count; i++)
            {
                var item = line.Items[i];
                float itemCross = isRow
                    ? item.Box.Height + item.Box.MarginTop + item.Box.MarginBottom
                    : item.Box.Width + item.Box.MarginLeft + item.Box.MarginRight;

                if (itemCross > maxCross)
                    maxCross = itemCross;
            }

            line.CrossSize = maxCross;
        }

        // For single-line flex container with explicit cross size, use that
        if (lines.Count == 1 && crossSize > 0 && crossSize > lines[0].CrossSize)
        {
            lines[0].CrossSize = crossSize;
        }
    }

    /// <summary>Compute cross-axis offsets for each line based on align-content.</summary>
    private static float[] ComputeAlignContentOffsets(List<FlexLine> lines, float crossSize,
        float crossGap, float totalLinesCross, string alignContent)
    {
        var offsets = new float[lines.Count];

        // If cross size is auto (0), align-content has no effect
        if (crossSize <= 0 || lines.Count == 0)
        {
            float offset = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                offsets[i] = offset;
                offset += lines[i].CrossSize + crossGap;
            }
            return offsets;
        }

        float freeSpace = crossSize - totalLinesCross;
        if (freeSpace < 0) freeSpace = 0;

        switch (alignContent)
        {
            case "flex-start":
            case "start":
            {
                float offset = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    offsets[i] = offset;
                    offset += lines[i].CrossSize + crossGap;
                }
                break;
            }
            case "flex-end":
            case "end":
            {
                float offset = freeSpace;
                for (int i = 0; i < lines.Count; i++)
                {
                    offsets[i] = offset;
                    offset += lines[i].CrossSize + crossGap;
                }
                break;
            }
            case "center":
            {
                float offset = freeSpace / 2;
                for (int i = 0; i < lines.Count; i++)
                {
                    offsets[i] = offset;
                    offset += lines[i].CrossSize + crossGap;
                }
                break;
            }
            case "space-between":
            {
                float gap = lines.Count > 1 ? freeSpace / (lines.Count - 1) : 0;
                float offset = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    offsets[i] = offset;
                    offset += lines[i].CrossSize + gap;
                }
                break;
            }
            case "space-around":
            {
                float gap = lines.Count > 0 ? freeSpace / lines.Count : 0;
                float offset = gap / 2;
                for (int i = 0; i < lines.Count; i++)
                {
                    offsets[i] = offset;
                    offset += lines[i].CrossSize + gap;
                }
                break;
            }
            case "space-evenly":
            {
                float gap = lines.Count > 0 ? freeSpace / (lines.Count + 1) : 0;
                float offset = gap;
                for (int i = 0; i < lines.Count; i++)
                {
                    offsets[i] = offset;
                    offset += lines[i].CrossSize + gap;
                }
                break;
            }
            case "stretch":
            default:
            {
                // Distribute extra space equally to each line
                float extraPerLine = lines.Count > 0 ? freeSpace / lines.Count : 0;
                float offset = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    lines[i].CrossSize += extraPerLine;
                    offsets[i] = offset;
                    offset += lines[i].CrossSize + crossGap;
                }
                break;
            }
        }

        return offsets;
    }

    /// <summary>Position items along the main axis.</summary>
    private static void PositionMainAxis(FlexLine line, float mainSize, float mainGap,
        string justifyContent, bool isRow, bool isReverse, LayoutBox container)
    {
        float totalGap = line.Items.Count > 1 ? mainGap * (line.Items.Count - 1) : 0;
        float totalItemMain = 0;
        for (int i = 0; i < line.Items.Count; i++)
        {
            totalItemMain += line.Items[i].MainSize;
            if (isRow)
                totalItemMain += line.Items[i].Box.MarginLeft + line.Items[i].Box.MarginRight;
            else
                totalItemMain += line.Items[i].Box.MarginTop + line.Items[i].Box.MarginBottom;
        }

        float freeSpace = mainSize - totalItemMain - totalGap;
        if (freeSpace < 0) freeSpace = 0;

        // Compute initial offset and spacing based on justify-content
        float mainOffset;
        float extraGap;
        ComputeJustifyContent(justifyContent, freeSpace, line.Items.Count, mainGap, out mainOffset, out extraGap);

        // Apply positions
        float pos = mainOffset;
        int count = line.Items.Count;
        for (int idx = 0; idx < count; idx++)
        {
            int i = isReverse ? count - 1 - idx : idx;
            var item = line.Items[i];

            if (isRow)
            {
                pos += item.Box.MarginLeft;
                float newX = container.X + container.PaddingLeft + pos;
                float deltaX = newX - item.Box.X;
                item.Box.X = newX;
                // Update children positions to follow the parent
                if (Math.Abs(deltaX) > 0.01f)
                    OffsetChildren(item.Box, deltaX, 0);
                pos += item.MainSize + item.Box.MarginRight;
            }
            else
            {
                pos += item.Box.MarginTop;
                float newY = container.Y + container.PaddingTop + pos;
                float deltaY = newY - item.Box.Y;
                item.Box.Y = newY;
                if (Math.Abs(deltaY) > 0.01f)
                    OffsetChildren(item.Box, 0, deltaY);
                pos += item.MainSize + item.Box.MarginBottom;
            }

            // Add gap between items (not after last)
            if (idx < count - 1)
                pos += extraGap;
        }
    }

    /// <summary>Compute justify-content offset and gap.</summary>
    private static void ComputeJustifyContent(string justifyContent, float freeSpace, int itemCount,
        float mainGap, out float mainOffset, out float extraGap)
    {
        extraGap = mainGap;
        mainOffset = 0;

        switch (justifyContent)
        {
            case "flex-start":
            case "start":
                mainOffset = 0;
                break;

            case "flex-end":
            case "end":
                mainOffset = freeSpace;
                break;

            case "center":
                mainOffset = freeSpace / 2;
                break;

            case "space-between":
                mainOffset = 0;
                if (itemCount > 1)
                    extraGap = mainGap + freeSpace / (itemCount - 1);
                break;

            case "space-around":
                if (itemCount > 0)
                {
                    float perItem = freeSpace / itemCount;
                    mainOffset = perItem / 2;
                    extraGap = mainGap + perItem;
                }
                break;

            case "space-evenly":
                if (itemCount > 0)
                {
                    float gap = freeSpace / (itemCount + 1);
                    mainOffset = gap;
                    extraGap = mainGap + gap;
                }
                break;

            default:
                mainOffset = 0;
                break;
        }
    }

    /// <summary>Position items along the cross axis.</summary>
    private static void PositionCrossAxis(FlexLine line, float lineCrossOffset, float lineCrossSize,
        string alignItems, bool isRow, LayoutBox container)
    {
        for (int i = 0; i < line.Items.Count; i++)
        {
            var item = line.Items[i];
            string alignment = item.AlignSelf ?? alignItems;

            float itemCross;
            float itemMarginBefore;
            float itemMarginAfter;

            if (isRow)
            {
                itemCross = item.Box.Height;
                itemMarginBefore = item.Box.MarginTop;
                itemMarginAfter = item.Box.MarginBottom;
            }
            else
            {
                itemCross = item.Box.Width;
                itemMarginBefore = item.Box.MarginLeft;
                itemMarginAfter = item.Box.MarginRight;
            }

            float totalItemCross = itemCross + itemMarginBefore + itemMarginAfter;

            float crossPos;
            switch (alignment)
            {
                case "flex-start":
                case "start":
                    crossPos = lineCrossOffset + itemMarginBefore;
                    break;

                case "flex-end":
                case "end":
                    crossPos = lineCrossOffset + lineCrossSize - itemCross - itemMarginAfter;
                    break;

                case "center":
                    crossPos = lineCrossOffset + (lineCrossSize - totalItemCross) / 2 + itemMarginBefore;
                    break;

                case "baseline":
                    // Simplified baseline: treat as flex-start
                    crossPos = lineCrossOffset + itemMarginBefore;
                    break;

                case "stretch":
                default:
                    crossPos = lineCrossOffset + itemMarginBefore;

                    // Stretch: expand item to fill cross size if no explicit cross size
                    float stretchSize = lineCrossSize - itemMarginBefore - itemMarginAfter;
                    if (stretchSize > itemCross)
                    {
                        bool hasExplicitCross;
                        if (isRow)
                        {
                            hasExplicitCross = !string.IsNullOrEmpty(item.Style.Height) && item.Style.Height != "auto";
                        }
                        else
                        {
                            hasExplicitCross = !string.IsNullOrEmpty(item.Style.Width) && item.Style.Width != "auto";
                        }

                        if (!hasExplicitCross)
                        {
                            if (isRow)
                            {
                                item.Box.Height = stretchSize;
                                item.Box.ContentHeight = stretchSize - item.Box.PaddingTop - item.Box.PaddingBottom;
                                if (item.Box.ContentHeight < 0) item.Box.ContentHeight = 0;
                            }
                            else
                            {
                                item.Box.Width = stretchSize;
                                item.Box.ContentWidth = stretchSize - item.Box.PaddingLeft - item.Box.PaddingRight;
                                if (item.Box.ContentWidth < 0) item.Box.ContentWidth = 0;
                            }
                        }
                    }
                    break;
            }

            if (isRow)
            {
                float newY = container.Y + container.PaddingTop + crossPos;
                float deltaY = newY - item.Box.Y;
                item.Box.Y = newY;
                if (Math.Abs(deltaY) > 0.01f)
                    OffsetChildren(item.Box, 0, deltaY);
            }
            else
            {
                float newX = container.X + container.PaddingLeft + crossPos;
                float deltaX = newX - item.Box.X;
                item.Box.X = newX;
                if (Math.Abs(deltaX) > 0.01f)
                    OffsetChildren(item.Box, deltaX, 0);
            }
        }
    }

    /// <summary>Recursively offset all children by delta X/Y when parent is repositioned.</summary>
    private static void OffsetChildren(LayoutBox box, float dx, float dy)
    {
        for (int i = 0; i < box.Children.Count; i++)
        {
            var child = box.Children[i];
            child.X += dx;
            child.Y += dy;
            OffsetChildren(child, dx, dy);
        }
    }

    /// <summary>Measure intrinsic content width for content-based sizing.</summary>
    private static float MeasureContentWidth(HtmlElement element, ComputedStyle style,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, float fontSize)
    {
        float maxWidth = 0;

        for (int i = 0; i < element.ChildNodes.Count; i++)
        {
            var child = element.ChildNodes[i];
            if (child is HtmlTextNode textNode)
            {
                var text = textNode.Data.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                float textWidth = TextMeasurer.MeasureWidth(text, fontSize,
                    style.FontFamily, style.FontWeight, style.Get("font-style"));
                if (textWidth > maxWidth) maxWidth = textWidth;
            }
            else if (child is HtmlElement childElem)
            {
                var childStyle = resolver(childElem, style);
                if (childStyle.Display == "none") continue;

                // Check for explicit width
                var explicitWidth = BlockLayout.ResolveOptionalLength(childStyle.Width, 0, fontSize);
                if (explicitWidth.HasValue)
                {
                    if (explicitWidth.Value > maxWidth) maxWidth = explicitWidth.Value;
                }
                else
                {
                    float childFontSize = BlockLayout.ResolveFontSize(childStyle.FontSize, fontSize);
                    float childContentWidth = MeasureContentWidth(childElem, childStyle, resolver, childFontSize);
                    if (childContentWidth > maxWidth) maxWidth = childContentWidth;
                }
            }
        }

        return maxWidth;
    }

    private static float ParseFloatSafe(string? value, float defaultValue)
    {
        if (string.IsNullOrEmpty(value))
            return defaultValue;
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            return result;
        return defaultValue;
    }

    private static int ParseIntSafe(string? value, int defaultValue)
    {
        if (string.IsNullOrEmpty(value))
            return defaultValue;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            return result;
        return defaultValue;
    }

    /// <summary>A flex item with its computed flex properties.</summary>
    private sealed class FlexItem
    {
        public LayoutBox Box = null!;
        public ComputedStyle Style = null!;
        public float FlexGrow;
        public float FlexShrink;
        public float BaseSize;
        public float HypotheticalMainSize;
        public float MainSize;
        public int Order;
        public string? AlignSelf;
        public float MinMain;
        public float MaxMain;
        public int SourceIndex;
    }

    /// <summary>A flex line containing one or more flex items.</summary>
    private sealed class FlexLine
    {
        public List<FlexItem> Items = new List<FlexItem>();
        public float CrossSize;
    }
}
