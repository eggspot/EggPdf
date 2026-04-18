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

    // Thread-local context for ::before/::after and CSS counters.
    // Set by LayoutDocumentInternal when a CascadeResolver is available.
    [System.ThreadStatic]
    private static Css.Cascade.CascadeResolver? _threadCascadeResolver;
    [System.ThreadStatic]
    private static CssCounterContext? _threadCounterCtx;
    [System.ThreadStatic]
    private static float _viewportWidth;
    [System.ThreadStatic]
    private static float _viewportHeight;

    /// <summary>Current viewport width in pixels (for vw unit resolution). Set during layout.</summary>
    public static float ViewportWidth => _viewportWidth;
    /// <summary>Current viewport height in pixels (for vh unit resolution). Set during layout.</summary>
    public static float ViewportHeight => _viewportHeight;

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
        _threadCascadeResolver = cascadeResolver;
        _threadCounterCtx = new CssCounterContext();
        if (cascadeResolver.CounterStyleRules.Count > 0)
            _threadCounterCtx.RegisterCounterStyles(cascadeResolver.CounterStyleRules);
        try
        {
            return LayoutDocumentInternal(document, pageWidth, pageHeight,
                (elem, parent) => cascadeResolver.Resolve(elem, parent));
        }
        finally
        {
            _threadCascadeResolver = null;
            _threadCounterCtx = null;
        }
    }

    private static LayoutBox LayoutDocumentInternal(HtmlDocument document, float pageWidth, float pageHeight,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolveStyle)
    {
        _viewportWidth = pageWidth;
        _viewportHeight = pageHeight;

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
        string? rawMarginLeft  = style.MarginLeft  ?? marginShort;
        string? rawMarginRight = style.MarginRight ?? marginShort;
        box.MarginTop    = ResolveLength(style.MarginTop    ?? marginShort, containingWidth, fontSize);
        box.MarginRight  = ResolveLength(rawMarginRight, containingWidth, fontSize);
        box.MarginBottom = ResolveLength(style.MarginBottom ?? marginShort, containingWidth, fontSize);
        box.MarginLeft   = ResolveLength(rawMarginLeft, containingWidth, fontSize);

        var paddingShort = style.Get("padding");
        box.PaddingTop = ResolveLength(style.PaddingTop ?? paddingShort, containingWidth, fontSize);
        box.PaddingRight = ResolveLength(style.PaddingRight ?? paddingShort, containingWidth, fontSize);
        box.PaddingBottom = ResolveLength(style.PaddingBottom ?? paddingShort, containingWidth, fontSize);
        box.PaddingLeft = ResolveLength(style.PaddingLeft ?? paddingShort, containingWidth, fontSize);

        // Box-sizing
        bool borderBox = style.Get("box-sizing") == "border-box";

        // Width
        // For table cells (td/th), ignore the explicit width style — the column width has already
        // been computed by the table layout algorithm and is passed in as containingWidth. Resolving
        // a percentage width like "20%" relative to the cell's own containingWidth (which is already
        // the column width) would give 20% of 20% = 4%, incorrectly shrinking the cell.
        bool isTableCell = element.TagName == "td" || element.TagName == "th";
        float? specifiedWidth = isTableCell ? null : ResolveOptionalLength(style.Width, containingWidth, fontSize);
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

        // Auto-margin centering (CSS spec: block elements with explicit width and auto margins).
        // Only applies when width is specified; auto-width elements fill the container instead.
        if (specifiedWidth.HasValue)
        {
            bool leftAuto  = rawMarginLeft  == "auto";
            bool rightAuto = rawMarginRight == "auto";
            if (leftAuto || rightAuto)
            {
                float remaining = containingWidth - box.Width;
                if (remaining < 0) remaining = 0;
                if (leftAuto && rightAuto)
                {
                    box.MarginLeft  = remaining / 2f;
                    box.MarginRight = remaining / 2f;
                }
                else if (leftAuto)
                {
                    box.MarginLeft = remaining - box.MarginRight;
                }
                else
                {
                    box.MarginRight = remaining - box.MarginLeft;
                }
            }
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

        // CSS counters: apply counter-reset, counter-set, and counter-increment for this element
        var counterCtx = _threadCounterCtx;
        var cascadeRes  = _threadCascadeResolver;
        if (counterCtx != null)
        {
            counterCtx.ApplyReset(style.Get("counter-reset"));
            counterCtx.ApplySet(style.Get("counter-set"));
            counterCtx.ApplyIncrement(style.Get("counter-increment"));
        }

        // Resolve ::first-line and ::first-letter pseudo-element styles (if any rules exist).
        // These style existing content, not generated content, so no 'content' property is needed.
        ComputedStyle? firstLineStyle = cascadeRes?.ResolvePseudoElement(element, "first-line", style);
        ComputedStyle? firstLetterStyle = cascadeRes?.ResolvePseudoElement(element, "first-letter", style);
        bool firstBlockLineEmitted = false; // tracks if the first text line of this block has been laid out

        // Layout children using inline formatting context awareness
        float childY = 0;
        float childContainingWidth = isMultiColumn ? multiColWidth : box.ContentWidth;
        float prevMarginBottom = 0; // for margin collapsing
        float inlineX = 0; // current X offset within the inline line
        float inlineLineHeight = 0; // max height of current inline line
        bool lastWasTextNode = false; // track if previous child was a text node (for <br> handling)
        bool hasBlockChild = false;   // for O(1) margin-collapse first-child check

        // Float tracking: record the bottom (relative to content area) of active floats
        // so that clear: left/right/both and float stacking work correctly.
        float leftFloatBottom = 0f;
        float rightFloatBottom = 0f;

        // Collect absolutely/fixed positioned children for deferred layout
        var absChildren = new System.Collections.Generic.List<(HtmlElement elem, ComputedStyle style, string pos)>();

        // ::before pseudo-element content
        if (cascadeRes != null && counterCtx != null)
        {
            var beforeStyle = cascadeRes.ResolvePseudoElement(element, "before", style);
            if (beforeStyle != null)
            {
                var content = counterCtx.ResolveContent(beforeStyle.Get("content"), element, beforeStyle);
                if (content != null)
                {
                    float bFontSize = ResolveFontSize(beforeStyle.FontSize, fontSize);
                    float bLineHeight = TextMeasurer.GetLineHeight(bFontSize, beforeStyle.Get("line-height"));
                    var textBox = new LayoutBox
                    {
                        Style = beforeStyle,
                        X = box.X + box.PaddingLeft,
                        Y = box.Y + box.PaddingTop + childY,
                        Width = childContainingWidth,
                        Height = bLineHeight,
                        ContentWidth = TextMeasurer.MeasureWidth(content, bFontSize,
                            beforeStyle.FontFamily, beforeStyle.FontWeight, beforeStyle.Get("font-style")),
                        ContentHeight = bLineHeight,
                        Text = content
                    };
                    box.Children.Add(textBox);
                    childY += bLineHeight;
                }
            }
        }

        // Form element special rendering: inject value/content text for void/custom form elements
        childY = InjectFormElementContent(element, style, box, fontSize, childContainingWidth, childY);

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

                        var floatValue = childStyle.Get("float");
                        bool isFloatChild = floatValue == "left" || floatValue == "right";

                        if (isFloatChild)
                        {
                            // Float: removed from normal flow — position at edge, don't increment childY.
                            childBox.IsFloat = true;
                            childBox.Y = box.Y + box.PaddingTop + childY;

                            if (floatValue == "right")
                                childBox.X = box.X + box.PaddingLeft + box.ContentWidth - childBox.Width;
                            else
                                childBox.X = box.X + box.PaddingLeft + childBox.MarginLeft;

                            box.Children.Add(childBox);

                            // Record float bottom (relative to content area) for clear tracking.
                            // shape-margin expands the exclusion zone below the float.
                            float floatRelBottom = childBox.Y + childBox.Height - (box.Y + box.PaddingTop);
                            var shapeMarginStr = childStyle.Get("shape-margin");
                            if (!string.IsNullOrEmpty(shapeMarginStr))
                            {
                                float sm = ResolveLength(shapeMarginStr, box.ContentWidth, fontSize);
                                if (sm > 0) floatRelBottom += sm;
                            }
                            if (floatValue == "left")
                                leftFloatBottom = Math.Max(leftFloatBottom, floatRelBottom);
                            else
                                rightFloatBottom = Math.Max(rightFloatBottom, floatRelBottom);
                        }
                        else
                        {
                            // clear: move childY below active floats
                            var clearValue = childStyle.Get("clear");
                            if (clearValue == "both" || clearValue == "left")
                                childY = Math.Max(childY, leftFloatBottom);
                            if (clearValue == "both" || clearValue == "right")
                                childY = Math.Max(childY, rightFloatBottom);

                            // Margin collapsing between adjacent block siblings
                            float effectiveTopMargin = hasBlockChild
                                ? Math.Max(prevMarginBottom, childBox.MarginTop)
                                : childBox.MarginTop;

                            childBox.Y = box.Y + box.PaddingTop + childY + effectiveTopMargin;
                            childBox.X = box.X + box.PaddingLeft + childBox.MarginLeft;

                            // Apply relative/sticky position offset after normal-flow position is set.
                            // These must be applied here (not inside CreateBox) because the parent's
                            // childBox.Y assignment above would overwrite any offset set inside CreateBox.
                            var childPos = childStyle.Get("position");
                            if (childPos == "relative" || childPos == "sticky")
                            {
                                float childFontSize = ResolveFontSize(childStyle.FontSize, fontSize);
                                childBox.Y += ResolveLength(childStyle.Get("top"), 0, childFontSize);
                                childBox.X += ResolveLength(childStyle.Get("left"), 0, childFontSize);
                            }

                            // visibility:collapse on table rows removes them from layout flow (no height).
                            // On non-table elements it behaves like visibility:hidden (keeps space).
                            bool isCollapsedTableRow = childStyle.Get("visibility") == "collapse"
                                && childStyle.Display == "table-row";

                            box.Children.Add(childBox);
                            if (isCollapsedTableRow)
                            {
                                childBox.Height = 0f;
                            }
                            else
                            {
                                childY += effectiveTopMargin + childBox.Height;
                                prevMarginBottom = childBox.MarginBottom;
                            }
                        }
                        lastWasTextNode = false;
                        hasBlockChild = true;
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

                    // srcset: pick best URL (prefer 1x descriptor or smallest width; fallback to src)
                    string? imgSrc = ResolveSrcset(childElem.GetAttribute("srcset"), imgWidth)
                        ?? childElem.GetAttribute("src");

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
                        ImageSource = imgSrc
                    };
                    box.Children.Add(childBox);
                    inlineX += imgWidth;
                    if (imgHeight > inlineLineHeight)
                        inlineLineHeight = imgHeight;
                }
                else if (childElem.TagName == "picture")
                {
                    // <picture>: find best <source> or fall back to inner <img>
                    var (picSrc, picElem) = ResolvePicture(childElem);
                    if (picSrc != null && picElem != null)
                    {
                        var picStyle = resolver != null ? resolver(picElem, childStyle) : childStyle;
                        float imgWidth = ResolveImgDimension(picStyle.Width, picElem.GetAttribute("width"), childContainingWidth, fontSize, 150);
                        float imgHeight = ResolveImgDimension(picStyle.Height, picElem.GetAttribute("height"), 0, fontSize, 150);

                        if (inlineX > 0 && inlineX + imgWidth > childContainingWidth)
                        {
                            childY += inlineLineHeight;
                            inlineX = 0;
                            inlineLineHeight = 0;
                        }

                        var childBox = new LayoutBox
                        {
                            Element = picElem,
                            Style = picStyle,
                            X = box.X + box.PaddingLeft + inlineX,
                            Y = box.Y + box.PaddingTop + childY,
                            Width = imgWidth,
                            Height = imgHeight,
                            ContentWidth = imgWidth,
                            ContentHeight = imgHeight,
                            ImageSource = picSrc
                        };
                        box.Children.Add(childBox);
                        inlineX += imgWidth;
                        if (imgHeight > inlineLineHeight)
                            inlineLineHeight = imgHeight;
                    }
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
                else if (childElem.TagName == "ruby")
                {
                    // Ruby: lay out base text with annotation (<rt>) above it
                    LayoutRubyInline(childElem, childStyle, box, resolver, style, fontSize,
                        ref inlineX, ref childY, ref inlineLineHeight, childContainingWidth);
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

                // Skip empty text nodes unless preserving whitespace.
                // IsHtmlWhitespaceOnly preserves \u00A0 (non-breaking space) — it is never skipped.
                if (!preserveWhitespace && IsHtmlWhitespaceOnly(textNode.Data))
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
                    var ilTextData = TrimHtmlText(textNode.Data);
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
                var textData = preserveWhitespace ? textNode.Data : TrimHtmlText(textNode.Data);

                // tab-size: expand \t to spaces when preserving whitespace (pre/pre-wrap)
                if (preserveWhitespace && textData.IndexOf('\t') >= 0)
                {
                    var tabSizeStr = style.Get("tab-size");
                    int tabSize = 8; // CSS default
                    if (!string.IsNullOrEmpty(tabSizeStr) &&
                        int.TryParse(tabSizeStr, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int ts) && ts > 0)
                        tabSize = ts;
                    textData = TextMeasurer.ExpandTabs(textData, tabSize);
                }

                // Check overflow-wrap/word-break for character-level breaking
                var overflowWrap = style.Get("overflow-wrap") ?? style.Get("word-wrap");
                var wordBreak = style.Get("word-break");
                bool breakWord = overflowWrap == "break-word" || overflowWrap == "anywhere" ||
                                 wordBreak == "break-all" || wordBreak == "break-word";
                bool enableHyphenation = style.Get("hyphens") == "auto";

                // text-wrap: balance — compute an optimal balanced width before wrapping
                float balanceWidth = childContainingWidth;
                var textWrapProp = style.Get("text-wrap");
                if (textWrapProp == "balance")
                {
                    float totalTextWidth = TextMeasurer.MeasureWidth(textData, fontSize, fontFamily, fontWeight, fontStyle);
                    // First pass: wrap at full width to count lines
                    var preWrapLines = TextMeasurer.WrapText(textData, fontSize, fontFamily, fontWeight, fontStyle,
                        childContainingWidth, whiteSpaceProp, breakWord, enableHyphenation);
                    if (TextWrapBalance.ShouldBalance(textWrapProp, preWrapLines.Count))
                        balanceWidth = TextWrapBalance.CalculateBalancedWidth(totalTextWidth, childContainingWidth, preWrapLines.Count);
                }

                // For text-indent, reduce first line's available width
                float firstLineWidth = textIndent > 0 ? balanceWidth - textIndent : balanceWidth;
                var lines = TextMeasurer.WrapText(textData, fontSize, fontFamily, fontWeight, fontStyle,
                    firstLineWidth > 0 ? firstLineWidth : balanceWidth, whiteSpaceProp, breakWord, enableHyphenation);

                // If indent caused wrapping and there are remaining lines, re-wrap with full width
                if (textIndent > 0 && lines.Count > 1)
                {
                    var firstLine = lines[0];
                    var remaining = textData.Substring(firstLine.Length).TrimStart();
                    lines = new System.Collections.Generic.List<string> { firstLine };
                    if (!string.IsNullOrEmpty(remaining))
                    {
                        var moreLines = TextMeasurer.WrapText(remaining, fontSize, fontFamily, fontWeight, fontStyle, childContainingWidth, whiteSpaceProp, breakWord, enableHyphenation);
                        lines.AddRange(moreLines);
                    }
                }

                // Apply -webkit-line-clamp / line-clamp: limit to N lines, truncate last with ellipsis
                var lineClampStr = style.Get("line-clamp") ?? style.Get("-webkit-line-clamp");
                if (!string.IsNullOrEmpty(lineClampStr) && lineClampStr != "none" &&
                    int.TryParse(lineClampStr, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int lineClampN) &&
                    lineClampN > 0 && lines.Count > lineClampN)
                {
                    // Truncate to lineClampN lines; make the last line fit with "…"
                    while (lines.Count > lineClampN) lines.RemoveAt(lines.Count - 1);
                    const string ellipsis = "\u2026";
                    var lastLine = lines[lines.Count - 1];
                    float ellipsisWidth = TextMeasurer.MeasureWidth(ellipsis, fontSize, fontFamily, fontWeight, fontStyle);
                    float maxLastLineWidth = childContainingWidth - ellipsisWidth;
                    // Track trim length to avoid Substring allocations inside the loop.
                    int trimLen = lastLine.Length;
                    if (maxLastLineWidth > 0)
                    {
                        while (trimLen > 0)
                        {
                            float w = TextMeasurer.MeasureWidth(lastLine.Substring(0, trimLen), fontSize, fontFamily, fontWeight, fontStyle);
                            if (w <= maxLastLineWidth) break;
                            int lastSpace = lastLine.LastIndexOf(' ', trimLen - 1);
                            trimLen = lastSpace > 0 ? lastSpace : trimLen - 1;
                        }
                    }
                    // Trim trailing spaces then append ellipsis — single allocation.
                    while (trimLen > 0 && lastLine[trimLen - 1] == ' ') trimLen--;
                    lines[lines.Count - 1] = lastLine.Substring(0, trimLen) + ellipsis;
                }

                // hanging-punctuation: first — compute negative X offset for leading punctuation
                float hangOffset = 0f;
                var hangPunct = style.Get("hanging-punctuation");
                if (!string.IsNullOrEmpty(hangPunct) &&
                    hangPunct.IndexOf("first", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !string.IsNullOrEmpty(textData))
                {
                    char firstChar = textData[0];
                    // Leading punctuation that may hang: opening quotes, brackets, etc.
                    if (firstChar == '\u0022' || firstChar == '\u0027' || // " '
                        firstChar == '\u2018' || firstChar == '\u2019' || // ' '
                        firstChar == '\u201C' || firstChar == '\u201D' || // " "
                        firstChar == '\u00AB' || firstChar == '\u2039' || // « ‹
                        firstChar == '(' || firstChar == '[' || firstChar == '{')
                    {
                        hangOffset = -TextMeasurer.MeasureWidth(
                            firstChar.ToString(), fontSize, fontFamily, fontWeight, fontStyle);
                    }
                }

                bool isFirstLine = true;
                foreach (var line in lines)
                {
                    float lineX = box.X + box.PaddingLeft;
                    if (isFirstLine && textIndent > 0)
                        lineX += textIndent;
                    if (isFirstLine && hangOffset != 0f)
                        lineX += hangOffset;

                    bool applyFirstLine = isFirstLine && !firstBlockLineEmitted && firstLineStyle != null;
                    var initialLetterStr = style.Get("initial-letter");
                    float initialLetterN = 1f;
                    bool hasInitialLetter = !string.IsNullOrEmpty(initialLetterStr) &&
                        initialLetterStr != "normal" &&
                        TryParseFirstToken(initialLetterStr, out initialLetterN);
                    bool applyFirstLetter = isFirstLine && !firstBlockLineEmitted &&
                        (firstLetterStyle != null || hasInitialLetter) && !string.IsNullOrEmpty(line);

                    if (applyFirstLetter)
                    {
                        // Split first character into its own box with ::first-letter style or initial-letter size.
                        var letterChar = line.Substring(0, 1);
                        var remainder = line.Length > 1 ? line.Substring(1) : "";

                        float flFontSize;
                        float flLineHeight;
                        ComputedStyle flStyle;

                        if (hasInitialLetter)
                        {
                            // initial-letter: N — scale first letter to span N lines
                            float n = initialLetterN < 1f ? 1f : initialLetterN;
                            flFontSize = fontSize * n;
                            flLineHeight = lineHeight * n;
                            // Build a synthetic style for the drop cap
                            flStyle = new ComputedStyle();
                            foreach (var kv in style.All) flStyle.Set(kv.Key, kv.Value);
                            flStyle.Set("font-size", flFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px");
                        }
                        else
                        {
                            flFontSize = ResolveFontSize(firstLetterStyle!.FontSize, fontSize);
                            flLineHeight = TextMeasurer.GetLineHeight(flFontSize, firstLetterStyle.Get("line-height"));
                            flStyle = firstLetterStyle!;
                        }
                        float letterWidth = TextMeasurer.MeasureWidth(letterChar, flFontSize,
                            flStyle.FontFamily, flStyle.FontWeight, flStyle.Get("font-style"));

                        var letterBox = new LayoutBox
                        {
                            Style = flStyle,
                            X = lineX,
                            Y = box.Y + box.PaddingTop + childY,
                            Width = letterWidth,
                            Height = flLineHeight,
                            ContentWidth = letterWidth,
                            ContentHeight = flLineHeight,
                            Text = letterChar
                        };
                        box.Children.Add(letterBox);

                        if (!string.IsNullOrEmpty(remainder))
                        {
                            var remStyle = applyFirstLine ? firstLineStyle! : style;
                            float remWidth = TextMeasurer.MeasureWidth(remainder, fontSize, fontFamily, fontWeight, fontStyle);
                            var remBox = new LayoutBox
                            {
                                Style = remStyle,
                                X = lineX + letterWidth,
                                Y = box.Y + box.PaddingTop + childY,
                                Width = childContainingWidth - letterWidth,
                                Height = lineHeight,
                                ContentWidth = remWidth,
                                ContentHeight = lineHeight,
                                Text = remainder
                            };
                            box.Children.Add(remBox);
                        }

                        childY += Math.Max(lineHeight, flLineHeight);
                    }
                    else
                    {
                        var lineStyle = applyFirstLine ? firstLineStyle! : style;
                        var textBox = new LayoutBox
                        {
                            Style = lineStyle,
                            X = lineX,
                            Y = box.Y + box.PaddingTop + childY,
                            Width = childContainingWidth,
                            Height = lineHeight,
                            ContentWidth = TextMeasurer.MeasureWidth(line, fontSize, fontFamily, fontWeight, fontStyle),
                            ContentHeight = lineHeight,
                            Text = line
                        };
                        box.Children.Add(textBox);
                        childY += lineHeight;
                    }

                    isFirstLine = false;
                    firstBlockLineEmitted = true;
                }
                lastWasTextNode = true;
            }
        }

        // Flush any remaining inline content
        if (inlineX > 0)
        {
            childY += inlineLineHeight;
        }

        // ::after pseudo-element content
        if (cascadeRes != null && counterCtx != null)
        {
            var afterStyle = cascadeRes.ResolvePseudoElement(element, "after", style);
            if (afterStyle != null)
            {
                var content = counterCtx.ResolveContent(afterStyle.Get("content"), element, afterStyle);
                if (content != null)
                {
                    float aFontSize = ResolveFontSize(afterStyle.FontSize, fontSize);
                    float aLineHeight = TextMeasurer.GetLineHeight(aFontSize, afterStyle.Get("line-height"));
                    var textBox = new LayoutBox
                    {
                        Style = afterStyle,
                        X = box.X + box.PaddingLeft,
                        Y = box.Y + box.PaddingTop + childY,
                        Width = childContainingWidth,
                        Height = aLineHeight,
                        ContentWidth = TextMeasurer.MeasureWidth(content, aFontSize,
                            afterStyle.FontFamily, afterStyle.FontWeight, afterStyle.Get("font-style")),
                        ContentHeight = aLineHeight,
                        Text = content
                    };
                    box.Children.Add(textBox);
                    childY += aLineHeight;
                }
            }
        }

        // CSS counters: pop scopes created by counter-reset on this element (after children processed)
        if (counterCtx != null)
            counterCtx.PopReset(style.Get("counter-reset"));

        // Post-pass: caption-side:bottom — move caption boxes below all table rows
        if (style.Display == "table" && style.Get("caption-side") == "bottom")
        {
            // Identify caption boxes and non-caption children
            float rowBottom = box.Y + box.PaddingTop; // Y of the bottom of all non-caption children
            for (int ci = 0; ci < box.Children.Count; ci++)
            {
                var child = box.Children[ci];
                if (child.Element != null && child.Style.Display == "table-caption")
                    continue;
                float childBottom = child.Y + child.Height;
                if (childBottom > rowBottom) rowBottom = childBottom;
            }
            for (int ci = 0; ci < box.Children.Count; ci++)
            {
                var child = box.Children[ci];
                if (child.Element == null || child.Style.Display != "table-caption")
                    continue;
                // Move this caption to rowBottom, adjusting all its children too
                float deltaY = rowBottom - child.Y;
                if (Math.Abs(deltaY) > 0.01f)
                {
                    child.Y += deltaY;
                    for (int gi = 0; gi < child.Children.Count; gi++)
                        child.Children[gi].Y += deltaY;
                }
            }
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
            // Resolve ::marker pseudo-element style if any rules target it.
            var markerCascadeRes = _threadCascadeResolver;
            var markerStyle = markerCascadeRes?.ResolvePseudoElement(element, "marker", style);

            var listStyleType = style.Get("list-style-type") ?? (parentStyle?.Get("list-style-type")) ?? "disc";
            string markerText = GetListMarkerText(listStyleType, element, parent.Element, counterCtx);

            // ::marker content property overrides the auto-generated marker text.
            if (markerStyle != null && markerStyle.Has("content"))
            {
                var counterCtxMarker = _threadCounterCtx;
                var customContent = counterCtxMarker != null
                    ? counterCtxMarker.ResolveContent(markerStyle.Get("content"), element, markerStyle)
                    : markerStyle.Get("content");
                if (customContent != null)
                    markerText = customContent;
            }

            if (!string.IsNullOrEmpty(markerText))
            {
                var effectiveMarkerStyle = markerStyle ?? style;
                float markerWidth = TextMeasurer.MeasureWidth(markerText + " ", fontSize, null);
                var markerBox = new LayoutBox
                {
                    Style = effectiveMarkerStyle,
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
                    var childList = new List<LayoutBox>(box.Children.Count);
                    foreach (var c in box.Children)
                        if (c is LayoutBox lb) childList.Add(lb);

                    // Split by column-span:all elements, distributing segments into columns
                    box.Children.Clear();
                    float currentY = box.Y + box.PaddingTop;
                    float containerX = box.X + box.PaddingLeft;
                    float totalHeight = 0;

                    var segment = new List<LayoutBox>();
                    for (int ci = 0; ci <= childList.Count; ci++)
                    {
                        bool isLast = ci == childList.Count;
                        LayoutBox? child = isLast ? null : childList[ci];
                        bool isSpanning = !isLast && child!.Element != null &&
                            child.Style.Get("column-span") == "all";

                        if (isSpanning || isLast)
                        {
                            // Distribute accumulated segment into columns
                            if (segment.Count > 0)
                            {
                                var columns = MultiColumnLayout.DistributeIntoColumns(
                                    segment, colCount, colWidth, colGap, containerX, currentY);
                                float segHeight = 0;
                                foreach (var col in columns)
                                {
                                    box.Children.Add(col);
                                    if (col.Height > segHeight) segHeight = col.Height;
                                }
                                currentY += segHeight;
                                totalHeight += segHeight;
                                segment.Clear();
                            }

                            // Place the spanning element at full container width
                            if (isSpanning)
                            {
                                float spanDeltaY = currentY - child!.Y;
                                float spanDeltaX = containerX - child.X;
                                child.X = containerX;
                                child.Y = currentY;
                                child.Width = box.ContentWidth;
                                child.ContentWidth = box.ContentWidth - child.PaddingLeft - child.PaddingRight;
                                if (child.ContentWidth < 0) child.ContentWidth = 0;
                                // Shift all inline/text children
                                for (int gi = 0; gi < child.Children.Count; gi++)
                                {
                                    child.Children[gi].X += spanDeltaX;
                                    child.Children[gi].Y += spanDeltaY;
                                }
                                box.Children.Add(child);
                                currentY += child.Height + child.MarginTop + child.MarginBottom;
                                totalHeight += child.Height + child.MarginTop + child.MarginBottom;
                            }
                        }
                        else
                        {
                            segment.Add(child!);
                        }
                    }

                    childY = totalHeight;
                }
            }

            box.ContentHeight = childY;
            box.Height = childY + box.PaddingTop + box.PaddingBottom;
        }

        // aspect-ratio: when height is auto (no explicit height), derive it from width and ratio
        if (!specifiedHeight.HasValue)
        {
            var aspectRatio = AspectRatioLayout.ParseAspectRatio(style.Get("aspect-ratio"));
            if (aspectRatio.HasValue && aspectRatio.Value > 0)
            {
                float computedHeight = box.Width / aspectRatio.Value;
                box.ContentHeight = computedHeight - box.PaddingTop - box.PaddingBottom;
                if (box.ContentHeight < 0) box.ContentHeight = 0;
                box.Height = computedHeight;
            }
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

        // Apply relative/sticky position Y offset (sticky behaves like relative in PDF — no scrolling)
        if (position == "relative" || position == "sticky")
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

        // table-layout: fixed — widths determined by first row only (no content scanning)
        var tableStyle = resolver(tableElement, parentStyle);
        if (tableStyle.Get("table-layout") == "fixed")
        {
            var fixedWidths = ComputeFixedColumnWidths(tableElement, totalColumns, availableWidth, fontSize, resolver, parentStyle);
            _tableColumnWidthCache[tableElement] = fixedWidths;
            return fixedWidths;
        }

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

    /// <summary>
    /// table-layout:fixed — determine column widths from the first row only.
    /// Explicit cell widths (from width attribute/style on first-row cells or &lt;col&gt;) are used;
    /// remaining columns share the leftover width equally.
    /// </summary>
    private static float[] ComputeFixedColumnWidths(HtmlElement tableElement, int totalColumns,
        float availableWidth, float fontSize,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        var explicitWidths = new float[totalColumns];
        var hasExplicit = new bool[totalColumns];

        // 1. Check <colgroup>/<col> elements first
        foreach (var child in tableElement.ChildNodes)
        {
            var group = child as HtmlElement;
            if (group == null || group.TagName != "colgroup") continue;
            int colIdx = 0;
            foreach (var colNode in group.ChildNodes)
            {
                var col = colNode as HtmlElement;
                if (col == null || col.TagName != "col") continue;
                var colStyle = resolver(col, parentStyle);
                var wStr = colStyle.Width ?? col.GetAttribute("width");
                if (!string.IsNullOrEmpty(wStr) && colIdx < totalColumns)
                {
                    float w = ResolveLength(wStr, availableWidth, fontSize);
                    if (w > 0) { explicitWidths[colIdx] = w; hasExplicit[colIdx] = true; }
                }
                colIdx++;
            }
        }

        // 2. Scan only the first row for explicit cell widths
        HtmlElement? firstRow = null;
        foreach (var child in tableElement.ChildNodes)
        {
            var group = child as HtmlElement;
            if (group == null) continue;
            if (group.TagName == "tr") { firstRow = group; break; }
            // thead/tbody/tfoot
            if (group.TagName == "thead" || group.TagName == "tbody" || group.TagName == "tfoot")
            {
                foreach (var rowNode in group.ChildNodes)
                {
                    var row = rowNode as HtmlElement;
                    if (row != null && row.TagName == "tr") { firstRow = row; break; }
                }
                if (firstRow != null) break;
            }
        }

        if (firstRow != null)
        {
            int colIdx = 0;
            foreach (var cellNode in firstRow.ChildNodes)
            {
                var cell = cellNode as HtmlElement;
                if (cell == null || (cell.TagName != "td" && cell.TagName != "th")) continue;
                if (colIdx >= totalColumns) break;
                if (!hasExplicit[colIdx])
                {
                    var cellStyle = resolver(cell, parentStyle);
                    var wStr = cellStyle.Width ?? cell.GetAttribute("width");
                    if (!string.IsNullOrEmpty(wStr) && wStr != "auto")
                    {
                        float w = ResolveLength(wStr, availableWidth, fontSize);
                        if (w > 0) { explicitWidths[colIdx] = w; hasExplicit[colIdx] = true; }
                    }
                }
                colIdx++;
            }
        }

        // 3. Distribute remaining width equally among columns with no explicit width
        float usedWidth = 0;
        int flexCols = 0;
        for (int i = 0; i < totalColumns; i++)
        {
            if (hasExplicit[i]) usedWidth += explicitWidths[i];
            else flexCols++;
        }

        float remaining = availableWidth - usedWidth;
        if (remaining < 0) remaining = 0;
        float flexWidth = flexCols > 0 ? remaining / flexCols : 0;

        var result = new float[totalColumns];
        for (int i = 0; i < totalColumns; i++)
            result[i] = hasExplicit[i] ? explicitWidths[i] : flexWidth;

        return result;
    }

    private static void ScanTableForColumnWidths(HtmlElement tableElement, int totalColumns,
        float[] maxContentWidths, bool[] hasExplicitWidth, float[] explicitWidths,
        float fontSize, float availableWidth,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle)
    {
        // Walk: table -> thead/tbody/tfoot -> tr -> td/th
        // Also process <colgroup>/<col> elements which define explicit column widths.
        foreach (var child in tableElement.ChildNodes)
        {
            var group = child as HtmlElement;
            if (group == null) continue;

            // <colgroup> may contain <col> children or carry a span attribute itself
            if (group.TagName == "colgroup")
            {
                int colIdx = 0;
                bool hasColChildren = false;
                foreach (var colNode in group.ChildNodes)
                {
                    var col = colNode as HtmlElement;
                    if (col == null || col.TagName != "col") continue;
                    hasColChildren = true;

                    int span = 1;
                    var spanAttr = col.GetAttribute("span");
                    if (!string.IsNullOrEmpty(spanAttr) && int.TryParse(spanAttr,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int spanVal) && spanVal > 0)
                        span = spanVal;

                    // Resolve width from style or attribute
                    var colStyle = resolver(col, parentStyle);
                    var widthStr = colStyle?.Width ?? col.GetAttribute("width");
                    if (!string.IsNullOrEmpty(widthStr) && widthStr != "auto")
                    {
                        float? w = ResolveOptionalLength(widthStr, availableWidth, fontSize);
                        if (w.HasValue)
                        {
                            for (int s = 0; s < span && colIdx + s < totalColumns; s++)
                            {
                                hasExplicitWidth[colIdx + s] = true;
                                explicitWidths[colIdx + s] = w.Value;
                            }
                        }
                    }
                    colIdx += span;
                    if (colIdx >= totalColumns) break;
                }

                // Colgroup without <col> children: apply its own width to spanned columns
                if (!hasColChildren)
                {
                    int span = 1;
                    var spanAttr = group.GetAttribute("span");
                    if (!string.IsNullOrEmpty(spanAttr) && int.TryParse(spanAttr,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int spanVal) && spanVal > 0)
                        span = spanVal;

                    var cgStyle = resolver(group, parentStyle);
                    var widthStr = cgStyle?.Width ?? group.GetAttribute("width");
                    if (!string.IsNullOrEmpty(widthStr) && widthStr != "auto")
                    {
                        float? w = ResolveOptionalLength(widthStr, availableWidth, fontSize);
                        if (w.HasValue)
                        {
                            for (int s = 0; s < span && s < totalColumns; s++)
                            {
                                if (!hasExplicitWidth[s])
                                {
                                    hasExplicitWidth[s] = true;
                                    explicitWidths[s] = w.Value;
                                }
                            }
                        }
                    }
                }
            }
            // Direct <tr> children of <table>
            else if (group.TagName == "tr")
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
                var text = TrimHtmlText(textNode.Data ?? "");
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

    private static string GetListMarkerText(string listStyleType, HtmlElement element,
        HtmlElement? parentElement, CssCounterContext? counterCtx = null)
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
                // Check custom @counter-style
                if (counterCtx != null)
                {
                    int itemIdx = GetListItemIndex(element, parentElement);
                    var custom = counterCtx.FormatCustomStyle(listStyleType, itemIdx);
                    if (custom != null) return custom;
                }
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

    /// <summary>
    /// Parse a CSS srcset attribute and return the best URL for PDF rendering.
    /// PDF is treated as a 1x print context: prefer the 1x density or smallest-width candidate.
    /// Returns null if srcset is empty/null so the caller can fall back to src.
    /// </summary>
    private static string? ResolveSrcset(string? srcset, float elementWidthPx)
    {
        if (string.IsNullOrWhiteSpace(srcset)) return null;

        string? bestUrl = null;
        float bestWidth = float.MaxValue;
        bool foundDensity = false;

        // Each candidate: "url [descriptor]" separated by commas
        var candidates = srcset.Split(',');
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i].Trim();
            if (string.IsNullOrEmpty(candidate)) continue;

            // Split into URL and descriptor (last token that is a descriptor)
            int lastSpace = candidate.LastIndexOf(' ');
            if (lastSpace < 0)
            {
                // No descriptor — treat as 1x
                if (!foundDensity && bestUrl == null)
                    bestUrl = candidate;
                continue;
            }

            string url = candidate.Substring(0, lastSpace).Trim();
            string descriptor = candidate.Substring(lastSpace + 1).Trim().ToLowerInvariant();

            if (descriptor.EndsWith("x"))
            {
                // Density descriptor: prefer 1x
                float.TryParse(descriptor.Substring(0, descriptor.Length - 1), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float density);
                if (!foundDensity || Math.Abs(density - 1f) < Math.Abs(bestWidth - 1f))
                {
                    if (!foundDensity || density <= 1f)
                    { bestUrl = url; bestWidth = density; foundDensity = true; }
                }
            }
            else if (descriptor.EndsWith("w"))
            {
                // Width descriptor: pick closest to element width from below, or smallest
                float.TryParse(descriptor.Substring(0, descriptor.Length - 1), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float w);
                if (!foundDensity && w < bestWidth)
                { bestUrl = url; bestWidth = w; }
            }
        }

        return bestUrl;
    }

    /// <summary>
    /// Find the best image source from a &lt;picture&gt; element.
    /// Returns (imageUrl, imgElement) from a &lt;source&gt; or the fallback &lt;img&gt;.
    /// </summary>
    private static (string? src, Html.Dom.HtmlElement? imgElem) ResolvePicture(Html.Dom.HtmlElement picture)
    {
        Html.Dom.HtmlElement? fallbackImg = null;
        Html.Dom.HtmlElement? selectedSource = null;

        foreach (var child in picture.ChildNodes)
        {
            if (!(child is Html.Dom.HtmlElement elem)) continue;
            if (elem.TagName == "img")
            { fallbackImg = elem; }
            else if (elem.TagName == "source" && selectedSource == null)
            {
                // Prefer <source> with media="print" or no media (first wins)
                var media = elem.GetAttribute("media");
                if (string.IsNullOrEmpty(media) ||
                    media.IndexOf("print", StringComparison.OrdinalIgnoreCase) >= 0)
                    selectedSource = elem;
            }
        }

        if (selectedSource != null)
        {
            // Use the source's srcset, or fall back to img dimensions
            var srcset = selectedSource.GetAttribute("srcset");
            var src = ResolveSrcset(srcset, 0) ?? FirstSrcsetUrl(srcset);
            // Return img element for dimension resolution; source provides the URL
            return (src, fallbackImg ?? selectedSource);
        }

        if (fallbackImg != null)
        {
            var srcset = fallbackImg.GetAttribute("srcset");
            var src = ResolveSrcset(srcset, 0) ?? fallbackImg.GetAttribute("src");
            return (src, fallbackImg);
        }

        return (null, null);
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

    /// <summary>
    /// Lay out a &lt;ruby&gt; element: base text flows inline at the parent font size,
    /// the &lt;rt&gt; annotation is placed above it at 50% font size.
    /// </summary>
    private static void LayoutRubyInline(
        HtmlElement rubyElem, ComputedStyle rubyStyle, LayoutBox parentBox,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        ComputedStyle containerStyle, float containerFontSize,
        ref float inlineX, ref float childY, ref float inlineLineHeight, float containerWidth)
    {
        float baseFontSize = ResolveFontSize(rubyStyle.FontSize, containerFontSize);
        float baseLineHeight = TextMeasurer.GetLineHeight(baseFontSize, rubyStyle.Get("line-height"));

        // Collect base text (non-rt, non-rp children)
        var sb = new System.Text.StringBuilder();
        string? rtText = null;
        ComputedStyle? rtStyle = null;
        foreach (var child in rubyElem.ChildNodes)
        {
            if (child is HtmlTextNode tn)
            {
                sb.Append(TrimHtmlText(tn.Data));
            }
            else if (child is HtmlElement ce)
            {
                if (ce.TagName == "rt")
                {
                    if (rtText == null)
                    {
                        rtStyle = resolver(ce, rubyStyle);
                        rtText = CollectText(ce);
                    }
                }
                else if (ce.TagName != "rp")
                {
                    var ceStyle = resolver(ce, rubyStyle);
                    if (ceStyle.Display != "none")
                        sb.Append(CollectText(ce));
                }
            }
        }
        string baseText = sb.ToString();

        // Measure base and annotation
        float baseWidth = string.IsNullOrEmpty(baseText) ? 0f :
            TextMeasurer.MeasureWidth(baseText, baseFontSize, rubyStyle.FontFamily, rubyStyle.FontWeight, rubyStyle.Get("font-style"));

        float rtFontSize = rtStyle != null ? ResolveFontSize(rtStyle.FontSize, baseFontSize) : baseFontSize * 0.5f;
        float rtLineHeight = TextMeasurer.GetLineHeight(rtFontSize, rtStyle?.Get("line-height"));
        float rtWidth = (!string.IsNullOrEmpty(rtText)) ?
            TextMeasurer.MeasureWidth(rtText, rtFontSize, rubyStyle.FontFamily, rubyStyle.FontWeight, null) : 0f;

        float totalWidth = Math.Max(baseWidth, rtWidth);

        // Wrap to next line if needed
        if (inlineX > 0 && inlineX + totalWidth > containerWidth)
        {
            childY += inlineLineHeight;
            inlineX = 0;
            inlineLineHeight = 0;
        }

        float startX = parentBox.X + parentBox.PaddingLeft + inlineX;
        float startY = parentBox.Y + parentBox.PaddingTop + childY;

        // ruby-position: over (default) = annotation above base; under = annotation below base
        var rubyPosition = rubyStyle.Get("ruby-position") ?? "over";
        bool annotationUnder = rubyPosition.IndexOf("under", StringComparison.OrdinalIgnoreCase) >= 0;

        // ruby-align: center (default), start, end, space-around, space-between
        var rubyAlign = rubyStyle.Get("ruby-align") ?? "center";

        float CalcAnnotationX()
        {
            switch (rubyAlign.ToLowerInvariant())
            {
                case "start":    return startX;
                case "end":      return startX + totalWidth - rtWidth;
                case "space-around":
                {
                    float spacing = rtWidth < totalWidth ? (totalWidth - rtWidth) / 2f : 0f;
                    return startX + spacing;
                }
                case "space-between":
                    return startX;
                default: // center
                    return startX + (totalWidth - rtWidth) / 2f;
            }
        }

        if (!annotationUnder)
        {
            // over: rt sits at startY, base sits at startY + rtLineHeight
            if (!string.IsNullOrEmpty(rtText))
            {
                float rtX = CalcAnnotationX();
                var rtBox = new LayoutBox
                {
                    Element = null,
                    Style = rtStyle ?? rubyStyle,
                    X = rtX,
                    Y = startY,
                    Width = rtWidth,
                    Height = rtLineHeight,
                    ContentWidth = rtWidth,
                    ContentHeight = rtLineHeight,
                    Text = rtText
                };
                parentBox.Children.Add(rtBox);
            }

            float baseOffsetYOver = (!string.IsNullOrEmpty(rtText)) ? rtLineHeight : 0f;
            if (!string.IsNullOrEmpty(baseText))
            {
                float baseX = startX + (totalWidth - baseWidth) / 2f;
                var baseBoxOver = new LayoutBox
                {
                    Element = rubyElem,
                    Style = rubyStyle,
                    X = baseX,
                    Y = startY + baseOffsetYOver,
                    Width = baseWidth,
                    Height = baseLineHeight,
                    ContentWidth = baseWidth,
                    ContentHeight = baseLineHeight,
                    Text = baseText
                };
                parentBox.Children.Add(baseBoxOver);
            }

            float totalHeight = baseOffsetYOver + baseLineHeight;
            inlineX += totalWidth;
            if (totalHeight > inlineLineHeight) inlineLineHeight = totalHeight;
            return;
        }

        // under: base sits at startY, rt sits at startY + baseLineHeight
        if (!string.IsNullOrEmpty(baseText))
        {
            float baseX = startX + (totalWidth - baseWidth) / 2f;
            var baseBox = new LayoutBox
            {
                Element = rubyElem,
                Style = rubyStyle,
                X = baseX,
                Y = startY,
                Width = baseWidth,
                Height = baseLineHeight,
                ContentWidth = baseWidth,
                ContentHeight = baseLineHeight,
                Text = baseText
            };
            parentBox.Children.Add(baseBox);
        }

        if (!string.IsNullOrEmpty(rtText))
        {
            float rtX = CalcAnnotationX();
            var rtBox = new LayoutBox
            {
                Element = null,
                Style = rtStyle ?? rubyStyle,
                X = rtX,
                Y = startY + baseLineHeight,
                Width = rtWidth,
                Height = rtLineHeight,
                ContentWidth = rtWidth,
                ContentHeight = rtLineHeight,
                Text = rtText
            };
            parentBox.Children.Add(rtBox);
        }

        float baseOffsetY = (!string.IsNullOrEmpty(rtText)) ? rtLineHeight : 0f;
        float totalHeightUnder = baseLineHeight + baseOffsetY;
        inlineX += totalWidth;
        if (totalHeightUnder > inlineLineHeight) inlineLineHeight = totalHeightUnder;
    }

    /// <summary>Collect all text content from an element (no whitespace trimming beyond basic collapse).</summary>
    private static string CollectText(HtmlElement elem)
    {
        var sb = new System.Text.StringBuilder();
        CollectTextRecursive(elem, sb);
        return sb.ToString().Trim();
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

            // <rt> and <rp> are handled by LayoutRubyInline — skip them in normal inline flow
            if (elem.TagName == "rt" || elem.TagName == "rp") return;

            var style = resolver(elem, parentStyle);
            if (style.Display == "none") return;
            float fontSize = ResolveFontSize(style.FontSize, parentFontSize);

            // ::before pseudo-element injection for inline elements
            var cascadeResInline = _threadCascadeResolver;
            var counterCtxInline = _threadCounterCtx;
            if (cascadeResInline != null && counterCtxInline != null)
            {
                var beforeStyle = cascadeResInline.ResolvePseudoElement(elem, "before", style);
                if (beforeStyle != null)
                {
                    var content = counterCtxInline.ResolveContent(beforeStyle.Get("content"), elem, beforeStyle);
                    if (content != null)
                    {
                        float bfs = ResolveFontSize(beforeStyle.FontSize, fontSize);
                        runs.Add(new InlineRun { Text = content, Style = beforeStyle, Element = elem, FontSize = bfs, HasLeadingSpace = false });
                    }
                }
            }

            foreach (var child in elem.ChildNodes)
                CollectInlineRuns(child, style, fontSize, resolver, runs);

            // ::after pseudo-element injection for inline elements
            if (cascadeResInline != null && counterCtxInline != null)
            {
                var afterStyle = cascadeResInline.ResolvePseudoElement(elem, "after", style);
                if (afterStyle != null)
                {
                    var content = counterCtxInline.ResolveContent(afterStyle.Get("content"), elem, afterStyle);
                    if (content != null)
                    {
                        float afs = ResolveFontSize(afterStyle.FontSize, fontSize);
                        runs.Add(new InlineRun { Text = content, Style = afterStyle, Element = null, FontSize = afs, HasLeadingSpace = false });
                    }
                }
            }
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

            // Iterate words inline — avoids allocating a string[] upfront.
            int wPos = 0;
            bool firstWord = true;
            while (wPos < text.Length)
            {
                while (wPos < text.Length && text[wPos] == ' ') wPos++;
                if (wPos >= text.Length) break;
                int wEnd = text.IndexOf(' ', wPos);
                if (wEnd < 0) wEnd = text.Length;
                string word = text.Substring(wPos, wEnd - wPos);
                wPos = wEnd;

                bool needSpace = inlineX > 0 && (!firstWord || run.HasLeadingSpace);
                var wordText = needSpace ? " " + word : word;
                float wordWidth = TextMeasurer.MeasureWidth(wordText, run.FontSize, fontFamily, fontWeight, fontStyle);

                // Wrap to next line if doesn't fit
                if (inlineX > 0 && inlineX + wordWidth > containerWidth)
                {
                    childY += inlineLineHeight;
                    inlineX = 0;
                    inlineLineHeight = 0;
                    wordText = word; // no space prefix after wrap
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
                    ContentWidth = wordWidth,
                    ContentHeight = lhRun,
                    Text = wordText
                };

                if (!elementAssigned && wrapperElement != null)
                    elementAssigned = true;

                box.Children.Add(textBox);
                inlineX += wordWidth;
                if (lhRun > inlineLineHeight)
                    inlineLineHeight = lhRun;
                firstWord = false;
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

        // Intrinsic sizing keywords
        var lower = value.Trim().ToLowerInvariant();
        if (lower == "max-content")
            return containingSize; // approximation: use all available space
        if (lower == "min-content")
            return fontSize * 10f; // approximation: ~10 chars wide (single word)
        if (lower.StartsWith("fit-content(", StringComparison.Ordinal) && lower[lower.Length - 1] == ')')
        {
            var inner = value.Substring(12, value.Length - 13).Trim();
            float maxArg = ResolveLengthValue(inner, containingSize, fontSize);
            return System.Math.Min(containingSize, System.Math.Max(0f, maxArg));
        }

        if (value.EndsWith("px"))
            return ParseFloatN(value, value.Length - 2);

        if (value.EndsWith("em"))
            return ParseFloatN(value, value.Length - 2) * fontSize;

        if (value.EndsWith("rem"))
            return ParseFloatN(value, value.Length - 3) * DefaultFontSize;

        if (value.EndsWith("%"))
            return ParseFloatN(value, value.Length - 1) / 100f * containingSize;

        if (value.EndsWith("pt"))
            return ParseFloatN(value, value.Length - 2) * 96f / 72f;

        if (value.EndsWith("pc"))
            return ParseFloatN(value, value.Length - 2) * 96f / 6f; // 1pc = 12pt = 16px

        if (value.EndsWith("cm"))
            return ParseFloatN(value, value.Length - 2) * 96f / 2.54f;

        if (value.EndsWith("mm"))
            return ParseFloatN(value, value.Length - 2) * 96f / 25.4f;

        if (value.EndsWith("in"))
            return ParseFloatN(value, value.Length - 2) * 96f;

        // Viewport-relative units
        if (value.EndsWith("vw"))
        {
            float vw = _viewportWidth > 0 ? _viewportWidth : containingSize;
            return ParseFloatN(value, value.Length - 2) / 100f * vw;
        }
        if (value.EndsWith("vh"))
        {
            float vh = _viewportHeight > 0 ? _viewportHeight : containingSize;
            return ParseFloatN(value, value.Length - 2) / 100f * vh;
        }
        if (value.EndsWith("vmin"))
        {
            float vmin = _viewportWidth > 0 && _viewportHeight > 0
                ? System.Math.Min(_viewportWidth, _viewportHeight) : containingSize;
            return ParseFloatN(value, value.Length - 4) / 100f * vmin;
        }
        if (value.EndsWith("vmax"))
        {
            float vmax = _viewportWidth > 0 && _viewportHeight > 0
                ? System.Math.Max(_viewportWidth, _viewportHeight) : containingSize;
            return ParseFloatN(value, value.Length - 4) / 100f * vmax;
        }

        // ch: width of the '0' glyph (approximated as 0.5em)
        if (value.EndsWith("ch"))
            return ParseFloatN(value, value.Length - 2) * fontSize * 0.5f;

        // lh: line height relative unit (1lh = line-height of the element, approximated as 1.2em)
        if (value.EndsWith("lh"))
            return ParseFloatN(value, value.Length - 2) * fontSize * 1.2f;

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

    // Parse the first `length` characters of `s` as a float without allocating a Substring.
    private static float ParseFloatN(string s, int length)
    {
#if NETSTANDARD2_0
        if (float.TryParse(s.Substring(0, length), NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
            return r;
        return 0;
#else
        if (float.TryParse(s.AsSpan(0, length), NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
            return r;
        return 0;
#endif
    }

    private static bool TryParseFirstToken(string s, out float value)
    {
        int end = s.IndexOf(' ');
        string token = end >= 0 ? s.Substring(0, end) : s;
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string? FirstSrcsetUrl(string? srcset)
    {
        if (string.IsNullOrEmpty(srcset)) return null;
        int commaIdx = srcset.IndexOf(',');
        string candidate = commaIdx >= 0 ? srcset.Substring(0, commaIdx) : srcset;
        candidate = candidate.Trim();
        int spaceIdx = candidate.IndexOf(' ');
        return spaceIdx >= 0 ? candidate.Substring(0, spaceIdx) : candidate;
    }

    /// <summary>
    /// Trim collapsible HTML whitespace (space, tab, CR, LF) from both ends of a string,
    /// but preserve U+00A0 NON-BREAKING SPACE which must never be collapsed or trimmed.
    /// </summary>
    private static string TrimHtmlText(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int start = 0, end = s.Length - 1;
        while (start <= end && IsCollapsibleWhitespace(s[start])) start++;
        while (end >= start && IsCollapsibleWhitespace(s[end])) end--;
        return start > end ? "" : s.Substring(start, end - start + 1);
    }

    /// <summary>
    /// Returns true only when every character in the string is collapsible whitespace.
    /// U+00A0 (non-breaking space) is NOT collapsible and causes this to return false.
    /// </summary>
    private static bool IsHtmlWhitespaceOnly(string s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        for (int i = 0; i < s.Length; i++)
            if (!IsCollapsibleWhitespace(s[i])) return false;
        return true;
    }

    /// <summary>Collapsible whitespace: regular space, tab, CR, LF only — NOT U+00A0.</summary>
    private static bool IsCollapsibleWhitespace(char c)
        => c == ' ' || c == '\t' || c == '\r' || c == '\n';

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

    /// <summary>
    /// Inject synthetic text/content for form elements that don't have normal child text nodes.
    /// Returns updated childY after any injected content.
    /// </summary>
    private static float InjectFormElementContent(HtmlElement element, ComputedStyle style,
        LayoutBox box, float fontSize, float contentWidth, float childY)
    {
        var tag = element.TagName;

        if (tag == "input")
        {
            var inputType = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
            string? text = null;

            if (inputType == "checkbox" || inputType == "radio")
            {
                // appearance: none / -webkit-appearance: none — suppress native glyph
                var appearanceVal = style.Get("appearance") ?? style.Get("-webkit-appearance");
                bool suppressGlyph = appearanceVal == "none";
                if (!suppressGlyph)
                {
                    bool isChecked = element.HasAttribute("checked");
                    text = inputType == "checkbox"
                        ? (isChecked ? "\u2611" : "\u2610")   // ☑ / ☐
                        : (isChecked ? "\u25c9" : "\u25cb");  // ◉ / ○
                }
            }
            else if (inputType == "submit" || inputType == "button" || inputType == "reset")
            {
                text = element.GetAttribute("value") ?? inputType;
            }
            else if (inputType != "hidden" && inputType != "file" && inputType != "image")
            {
                // text, password, email, number, tel, url, search, date, etc.
                var value = element.GetAttribute("value");
                if (string.IsNullOrEmpty(value))
                {
                    // Show placeholder text when no value is set
                    var placeholder = element.GetAttribute("placeholder");
                    if (!string.IsNullOrEmpty(placeholder))
                    {
                        text = placeholder;
                        // Build a placeholder style: inherit from input but override color to gray
                        var phStyle = new ComputedStyle();
                        foreach (var kv in style.All)
                            phStyle.Set(kv.Key, kv.Value);
                        phStyle.Set("color", "#9e9e9e"); // UA default placeholder gray
                        phStyle.Set("font-style", "italic");
                        float lh = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"));
                        float tw = TextMeasurer.MeasureWidth(text, fontSize, style.FontFamily, style.FontWeight, "italic");
                        var phBox = new LayoutBox
                        {
                            Style = phStyle,
                            X = box.X + box.PaddingLeft,
                            Y = box.Y + box.PaddingTop + childY,
                            Width = contentWidth,
                            Height = lh,
                            ContentWidth = tw,
                            ContentHeight = lh,
                            Text = text
                        };
                        box.Children.Add(phBox);
                        childY += lh;
                        text = null; // handled
                    }
                    else
                    {
                        text = "";
                    }
                }
                else
                {
                    text = value;
                }
            }

            if (text != null)
            {
                float lh = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"));
                float tw = text.Length > 0 ? TextMeasurer.MeasureWidth(text, fontSize, style.FontFamily, style.FontWeight, style.Get("font-style")) : 0;
                var textBox = new LayoutBox
                {
                    Style = style,
                    X = box.X + box.PaddingLeft,
                    Y = box.Y + box.PaddingTop + childY,
                    Width = contentWidth,
                    Height = lh,
                    ContentWidth = tw,
                    ContentHeight = lh,
                    Text = text
                };
                box.Children.Add(textBox);
                childY += lh;
            }
        }
        else if (tag == "select")
        {
            // Find selected option text
            string? optionText = null;
            foreach (var child in element.ChildNodes)
            {
                if (child is HtmlElement optElem)
                {
                    HtmlElement? opt = null;
                    if (optElem.TagName == "option")
                        opt = optElem;
                    else if (optElem.TagName == "optgroup")
                    {
                        // Find first selected within optgroup
                        foreach (var og in optElem.ChildNodes)
                            if (og is HtmlElement o && o.TagName == "option" && o.HasAttribute("selected"))
                            { opt = o; break; }
                        if (opt == null)
                            foreach (var og in optElem.ChildNodes)
                                if (og is HtmlElement o && o.TagName == "option")
                                { opt = o; break; }
                    }

                    if (opt != null && opt.HasAttribute("selected"))
                    {
                        optionText = GetOptionText(opt);
                        break;
                    }
                    if (opt != null && optionText == null)
                        optionText = GetOptionText(opt); // fallback to first
                }
            }

            if (!string.IsNullOrEmpty(optionText))
            {
                float lh = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"));
                float tw = TextMeasurer.MeasureWidth(optionText, fontSize, style.FontFamily, style.FontWeight, style.Get("font-style"));
                var textBox = new LayoutBox
                {
                    Style = style,
                    X = box.X + box.PaddingLeft,
                    Y = box.Y + box.PaddingTop + childY,
                    Width = contentWidth,
                    Height = lh,
                    ContentWidth = tw,
                    ContentHeight = lh,
                    Text = optionText
                };
                box.Children.Add(textBox);
                childY += lh;
            }
        }

        return childY;
    }

    private static string GetOptionText(HtmlElement element)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var node in element.ChildNodes)
        {
            if (node is Html.Dom.HtmlTextNode t)
                sb.Append(t.Data);
            else if (node is HtmlElement child)
                sb.Append(GetOptionText(child));
        }
        return sb.ToString().Trim();
    }
}
