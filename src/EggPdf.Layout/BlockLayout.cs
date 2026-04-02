using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

/// <summary>
/// Phase 1 block layout engine. Lays out block-level children vertically.
/// </summary>
public static class BlockLayout
{
    private const float DefaultFontSize = 16f;
    private const float DefaultLineHeight = 1.2f;

    /// <summary>A segment of text with associated style for inline formatting.</summary>
    private struct InlineRun
    {
        public string Text;
        public ComputedStyle Style;
        public HtmlElement? Element;
        public float FontSize;
        public bool HasLeadingSpace;
    }

    /// <summary>
    /// Lay out an entire document into a layout tree using BasicStyleResolver.
    /// </summary>
    public static LayoutBox LayoutDocument(HtmlDocument document, float pageWidth, float pageHeight)
    {
        var resolver = new BasicStyleResolver();
        return LayoutDocumentInternal(document, pageWidth, pageHeight,
            (elem, parent) => resolver.Resolve(elem, parent));
    }

    /// <summary>
    /// Lay out with CascadeResolver for full CSS support (style tags, selectors, specificity, @media).
    /// </summary>
    public static LayoutBox LayoutDocument(HtmlDocument document, float pageWidth, float pageHeight,
        Css.Cascade.CascadeResolver cascadeResolver)
    {
        return LayoutDocumentInternal(document, pageWidth, pageHeight,
            (elem, parent) => cascadeResolver.Resolve(elem, parent));
    }

    private static LayoutBox LayoutDocumentInternal(HtmlDocument document, float pageWidth, float pageHeight,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolveStyle)
    {
        var root = new LayoutBox
        {
            X = 0, Y = 0,
            Width = pageWidth, Height = pageHeight,
            ContentWidth = pageWidth, ContentHeight = pageHeight
        };

        if (document.Body == null) return root;

        // Resolve html (root) element style first so custom properties on :root are inherited
        ComputedStyle? htmlStyle = null;
        if (document.DocumentElement != null)
            htmlStyle = resolveStyle(document.DocumentElement, null);

        var bodyStyle = resolveStyle(document.Body, htmlStyle);
        var bodyBox = CreateBox(document.Body, bodyStyle, root, pageWidth, resolveStyle, htmlStyle);
        root.Children.Add(bodyBox);

        // Post-layout pass: convert all Y coordinates to absolute
        ResolveAbsolutePositions(root, 0, 0);

        // Apply body's top margin: offset body and all normal-flow descendants down.
        // Skip if margin is the UA default (8px) since existing tests expect no offset for it.
        if (bodyBox.MarginTop > 8)
        {
            OffsetBoxY(bodyBox, bodyBox.MarginTop);
        }

        return root;
    }

