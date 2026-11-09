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
public static partial class FlexLayout
{
    private const float DefaultFontSize = 16f;
    private const float ApproximateAscentEm = 0.8f;

    /// <summary>
    /// Lay out children of a flex container according to the flexbox algorithm.
    /// The container's box model (Width, ContentWidth, Padding, Margin) must already be computed.
    /// This method populates container.Children with properly positioned child LayoutBoxes.
    /// </summary>
    public static void LayoutFlex(LayoutBox container, HtmlElement element, ComputedStyle style,
        float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        string flexDirection = style.Get("flex-direction") ?? "row";
        string flexWrap = style.Get("flex-wrap") ?? "nowrap";
        string justifyContent = style.Get("justify-content") ?? "flex-start";
        string alignItems = style.Get("align-items") ?? "stretch";
        string alignContent = style.Get("align-content") ?? "stretch";

        bool isRow = flexDirection == "row" || flexDirection == "row-reverse";
        bool isReverse = flexDirection == "row-reverse" || flexDirection == "column-reverse";
        bool isWrap = flexWrap == "wrap" || flexWrap == "wrap-reverse";
        bool isWrapReverse = flexWrap == "wrap-reverse";

        float parentFontSize = parentStyle != null ? BlockLayout.ResolveFontSize(parentStyle.FontSize, DefaultFontSize) : DefaultFontSize;
        float fontSize = BlockLayout.ResolveFontSize(style.FontSize, parentFontSize);

        float mainGap = ResolveGap(style, isRow, container.ContentWidth, fontSize);
        float crossGap = ResolveGap(style, !isRow, container.ContentWidth, fontSize);

        float mainSize = isRow ? container.ContentWidth : GetColumnMainSize(container, style, fontSize);
        float crossSize = isRow ? GetContainerCrossSize(container, style, isRow, fontSize) : container.ContentWidth;

        var items = CollectFlexItems(element, style, container, containingWidth, resolver, isRow, fontSize);

        if (items.Count == 0) return;

        SortByOrder(items);

        bool mainIndefinite = !isRow && mainSize <= 0;

        var lines = CollectFlexLines(items, mainIndefinite ? float.MaxValue : mainSize, mainGap, isWrap, isRow);

        for (int li = 0; li < lines.Count; li++)
        {
            float lineMain = mainIndefinite ? SumLineBaseMain(lines[li], mainGap, isRow) : mainSize;
            ResolveFlexibleLengths(lines[li], lineMain, mainGap, isRow);
        }

        if (isRow)
        {
            for (int li = 0; li < lines.Count; li++)
                RelayoutRowItemsIfResized(lines[li], container, resolver, style);
        }

        DetermineLineCrossSizes(lines, crossSize, crossGap, alignItems, isRow);

        float totalLinesCross = 0;
        for (int li = 0; li < lines.Count; li++)
        {
            totalLinesCross += lines[li].CrossSize;
            if (li < lines.Count - 1)
                totalLinesCross += crossGap;
        }

        float[] lineOffsets = ComputeAlignContentOffsets(lines, crossSize, crossGap, totalLinesCross, alignContent);

        for (int li = 0; li < lines.Count; li++)
        {
            int lineIdx = isWrapReverse ? lines.Count - 1 - li : li;
            var line = lines[lineIdx];

            float lineCrossOffset = lineOffsets[lineIdx];

            float positionMain = mainIndefinite ? SumLineBaseMain(line, mainGap, isRow) : mainSize;
            PositionMainAxis(line, positionMain, mainGap, justifyContent, isRow, isReverse, container);

            PositionCrossAxis(line, lineCrossOffset, line.CrossSize, alignItems, isRow, container);
        }

        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            for (int i = 0; i < line.Items.Count; i++)
            {
                container.Children.Add(line.Items[i].Box);
            }
        }
    }

    private static float ResolveGap(ComputedStyle style, bool isMainRow, float containingWidth, float fontSize)
    {
        string? gapValue = isMainRow ? style.Get("column-gap") : style.Get("row-gap");

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
                    gapValue = isMainRow ? parts[1] : parts[0];
                }
            }
        }

        return BlockLayout.ResolveLength(gapValue, containingWidth, fontSize);
    }

    private static float GetColumnMainSize(LayoutBox container, ComputedStyle style, float fontSize)
    {
        var h = BlockLayout.ResolveOptionalLength(style.Height, 0, fontSize)
             ?? BlockLayout.ResolveOptionalLength(style.Get("min-height"), 0, fontSize);
        if (!h.HasValue) return 0;

        if (style.Get("box-sizing") == "border-box")
        {
            float content = h.Value - container.PaddingTop - container.PaddingBottom;
            return content > 0 ? content : 0;
        }
        return h.Value;
    }

    private static float SumLineBaseMain(FlexLine line, float mainGap, bool isRow)
    {
        float total = line.Items.Count > 1 ? mainGap * (line.Items.Count - 1) : 0;
        for (int i = 0; i < line.Items.Count; i++)
        {
            var item = line.Items[i];
            total += item.HypotheticalMainSize;
            total += isRow
                ? item.Box.MarginLeft + item.Box.MarginRight
                : item.Box.MarginTop + item.Box.MarginBottom;
        }
        return total;
    }

    private const float AutoCrossSize = 0f;

    private static float GetContainerCrossSize(LayoutBox container, ComputedStyle style, bool isRow, float fontSize)
    {
        if (!isRow) return container.ContentWidth;
        return BlockLayout.ResolveOptionalLength(style.Height, 0, fontSize) ?? AutoCrossSize;
    }

    private static List<FlexItem> CollectFlexItems(HtmlElement element, ComputedStyle containerStyle,
        LayoutBox container, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        bool isRow, float fontSize)
    {
        var items = new List<FlexItem>();

        var anonLines = new List<string>();
        var anonCurrent = new System.Text.StringBuilder();

        void FlushAnonymousItem()
        {
            if (anonCurrent.Length > 0)
            {
                anonLines.Add(anonCurrent.ToString());
                anonCurrent.Clear();
            }
            bool hasContent = false;
            for (int li = 0; li < anonLines.Count; li++)
                if (anonLines[li].Length > 0) { hasContent = true; break; }
            if (!hasContent) { anonLines.Clear(); return; }

            var fam = containerStyle.FontFamily;
            var wt = containerStyle.FontWeight;
            var fs = containerStyle.Get("font-style");
            float ls = BlockLayout.ResolveLength(containerStyle.Get("letter-spacing"), 0, fontSize);
            float lineH = TextMeasurer.GetLineHeight(fontSize, containerStyle.Get("line-height"));
            bool centerLines = containerStyle.Get("text-align") == "center";

            var anonStyle = AnonymousBoxStyle.From(containerStyle);

            float maxW = 0;
            for (int li = 0; li < anonLines.Count; li++)
            {
                float w = anonLines[li].Length > 0
                    ? TextMeasurer.MeasureWidth(anonLines[li], fontSize, fam, wt, fs, ls) : 0;
                if (w > maxW) maxW = w;
            }

            var anonBox = new LayoutBox { Style = anonStyle };
            float y = 0;
            for (int li = 0; li < anonLines.Count; li++)
            {
                var line = anonLines[li];
                float w = line.Length > 0 ? TextMeasurer.MeasureWidth(line, fontSize, fam, wt, fs, ls) : 0;
                anonBox.Children.Add(new LayoutBox
                {
                    Style = anonStyle,
                    Text = line.Length > 0 ? line : null,
                    X = centerLines ? (maxW - w) / 2 : 0,
                    Y = y,
                    Width = w,
                    Height = lineH,
                    ContentWidth = w,
                    ContentHeight = lineH
                });
                y += lineH;
            }
            anonBox.Width = maxW;
            anonBox.ContentWidth = maxW;
            anonBox.Height = y;
            anonBox.ContentHeight = y;

            items.Add(new FlexItem
            {
                Box = anonBox,
                Element = null!,
                Style = anonStyle,
                FlexGrow = 0,
                FlexShrink = 1,
                BaseSize = isRow ? maxW : y,
                HypotheticalMainSize = isRow ? maxW : y,
                Order = 0,
                AlignSelf = null,
                MinMain = 0,
                MaxMain = float.MaxValue,
                SourceIndex = items.Count,
                InitialBoxWidth = maxW
            });
            anonLines.Clear();
        }

        for (int i = 0; i < element.ChildNodes.Count; i++)
        {
            var childNode = element.ChildNodes[i];

            if (childNode is HtmlTextNode textNode)
            {
                var t = textNode.Data.Trim();
                if (t.Length > 0)
                {
                    if (anonCurrent.Length > 0) anonCurrent.Append(' ');
                    anonCurrent.Append(t);
                }
                continue;
            }

            if (!(childNode is HtmlElement childElem))
                continue;

            if (childElem.TagName == "br")
            {
                anonLines.Add(anonCurrent.ToString());
                anonCurrent.Clear();
                continue;
            }

            var childStyle = resolver(childElem, containerStyle);
            if (childStyle.Display == "none")
                continue;

            var childPosition = childStyle.Get("position");
            if (childPosition == "absolute" || childPosition == "fixed")
                continue;

            FlushAnonymousItem();

            float flexGrow = ParseFloatSafe(childStyle.Get("flex-grow"), 0);
            float flexShrink = ParseFloatSafe(childStyle.Get("flex-shrink"), 1);
            string? flexBasis = childStyle.Get("flex-basis");
            int order = ParseIntSafe(childStyle.Get("order"), 0);
            string? alignSelf = childStyle.Get("align-self");

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

            var childBox = BlockLayout.CreateBox(childElem, childStyle, container,
                container.ContentWidth, resolver, containerStyle);

            float baseSize;
            if (!string.IsNullOrEmpty(flexBasis) && flexBasis != "auto")
            {
                float? resolved = BlockLayout.ResolveOptionalLength(flexBasis, isRow ? container.ContentWidth : 0, fontSize);
                baseSize = resolved ?? (isRow ? childBox.Width : childBox.Height);
            }
            else if (hasExplicitMainSize)
            {
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
                if (isRow)
                {
                    baseSize = Math.Max(GetMaxLeafContentWidth(childBox),
                        MeasureMaxContentWidth(childElem, childStyle, resolver,
                            BlockLayout.ResolveFontSize(childStyle.FontSize, fontSize)));
                }
                else
                {
                    baseSize = childBox.Height;
                }
            }
            baseSize = WithMainAxisInsets(baseSize, childElem, childStyle, childBox, isRow,
                specifiedSize: (!string.IsNullOrEmpty(flexBasis) && flexBasis != "auto") || hasExplicitMainSize);

            float? minMain, maxMain;
            if (isRow)
            {
                minMain = BlockLayout.ResolveOptionalLength(childStyle.Get("min-width"), container.ContentWidth, fontSize);
                maxMain = BlockLayout.ResolveOptionalLength(childStyle.Get("max-width"), container.ContentWidth, fontSize);
                if (!minMain.HasValue)
                    minMain = AutomaticMinimumWidth(childElem, childStyle, resolver, fontSize, hasExplicitMainSize ? baseSize : (float?)null, maxMain,
                        childBox.PaddingLeft + childBox.PaddingRight);
            }
            else
            {
                minMain = BlockLayout.ResolveOptionalLength(childStyle.Get("min-height"), 0, fontSize);
                maxMain = BlockLayout.ResolveOptionalLength(childStyle.Get("max-height"), 0, fontSize);

                if (!minMain.HasValue && !hasExplicitMainSize)
                {
                    var overflow = childStyle.Get("overflow-y") ?? childStyle.Get("overflow");
                    if (string.IsNullOrEmpty(overflow) || overflow == "visible" || overflow == "clip")
                    {
                        minMain = baseSize;
                        if (maxMain.HasValue && minMain.Value > maxMain.Value)
                            minMain = maxMain.Value;
                    }
                }
            }

            if (minMain.HasValue && baseSize < minMain.Value)
                baseSize = minMain.Value;
            if (maxMain.HasValue && baseSize > maxMain.Value)
                baseSize = maxMain.Value;

            items.Add(new FlexItem
            {
                Box = childBox,
                Element = childElem,
                Style = childStyle,
                FlexGrow = flexGrow,
                FlexShrink = flexShrink,
                BaseSize = baseSize,
                HypotheticalMainSize = baseSize,
                Order = order,
                AlignSelf = alignSelf,
                MinMain = minMain ?? 0,
                MaxMain = maxMain ?? float.MaxValue,
                SourceIndex = i,
                InitialBoxWidth = childBox.Width
            });
        }

        FlushAnonymousItem();

        return items;
    }

    private static void SortByOrder(List<FlexItem> items)
    {
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

    private static List<FlexLine> CollectFlexLines(List<FlexItem> items, float mainSize, float mainGap, bool isWrap, bool isRow)
    {
        var lines = new List<FlexLine>();

        if (!isWrap)
        {
            var line = new FlexLine();
            for (int i = 0; i < items.Count; i++)
                line.Items.Add(items[i]);
            lines.Add(line);
            return lines;
        }

        var currentLine = new FlexLine();
        float usedMain = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var box = items[i].Box;
            float itemSize = items[i].HypotheticalMainSize +
                (isRow ? box.MarginLeft + box.MarginRight : box.MarginTop + box.MarginBottom);
            float gapBefore = currentLine.Items.Count > 0 ? mainGap : 0;

            if (currentLine.Items.Count > 0 && usedMain + gapBefore + itemSize > mainSize)
            {
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

    private static void ResolveFlexibleLengths(FlexLine line, float mainSize, float mainGap, bool isRow)
    {
        float totalGap = line.Items.Count > 1 ? mainGap * (line.Items.Count - 1) : 0;
        float availableMain = mainSize - totalGap;

        float totalHypothetical = 0;
        for (int i = 0; i < line.Items.Count; i++)
        {
            var it = line.Items[i];
            totalHypothetical += it.HypotheticalMainSize;
            totalHypothetical += isRow
                ? it.Box.MarginLeft + it.Box.MarginRight
                : it.Box.MarginTop + it.Box.MarginBottom;
        }

        float freeSpace = availableMain - totalHypothetical;

        if (freeSpace > 0)
        {
            GrowItemsHonoringLimits(line.Items, freeSpace);
        }
        else if (freeSpace < 0)
        {
            ShrinkItemsHonoringMinimums(line.Items, -freeSpace);
        }
        else
        {
            for (int i = 0; i < line.Items.Count; i++)
                line.Items[i].MainSize = line.Items[i].HypotheticalMainSize;
        }

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

    private static void GrowItemsHonoringLimits(List<FlexItem> items, float freeSpace)
    {
        bool[]? frozen = null;
        for (int pass = 0; pass <= items.Count; pass++)
        {
            float totalGrow = 0f, takenByFrozen = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                if (frozen != null && frozen[i]) takenByFrozen += items[i].MainSize - items[i].HypotheticalMainSize;
                else totalGrow += items[i].FlexGrow;
            }

            float remaining = freeSpace - takenByFrozen;
            bool anyClamped = false;
            for (int i = 0; i < items.Count; i++)
            {
                if (frozen != null && frozen[i]) continue;
                var item = items[i];
                float growth = totalGrow > 0f && item.FlexGrow > 0f ? remaining * item.FlexGrow / totalGrow : 0f;
                item.MainSize = item.HypotheticalMainSize + growth;
                if (item.MainSize > item.MaxMain || item.MainSize < item.MinMain)
                {
                    item.MainSize = item.MainSize > item.MaxMain ? item.MaxMain : item.MinMain;
                    frozen ??= new bool[items.Count];
                    frozen[i] = true;
                    anyClamped = true;
                }
            }
            if (!anyClamped) return;
        }
    }

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

        if (lines.Count == 1 && crossSize > 0 && crossSize > lines[0].CrossSize)
        {
            lines[0].CrossSize = crossSize;
        }
    }

    private static float[] ComputeAlignContentOffsets(List<FlexLine> lines, float crossSize,
        float crossGap, float totalLinesCross, string alignContent)
    {
        var offsets = new float[lines.Count];

        if (crossSize <= AutoCrossSize || lines.Count == 0)
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
                    offset += lines[i].CrossSize + gap + crossGap;
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
                    offset += lines[i].CrossSize + gap + crossGap;
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
                    offset += lines[i].CrossSize + gap + crossGap;
                }
                break;
            }
            case "stretch":
            default:
            {
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

    private static float GetMaxLeafContentWidth(LayoutBox box)
    {
        if (box.Text != null)
            return box.ContentWidth + box.PaddingLeft + box.PaddingRight;

        if (box.Children.Count == 0)
            return 0;

        float maxChildWidth = 0;
        for (int i = 0; i < box.Children.Count; i++)
        {
            float w = GetMaxLeafContentWidth(box.Children[i]);
            if (w > maxChildWidth) maxChildWidth = w;
        }
        return maxChildWidth + box.PaddingLeft + box.PaddingRight;
    }

    private static void RelayoutRowItemsIfResized(FlexLine line, LayoutBox container,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? containerStyle)
    {
        for (int i = 0; i < line.Items.Count; i++)
        {
            var item = line.Items[i];
            if (item.Element == null) continue;
            if (Math.Abs(item.Box.Width - item.InitialBoxWidth) < 0.5f) continue;

            float flexResolvedWidth = item.Box.Width;
            float reLayoutContaining = flexResolvedWidth + item.Box.MarginLeft + item.Box.MarginRight;

            var newBox = BlockLayout.CreateBox(item.Element, item.Style, container,
                reLayoutContaining, resolver, containerStyle);

            if (Math.Abs(newBox.Width - flexResolvedWidth) > 0.5f)
            {
                newBox.Width = flexResolvedWidth;
                newBox.ContentWidth = flexResolvedWidth - newBox.PaddingLeft - newBox.PaddingRight;
                if (newBox.ContentWidth < 0) newBox.ContentWidth = 0;
            }

            item.Box = newBox;
        }
    }

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

        int autoMarginCount = 0;
        for (int i = 0; i < line.Items.Count; i++)
        {
            var st = line.Items[i].Style;
            if (isRow)
            {
                if (st.Get("margin-left") == "auto") autoMarginCount++;
                if (st.Get("margin-right") == "auto") autoMarginCount++;
            }
            else
            {
                if (st.Get("margin-top") == "auto") autoMarginCount++;
                if (st.Get("margin-bottom") == "auto") autoMarginCount++;
            }
        }
        float autoMarginShare = 0;
        if (autoMarginCount > 0 && freeSpace > 0)
        {
            autoMarginShare = freeSpace / autoMarginCount;
            freeSpace = 0;
        }

        float mainOffset;
        float extraGap;
        ComputeJustifyContent(justifyContent, freeSpace, line.Items.Count, mainGap, out mainOffset, out extraGap);

        float pos = mainOffset;
        int count = line.Items.Count;
        for (int idx = 0; idx < count; idx++)
        {
            var item = line.Items[idx];

            if (isRow)
            {
                if (autoMarginShare > 0 && item.Style.Get("margin-left") == "auto")
                    pos += autoMarginShare;
                pos += item.Box.MarginLeft;
                float start = isReverse ? mainSize - pos - item.MainSize : pos;
                float newX = container.X + container.PaddingLeft + start;
                float deltaX = newX - item.Box.X;
                item.Box.X = newX;
                if (Math.Abs(deltaX) > 0.01f)
                    OffsetChildren(item.Box, deltaX, 0);
                pos += item.MainSize + item.Box.MarginRight;
                if (autoMarginShare > 0 && item.Style.Get("margin-right") == "auto")
                    pos += autoMarginShare;
            }
            else
            {
                if (autoMarginShare > 0 && item.Style.Get("margin-top") == "auto")
                    pos += autoMarginShare;
                pos += item.Box.MarginTop;
                float start = isReverse ? mainSize - pos - item.MainSize : pos;
                item.Box.Y = container.Y + container.PaddingTop + start;
                pos += item.MainSize + item.Box.MarginBottom;
                if (autoMarginShare > 0 && item.Style.Get("margin-bottom") == "auto")
                    pos += autoMarginShare;
            }

            if (idx < count - 1)
                pos += extraGap;
        }
    }

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

    private static void PositionCrossAxis(FlexLine line, float lineCrossOffset, float lineCrossSize,
        string alignItems, bool isRow, LayoutBox container)
    {
        for (int i = 0; i < line.Items.Count; i++)
        {
            var item = line.Items[i];
            string alignment = item.AlignSelf == null || item.AlignSelf == "auto" ? alignItems : item.AlignSelf;

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
                    if (isRow)
                    {
                        float maxAscent = 0;
                        for (int bi = 0; bi < line.Items.Count; bi++)
                        {
                            float fs = BlockLayout.ResolveFontSize(line.Items[bi].Style.FontSize, DefaultFontSize);
                            float asc = fs * ApproximateAscentEm + line.Items[bi].Box.MarginTop;
                            if (asc > maxAscent) maxAscent = asc;
                        }
                        float itemAscent = BlockLayout.ResolveFontSize(item.Style.FontSize, DefaultFontSize) * ApproximateAscentEm
                            + itemMarginBefore;
                        crossPos = lineCrossOffset + itemMarginBefore + (maxAscent - itemAscent);
                    }
                    else
                    {
                        crossPos = lineCrossOffset + itemMarginBefore;
                    }
                    break;

                case "stretch":
                default:
                    crossPos = lineCrossOffset + itemMarginBefore;

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
                item.Box.Y = container.Y + container.PaddingTop + crossPos;
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

    internal static void OffsetChildren(LayoutBox box, float dx, float dy)
    {
        for (int i = 0; i < box.Children.Count; i++)
        {
            var child = box.Children[i];
            child.X += dx;
            child.Y += dy;
            OffsetChildren(child, dx, dy);
        }
    }

    internal static float MeasureMaxContentWidth(HtmlElement element, ComputedStyle style,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, float fontSize)
    {
        float maxWidth = 0;
        float inlineRun = 0;
        float letterSpacing = BlockLayout.ResolveLength(style.Get("letter-spacing"), 0, fontSize);
        var textTransform = style.Get("text-transform");

        for (int i = 0; i < element.ChildNodes.Count; i++)
        {
            var child = element.ChildNodes[i];
            if (child is HtmlTextNode textNode)
            {
                var text = textNode.Data.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                if (textTransform == "uppercase") text = text.ToUpperInvariant();
                else if (textTransform == "lowercase") text = text.ToLowerInvariant();
                if (inlineRun > 0)
                    inlineRun += TextMeasurer.MeasureWidth(" ", fontSize,
                        style.FontFamily, style.FontWeight, style.Get("font-style"), letterSpacing);
                inlineRun += TextMeasurer.MeasureWidth(text, fontSize,
                    style.FontFamily, style.FontWeight, style.Get("font-style"), letterSpacing);
            }
            else if (child is HtmlElement childElem)
            {
                var childStyle = resolver(childElem, style);
                if (childStyle.Display == "none") continue;

                if (childElem.TagName == "br")
                {
                    if (inlineRun > maxWidth) maxWidth = inlineRun;
                    inlineRun = 0;
                    continue;
                }

                float childWidth;
                var explicitWidth = BlockLayout.ResolveOptionalLength(childStyle.Width, 0, fontSize);
                if (explicitWidth.HasValue)
                    childWidth = explicitWidth.Value;
                else if (childElem.TagName == "img" || childElem.TagName == "svg")
                    childWidth = BlockLayout.ResolveReplacedElementWidth(childStyle, childElem, fontSize);
                else
                {
                    float childFontSize = BlockLayout.ResolveFontSize(childStyle.FontSize, fontSize);
                    childWidth = MeasureMaxContentWidth(childElem, childStyle, resolver, childFontSize);
                }
                childWidth += BlockLayout.ResolveLength(childStyle.Get("padding-left"), 0, fontSize)
                              + BlockLayout.ResolveLength(childStyle.Get("padding-right"), 0, fontSize);
                float childMargins = BlockLayout.ResolveLength(childStyle.Get("margin-left"), 0, fontSize)
                                     + BlockLayout.ResolveLength(childStyle.Get("margin-right"), 0, fontSize);

                bool isInline = childStyle.Display == "inline" || childStyle.Display == "inline-block" ||
                                ((childElem.TagName == "img" || childElem.TagName == "svg") && childStyle.Display != "block");
                if (isInline)
                {
                    if (inlineRun > 0)
                        inlineRun += TextMeasurer.MeasureWidth(" ", fontSize,
                            style.FontFamily, style.FontWeight, style.Get("font-style"), letterSpacing);
                    inlineRun += childWidth + childMargins;
                }
                else
                {
                    if (inlineRun > maxWidth) maxWidth = inlineRun;
                    inlineRun = 0;
                    if (childWidth + childMargins > maxWidth) maxWidth = childWidth + childMargins;
                }
            }
        }

        if (inlineRun > maxWidth) maxWidth = inlineRun;
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

    private sealed class FlexItem
    {
        public LayoutBox Box = null!;
        public HtmlElement Element = null!;
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
        public float InitialBoxWidth;
    }

    private sealed class FlexLine
    {
        public List<FlexItem> Items = new List<FlexItem>();
        public float CrossSize;
    }
}
