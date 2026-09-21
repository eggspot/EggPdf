using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

/// <summary>
/// Phase 1 block layout engine. Lays out block-level children vertically.
/// </summary>
public static partial class BlockLayout
{
    private const float DefaultFontSize = 16f;

    /// <summary>U+00A0 — rendered content, never a collapsible boundary space.</summary>
    private const char NonBreakingSpace = (char)0x00A0;
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
    // The TRUE physical page dimensions, as opposed to pageWidth/pageHeight above (which are
    // the content area only, already reduced by @page margins). position:fixed's containing
    // block must be the full physical page — a "bottom: 0" footer should reach the literal
    // page edge (so it can occupy the @page margin-bottom area the author reserved for it),
    // not the content area's own bottom edge. Defaults to the content dimensions when unset,
    // matching prior behavior for callers (mostly tests) that never had a real @page margin.
    [System.ThreadStatic]
    private static float _fullPageWidthPx;
    [System.ThreadStatic]
    private static float _fullPageHeightPx;

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
        _fullPageWidthPx = pageWidth;
        _fullPageHeightPx = pageHeight;
        return LayoutDocumentInternal(document, pageWidth, pageHeight,
            (elem, parent) => resolver.Resolve(elem, parent));
    }

    /// <summary>
    /// Lay out with CascadeResolver for full CSS support (style tags, selectors, specificity, @media).
    /// </summary>
    public static LayoutBox LayoutDocument(HtmlDocument document, float pageWidth, float pageHeight,
        Css.Cascade.CascadeResolver cascadeResolver, float? fullPageWidth = null, float? fullPageHeight = null)
    {
        _threadCascadeResolver = cascadeResolver;
        _threadCounterCtx = new CssCounterContext();
        if (cascadeResolver.CounterStyleRules.Count > 0)
            _threadCounterCtx.RegisterCounterStyles(cascadeResolver.CounterStyleRules);
        _fullPageWidthPx = fullPageWidth ?? pageWidth;
        _fullPageHeightPx = fullPageHeight ?? pageHeight;
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
        // Per-render cache: without clearing, a pooled thread retains every
        // table element from every past render (unbounded growth).
        _tableColumnWidthCache?.Clear();

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
        LayoutBox parent, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle,
        FloatContext? ambientFloats = null, float floatOriginY = 0f)
    {
        var box = new LayoutBox { Element = element, Style = style };

        // Skip display:none
        if (style.Display == "none")
            return box;

        // Resolve box model values: margins, padding, width (incl. min/max and
        // auto-margin centering), and the X position/relative offset. Mutates
        // box in place; returns the values later branches need (fontSize is
        // needed to resolve every other length property on this element, not
        // just the box model, so it flows out too).
        var (fontSize, borderBox, position) = ResolveBoxModel(box, element, style, parent, containingWidth, parentStyle);

        // content-visibility: hidden -- unlike display:none, the element's own box (size,
        // background, border) still exists and is already resolved above; only its
        // descendants are skipped (they contribute nothing to its size, matching the
        // spec's implied size containment for an unsized element). Height still needs
        // resolving here since normal flow/flex/grid child-layout (which usually does it)
        // never runs.
        if (style.Get("content-visibility") == "hidden")
        {
            float? cvHeight = ResolveOptionalLength(style.Height, 0, fontSize);
            if (cvHeight.HasValue)
            {
                if (borderBox)
                {
                    box.Height = cvHeight.Value;
                    box.ContentHeight = Math.Max(0, cvHeight.Value - box.PaddingTop - box.PaddingBottom);
                }
                else
                {
                    box.ContentHeight = cvHeight.Value;
                    box.Height = cvHeight.Value + box.PaddingTop + box.PaddingBottom;
                }
            }
            else
            {
                var cvAspectRatio = AspectRatioLayout.ParseAspectRatio(style.Get("aspect-ratio"));
                if (cvAspectRatio.HasValue && cvAspectRatio.Value > 0)
                {
                    box.Height = box.Width / cvAspectRatio.Value;
                    box.ContentHeight = Math.Max(0, box.Height - box.PaddingTop - box.PaddingBottom);
                }
                else
                {
                    box.Height = box.PaddingTop + box.PaddingBottom;
                    box.ContentHeight = 0;
                }
            }

            float? cvMinHeight = ResolveOptionalLength(style.Get("min-height"), 0, fontSize);
            float? cvMaxHeight = ResolveOptionalLength(style.Get("max-height"), 0, fontSize);
            if (cvMinHeight.HasValue && box.Height < cvMinHeight.Value) box.Height = cvMinHeight.Value;
            if (cvMaxHeight.HasValue && box.Height > cvMaxHeight.Value) box.Height = cvMaxHeight.Value;

            return box;
        }

        // Flex layout: delegate to FlexLayout when display is flex
        if (style.Display == "flex")
        {
            return LayoutFlexContainer(box, element, style, parent, containingWidth, resolver, parentStyle,
                position, fontSize, borderBox);
        }

        // Grid layout: delegate to GridLayout when display is grid
        if (style.Display == "grid")
        {
            return LayoutGridContainer(box, element, style, parent, containingWidth, resolver, parentStyle,
                position, fontSize, borderBox);
        }

        return LayoutNormalFlowChildren(box, element, style, parent, containingWidth, resolver, parentStyle, fontSize, borderBox, position, ambientFloats, floatOriginY);
    }

    /// <summary>
    /// Lays out an element's children in normal flow: block/inline formatting
    /// context, floats, margin collapsing, CSS counters and ::first-line/
    /// ::first-letter pseudo-styles, ::before content, multi-column, and
    /// deferred absolutely/fixed positioned children. Split out of CreateBox
    /// as its own stage. It stays one method rather than being decomposed
    /// further: this is a single continuous stateful algorithm (childY,
    /// inlineX, float tracking, absChildren and more all thread through the
    /// entire child-node walk), so splitting it further would mean
    /// restructuring how that state is carried between pieces — a real design
    /// change with behavior risk, not a mechanical extraction.
    /// </summary>
    private static LayoutBox LayoutNormalFlowChildren(LayoutBox box, HtmlElement element, ComputedStyle style,
        LayoutBox parent, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        ComputedStyle? parentStyle, float fontSize, bool borderBox, string? position, FloatContext? ambientFloats = null,
        float floatOriginY = 0f)
    {
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
        // so that clear: left/right/both and float stacking work correctly. floatCtx also
        // narrows sibling text per line (see the shared-instance-through-ambientFloats
        // reasoning below) -- floats reuse the ambient context from an outer, non-BFC-
        // establishing block (this box's own inline content, plus normal-flow block
        // children's own descendants, all see floats registered earlier in the same BFC),
        // or start a fresh one when this box itself is a BFC root (no ambientFloats given).
        float leftFloatBottom = 0f;
        float rightFloatBottom = 0f;
        var floatCtx = ambientFloats ?? new FloatContext();

        // Collect absolutely/fixed positioned children for deferred layout
        var absChildren = new System.Collections.Generic.List<(HtmlElement elem, ComputedStyle style, string pos)>();

        // ::before pseudo-element content
        if (TryResolvePseudoContent(element, "before", style, out var beforeStyle, out var beforeContent))
        {
            float bFontSize = ResolveFontSize(beforeStyle!.FontSize, fontSize);
            float bLineHeight = TextMeasurer.GetLineHeight(bFontSize, beforeStyle.Get("line-height"));
            var textBox = new LayoutBox
            {
                Style = beforeStyle,
                X = box.X + box.PaddingLeft,
                Y = box.Y + box.PaddingTop + childY,
                Width = childContainingWidth,
                Height = bLineHeight,
                ContentWidth = TextMeasurer.MeasureWidth(beforeContent!, bFontSize,
                    beforeStyle.FontFamily, beforeStyle.FontWeight, beforeStyle.Get("font-style")),
                ContentHeight = bLineHeight,
                Text = beforeContent
            };
            box.Children.Add(textBox);
            childY += bLineHeight;
        }

        // Form element special rendering: inject value/content text for void/custom form elements
        childY = InjectFormElementContent(element, style, box, fontSize, childContainingWidth, childY);

        // Tracks a collapsed boundary space owned by the preceding text node
        // ("Căn cứ <strong>Luật</strong>": the space precedes the inline element).
        bool prevTextEndedWithSpace = false;

        // Table-row column geometry, computed once per row on first cell —
        // the previous per-cell rescans were O(cells²) per row.
        Dictionary<HtmlElement, int>? rowCellColOffsets = null;
        int rowTotalColumns = 0;

        // CSS 2.1 anonymous table-row generation (simplified): a display:table box whose
        // direct children are display:table-cell, with no display:table-row wrapper between
        // them, must still lay those cells out side-by-side as an implicit single row — the
        // common "table div > cell divs" pattern used for equal-height columns without real
        // <table> markup. Real UA stylesheets always insert a table-row layer for actual
        // <table><tr><td> markup (see BasicStyleResolver's tr -> table-row mapping), so this
        // only engages for synthetic table displays that skip the row element entirely.
        bool actsAsImplicitTableRow = false;

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

                // Images must keep their dedicated branch even when display:block
                // (the generic block path would lose ImageSource entirely).
                bool isImageElement = childElem.TagName == "img" || childElem.TagName == "picture";

                if (IsBlockLevel(childStyle.Display) && !isImageElement)
                {
                    // Flush any pending inline content
                    if (inlineX > 0)
                    {
                        childY += inlineLineHeight;
                        inlineX = 0;
                        inlineLineHeight = 0;
                    }

                    // Table row layout: cells go side-by-side (horizontal). A display:table
                    // box acts as an implicit single row for direct table-cell children that
                    // have no table-row wrapper (anonymous row generation, simplified).
                    bool isImplicitRow = style.Display == "table" && IsTableCell(childStyle.Display);
                    if ((IsTableRow(style.Display) && IsTableCell(childStyle.Display)) || isImplicitRow)
                    {
                        if (isImplicitRow) actsAsImplicitTableRow = true;
                        if (rowCellColOffsets == null)
                        {
                            rowCellColOffsets = new Dictionary<HtmlElement, int>();
                            int running = 0;
                            foreach (var rowChild in element.ChildNodes)
                            {
                                if (rowChild is HtmlElement rc)
                                {
                                    rowCellColOffsets[rc] = running;
                                    // Real <td>/<th> count towards the column total. For the
                                    // implicit-row case, every direct element child counts too
                                    // (rather than re-resolving each sibling's style here to
                                    // confirm table-cell-ness): resolving the same element's
                                    // style a second time, ahead of the normal per-child loop
                                    // below, isn't reliable for stylesheet-driven CSS (selector
                                    // matching state some resolvers keep isn't safe to query
                                    // out of document order) even though it works for inline
                                    // styles -- and a display:table box whose children aren't
                                    // ALL cells is a degenerate case not worth the extra call.
                                    // A div-based fake cell has no colspan attribute, so
                                    // GetColspan naturally returns 1 for it.
                                    if (rc.TagName == "td" || rc.TagName == "th" || isImplicitRow)
                                        running += GetColspan(rc);
                                }
                            }
                            rowTotalColumns = running;
                        }
                        int totalColumns = rowTotalColumns;
                        int colOffset = rowCellColOffsets.TryGetValue(childElem, out var cachedOffset)
                            ? cachedOffset : 0;
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

                        // Calculate X from sum of preceding column widths. In an RTL table,
                        // columns lay out right-to-left -- the first column in DOM/table-model
                        // order (colOffset 0) renders at the right edge, not the left, so the
                        // offset accumulated from preceding columns is measured from the right.
                        bool tableIsRTL = childStyle.Get("direction") == "rtl";
                        float cellX;
                        if (tableIsRTL)
                        {
                            float offsetFromRight = 0;
                            for (int ci = 0; ci < colOffset && ci < columnWidths.Length; ci++)
                                offsetFromRight += columnWidths[ci] + borderSpacing;
                            cellX = box.X + box.PaddingLeft + box.ContentWidth - offsetFromRight - cellWidth;
                        }
                        else
                        {
                            cellX = box.X + box.PaddingLeft;
                            for (int ci = 0; ci < colOffset && ci < columnWidths.Length; ci++)
                                cellX += columnWidths[ci] + borderSpacing;
                        }
                        // Update cell X and offset the whole subtree that was laid out with the
                        // old X (grandchildren too -- a cell's content is rarely just one level
                        // deep, e.g. nested divs of text, so a shift of only direct children
                        // left everything below that level stuck at the pre-shift X).
                        float deltaX = cellX - childBox.X;
                        childBox.X = cellX;
                        if (Math.Abs(deltaX) > 0.01f)
                            FlexLayout.OffsetChildren(childBox, deltaX, 0);

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
                        var floatValue = childStyle.Get("float");
                        bool isFloatChild = floatValue == "left" || floatValue == "right";

                        // clear: resolved BEFORE recursing into the child (rather than after,
                        // as previously) so floatOriginY -- passed into the child's own
                        // recursive layout below -- reflects the post-clear Y. box.Y is
                        // unreliable here (provisional/still 0 while this box's own ancestor
                        // chain hasn't been fully positioned -- see ResolveAbsolutePositions),
                        // so anything that needs this box's real position for a float lookup
                        // must be threaded down explicitly via floatOriginY instead.
                        if (!isFloatChild)
                        {
                            var clearValue = childStyle.Get("clear");
                            if (clearValue == "both" || clearValue == "left")
                                childY = Math.Max(childY, leftFloatBottom);
                            if (clearValue == "both" || clearValue == "right")
                                childY = Math.Max(childY, rightFloatBottom);
                        }

                        // A float is a new BFC root: its own descendants must not see the
                        // outer floats (isolated, fresh context if it turns out to need one),
                        // but every other normal-flow child stays in the same BFC and must
                        // see floats registered earlier in document order via floatCtx.
                        // The child's own top margin (collapsed with the previous sibling's bottom margin)
                        // is added to childY only after CreateBox returns, but its line wrapping consults
                        // the float context while it is being created: fold the margin into the origin now,
                        // or lines just below a float would still look like they sit beside it.
                        float predictedTopMargin = 0f;
                        if (!isFloatChild && floatCtx.HasFloats)
                        {
                            float predictedFontSize = ResolveFontSize(childStyle.FontSize, fontSize);
                            predictedTopMargin = ResolveLength(childStyle.MarginTop ?? childStyle.Get("margin"),
                                childContainingWidth, predictedFontSize);
                            if (hasBlockChild) predictedTopMargin = Math.Max(prevMarginBottom, predictedTopMargin);
                        }
                        float childFloatOriginY = floatOriginY + box.PaddingTop + childY + predictedTopMargin;
                        var childBox = CreateBox(childElem, childStyle, box, childContainingWidth, resolver, style,
                            ambientFloats: isFloatChild ? null : floatCtx,
                            floatOriginY: isFloatChild ? 0f : childFloatOriginY);

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

                            // Register with floatCtx so later sibling content's line-wrapping
                            // narrows around it. shape-outside gives it a non-rectangular
                            // exclusion (circle/ellipse/polygon/inset -- see ShapeOutsideParser);
                            // unsupported/absent shapes keep the plain rectangular exclusion.
                            // The registered Y uses the reliable floatOriginY-based origin, not
                            // childBox.Y (which is only relative-to-box.Y, itself possibly still
                            // provisional -- see the childFloatOriginY comment above).
                            float floatRegY = floatOriginY + box.PaddingTop + childY;
                            var shape = ShapeOutsideParser.Parse(childStyle.Get("shape-outside"),
                                childBox.Width, childBox.Height, fontSize,
                                ShapeOutsideParser.ParseThreshold(childStyle.Get("shape-image-threshold")));
                            if (floatValue == "left")
                                floatCtx.AddLeftFloat(childBox.X, floatRegY, childBox.Width, childBox.Height, shape);
                            else
                                floatCtx.AddRightFloat(childBox.X, floatRegY, childBox.Width, childBox.Height, shape);

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
                            // clear was already resolved above (before CreateBox recursed into
                            // this child), so childY here is already post-clear.

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

                    // A floated <img> leaves normal flow: pinned to the container edge, registered so
                    // following text wraps around it (also around its shape-outside)
                    var imgFloatSide = childStyle.Get("float");
                    if (imgFloatSide == "left" || imgFloatSide == "right")
                    {
                        if (inlineX > 0)
                        {
                            childY += inlineLineHeight;
                            inlineX = 0;
                            inlineLineHeight = 0;
                        }
                        AddFloatedImage(box, childElem, childStyle, imgFloatSide, imgWidth, imgHeight,
                            ResolveSrcset(childElem.GetAttribute("srcset"), imgWidth) ?? childElem.GetAttribute("src"),
                            childContainingWidth, fontSize, floatCtx, floatOriginY, ref childY,
                            ref leftFloatBottom, ref rightFloatBottom);
                        lastWasTextNode = false;
                        continue;
                    }

                    // display:block images occupy their own line
                    bool imgIsBlock = IsBlockLevel(childStyle.Display);

                    // Check if image fits on current inline line
                    if (inlineX > 0 && (imgIsBlock || inlineX + imgWidth > childContainingWidth))
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

                    if (imgIsBlock)
                    {
                        childY += imgHeight;
                        inlineX = 0;
                        inlineLineHeight = 0;
                        hasBlockChild = true;
                    }
                    else
                    {
                        inlineX += imgWidth;
                        if (imgHeight > inlineLineHeight)
                            inlineLineHeight = imgHeight;
                    }
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
                else if (childElem.TagName == "svg")
                {
                    // <svg> is a replaced element like <img>: width/height attributes size
                    // it even though it has no UA default beyond display:inline (no "svg"
                    // entry in BasicStyleResolver), and its "text content" (circle/rect/...)
                    // isn't HTML the generic inline-text path would ever find.
                    // SVG's own default viewport (no width/height given) is 300x150.
                    float svgWidth = ResolveImgDimension(childStyle.Width, childElem.GetAttribute("width"), childContainingWidth, fontSize, 300);
                    float svgHeight = ResolveImgDimension(childStyle.Height, childElem.GetAttribute("height"), 0, fontSize, 150);

                    bool svgIsBlock = IsBlockLevel(childStyle.Display);

                    if (inlineX > 0 && (svgIsBlock || inlineX + svgWidth > childContainingWidth))
                    {
                        childY += inlineLineHeight;
                        inlineX = 0;
                        inlineLineHeight = 0;
                    }

                    var svgBox = new LayoutBox
                    {
                        Element = childElem,
                        Style = childStyle,
                        X = box.X + box.PaddingLeft + inlineX,
                        Y = box.Y + box.PaddingTop + childY,
                        Width = svgWidth,
                        Height = svgHeight,
                        ContentWidth = svgWidth,
                        ContentHeight = svgHeight,
                    };
                    box.Children.Add(svgBox);

                    if (svgIsBlock)
                    {
                        childY += svgHeight;
                        inlineX = 0;
                        inlineLineHeight = 0;
                        hasBlockChild = true;
                    }
                    else
                    {
                        inlineX += svgWidth;
                        if (svgHeight > inlineLineHeight)
                            inlineLineHeight = svgHeight;
                    }
                }
                else if (childStyle.Display == "inline-block")
                {
                    // Inline-block: create a box that flows inline but has block internals
                    var childBox = CreateBox(childElem, childStyle, box, childContainingWidth, resolver, style);

                    // Shrink-to-fit: an auto-width inline-block wraps its content
                    // (CSS 2.1 §10.3.9) instead of filling the containing block.
                    if (string.IsNullOrEmpty(childStyle.Width) || childStyle.Width == "auto")
                    {
                        float contentRight = childBox.X + childBox.PaddingLeft;
                        for (int ci = 0; ci < childBox.Children.Count; ci++)
                        {
                            float r = childBox.Children[ci].X + childBox.Children[ci].Width;
                            if (r > contentRight) contentRight = r;
                        }
                        float shrunk = contentRight - childBox.X + childBox.PaddingRight;
                        if (shrunk > 0 && shrunk < childBox.Width)
                        {
                            childBox.Width = shrunk;
                            childBox.ContentWidth = shrunk - childBox.PaddingLeft - childBox.PaddingRight;
                            if (childBox.ContentWidth < 0) childBox.ContentWidth = 0;
                        }
                    }

                    float ibWidth = childBox.Width;
                    if (inlineX > 0 && inlineX + ibWidth > childContainingWidth)
                    {
                        childY += inlineLineHeight;
                        inlineX = 0;
                        inlineLineHeight = 0;
                    }

                    // text-align on the parent positions the inline-block within the line
                    float ibAlignOffset = 0;
                    if (inlineX == 0 && ibWidth < childContainingWidth)
                    {
                        var parentTextAlign = LogicalPropertyResolver.PhysicalTextAlign(style);
                        if (parentTextAlign == "center")
                            ibAlignOffset = (childContainingWidth - ibWidth) / 2;
                        else if (parentTextAlign == "right")
                            ibAlignOffset = childContainingWidth - ibWidth;
                    }

                    // X is absolute (shift descendants); Y is parent-relative and
                    // resolved in the post-layout pass.
                    float ibDeltaX = box.X + box.PaddingLeft + inlineX + ibAlignOffset - childBox.X;
                    childBox.X += ibDeltaX;
                    childBox.Y = box.Y + box.PaddingTop + childY;
                    if (Math.Abs(ibDeltaX) > 0.01f)
                        FlexLayout.OffsetChildren(childBox, ibDeltaX, 0);
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
                    if (runs.Count > 0 && prevTextEndedWithSpace)
                    {
                        var firstRun = runs[0];
                        firstRun.HasLeadingSpace = true;
                        runs[0] = firstRun;
                    }
                    prevTextEndedWithSpace = false;
                    if (runs.Count > 0)
                    {
                        LayoutInlineRuns(runs, box, childElem, ref inlineX, ref childY, ref inlineLineHeight,
                            childContainingWidth, style, fontSize, floatCtx.HasFloats ? floatCtx : null, floatOriginY);
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
                bool hasInlineSiblings = HasInlineElementSiblings(element, style, resolver);

                // If there are inline siblings, participate in inline flow
                if (hasInlineSiblings && !preserveWhitespace)
                {
                    var ilFontFamily = style.FontFamily;
                    var ilFontWeight = style.FontWeight;
                    var ilFontStyle = style.Get("font-style");
                    float ilLetterSpacing = ResolveLength(style.Get("letter-spacing"), 0, fontSize);
                    float ilLineHeight = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"),
                        ilFontFamily, ilFontWeight, ilFontStyle, textNode.Data);
                    var ilTextData = TrimHtmlText(textNode.Data);
                    if (string.IsNullOrEmpty(ilTextData)) continue;

                    // Measure the transformed text (uppercase is wider); paint-time
                    // transform is idempotent so the box text may carry it too.
                    ilTextData = ApplyTextTransformForMeasure(ilTextData, style);

                    // Split into words and lay them out inline. TrimHtmlText only trims the
                    // edges — it deliberately doesn't collapse internal whitespace runs (unlike
                    // NormalizeInlineWhitespace), so a CRLF or bare \n/\t sitting between two
                    // space-separated words must also be treated as a delimiter here, or it
                    // stays glued to the end of a "word" and reaches the glyph layer as a raw
                    // control character with no printable glyph (renders as a tofu box).
                    var words = ilTextData.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    // Thai/Lao/Khmer/Myanmar have no inter-word spaces: offer syllable-boundary pieces that rejoin space-free.
                    bool[]? ilNoSpace = null;
                    if (EggPdf.Text.SpacelessLineBreaker.Contains(ilTextData))
                        words = ExpandSpacelessWords(words, out ilNoSpace);
                    int ilWordIndex = -1;
                    foreach (var word in words)
                    {
                        ilWordIndex++;
                        bool ilJoin = ilNoSpace != null && ilNoSpace[ilWordIndex];
                        var wordWithSpace = (inlineX > 0 && !ilJoin ? " " : "") + word;
                        float wordWidth = TextMeasurer.MeasureWidth(wordWithSpace, fontSize, ilFontFamily, ilFontWeight, ilFontStyle, ilLetterSpacing);

                        // Wrap to next line if word doesn't fit
                        if (inlineX > 0 && inlineX + wordWidth > childContainingWidth)
                        {
                            childY += inlineLineHeight;
                            inlineX = 0;
                            inlineLineHeight = 0;
                            wordWithSpace = word;
                            wordWidth = TextMeasurer.MeasureWidth(word, fontSize, ilFontFamily, ilFontWeight, ilFontStyle, ilLetterSpacing);
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

                    // NBSP is rendered content, not a collapsible boundary space
                    char ilLastChar = textNode.Data.Length > 0 ? textNode.Data[textNode.Data.Length - 1] : '\0';
                    prevTextEndedWithSpace = ilLastChar != NonBreakingSpace && char.IsWhiteSpace(ilLastChar);
                    continue;
                }

                // Float-aware fallback: TextMeasurer.WrapText below wraps this whole text
                // node at one flat width, which can't express a per-line boundary that
                // varies with active floats/shape-outside. When floats are active, place
                // words one at a time instead (skipping this branch's secondary features --
                // first-letter/initial-letter, line-clamp, text-wrap:balance, hyphenation --
                // a documented simplification for the rare combination of those with floats).
                if (floatCtx.HasFloats)
                {
                    var fatFontFamily = style.FontFamily;
                    var fatFontWeight = style.FontWeight;
                    var fatFontStyle = style.Get("font-style");
                    float fatLineHeight = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"),
                        fatFontFamily, fatFontWeight, fatFontStyle, textNode.Data);
                    float fatLetterSpacing = ResolveLength(style.Get("letter-spacing"), 0, fontSize);
                    var fatWhiteSpace = style.Get("white-space") ?? "normal";
                    bool fatPreserve = fatWhiteSpace == "pre" || fatWhiteSpace == "pre-wrap" || fatWhiteSpace == "pre-line";
                    var fatText = ApplyTextTransformForMeasure(fatPreserve ? textNode.Data : TrimHtmlText(textNode.Data), style);

                    float absContainerLeft = box.X + box.PaddingLeft;
                    float absContainerRight = absContainerLeft + childContainingWidth;

                    // Words on the same visual line are accumulated into ONE LayoutBox
                    // (flushed on wrap/end) rather than one box per word -- callers that
                    // search rendered PDF text for a multi-word phrase (e.g. "Cleared
                    // content") must find it as one contiguous string, matching how the
                    // non-float WrapText path emits a single string per line.
                    string fatLineText = "";
                    float fatLineBoxX = 0f, fatLineBoxY = 0f;
                    bool fatLineHasContent = false;

                    void FlushFatLine()
                    {
                        if (!fatLineHasContent) return;
                        float lineWidth = (box.X + box.PaddingLeft + inlineX) - fatLineBoxX;
                        box.Children.Add(new LayoutBox
                        {
                            Style = style,
                            X = fatLineBoxX,
                            Y = fatLineBoxY,
                            Width = lineWidth,
                            Height = fatLineHeight,
                            ContentWidth = lineWidth,
                            ContentHeight = fatLineHeight,
                            Text = fatLineText
                        });
                        fatLineText = "";
                        fatLineHasContent = false;
                    }

                    var fatWords = fatText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var word in fatWords)
                    {
                        if (inlineX == 0)
                        {
                            float absY = floatOriginY + box.PaddingTop + childY;
                            float startX = floatCtx.GetContentStartX(absY, fatLineHeight, absContainerLeft);
                            float inset = startX - absContainerLeft;
                            if (inset > 0) inlineX = inset;
                        }

                        var wordWithSpace = (inlineX > 0 ? " " : "") + word;
                        float wordWidth = TextMeasurer.MeasureWidth(wordWithSpace, fontSize, fatFontFamily, fatFontWeight, fatFontStyle, fatLetterSpacing);

                        float absYForLimit = floatOriginY + box.PaddingTop + childY;
                        float rightOffset = floatCtx.GetRightOffset(absYForLimit, fatLineHeight, absContainerRight);
                        float rightLimit = childContainingWidth - rightOffset;
                        if (rightLimit < 0) rightLimit = 0;

                        if (inlineX > 0 && inlineX + wordWidth > rightLimit)
                        {
                            FlushFatLine();
                            childY += inlineLineHeight;
                            inlineX = 0;
                            inlineLineHeight = 0;

                            float absY2 = floatOriginY + box.PaddingTop + childY;
                            float startX2 = floatCtx.GetContentStartX(absY2, fatLineHeight, absContainerLeft);
                            float inset2 = startX2 - absContainerLeft;
                            if (inset2 > 0) inlineX = inset2;

                            wordWithSpace = word;
                            wordWidth = TextMeasurer.MeasureWidth(word, fontSize, fatFontFamily, fatFontWeight, fatFontStyle, fatLetterSpacing);
                        }

                        if (!fatLineHasContent)
                        {
                            fatLineBoxX = box.X + box.PaddingLeft + inlineX;
                            fatLineBoxY = box.Y + box.PaddingTop + childY;
                            fatLineHasContent = true;
                        }
                        fatLineText += wordWithSpace;

                        inlineX += wordWidth;
                        if (fatLineHeight > inlineLineHeight)
                            inlineLineHeight = fatLineHeight;
                    }
                    FlushFatLine();

                    char fatLastChar = textNode.Data.Length > 0 ? textNode.Data[textNode.Data.Length - 1] : '\0';
                    prevTextEndedWithSpace = fatLastChar != NonBreakingSpace && char.IsWhiteSpace(fatLastChar);
                    continue;
                }

                // Text content with line wrapping
                var fontFamily = style.FontFamily;
                var fontWeight = style.FontWeight;
                var fontStyle = style.Get("font-style");
                float lineHeight = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"),
                    fontFamily, fontWeight, fontStyle, textNode.Data);
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
                float blockLetterSpacing = ResolveLength(style.Get("letter-spacing"), 0, fontSize);

                // Wrap and measure the transformed text (uppercase is wider); the
                // paint-time transform is idempotent, so storing it in the box is safe.
                textData = ApplyTextTransformForMeasure(textData, style);

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
                    firstLineWidth > 0 ? firstLineWidth : balanceWidth, whiteSpaceProp, breakWord, enableHyphenation, blockLetterSpacing);

                // If indent caused wrapping and there are remaining lines, re-wrap with full width
                if (textIndent > 0 && lines.Count > 1)
                {
                    var firstLine = lines[0];
                    var remaining = textData.Substring(firstLine.Length).TrimStart();
                    lines = new System.Collections.Generic.List<string> { firstLine };
                    if (!string.IsNullOrEmpty(remaining))
                    {
                        var moreLines = TextMeasurer.WrapText(remaining, fontSize, fontFamily, fontWeight, fontStyle, childContainingWidth, whiteSpaceProp, breakWord, enableHyphenation, blockLetterSpacing);
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
                            ContentWidth = TextMeasurer.MeasureWidth(line, fontSize, fontFamily, fontWeight, fontStyle, blockLetterSpacing),
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
        if (TryResolvePseudoContent(element, "after", style, out var afterStyle, out var afterContent))
        {
            float aFontSize = ResolveFontSize(afterStyle!.FontSize, fontSize);
            float aLineHeight = TextMeasurer.GetLineHeight(aFontSize, afterStyle.Get("line-height"));
            var textBox = new LayoutBox
            {
                Style = afterStyle,
                X = box.X + box.PaddingLeft,
                Y = box.Y + box.PaddingTop + childY,
                Width = childContainingWidth,
                Height = aLineHeight,
                ContentWidth = TextMeasurer.MeasureWidth(afterContent!, aFontSize,
                    afterStyle.FontFamily, afterStyle.FontWeight, afterStyle.Get("font-style")),
                ContentHeight = aLineHeight,
                Text = afterContent
            };
            box.Children.Add(textBox);
            childY += aLineHeight;
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
        if ((IsTableRow(style.Display) || actsAsImplicitTableRow) && childY > 0)
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
                // An outside marker hangs off the side the text starts from: the left in LTR,
                // but the right in RTL (direction is inherited, so `style` already carries it).
                bool markerIsRTL = style.Get("direction") == "rtl";
                var markerBox = new LayoutBox
                {
                    Style = effectiveMarkerStyle,
                    IsListMarker = true,
                    X = markerIsRTL ? box.X + box.Width : box.X - markerWidth,
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
                                child!.Width = box.ContentWidth;
                                child.ContentWidth = box.ContentWidth - child.PaddingLeft - child.PaddingRight;
                                if (child.ContentWidth < 0) child.ContentWidth = 0;
                                // X is absolute: shift the whole subtree. Y is parent-relative
                                // (resolved in the post-layout pass): only the spanning box moves.
                                MultiColumnLayout.ShiftSubtreeX(child, containerX - child.X);
                                child.Y = currentY;
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
        LayoutAbsoluteChildren(absChildren, box, position, containingWidth, parent, fontSize, resolver, style);

        // Apply relative/sticky position Y offset (sticky behaves like relative in PDF — no scrolling)
        if (position == "relative" || position == "sticky")
        {
            float offsetTop = ResolveLength(style.Get("top"), 0, fontSize);
            box.Y += offsetTop;
        }

        return box;
    }

    /// <summary>
    /// Resolves margins, padding, width (with min/max clamping and auto-margin
    /// centering), and X position/relative offset for a box, mutating it in
    /// place. Split out of CreateBox as the first self-contained stage: every
    /// local it computes besides the three returned values (fontSize, whether
    /// box-sizing is border-box, and the position value) is read only within
    /// this stage and never again afterward.
    /// </summary>
    private static (float fontSize, bool borderBox, string? position) ResolveBoxModel(
        LayoutBox box, HtmlElement element, ComputedStyle style, LayoutBox parent,
        float containingWidth, ComputedStyle? parentStyle)
    {
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

        return (fontSize, borderBox, position);
    }

    /// <summary>
    /// Finishes a flex container box: delegates to FlexLayout for children,
    /// then resolves height from the specified value or children, applies
    /// min/max-height, the relative-position Y offset, and out-of-flow
    /// absolutely positioned children. Split out of CreateBox: this branch
    /// returns early and shares no state with normal-flow child layout.
    /// </summary>
    private static LayoutBox LayoutFlexContainer(LayoutBox box, HtmlElement element, ComputedStyle style,
        LayoutBox parent, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        ComputedStyle? parentStyle, string? position, float fontSize, bool borderBox)
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

        // Absolutely positioned children are out-of-flow (excluded from flex
        // items); place them now that the container's final size is known.
        var flexAbsChildren = CollectAbsoluteChildren(element, style, resolver);
        if (flexAbsChildren.Count > 0)
            LayoutAbsoluteChildren(flexAbsChildren, box, position, containingWidth, parent, fontSize, resolver, style);

        return box;
    }

    /// <summary>
    /// Finishes a grid container box: delegates to GridLayout for children,
    /// then resolves height from the specified value or children, applies
    /// min/max-height, the relative-position Y offset, and out-of-flow
    /// absolutely positioned children. Split out of CreateBox: this branch
    /// returns early and shares no state with normal-flow child layout.
    /// </summary>
    private static LayoutBox LayoutGridContainer(LayoutBox box, HtmlElement element, ComputedStyle style,
        LayoutBox parent, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        ComputedStyle? parentStyle, string? position, float fontSize, bool borderBox)
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

        // Absolutely positioned children are out-of-flow (excluded from grid
        // items); place them now that the container's final size is known.
        var gridAbsChildren = CollectAbsoluteChildren(element, style, resolver);
        if (gridAbsChildren.Count > 0)
            LayoutAbsoluteChildren(gridAbsChildren, box, position, containingWidth, parent, fontSize, resolver, style);

        return box;
    }
}