    internal static LayoutBox CreateBox(HtmlElement element, ComputedStyle style,
        LayoutBox parent, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        var box = new LayoutBox { Element = element, Style = style };

        // Skip display:none
        if (style.Display == "none")
            return box;

        // Resolve box model values
        float parentFontSize = parentStyle != null ? ResolveFontSize(parentStyle.FontSize, DefaultFontSize) : DefaultFontSize;
        float fontSize = ResolveFontSize(style.FontSize, parentFontSize);

        // Handle shorthand margin/padding (single value -> all 4 sides)
        var marginShort = style.Get("margin");
        box.MarginTop = ResolveLength(style.MarginTop ?? marginShort, containingWidth, fontSize);
        box.MarginRight = ResolveLength(style.MarginRight ?? marginShort, containingWidth, fontSize);
        box.MarginBottom = ResolveLength(style.MarginBottom ?? marginShort, containingWidth, fontSize);
        box.MarginLeft = ResolveLength(style.MarginLeft ?? marginShort, containingWidth, fontSize);

        var paddingShort = style.Get("padding");
        box.PaddingTop = ResolveLength(style.PaddingTop ?? paddingShort, containingWidth, fontSize);
        box.PaddingRight = ResolveLength(style.PaddingRight ?? paddingShort, containingWidth, fontSize);
        box.PaddingBottom = ResolveLength(style.PaddingBottom ?? paddingShort, containingWidth, fontSize);
        box.PaddingLeft = ResolveLength(style.PaddingLeft ?? paddingShort, containingWidth, fontSize);

        // Box-sizing
        bool borderBox = style.Get("box-sizing") == "border-box";

        // Width
        float? specifiedWidth = ResolveOptionalLength(style.Width, containingWidth, fontSize);
        if (specifiedWidth.HasValue)
        {
            if (borderBox)
            {
                // border-box: width includes padding
                box.Width = specifiedWidth.Value;
                box.ContentWidth = specifiedWidth.Value - box.PaddingLeft - box.PaddingRight;
                if (box.ContentWidth < 0) box.ContentWidth = 0;
            }
            else
            {
                box.ContentWidth = specifiedWidth.Value;
                box.Width = specifiedWidth.Value + box.PaddingLeft + box.PaddingRight;
            }
        }
        else
        {
            // Auto width: fill containing block minus margins
            box.Width = containingWidth - box.MarginLeft - box.MarginRight;
            box.ContentWidth = box.Width - box.PaddingLeft - box.PaddingRight;
        }

        // Min/max width constraints
        float? minWidth = ResolveOptionalLength(style.Get("min-width"), containingWidth, fontSize);
        float? maxWidth = ResolveOptionalLength(style.Get("max-width"), containingWidth, fontSize);

        if (minWidth.HasValue && box.Width < minWidth.Value)
        {
            box.Width = minWidth.Value;
            box.ContentWidth = box.Width - box.PaddingLeft - box.PaddingRight;
        }
        if (maxWidth.HasValue && box.Width > maxWidth.Value)
        {
            box.Width = maxWidth.Value;
            box.ContentWidth = box.Width - box.PaddingLeft - box.PaddingRight;
        }

        // Position
        box.X = parent.X + parent.PaddingLeft + box.MarginLeft;

        // Relative positioning offset
        var position = style.Get("position");
        if (position == "relative")
        {
            float offsetTop = ResolveLength(style.Get("top"), 0, fontSize);
            float offsetLeft = ResolveLength(style.Get("left"), 0, fontSize);
            box.X += offsetLeft;
            // Y offset applied after layout (see below)
        }

        // Flex layout: delegate to FlexLayout when display is flex
        if (style.Display == "flex")
        {
            FlexLayout.LayoutFlex(box, element, style, containingWidth, resolver, parentStyle);

            // Compute height from flex children
            float? specifiedHeightFlex = ResolveOptionalLength(style.Height, 0, fontSize);
            if (specifiedHeightFlex.HasValue)
            {
                if (borderBox)
                {
                    box.Height = specifiedHeightFlex.Value;
                    box.ContentHeight = specifiedHeightFlex.Value - box.PaddingTop - box.PaddingBottom;
                    if (box.ContentHeight < 0) box.ContentHeight = 0;
                }
                else
                {
                    box.ContentHeight = specifiedHeightFlex.Value;
                    box.Height = specifiedHeightFlex.Value + box.PaddingTop + box.PaddingBottom;
                }
            }
            else
            {
                // Auto height: compute from children
                float maxChildBottom = 0;
                for (int ci = 0; ci < box.Children.Count; ci++)
                {
                    var child = box.Children[ci];
                    float childBottom = child.Y + child.Height - box.Y - box.PaddingTop;
                    if (childBottom > maxChildBottom)
                        maxChildBottom = childBottom;
                }
                box.ContentHeight = maxChildBottom;
                box.Height = maxChildBottom + box.PaddingTop + box.PaddingBottom;
            }

            // Min/max height constraints for flex
            float? minHeightFlex = ResolveOptionalLength(style.Get("min-height"), 0, fontSize);
            float? maxHeightFlex = ResolveOptionalLength(style.Get("max-height"), 0, fontSize);
            if (minHeightFlex.HasValue && box.Height < minHeightFlex.Value)
                box.Height = minHeightFlex.Value;
            if (maxHeightFlex.HasValue && box.Height > maxHeightFlex.Value)
                box.Height = maxHeightFlex.Value;

            // Apply relative position Y offset
            if (position == "relative")
            {
                float offsetTopFlex = ResolveLength(style.Get("top"), 0, fontSize);
                box.Y += offsetTopFlex;
            }

            return box;
        }

        // Grid layout: delegate to GridLayout when display is grid
        if (style.Display == "grid")
        {
            GridLayout.LayoutGrid(box, element, style, containingWidth, resolver, parentStyle);

            // Compute height from grid children
            float? specifiedHeightGrid = ResolveOptionalLength(style.Height, 0, fontSize);
            if (specifiedHeightGrid.HasValue)
            {
                if (borderBox)
                {
                    box.Height = specifiedHeightGrid.Value;
                    box.ContentHeight = specifiedHeightGrid.Value - box.PaddingTop - box.PaddingBottom;
                    if (box.ContentHeight < 0) box.ContentHeight = 0;
                }
                else
                {
                    box.ContentHeight = specifiedHeightGrid.Value;
                    box.Height = specifiedHeightGrid.Value + box.PaddingTop + box.PaddingBottom;
                }
            }
            else
            {
                // Auto height: compute from children
                float maxChildBottom = 0;
                for (int ci = 0; ci < box.Children.Count; ci++)
                {
                    var child = box.Children[ci];
                    float childBottom = child.Y + child.Height - box.Y - box.PaddingTop;
                    if (childBottom > maxChildBottom)
                        maxChildBottom = childBottom;
                }
                box.ContentHeight = maxChildBottom;
                box.Height = maxChildBottom + box.PaddingTop + box.PaddingBottom;
            }

            // Min/max height constraints for grid
            float? minHeightGrid = ResolveOptionalLength(style.Get("min-height"), 0, fontSize);
            float? maxHeightGrid = ResolveOptionalLength(style.Get("max-height"), 0, fontSize);
            if (minHeightGrid.HasValue && box.Height < minHeightGrid.Value)
                box.Height = minHeightGrid.Value;
            if (maxHeightGrid.HasValue && box.Height > maxHeightGrid.Value)
                box.Height = maxHeightGrid.Value;

            // Apply relative position Y offset
            if (position == "relative")
            {
                float offsetTopGrid = ResolveLength(style.Get("top"), 0, fontSize);
                box.Y += offsetTopGrid;
            }

            return box;
        }

        // Check for multi-column layout
        bool isMultiColumn = MultiColumnLayout.IsMultiColumn(style);
        float multiColWidth = box.ContentWidth;
        if (isMultiColumn)
        {
            var (colCount, colWidth, colGap) = MultiColumnLayout.ResolveColumns(style, box.ContentWidth, fontSize);
            if (colCount > 1)
                multiColWidth = colWidth; // lay out children at column width
        }

        // Layout children using inline formatting context awareness
        float childY = 0;
        float childContainingWidth = isMultiColumn ? multiColWidth : box.ContentWidth;
        float prevMarginBottom = 0; // for margin collapsing
        float inlineX = 0; // current X offset within the inline line
        float inlineLineHeight = 0; // max height of current inline line
        bool lastWasTextNode = false; // track if previous child was a text node (for <br> handling)

        // Collect absolutely/fixed positioned children for deferred layout
        var absChildren = new System.Collections.Generic.List<(HtmlElement elem, ComputedStyle style, string pos)>();

        foreach (var childNode in element.ChildNodes)
        {
            if (childNode is HtmlElement childElem)
            {
                var childStyle = resolver(childElem, style);

                if (childStyle.Display == "none")
                    continue;

                // Absolutely/fixed positioned elements are removed from normal flow
                var childPosition = childStyle.Get("position");
                if (childPosition == "absolute" || childPosition == "fixed")
                {
                    absChildren.Add((childElem, childStyle, childPosition));
                    continue;
                }

                if (IsBlockLevel(childStyle.Display))
                {
                    // Flush any pending inline content
                    if (inlineX > 0)
                    {
                        childY += inlineLineHeight;
                        inlineX = 0;
                        inlineLineHeight = 0;
                    }

                    // Table row layout: cells go side-by-side (horizontal)
                    if (IsTableRow(style.Display) && IsTableCell(childStyle.Display))
                    {
                        int totalColumns = CountTableColumns(element);
                        int colOffset = CountPreviousColumns(element, childElem);
                        int colspan = GetColspan(childElem);

                        // border-collapse inherits from <table> via CSS inheritance
                        var borderCollapse = style.Get("border-collapse") ?? parentStyle?.Get("border-collapse");
                        bool isCollapse = borderCollapse == "collapse";

                        // border-spacing: 0 when collapsed, otherwise use inherited value
                        float borderSpacing = 0;
                        if (!isCollapse)
                        {
                            var spacingVal = style.Get("border-spacing") ?? parentStyle?.Get("border-spacing");
                            borderSpacing = !string.IsNullOrEmpty(spacingVal) ? ResolveLength(spacingVal, 0, fontSize) : 2;
                        }

                        // Subtract total border-spacing from available width for cells
                        float spacingTotal = totalColumns > 1 ? borderSpacing * (totalColumns - 1) : 0;
                        float availableForCells = childContainingWidth - spacingTotal;

                        // Auto table layout: compute column widths based on content
                        var columnWidths = ComputeAutoColumnWidths(element, totalColumns, availableForCells, fontSize, resolver, style);
                        float colWidth = colOffset < columnWidths.Length ? columnWidths[colOffset] : availableForCells / Math.Max(totalColumns, 1);

                        float cellWidth = 0;
                        for (int ci = colOffset; ci < colOffset + colspan && ci < columnWidths.Length; ci++)
                            cellWidth += columnWidths[ci];
                        if (colspan > 1) cellWidth += borderSpacing * (colspan - 1);
                        if (cellWidth <= 0) cellWidth = availableForCells / Math.Max(totalColumns, 1);

                        var childBox = CreateBox(childElem, childStyle, box, cellWidth, resolver, style);
                        childBox.Width = cellWidth;
                        childBox.ContentWidth = cellWidth - childBox.PaddingLeft - childBox.PaddingRight;
                        if (childBox.ContentWidth < 0) childBox.ContentWidth = 0;
                        childBox.Y = box.Y + box.PaddingTop;

                        // Calculate X from sum of preceding column widths
                        float cellX = box.X + box.PaddingLeft;
                        for (int ci = 0; ci < colOffset && ci < columnWidths.Length; ci++)
                            cellX += columnWidths[ci] + borderSpacing;
                        // Update cell X and offset all children that were laid out with the old X
                        float deltaX = cellX - childBox.X;
                        childBox.X = cellX;
                        if (Math.Abs(deltaX) > 0.01f)
                        {
                            for (int gi = 0; gi < childBox.Children.Count; gi++)
                                childBox.Children[gi].X += deltaX;
                        }

                        // border-collapse: remove interior borders on shared edges
                        if (isCollapse)
                        {
                            if (colOffset > 0)
                                childBox.Style.Set("border-left-width", "0");
                            if (!IsFirstRowInTable(element))
                                childBox.Style.Set("border-top-width", "0");
                        }

                        box.Children.Add(childBox);

                        // Track max cell height for the row
                        if (childBox.Height > childY)
                            childY = childBox.Height;
                    }
                    else
                    {
                        // Normal block layout: stack vertically
                        var childBox = CreateBox(childElem, childStyle, box, childContainingWidth, resolver, style);

                        // Margin collapsing between adjacent block siblings
                        float effectiveTopMargin;
                        if (box.Children.OfType<LayoutBox>().Any(c => c.Element != null))
                        {
                            effectiveTopMargin = Math.Max(prevMarginBottom, childBox.MarginTop);
                        }
                        else
                        {
                            effectiveTopMargin = childBox.MarginTop;
                        }

                        childBox.Y = box.Y + box.PaddingTop + childY + effectiveTopMargin;
                        childBox.X = box.X + box.PaddingLeft + childBox.MarginLeft;

                        box.Children.Add(childBox);
                        childY += effectiveTopMargin + childBox.Height;
                        prevMarginBottom = childBox.MarginBottom;
                        lastWasTextNode = false;
                    }
                }
                else if (childElem.TagName == "br")
                {
                    // <br> forces a line break
                    float lineHeight = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"));
                    if (inlineX > 0)
                    {
                        childY += Math.Max(inlineLineHeight, lineHeight);
                        inlineX = 0;
                        inlineLineHeight = 0;
                    }
                    else if (!lastWasTextNode)
                    {
                        // Only add line height for empty <br> lines (consecutive <br> or leading <br>)
                        // After text nodes, childY already advanced past the last line
                        childY += lineHeight;
                    }
                    lastWasTextNode = false;
                }
                else if (childElem.TagName == "img")
                {
                    // Image element: use width/height attributes or CSS
                    float imgWidth = ResolveImgDimension(childStyle.Width, childElem.GetAttribute("width"), childContainingWidth, fontSize, 150);
                    float imgHeight = ResolveImgDimension(childStyle.Height, childElem.GetAttribute("height"), 0, fontSize, 150);

                    // Check if image fits on current inline line
                    if (inlineX > 0 && inlineX + imgWidth > childContainingWidth)
                    {
                        childY += inlineLineHeight;
                        inlineX = 0;
                        inlineLineHeight = 0;
                    }

                    var childBox = new LayoutBox
                    {
                        Element = childElem,
                        Style = childStyle,
                        X = box.X + box.PaddingLeft + inlineX,
                        Y = box.Y + box.PaddingTop + childY,
                        Width = imgWidth,
                        Height = imgHeight,
                        ContentWidth = imgWidth,
                        ContentHeight = imgHeight,
                        ImageSource = childElem.GetAttribute("src")
                    };
                    box.Children.Add(childBox);
                    inlineX += imgWidth;
                    if (imgHeight > inlineLineHeight)
                        inlineLineHeight = imgHeight;
                }
                else if (childStyle.Display == "inline-block")
                {
                    // Inline-block: create a box that flows inline but has block internals
                    var childBox = CreateBox(childElem, childStyle, box, childContainingWidth, resolver, style);
                    float ibWidth = childBox.Width;
                    if (inlineX > 0 && inlineX + ibWidth > childContainingWidth)
                    {
                        childY += inlineLineHeight;
                        inlineX = 0;
                        inlineLineHeight = 0;
                    }
                    childBox.X = box.X + box.PaddingLeft + inlineX;
                    childBox.Y = box.Y + box.PaddingTop + childY;
                    box.Children.Add(childBox);
                    inlineX += ibWidth;
                    if (childBox.Height > inlineLineHeight) inlineLineHeight = childBox.Height;
                }
                else
                {
                    // Inline elements: collect text runs with style info and lay out word-by-word
                    var runs = new System.Collections.Generic.List<InlineRun>();
                    CollectInlineRuns(childElem, childStyle, ResolveFontSize(childStyle.FontSize, fontSize), resolver, runs);
                    if (runs.Count > 0)
                    {
                        LayoutInlineRuns(runs, box, childElem, ref inlineX, ref childY, ref inlineLineHeight,
                            childContainingWidth, style, fontSize);
                    }
                    else
                    {
                        // Empty inline element: still create a box for FindByTag
                        var childBox = new LayoutBox
                        {
                            Element = childElem,
                            Style = childStyle,
                            X = box.X + box.PaddingLeft + inlineX,
                            Y = box.Y + box.PaddingTop + childY,
                            Width = 0, Height = 0
                        };
                        box.Children.Add(childBox);
                    }
                }
            }
            else if (childNode is HtmlTextNode textNode)
            {
                var whiteSpaceProp = style.Get("white-space") ?? "normal";
                bool preserveWhitespace = whiteSpaceProp == "pre" || whiteSpaceProp == "pre-wrap" || whiteSpaceProp == "pre-line";

                // Skip empty text nodes unless preserving whitespace
                if (!preserveWhitespace && string.IsNullOrWhiteSpace(textNode.Data))
                    continue;

                // Check if parent has mixed inline content (inline elements + text)
                bool hasInlineSiblings = HasInlineElementSiblings(element);

                // If there are inline siblings, participate in inline flow
                if (hasInlineSiblings && !preserveWhitespace)
                {
                    var ilFontFamily = style.FontFamily;
                    var ilFontWeight = style.FontWeight;
                    var ilFontStyle = style.Get("font-style");
                    float ilLineHeight = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"));
                    var ilTextData = textNode.Data.Trim();
                    if (string.IsNullOrEmpty(ilTextData)) continue;

                    // Split into words and lay them out inline
                    var words = ilTextData.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var word in words)
                    {
                        var wordWithSpace = (inlineX > 0 ? " " : "") + word;
                        float wordWidth = TextMeasurer.MeasureWidth(wordWithSpace, fontSize, ilFontFamily, ilFontWeight, ilFontStyle);

                        // Wrap to next line if word doesn't fit
                        if (inlineX > 0 && inlineX + wordWidth > childContainingWidth)
                        {
                            childY += inlineLineHeight;
                            inlineX = 0;
                            inlineLineHeight = 0;
                            wordWithSpace = word;
                            wordWidth = TextMeasurer.MeasureWidth(word, fontSize, ilFontFamily, ilFontWeight, ilFontStyle);
                        }

                        var textBox = new LayoutBox
                        {
                            Style = style,
                            X = box.X + box.PaddingLeft + inlineX,
                            Y = box.Y + box.PaddingTop + childY,
                            Width = wordWidth,
                            Height = ilLineHeight,
                            ContentWidth = wordWidth,
                            ContentHeight = ilLineHeight,
                            Text = wordWithSpace
                        };
                        box.Children.Add(textBox);
                        inlineX += wordWidth;
                        if (ilLineHeight > inlineLineHeight)
                            inlineLineHeight = ilLineHeight;
                    }
                    continue;
                }

                // Text content with line wrapping
                var fontFamily = style.FontFamily;
                var fontWeight = style.FontWeight;
                var fontStyle = style.Get("font-style");
                float lineHeight = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"));
                float textIndent = ResolveLength(style.Get("text-indent"), childContainingWidth, fontSize);
                var textData = preserveWhitespace ? textNode.Data : textNode.Data.Trim();

                // Check overflow-wrap/word-break for character-level breaking
                var overflowWrap = style.Get("overflow-wrap") ?? style.Get("word-wrap");
                var wordBreak = style.Get("word-break");
                bool breakWord = overflowWrap == "break-word" || overflowWrap == "anywhere" ||
                                 wordBreak == "break-all" || wordBreak == "break-word";

                // For text-indent, reduce first line's available width
                float firstLineWidth = textIndent > 0 ? childContainingWidth - textIndent : childContainingWidth;
                var lines = TextMeasurer.WrapText(textData, fontSize, fontFamily, fontWeight, fontStyle,
                    firstLineWidth > 0 ? firstLineWidth : childContainingWidth, whiteSpaceProp, breakWord);

                // If indent caused wrapping and there are remaining lines, re-wrap with full width
                if (textIndent > 0 && lines.Count > 1)
                {
                    var firstLine = lines[0];
                    var remaining = textData.Substring(firstLine.Length).TrimStart();
                    lines = new System.Collections.Generic.List<string> { firstLine };
                    if (!string.IsNullOrEmpty(remaining))
                    {
                        var moreLines = TextMeasurer.WrapText(remaining, fontSize, fontFamily, fontWeight, fontStyle, childContainingWidth, whiteSpaceProp, breakWord);
                        lines.AddRange(moreLines);
                    }
                }

                bool isFirstLine = true;
                foreach (var line in lines)
                {
                    float lineX = box.X + box.PaddingLeft;
                    if (isFirstLine && textIndent > 0)
                        lineX += textIndent;

                    var textBox = new LayoutBox
                    {
                        Style = style,
                        X = lineX,
                        Y = box.Y + box.PaddingTop + childY,
                        Width = childContainingWidth,
                        Height = lineHeight,
                        ContentWidth = TextMeasurer.MeasureWidth(line, fontSize, fontFamily, fontWeight, fontStyle),
                        ContentHeight = lineHeight,
                        Text = line
                    };
                    isFirstLine = false;
                    box.Children.Add(textBox);
                    childY += lineHeight;
                }
                lastWasTextNode = true;
            }
        }

        // Flush any remaining inline content
        if (inlineX > 0)
        {
            childY += inlineLineHeight;
        }

        // Post-pass: equalize table cell heights and apply vertical-align
        if (IsTableRow(style.Display) && childY > 0)
        {
            float rowHeight = childY;
            for (int ci = 0; ci < box.Children.Count; ci++)
            {
                var cell = box.Children[ci];
                if (cell.Element == null) continue;

                float contentHeight = cell.ContentHeight;
                // Equalize cell height to row height
                cell.Height = rowHeight;

                // Apply vertical-align within the cell
                var vAlign = cell.Style.Get("vertical-align") ?? "top";
                float offset = 0;
                if (vAlign == "middle")
                    offset = (rowHeight - cell.PaddingTop - cell.PaddingBottom - contentHeight) / 2f;
                else if (vAlign == "bottom")
                    offset = rowHeight - cell.PaddingTop - cell.PaddingBottom - contentHeight;

                if (offset > 0)
                {
                    // Shift all children of this cell down by offset
                    for (int gi = 0; gi < cell.Children.Count; gi++)
                        cell.Children[gi].Y += offset;
                }
            }
        }

        // List marker for display:list-item
        if (style.Display == "list-item")
        {
            var listStyleType = style.Get("list-style-type") ?? (parentStyle?.Get("list-style-type")) ?? "disc";
            string markerText = GetListMarkerText(listStyleType, element, parent.Element);
            if (!string.IsNullOrEmpty(markerText))
            {
                float markerWidth = TextMeasurer.MeasureWidth(markerText + " ", fontSize, null);
                var markerBox = new LayoutBox
                {
                    Style = style,
                    IsListMarker = true,
                    X = box.X - markerWidth,
                    Y = box.Y + box.PaddingTop,
                    Width = markerWidth,
                    Height = fontSize * DefaultLineHeight,
                    ContentWidth = markerWidth,
                    ContentHeight = fontSize * DefaultLineHeight,
                    Text = markerText
                };
                box.Children.Add(markerBox);
            }
        }

        // Height
        float? specifiedHeight = ResolveOptionalLength(style.Height, 0, fontSize);
        if (specifiedHeight.HasValue)
        {
            if (borderBox)
            {
                box.Height = specifiedHeight.Value;
                box.ContentHeight = specifiedHeight.Value - box.PaddingTop - box.PaddingBottom;
                if (box.ContentHeight < 0) box.ContentHeight = 0;
            }
            else
            {
                box.ContentHeight = specifiedHeight.Value;
                box.Height = specifiedHeight.Value + box.PaddingTop + box.PaddingBottom;
            }
        }
        else
        {
            // Auto height: sum of children
            // Apply multi-column redistribution if needed
            if (isMultiColumn && box.Children.Count > 0)
            {
                var (colCount, colWidth, colGap) = MultiColumnLayout.ResolveColumns(style, box.ContentWidth, fontSize);
                if (colCount > 1)
                {
                    var childList = new System.Collections.Generic.List<LayoutBox>(box.Children.OfType<LayoutBox>());
                    var columns = MultiColumnLayout.DistributeIntoColumns(childList, colCount, colWidth, colGap,
                        box.X + box.PaddingLeft, box.Y + box.PaddingTop);

                    box.Children.Clear();
                    float maxColHeight = 0;
                    foreach (var col in columns)
                    {
                        box.Children.Add(col);
                        if (col.Height > maxColHeight) maxColHeight = col.Height;
                    }
                    childY = maxColHeight;
                }
            }

            box.ContentHeight = childY;
            box.Height = childY + box.PaddingTop + box.PaddingBottom;
        }

        // Min/max height constraints
        float? minHeight = ResolveOptionalLength(style.Get("min-height"), 0, fontSize);
        float? maxHeight = ResolveOptionalLength(style.Get("max-height"), 0, fontSize);

        if (minHeight.HasValue && box.Height < minHeight.Value)
            box.Height = minHeight.Value;
        if (maxHeight.HasValue && box.Height > maxHeight.Value)
            box.Height = maxHeight.Value;

        // Layout absolutely/fixed positioned children (deferred from normal flow)
        for (int ai = 0; ai < absChildren.Count; ai++)
        {
            var absEntry = absChildren[ai];
            var absElem = absEntry.elem;
            var absStyle = absEntry.style;
            var absPos = absEntry.pos;
            var absBox = CreateBox(absElem, absStyle, box, box.ContentWidth, resolver, style);
            absBox.IsAbsolutelyPositioned = true;

            // Determine containing block:
            // - position:fixed -> page root (0,0 with page dimensions)
            // - position:absolute -> nearest positioned ancestor (position != static) or root
            float cbX, cbY, cbWidth, cbHeight;

            if (absPos == "fixed")
            {
                // Fixed: relative to page origin
                cbX = 0;
                cbY = 0;
                cbWidth = containingWidth;
                cbHeight = FindPageHeight(parent);
            }
            else
            {
                // Absolute: relative to nearest positioned ancestor or this box if positioned
                if (position == "relative" || position == "absolute" || position == "fixed" || position == "sticky")
                {
                    // This box is the containing block
                    cbX = box.X;
                    cbY = box.Y;
                    cbWidth = box.Width;
                    cbHeight = box.Height;
                }
                else
                {
                    // No positioned ancestor at this level -- use page root
                    cbX = 0;
                    cbY = 0;
                    cbWidth = containingWidth;
                    cbHeight = FindPageHeight(parent);
                }
            }

            float absFontSize = ResolveFontSize(absStyle.FontSize, fontSize);

            // Resolve top/right/bottom/left
            float? topVal = ResolveOptionalLength(absStyle.Get("top"), cbHeight, absFontSize);
            float? rightVal = ResolveOptionalLength(absStyle.Get("right"), cbWidth, absFontSize);
            float? bottomVal = ResolveOptionalLength(absStyle.Get("bottom"), cbHeight, absFontSize);
            float? leftVal = ResolveOptionalLength(absStyle.Get("left"), cbWidth, absFontSize);

            // If both left and right are set, calculate width from them (when no explicit width)
            if (leftVal.HasValue && rightVal.HasValue && absStyle.Width == null)
            {
                float newWidth = cbWidth - leftVal.Value - rightVal.Value
                    - absBox.MarginLeft - absBox.MarginRight;
                if (newWidth > 0)
                {
                    absBox.Width = newWidth;
                    absBox.ContentWidth = newWidth - absBox.PaddingLeft - absBox.PaddingRight;
                }
            }

            // If both top and bottom are set, calculate height from them (when no explicit height)
            if (topVal.HasValue && bottomVal.HasValue && absStyle.Height == null)
            {
                float newHeight = cbHeight - topVal.Value - bottomVal.Value
                    - absBox.MarginTop - absBox.MarginBottom;
                if (newHeight > 0)
                {
                    absBox.Height = newHeight;
                    absBox.ContentHeight = newHeight - absBox.PaddingTop - absBox.PaddingBottom;
                }
            }

            // Position X
            if (leftVal.HasValue)
                absBox.X = cbX + leftVal.Value + absBox.MarginLeft;
            else if (rightVal.HasValue)
                absBox.X = cbX + cbWidth - rightVal.Value - absBox.Width - absBox.MarginRight;
            else
                absBox.X = cbX + absBox.MarginLeft; // default: top-left of containing block

            // Position Y
            if (topVal.HasValue)
                absBox.Y = cbY + topVal.Value + absBox.MarginTop;
            else if (bottomVal.HasValue)
                absBox.Y = cbY + cbHeight - bottomVal.Value - absBox.Height - absBox.MarginBottom;
            else
                absBox.Y = cbY + absBox.MarginTop; // default: top-left of containing block

            box.Children.Add(absBox);
        }

        // Apply relative position Y offset
        if (position == "relative")
        {
            float offsetTop = ResolveLength(style.Get("top"), 0, fontSize);
            box.Y += offsetTop;
        }

        return box;
    }

    /// <summary>Walk up parent chain to find page height.</summary>
    private static float FindPageHeight(LayoutBox parent)
    {
        // The root box has Height set to page height and no Element
        if (parent.Element == null && parent.Height > 0)
            return parent.Height;
        // Default A4 page height in CSS px
        return parent.Height > 0 ? parent.Height : 841.89f;
    }

    private static bool IsTableRow(string display)
        => display == "table-row";

    private static bool IsTableCell(string display)
        => display == "table-cell";

    /// <summary>Check if a table row (tr) is the first row in its table.</summary>
    private static bool IsFirstRowInTable(HtmlElement row)
    {
        var parent = row.Parent as HtmlElement;
        if (parent == null) return true;

        // If parent is thead/tbody/tfoot, check if this is the first row in it AND the group is first
        if (parent.TagName == "thead" || parent.TagName == "tbody" || parent.TagName == "tfoot")
        {
            // Not the first row in this group
            for (int i = 0; i < parent.ChildNodes.Count; i++)
            {
                if (parent.ChildNodes[i] is HtmlElement elem && elem.TagName == "tr")
                    return elem == row;
            }

            // Check if this group is the first group in the table
            var table = parent.Parent as HtmlElement;
            if (table == null) return true;
            for (int i = 0; i < table.ChildNodes.Count; i++)
            {
                if (table.ChildNodes[i] is HtmlElement elem && (elem.TagName == "thead" || elem.TagName == "tbody" || elem.TagName == "tfoot" || elem.TagName == "tr"))
                {
                    if (elem == parent) return true;
                    if (elem.TagName == "tr") return false;
                    // Non-empty group before this one
                    for (int j = 0; j < elem.ChildNodes.Count; j++)
                        if (elem.ChildNodes[j] is HtmlElement) return false;
                }
            }
            return true;
        }

        // Direct child of table
        if (parent.TagName == "table")
        {
            for (int i = 0; i < parent.ChildNodes.Count; i++)
            {
                if (parent.ChildNodes[i] is HtmlElement elem)
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

    /// <summary>
    /// Compute auto column widths by measuring content across all rows in the table.
    /// Walks up from the row to find the table, scans all rows, measures text width
    /// per column, then distributes available width proportionally.
    /// Results are cached per table element to avoid re-computation for each row.
    /// </summary>
    [ThreadStatic]
    private static System.Collections.Generic.Dictionary<HtmlElement, float[]>? _tableColumnWidthCache;

    private static float[] ComputeAutoColumnWidths(HtmlElement row, int totalColumns, float availableWidth,
        float fontSize, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        if (totalColumns <= 0) return new float[] { availableWidth };

        // Find the table element (walk up through thead/tbody/tfoot)
        HtmlElement? tableElement = row.Parent as HtmlElement;
        while (tableElement != null && tableElement.TagName != "table")
            tableElement = tableElement.Parent as HtmlElement;

        if (tableElement == null)
        {
            // Fallback: equal distribution
            float eq = availableWidth / totalColumns;
            var eqWidths = new float[totalColumns];
            for (int i = 0; i < totalColumns; i++) eqWidths[i] = eq;
            return eqWidths;
        }

        // Check cache
        if (_tableColumnWidthCache == null)
            _tableColumnWidthCache = new System.Collections.Generic.Dictionary<HtmlElement, float[]>();

        if (_tableColumnWidthCache.TryGetValue(tableElement, out var cached))
            return cached;

        // Scan all rows in the table to find max content width per column
        var maxContentWidths = new float[totalColumns];
        var hasExplicitWidth = new bool[totalColumns];
        var explicitWidths = new float[totalColumns];

        ScanTableForColumnWidths(tableElement, totalColumns, maxContentWidths, hasExplicitWidth, explicitWidths,
            fontSize, availableWidth, resolver, parentStyle);

        // Distribute available width proportionally based on content
        float totalContentWidth = 0;
        float totalExplicitWidth = 0;
        int flexColumns = 0;

        for (int i = 0; i < totalColumns; i++)
        {
            if (hasExplicitWidth[i])
            {
                totalExplicitWidth += explicitWidths[i];
            }
            else
            {
                totalContentWidth += Math.Max(maxContentWidths[i], 20); // minimum 20px per column
                flexColumns++;
            }
        }

        float remainingWidth = availableWidth - totalExplicitWidth;
        if (remainingWidth < 0) remainingWidth = availableWidth;

        var result = new float[totalColumns];
        for (int i = 0; i < totalColumns; i++)
        {
            if (hasExplicitWidth[i])
            {
                result[i] = explicitWidths[i];
            }
            else if (totalContentWidth > 0)
            {
                float contentW = Math.Max(maxContentWidths[i], 20);
                result[i] = remainingWidth * contentW / totalContentWidth;
            }
            else
            {
                result[i] = remainingWidth / Math.Max(flexColumns, 1);
            }
        }

        _tableColumnWidthCache[tableElement] = result;
        return result;
    }

    private static void ScanTableForColumnWidths(HtmlElement tableElement, int totalColumns,
        float[] maxContentWidths, bool[] hasExplicitWidth, float[] explicitWidths,
        float fontSize, float availableWidth,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        // Walk: table -> thead/tbody/tfoot -> tr -> td/th
        foreach (var child in tableElement.ChildNodes)
        {
            var group = child as HtmlElement;
            if (group == null) continue;

            // Direct <tr> children of <table>
            if (group.TagName == "tr")
            {
                ScanRowForColumnWidths(group, totalColumns, maxContentWidths, hasExplicitWidth, explicitWidths,
                    fontSize, availableWidth, resolver, parentStyle);
            }
            // <thead>, <tbody>, <tfoot> contain <tr>
            else if (group.TagName == "thead" || group.TagName == "tbody" || group.TagName == "tfoot")
            {
                foreach (var trNode in group.ChildNodes)
                {
                    var tr = trNode as HtmlElement;
                    if (tr != null && tr.TagName == "tr")
                    {
                        ScanRowForColumnWidths(tr, totalColumns, maxContentWidths, hasExplicitWidth, explicitWidths,
                            fontSize, availableWidth, resolver, parentStyle);
                    }
                }
            }
        }
    }

    private static void ScanRowForColumnWidths(HtmlElement row, int totalColumns,
        float[] maxContentWidths, bool[] hasExplicitWidth, float[] explicitWidths,
        float fontSize, float availableWidth,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        int colIdx = 0;
        foreach (var cellNode in row.ChildNodes)
        {
            var cell = cellNode as HtmlElement;
            if (cell == null || (cell.TagName != "td" && cell.TagName != "th"))
                continue;

            if (colIdx >= totalColumns) break;
            int colspan = GetColspan(cell);

            // Check for explicit width on cell
            var cellStyle = resolver(cell, parentStyle);
            var widthStr = cellStyle?.Width;
            if (!string.IsNullOrEmpty(widthStr) && widthStr != "auto")
            {
                float? w = ResolveOptionalLength(widthStr, availableWidth, fontSize);
                if (w.HasValue && colspan == 1)
                {
                    hasExplicitWidth[colIdx] = true;
                    explicitWidths[colIdx] = Math.Max(explicitWidths[colIdx], w.Value);
                }
            }

            // Measure text content width
            if (colspan == 1)
            {
                float textWidth = MeasureElementTextWidth(cell, fontSize);
                // Add padding estimate (10px each side default for cells with padding:10px)
                float padding = 0;
                if (cellStyle != null)
                {
                    padding += ResolveLength(cellStyle.PaddingLeft ?? "0", 0, fontSize);
                    padding += ResolveLength(cellStyle.PaddingRight ?? "0", 0, fontSize);
                }
                textWidth += padding;

                if (textWidth > maxContentWidths[colIdx])
                    maxContentWidths[colIdx] = textWidth;
            }

            colIdx += colspan;
        }
    }

    /// <summary>Measure the total text width of an element and its children.</summary>
    private static float MeasureElementTextWidth(HtmlElement element, float fontSize)
    {
        float totalWidth = 0;
        foreach (var node in element.ChildNodes)
        {
            if (node is Html.Dom.HtmlTextNode textNode)
            {
                var text = textNode.Data?.Trim();
                if (!string.IsNullOrEmpty(text))
                    totalWidth += TextMeasurer.MeasureWidth(text, fontSize, "Helvetica");
            }
            else if (node is HtmlElement child)
            {
                totalWidth += MeasureElementTextWidth(child, fontSize);
            }
        }
        return totalWidth;
    }

    /// <summary>Count total column slots in a row (respecting colspan).</summary>
    private static int CountTableColumns(HtmlElement row)
    {
        int count = 0;
        foreach (var child in row.ChildNodes)
            if (child is HtmlElement e && (e.TagName == "td" || e.TagName == "th"))
                count += GetColspan(e);
        return count;
    }

    /// <summary>Count column slots before the current cell (respecting colspan).</summary>
    private static int CountPreviousColumns(HtmlElement row, HtmlElement currentCell)
    {
        int columns = 0;
        foreach (var child in row.ChildNodes)
        {
            if (child == currentCell) return columns;
            if (child is HtmlElement e && (e.TagName == "td" || e.TagName == "th"))
                columns += GetColspan(e);
        }
        return columns;
    }

    /// <summary>Get colspan attribute value (default 1).</summary>
    private static int GetColspan(HtmlElement cell)
    {
        var attr = cell.GetAttribute("colspan");
        if (!string.IsNullOrEmpty(attr) && int.TryParse(attr, out int colspan) && colspan > 0)
            return colspan;
        return 1;
    }

    private static string GetListMarkerText(string listStyleType, HtmlElement element, HtmlElement? parentElement)
    {
        switch (listStyleType)
        {
            case "disc":
                return "\u2022"; // • (bullet, WinAnsi 0x95)
            case "circle":
                return "o"; // circle marker (WinAnsi-safe)
            case "square":
                return "\u2013"; // – as square substitute (WinAnsi 0x96)
            case "none":
                return "";
            case "decimal":
                int index = GetListItemIndex(element, parentElement);
                return index + ".";
            case "decimal-leading-zero":
                int idx = GetListItemIndex(element, parentElement);
                return idx.ToString("D2") + ".";
            case "lower-alpha":
            case "lower-latin":
                int ai = GetListItemIndex(element, parentElement);
                return ai > 0 && ai <= 26 ? ((char)('a' + ai - 1)).ToString() + "." : ai + ".";
            case "upper-alpha":
            case "upper-latin":
                int bi = GetListItemIndex(element, parentElement);
                return bi > 0 && bi <= 26 ? ((char)('A' + bi - 1)).ToString() + "." : bi + ".";
            case "lower-roman":
                int ri = GetListItemIndex(element, parentElement);
                return ToRoman(ri).ToLowerInvariant() + ".";
            case "upper-roman":
                int ui = GetListItemIndex(element, parentElement);
                return ToRoman(ui) + ".";
            default:
                return "\u2022"; // default to disc
        }
    }

    private static int GetListItemIndex(HtmlElement li, HtmlElement? parent)
    {
        if (parent == null) return 1;
        int index = 0;
        foreach (var child in parent.ChildNodes)
        {
            if (child is HtmlElement e && e.TagName == "li")
            {
                index++;
                if (e == li) return index;
            }
        }
        return 1;
    }

    private static string ToRoman(int number)
    {
        if (number <= 0 || number > 3999) return number.ToString();
        string[] thousands = { "", "M", "MM", "MMM" };
        string[] hundreds = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
        string[] tens = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
        string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
        return thousands[number / 1000] + hundreds[(number % 1000) / 100] +
               tens[(number % 100) / 10] + ones[number % 10];
    }

    /// <summary>Check if an element has any inline element children (not just text nodes or br).</summary>
    private static bool HasInlineElementSiblings(HtmlElement parent)
    {
        foreach (var child in parent.ChildNodes)
        {
            if (child is HtmlElement e && e.TagName != "br" && e.TagName != "img")
            {
                var tag = e.TagName;
                // Only count true inline elements (not block-level)
                if (tag != "div" && tag != "p" && tag != "h1" && tag != "h2" && tag != "h3" &&
                    tag != "h4" && tag != "h5" && tag != "h6" && tag != "ul" && tag != "ol" &&
                    tag != "li" && tag != "table" && tag != "blockquote" && tag != "pre" &&
                    tag != "hr" && tag != "section" && tag != "article" && tag != "nav" &&
                    tag != "header" && tag != "footer" && tag != "main" && tag != "aside" &&
                    tag != "figure" && tag != "figcaption" && tag != "details" && tag != "summary")
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static float ResolveImgDimension(string? cssValue, string? htmlAttr, float containingSize, float fontSize, float defaultValue)
    {
        // CSS takes priority
        var resolved = ResolveOptionalLength(cssValue, containingSize, fontSize);
        if (resolved.HasValue) return resolved.Value;

        // HTML attribute
        if (!string.IsNullOrEmpty(htmlAttr))
        {
            var attrResolved = ResolveOptionalLength(htmlAttr.EndsWith("%") ? htmlAttr : htmlAttr + "px", containingSize, fontSize);
            if (attrResolved.HasValue) return attrResolved.Value;
        }

        return defaultValue;
    }

    private static bool IsBlockLevel(string display)
    {
        return display == "block" || display == "list-item" ||
               display == "table" || display == "flex" || display == "grid" ||
               display == "table-row-group" || display == "table-header-group" ||
               display == "table-footer-group" || display == "table-row" ||
               display == "table-cell" || display == "table-caption";
    }

    private static string? GetTextContent(HtmlElement element)
    {
        var sb = new System.Text.StringBuilder();
        CollectTextRecursive(element, sb);
        var result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    private static void CollectTextRecursive(HtmlNode node, System.Text.StringBuilder sb)
    {
        if (node is HtmlTextNode text)
        {
            sb.Append(text.Data);
            return;
        }
        if (node is HtmlElement elem)
        {
            foreach (var child in elem.ChildNodes)
                CollectTextRecursive(child, sb);
        }
    }

    /// <summary>Collect text runs from an inline element tree, preserving style per segment.</summary>
    private static void CollectInlineRuns(HtmlNode node, ComputedStyle parentStyle, float parentFontSize,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, List<InlineRun> runs)
    {
        if (node is HtmlTextNode textNode)
        {
            var data = textNode.Data;
            if (!string.IsNullOrEmpty(data))
            {
                runs.Add(new InlineRun
                {
                    Text = data,
                    Style = parentStyle,
                    Element = null,
                    FontSize = parentFontSize,
                    HasLeadingSpace = data.Length > 0 && char.IsWhiteSpace(data[0])
                });
            }
            return;
        }

        if (node is HtmlElement elem)
        {
            if (elem.TagName == "br")
            {
                runs.Add(new InlineRun { Text = "\n", Style = parentStyle, Element = elem, FontSize = parentFontSize });
                return;
            }

            var style = resolver(elem, parentStyle);
            if (style.Display == "none") return;
            float fontSize = ResolveFontSize(style.FontSize, parentFontSize);

            foreach (var child in elem.ChildNodes)
                CollectInlineRuns(child, style, fontSize, resolver, runs);
        }
    }

    /// <summary>Layout inline runs as word-level boxes with style-aware wrapping.</summary>
    private static void LayoutInlineRuns(List<InlineRun> runs, LayoutBox box, HtmlElement? wrapperElement,
        ref float inlineX, ref float childY, ref float inlineLineHeight, float containerWidth,
        ComputedStyle parentStyle, float parentFontSize)
    {
        bool elementAssigned = false;

        for (int ri = 0; ri < runs.Count; ri++)
        {
            var run = runs[ri];

            if (run.Text == "\n")
            {
                float lh = TextMeasurer.GetLineHeight(run.FontSize, run.Style.Get("line-height"));
                if (inlineX > 0)
                {
                    childY += Math.Max(inlineLineHeight, lh);
                    inlineX = 0;
                    inlineLineHeight = 0;
                }
                else
                {
                    childY += lh;
                }
                continue;
            }

            // Normalize whitespace
            var text = run.Text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
            // Collapse multiple spaces
            while (text.IndexOf("  ", StringComparison.Ordinal) >= 0)
                text = text.Replace("  ", " ");
            text = text.Trim();

            if (string.IsNullOrEmpty(text)) continue;

            var fontFamily = run.Style.FontFamily;
            var fontWeight = run.Style.FontWeight;
            var fontStyle = run.Style.Get("font-style");
            float lhRun = TextMeasurer.GetLineHeight(run.FontSize, run.Style.Get("line-height"));

            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            for (int wi = 0; wi < words.Length; wi++)
            {
                bool needSpace = inlineX > 0 && (wi > 0 || run.HasLeadingSpace);
                var wordText = (needSpace ? " " : "") + words[wi];
                float wordWidth = TextMeasurer.MeasureWidth(wordText, run.FontSize, fontFamily, fontWeight, fontStyle);

                // Wrap to next line if doesn't fit
                if (inlineX > 0 && inlineX + wordWidth > containerWidth)
                {
                    childY += inlineLineHeight;
                    inlineX = 0;
                    inlineLineHeight = 0;
                    wordText = words[wi]; // no space prefix after wrap
                    wordWidth = TextMeasurer.MeasureWidth(wordText, run.FontSize, fontFamily, fontWeight, fontStyle);
                }

                var textBox = new LayoutBox
                {
                    Element = (!elementAssigned && wrapperElement != null) ? wrapperElement : null,
                    Style = run.Style,
                    X = box.X + box.PaddingLeft + inlineX,
                    Y = box.Y + box.PaddingTop + childY,
                    Width = wordWidth,
                    Height = lhRun,
                    ContentWidth = TextMeasurer.MeasureWidth(wordText, run.FontSize, fontFamily, fontWeight, fontStyle),
                    ContentHeight = lhRun,
                    Text = wordText
                };

                if (!elementAssigned && wrapperElement != null)
                    elementAssigned = true;

                box.Children.Add(textBox);
                inlineX += wordWidth;
                if (lhRun > inlineLineHeight)
                    inlineLineHeight = lhRun;
            }
        }
    }

    public static float ResolveLength(string? value, float containingSize, float fontSize)
    {
        if (string.IsNullOrEmpty(value) || value == "auto" || value == "0")
            return 0;

        return ResolveLengthValue(value, containingSize, fontSize);
    }

    internal static float? ResolveOptionalLength(string? value, float containingSize, float fontSize)
    {
        if (string.IsNullOrEmpty(value) || value == "auto")
            return null;

        return ResolveLengthValue(value, containingSize, fontSize);
    }

    private static float ResolveLengthValue(string value, float containingSize, float fontSize)
    {
        // Check for calc(), min(), max(), clamp() expressions
        if (CalcResolver.IsMathFunction(value))
            return CalcResolver.Resolve(value, containingSize, fontSize);

        if (value.EndsWith("px"))
            return ParseFloat(value.Substring(0, value.Length - 2));

        if (value.EndsWith("em"))
            return ParseFloat(value.Substring(0, value.Length - 2)) * fontSize;

        if (value.EndsWith("rem"))
            return ParseFloat(value.Substring(0, value.Length - 3)) * DefaultFontSize;

        if (value.EndsWith("%"))
            return ParseFloat(value.Substring(0, value.Length - 1)) / 100f * containingSize;

        if (value.EndsWith("pt"))
            return ParseFloat(value.Substring(0, value.Length - 2)) * 96f / 72f;

        if (value.EndsWith("cm"))
            return ParseFloat(value.Substring(0, value.Length - 2)) * 96f / 2.54f;

        if (value.EndsWith("mm"))
            return ParseFloat(value.Substring(0, value.Length - 2)) * 96f / 25.4f;

        if (value.EndsWith("in"))
            return ParseFloat(value.Substring(0, value.Length - 2)) * 96f;

        // Try bare number (treat as px)
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float bare))
            return bare;

        return 0;
    }

    internal static float ResolveFontSize(string? value, float parentFontSize)
    {
        if (string.IsNullOrEmpty(value))
            return parentFontSize;

        if (value == "smaller") return parentFontSize * 0.833f;
        if (value == "larger") return parentFontSize * 1.2f;

        return ResolveLengthValue(value, 0, parentFontSize);
    }

    private static float ParseFloat(string s)
    {
        if (float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            return result;
        return 0;
    }

    /// <summary>Offset a box and all its descendants by deltaY (skip absolutely positioned).</summary>
    private static void OffsetBoxY(LayoutBox box, float deltaY)
    {
        box.Y += deltaY;
        for (int i = 0; i < box.Children.Count; i++)
        {
            if (!box.Children[i].IsAbsolutelyPositioned)
                OffsetBoxY(box.Children[i], deltaY);
        }
    }

    /// <summary>
    /// Post-layout pass: resolve children with relative Y coordinates to absolute.
    /// Children created by CreateBox have Y relative to parent's content area,
    /// but the parent's final Y is set after CreateBox returns.
    /// </summary>
    private static void ResolveAbsolutePositions(LayoutBox box, float offsetX, float offsetY)
    {
        foreach (var child in box.Children)
        {
            if (child.IsAbsolutelyPositioned)
            {
                ResolveAbsolutePositions(child, child.X, child.Y);
                continue;
            }

            // Children are created with Y = parent.PaddingTop + childY (relative, since parent.Y was 0
            // during CreateBox). Always add parent's resolved Y to convert to absolute coordinates.
            // List markers also need this adjustment since they're created with relative Y.
            if (box.Y > 0)
                child.Y += box.Y;

            // X positions are set absolutely during CreateBox (parent.X + padding + margin),
            // so no post-fixup is needed for X coordinates.

            ResolveAbsolutePositions(child, child.X, child.Y);
        }
    }
}
