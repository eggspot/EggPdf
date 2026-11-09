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

    private const char NonBreakingSpace = (char)0x00A0;
    private const float DefaultLineHeight = 1.2f;
    private const float UserAgentBorderSpacing = 2f;
    private const int DefaultTabSize = 8;

    [System.ThreadStatic]
    private static Css.Cascade.CascadeResolver? _threadCascadeResolver;
    [System.ThreadStatic]
    private static CssCounterContext? _threadCounterCtx;
    [System.ThreadStatic]
    private static float _viewportWidth;
    [System.ThreadStatic]
    private static float _viewportHeight;
    [System.ThreadStatic]
    private static float _fullPageWidthPx;
    [System.ThreadStatic]
    private static float _fullPageHeightPx;

    [System.ThreadStatic]
    private static float _rootFontSize;

    internal static float RootFontSize => _rootFontSize > 0f ? _rootFontSize : DefaultFontSize;

    internal static void ResetRootFontSize() => _rootFontSize = 0f;

    /// <summary>Current viewport width in pixels (for vw unit resolution). Set during layout.</summary>
    public static float ViewportWidth => _viewportWidth;
    /// <summary>Current viewport height in pixels (for vh unit resolution). Set during layout.</summary>
    public static float ViewportHeight => _viewportHeight;

    private struct InlineRun
    {
        public string Text;
        public ComputedStyle Style;
        public HtmlElement? Element;
        public float FontSize;
        public bool HasLeadingSpace;
        public bool KeepsSpaces;
        public float StartInset;
        public float EndInset;
        public float StartMargin;
        public float EndMargin;
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
        _tableColumnWidthCache?.Clear();
        _tableGridCache?.Clear();
        _tableColumnMeasureCache?.Clear();
        _listOrdinalCursors?.Clear();

        var root = new LayoutBox
        {
            X = 0, Y = 0,
            Width = pageWidth, Height = pageHeight,
            ContentWidth = pageWidth, ContentHeight = pageHeight
        };

        if (document.Body == null) return root;

        ComputedStyle? htmlStyle = null;
        if (document.DocumentElement != null)
            htmlStyle = resolveStyle(document.DocumentElement, null);
        _rootFontSize = DefaultFontSize;
        if (htmlStyle != null && !string.IsNullOrEmpty(htmlStyle.FontSize))
            _rootFontSize = ResolveFontSize(htmlStyle.FontSize, DefaultFontSize);

        var bodyStyle = resolveStyle(document.Body, htmlStyle);
        var bodyBox = CreateBox(document.Body, bodyStyle, root, pageWidth, resolveStyle, htmlStyle);
        root.Children.Add(bodyBox);

        ResolveAbsolutePositions(root, 0, 0);

        if (bodyBox.MarginTop != 0f)
            OffsetBoxY(bodyBox, bodyBox.MarginTop);

        return root;
    }

    internal static LayoutBox CreateBox(HtmlElement element, ComputedStyle style,
        LayoutBox parent, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle? parentStyle,
        FloatContext? ambientFloats = null, float floatOriginY = 0f, bool deferInlineAlignment = false)
    {
        var box = new LayoutBox { Element = element, Style = style };

        if (style.Display == "none")
            return box;

        var (fontSize, borderBox, position) = ResolveBoxModel(box, element, style, parent, containingWidth, parentStyle);
        if (element.TagName == "table")
            ShrinkTableToContent(box, element, style, fontSize, resolver, parentStyle);

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

        if (element.TagName == "img" || element.TagName == "svg")
            return SizeReplacedElementBox(box, element, style, containingWidth, fontSize);

        if (style.Display == "flex")
        {
            return LayoutFlexContainer(box, element, style, parent, containingWidth, resolver, parentStyle,
                position, fontSize, borderBox);
        }

        if (style.Display == "grid")
        {
            return LayoutGridContainer(box, element, style, parent, containingWidth, resolver, parentStyle,
                position, fontSize, borderBox);
        }

        return LayoutNormalFlowChildren(box, element, style, parent, containingWidth, resolver, parentStyle, fontSize, borderBox, position, ambientFloats, floatOriginY, deferInlineAlignment);
    }

    private static LayoutBox LayoutNormalFlowChildren(LayoutBox box, HtmlElement element, ComputedStyle style,
        LayoutBox parent, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        ComputedStyle? parentStyle, float fontSize, bool borderBox, string? position, FloatContext? ambientFloats = null,
        float floatOriginY = 0f, bool deferInlineAlignment = false)
    {
        bool isMultiColumn = MultiColumnLayout.IsMultiColumn(style);
        float multiColWidth = box.ContentWidth;
        if (isMultiColumn)
        {
            var (colCount, colWidth, colGap) = MultiColumnLayout.ResolveColumns(style, box.ContentWidth, fontSize);
            if (colCount > 1)
                multiColWidth = colWidth;
        }

        var counterCtx = _threadCounterCtx;
        var cascadeRes  = _threadCascadeResolver;
        if (counterCtx != null)
        {
            counterCtx.ApplyReset(style.Get("counter-reset"));
            counterCtx.ApplySet(style.Get("counter-set"));
            counterCtx.ApplyIncrement(style.Get("counter-increment"));
        }

        ComputedStyle? firstLineStyle = cascadeRes?.ResolvePseudoElement(element, "first-line", style);
        ComputedStyle? firstLetterStyle = cascadeRes?.ResolvePseudoElement(element, "first-letter", style);
        bool firstBlockLineEmitted = false;
        float initialLetterScale = InitialLetterScale(style);
        bool firstLetterPending = firstLetterStyle != null || initialLetterScale > 0f;

        float childY = 0;
        float childContainingWidth = isMultiColumn ? multiColWidth : box.ContentWidth;
        float inlineWrapWidth = BalancedInlineWrapWidth(element, style, childContainingWidth, fontSize, resolver);
        int lineClamp = LineClampCount(style);
        float prevMarginBottom = 0;
        float childYAfterLastBlock = float.NaN;
        float marginCollapsedThroughTop = 0f;
        bool collapsesTopWithChildren = !isMultiColumn && CollapsesMarginsWithChildren(element, style, parentStyle);
        bool collapsesBottomWithChildren = collapsesTopWithChildren && element.TagName != "body";
        float inlineX = 0;
        float inlineLineHeight = 0;
        bool lastWasTextNode = false;
        bool hasBlockChild = false;

        float leftFloatBottom = 0f;
        float rightFloatBottom = 0f;
        var floatCtx = ambientFloats ?? new FloatContext();

        var absChildren = new System.Collections.Generic.List<(HtmlElement elem, ComputedStyle style, string pos)>();

        bool prevTextEndedWithSpace = false;
        bool prevInlineElementEndedWithSpace = false;

        bool? childrenFlowInline = null;
        var afterPseudoStyle = ResolvePseudoElementStyle(element, "after", style);
        if (afterPseudoStyle?.Display == "none") afterPseudoStyle = null;
        bool hasBefore = TryResolvePseudoContent(element, "before", style, out var beforeStyle, out var beforeContent);
        if (beforeStyle?.Display == "none")
        {
            hasBefore = false;
            beforeStyle = null;
        }
        bool pseudoElementsCanFlowInline = false;
        if ((hasBefore && GeneratesInlineBox(beforeStyle)) || GeneratesInlineBox(afterPseudoStyle))
        {
            childrenFlowInline = HasInlineElementSiblings(element, style, resolver);
            pseudoElementsCanFlowInline = !PreservesWhitespace(style) &&
                (childrenFlowInline.Value || PlainTextKeepsItsFeaturesInInlineFlow(style, firstLineStyle, firstLetterStyle));
        }

        bool afterIsInline = pseudoElementsCanFlowInline && GeneratesInlineBox(afterPseudoStyle);
        bool beforeIsInline = hasBefore && pseudoElementsCanFlowInline && GeneratesInlineBox(beforeStyle);

        float firstLineIndent = 0f;
        float firstLineIndentY = float.NaN;
        float textIndent = FirstLineTextIndent(style, childContainingWidth, fontSize);
        bool firstLineIsBlockBefore = hasBefore && !beforeIsInline;
        if (textIndent != 0f && !PreservesWhitespace(style) && !firstLineIsBlockBefore)
        {
            childrenFlowInline ??= HasInlineElementSiblings(element, style, resolver);
            if (childrenFlowInline.Value || beforeIsInline || afterIsInline)
            {
                firstLineIndent = textIndent;
                firstLineIndentY = childY;
                inlineX = textIndent;
            }
        }

        bool LineHasContent() => inlineX > LineStartFor(childY, firstLineIndentY, firstLineIndent);

        void SplitFirstLetterIfPending(System.Collections.Generic.List<InlineRun> runs)
        {
            if (!firstLetterPending || firstBlockLineEmitted || hasBlockChild) return;
            if (SplitFirstLetterRun(runs, style, firstLetterStyle, initialLetterScale))
            {
                firstLetterPending = false;
                firstBlockLineEmitted = true;
            }
        }

        if (hasBefore)
        {
            if (beforeIsInline)
            {
                LayoutInlinePseudoElement(beforeContent!, beforeStyle!, false, box, ref inlineX, ref childY, ref inlineLineHeight,
                    inlineWrapWidth, style, fontSize, floatCtx.HasFloats ? floatCtx : null, floatOriginY,
                    LineStartFor(childY, firstLineIndentY, firstLineIndent));
                prevInlineElementEndedWithSpace = EndsWithCollapsibleWhitespace(beforeContent!);
            }
            else
            {
                childY = LayoutPseudoElementLine(beforeContent!, beforeStyle!, box, childY, childContainingWidth, fontSize);
            }
        }
        else if (IsBlockLevelEmptyStringPseudoElement(beforeStyle))
        {
            childY = LayoutEmptyPseudoElementBox(beforeStyle!, box, childY, childContainingWidth, fontSize);
        }
        bool hasInlinePseudoContent = beforeIsInline || afterIsInline;

        childY = InjectFormElementContent(element, style, box, fontSize, childContainingWidth, childY);

        Dictionary<HtmlElement, int>? rowCellColOffsets = null;
        int rowTotalColumns = 0;

        bool actsAsImplicitTableRow = false;

        foreach (var childNode in TableChildrenInRenderOrder(element, style))
        {
            if (childNode is HtmlElement childElem)
            {
                prevInlineElementEndedWithSpace = false;
                var childStyle = resolver(childElem, style);

                if (childStyle.Display == "none")
                    continue;

                var childPosition = childStyle.Get("position");
                if (childPosition == "absolute" || childPosition == "fixed")
                {
                    absChildren.Add((childElem, childStyle, childPosition));
                    continue;
                }

                bool isImageElement = childElem.TagName == "img" || childElem.TagName == "picture";

                if (IsBlockLevel(childStyle.Display) && !isImageElement)
                {
                    if (LineHasContent())
                    {
                        MarkForcedLineEnd(box);
                        childY += inlineLineHeight;
                        inlineLineHeight = 0;
                    }
                    inlineX = 0;
                    firstLineIndentY = float.NaN;

                    bool isImplicitRow = style.Display == "table" && IsTableCell(childStyle.Display);
                    if ((IsTableRow(style.Display) && IsTableCell(childStyle.Display)) || isImplicitRow)
                    {
                        if (isImplicitRow) actsAsImplicitTableRow = true;
                        int gridColumnCount = 0;
                        var gridSlot = isImplicitRow ? null : CellSlot(element, childElem, out gridColumnCount);
                        if (gridSlot == null && rowCellColOffsets == null)
                        {
                            rowCellColOffsets = new Dictionary<HtmlElement, int>();
                            int running = 0;
                            foreach (var rowChild in element.ChildNodes)
                            {
                                if (rowChild is HtmlElement rc)
                                {
                                    rowCellColOffsets[rc] = running;
                                    if (rc.TagName == "td" || rc.TagName == "th" || isImplicitRow)
                                        running += GetColspan(rc);
                                }
                            }
                            rowTotalColumns = running;
                        }
                        int totalColumns, colOffset, colspan, rowspan;
                        if (gridSlot is { } slot)
                        {
                            totalColumns = gridColumnCount;
                            colOffset = slot.Column;
                            colspan = slot.ColSpan;
                            rowspan = slot.RowSpan;
                        }
                        else
                        {
                            totalColumns = rowTotalColumns;
                            colOffset = rowCellColOffsets!.TryGetValue(childElem, out var cachedOffset) ? cachedOffset : 0;
                            colspan = GetColspan(childElem);
                            rowspan = 1;
                        }

                        var borderCollapse = style.Get("border-collapse") ?? parentStyle?.Get("border-collapse");
                        bool isCollapse = borderCollapse == "collapse";

                        float borderSpacing = 0;
                        if (!isCollapse)
                        {
                            var spacingVal = style.Get("border-spacing") ?? parentStyle?.Get("border-spacing");
                            borderSpacing = !string.IsNullOrEmpty(spacingVal) ? ResolveLength(spacingVal, 0, fontSize) : UserAgentBorderSpacing;
                        }

                        float spacingTotal = totalColumns > 1 ? borderSpacing * (totalColumns - 1) : 0;
                        float availableForCells = childContainingWidth - spacingTotal;

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
                        float deltaX = cellX - childBox.X;
                        childBox.X = cellX;
                        if (Math.Abs(deltaX) > 0.01f)
                            FlexLayout.OffsetChildren(childBox, deltaX, 0);

                        if (isCollapse)
                        {
                            if (colOffset > 0)
                                childBox.Style.Set("border-left-width", "0");
                            if (!IsFirstRowInTable(element))
                                childBox.Style.Set("border-top-width", "0");
                        }

                        box.Children.Add(childBox);

                        if (rowspan == 1 && childBox.Height > childY)
                            childY = childBox.Height;
                    }
                    else
                    {
                        var floatValue = childStyle.Get("float");
                        bool isFloatChild = floatValue == "left" || floatValue == "right";

                        if (!isFloatChild)
                        {
                            var clearValue = childStyle.Get("clear");
                            if (clearValue == "both" || clearValue == "left")
                                childY = Math.Max(childY, leftFloatBottom);
                            if (clearValue == "both" || clearValue == "right")
                                childY = Math.Max(childY, rightFloatBottom);
                        }

                        float predictedTopMargin = 0f;
                        if (!isFloatChild && floatCtx.HasFloats)
                        {
                            float predictedFontSize = ResolveFontSize(childStyle.FontSize, fontSize);
                            predictedTopMargin = ResolveLength(childStyle.MarginTop ?? childStyle.Get("margin"),
                                childContainingWidth, predictedFontSize);
                            if (hasBlockChild) predictedTopMargin = CollapseMargins(prevMarginBottom, predictedTopMargin);
                            else if (childY == 0f && collapsesTopWithChildren && box.PaddingTop == 0f) predictedTopMargin = 0f;
                        }
                        float childFloatOriginY = floatOriginY + box.PaddingTop + childY + predictedTopMargin;
                        float childBoxContainingWidth = isFloatChild
                            ? FloatShrinkToFitContainingWidth(childElem, childStyle, childContainingWidth, fontSize, resolver)
                            : childContainingWidth;
                        var childBox = CreateBox(childElem, childStyle, box, childBoxContainingWidth, resolver, style,
                            ambientFloats: isFloatChild ? null : floatCtx,
                            floatOriginY: isFloatChild ? 0f : childFloatOriginY);

                        if (isFloatChild)
                        {
                            childBox.IsFloat = true;
                            childBox.Y = box.Y + box.PaddingTop + childY;

                            float floatTargetX = floatValue == "right"
                                ? box.X + box.PaddingLeft + box.ContentWidth - childBox.Width - childBox.MarginRight
                                : box.X + box.PaddingLeft + childBox.MarginLeft;
                            float floatDeltaX = floatTargetX - childBox.X;
                            childBox.X = floatTargetX;
                            if (Math.Abs(floatDeltaX) > 0.01f)
                                FlexLayout.OffsetChildren(childBox, floatDeltaX, 0f);

                            box.Children.Add(childBox);

                            float floatRegY = floatOriginY + box.PaddingTop + childY;
                            var shape = ShapeOutsideParser.Parse(childStyle.Get("shape-outside"),
                                childBox.Width, childBox.Height, fontSize,
                                ShapeOutsideParser.ParseThreshold(childStyle.Get("shape-image-threshold")));
                            if (floatValue == "left")
                                floatCtx.AddLeftFloat(childBox.X, floatRegY, childBox.Width, childBox.Height, shape);
                            else
                                floatCtx.AddRightFloat(childBox.X, floatRegY, childBox.Width, childBox.Height, shape);

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
                            float effectiveTopMargin = hasBlockChild
                                ? CollapseMargins(prevMarginBottom, childBox.MarginTop)
                                : childBox.MarginTop;
                            if (!hasBlockChild && childY == 0f && collapsesTopWithChildren && box.PaddingTop == 0f)
                            {
                                marginCollapsedThroughTop = childBox.MarginTop;
                                effectiveTopMargin = 0f;
                            }

                            childBox.Y = box.Y + box.PaddingTop + childY + effectiveTopMargin;
                            childBox.X = box.X + box.PaddingLeft + childBox.MarginLeft;

                            var childPos = childStyle.Get("position");
                            if (childPos == "relative" || childPos == "sticky")
                            {
                                float childFontSize = ResolveFontSize(childStyle.FontSize, fontSize);
                                childBox.Y += RelativeOffsetY(childStyle, childFontSize);
                                childBox.X += RelativeOffsetX(childStyle, childFontSize);
                            }

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
                                childYAfterLastBlock = childY;
                            }
                        }
                        lastWasTextNode = false;
                        hasBlockChild = true;
                    }
                }
                else if (childElem.TagName == "br")
                {
                    float lineHeight = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"));
                    if (LineHasContent())
                    {
                        MarkForcedLineEnd(box);
                        childY += Math.Max(inlineLineHeight, lineHeight);
                        inlineLineHeight = 0;
                    }
                    else if (!lastWasTextNode)
                    {
                        childY += lineHeight;
                    }
                    inlineX = 0;
                    lastWasTextNode = false;
                }
                else if (childElem.TagName == "img" || childElem.TagName == "picture")
                {
                    var imgElem = childElem;
                    var imgStyle = childStyle;
                    string? imgSrc;
                    if (childElem.TagName == "picture")
                    {
                        var (picSrc, picElem) = ResolvePicture(childElem);
                        if (picSrc == null || picElem == null) continue;
                        imgElem = picElem;
                        imgStyle = resolver != null ? resolver(picElem, childStyle) : childStyle;
                        imgSrc = picSrc;
                    }
                    else
                    {
                        imgSrc = ResolveSrcset(childElem.GetAttribute("srcset"),
                                ExplicitImageWidth(childStyle, childElem, childContainingWidth, fontSize) ?? DefaultImageWidthPx)
                            ?? childElem.GetAttribute("src");
                    }
                    var (imgWidth, imgHeight) = ResolveImageBoxSize(imgStyle, imgElem, imgSrc, childContainingWidth, fontSize);

                    var imgFloatSide = FloatedImageSide(childStyle, imgStyle);
                    if (imgFloatSide != null)
                    {
                        if (LineHasContent())
                        {
                            MarkForcedLineEnd(box);
                            childY += inlineLineHeight;
                            inlineX = 0;
                            inlineLineHeight = 0;
                        }
                        AddFloatedImage(box, imgElem, imgStyle, imgFloatSide, imgWidth, imgHeight, imgSrc,
                            childContainingWidth, fontSize, floatCtx, floatOriginY, ref childY,
                            ref leftFloatBottom, ref rightFloatBottom);
                        lastWasTextNode = false;
                        continue;
                    }

                    bool imgIsBlock = IsBlockLevel(childStyle.Display) ||
                                      (imgElem != childElem && IsBlockLevel(imgStyle.Display));

                    if (LineHasContent() && (imgIsBlock || inlineX + imgWidth > childContainingWidth))
                    {
                        if (imgIsBlock) MarkForcedLineEnd(box);
                        childY += inlineLineHeight;
                        inlineX = 0;
                        inlineLineHeight = 0;
                    }

                    var childBox = new LayoutBox
                    {
                        Element = imgElem,
                        Style = imgStyle,
                        InlineLineTop = imgIsBlock ? float.NaN : childY,
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
                        firstLineIndentY = float.NaN;
                        hasBlockChild = true;
                    }
                    else
                    {
                        inlineX += imgWidth;
                        if (imgHeight > inlineLineHeight)
                            inlineLineHeight = imgHeight;
                    }
                }
                else if (childElem.TagName == "svg")
                {
                    var (svgWidth, svgHeight) = ResolveSvgBoxSize(childStyle, childElem, childContainingWidth, fontSize);

                    bool svgIsBlock = IsBlockLevel(childStyle.Display);

                    if (LineHasContent() && (svgIsBlock || inlineX + svgWidth > childContainingWidth))
                    {
                        if (svgIsBlock) MarkForcedLineEnd(box);
                        childY += inlineLineHeight;
                        inlineX = 0;
                        inlineLineHeight = 0;
                    }

                    var svgBox = new LayoutBox
                    {
                        Element = childElem,
                        Style = childStyle,
                        InlineLineTop = svgIsBlock ? float.NaN : childY,
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
                        firstLineIndentY = float.NaN;
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
                    bool ibShrinkToFit = string.IsNullOrEmpty(childStyle.Width) || childStyle.Width == "auto";
                    var childBox = CreateBox(childElem, childStyle, box, childContainingWidth, resolver, style,
                        deferInlineAlignment: ibShrinkToFit);

                    if (ibShrinkToFit)
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
                        FinishInlineLines(childBox, childBox.ContentWidth, null, 0f);
                    }

                    float ibWidth = childBox.Width;
                    if (LineHasContent() && inlineX + ibWidth > childContainingWidth)
                    {
                        childY += inlineLineHeight;
                        inlineX = 0;
                        inlineLineHeight = 0;
                    }

                    float ibDeltaX = box.X + box.PaddingLeft + inlineX - childBox.X;
                    childBox.X += ibDeltaX;
                    childBox.Y = box.Y + box.PaddingTop + childY;
                    childBox.InlineLineTop = childY;
                    if (Math.Abs(ibDeltaX) > 0.01f)
                        FlexLayout.OffsetChildren(childBox, ibDeltaX, 0);
                    box.Children.Add(childBox);
                    inlineX += ibWidth;
                    if (childBox.Height > inlineLineHeight) inlineLineHeight = childBox.Height;
                }
                else if (childElem.TagName == "ruby")
                {
                    LayoutRubyInline(childElem, childStyle, box, resolver, style, fontSize,
                        ref inlineX, ref childY, ref inlineLineHeight, childContainingWidth);
                }
                else
                {
                    var runs = new System.Collections.Generic.List<InlineRun>();
                    CollectInlineRuns(childElem, style, fontSize, resolver, runs);
                    if (runs.Count > 0 && prevTextEndedWithSpace)
                    {
                        var firstRun = runs[0];
                        firstRun.HasLeadingSpace = true;
                        runs[0] = firstRun;
                    }
                    prevTextEndedWithSpace = false;
                    if (runs.Count > 0)
                    {
                        SplitFirstLetterIfPending(runs);
                        LayoutInlineRuns(runs, box, childElem, ref inlineX, ref childY, ref inlineLineHeight,
                            inlineWrapWidth, style, fontSize, floatCtx.HasFloats ? floatCtx : null, floatOriginY,
                            LineStartFor(childY, firstLineIndentY, firstLineIndent));
                        prevInlineElementEndedWithSpace = EndsWithCollapsibleWhitespace(runs[runs.Count - 1].Text);
                    }
                    else
                    {
                        var childBox = new LayoutBox
                        {
                            Element = childElem,
                            Style = childStyle,
                            InlineLineTop = childY,
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

                if (!preserveWhitespace && IsHtmlWhitespaceOnly(textNode.Data))
                {
                    if (textNode.Data.Length > 0)
                        prevTextEndedWithSpace = true;
                    prevInlineElementEndedWithSpace = false;
                    continue;
                }

                childrenFlowInline ??= HasInlineElementSiblings(element, style, resolver);
                bool hasInlineSiblings = hasInlinePseudoContent || childrenFlowInline.Value;

                if (hasInlineSiblings && preserveWhitespace)
                {
                    var preservedRuns = new System.Collections.Generic.List<InlineRun>();
                    AddPreservedTextRuns(textNode.Data, style, fontSize, preservedRuns);
                    SplitFirstLetterIfPending(preservedRuns);
                    LayoutInlineRuns(preservedRuns, box, null, ref inlineX, ref childY, ref inlineLineHeight,
                        inlineWrapWidth, style, fontSize, floatCtx.HasFloats ? floatCtx : null, floatOriginY,
                        LineStartFor(childY, firstLineIndentY, firstLineIndent));
                    prevTextEndedWithSpace = false;
                    prevInlineElementEndedWithSpace = false;
                    continue;
                }

                if (hasInlineSiblings && !preserveWhitespace)
                {
                    var textRuns = new System.Collections.Generic.List<InlineRun>(1)
                    {
                        new InlineRun
                        {
                            Text = textNode.Data,
                            Style = style,
                            Element = null,
                            FontSize = fontSize,
                            HasLeadingSpace = StartsWithCollapsibleWhitespace(textNode.Data)
                                || prevInlineElementEndedWithSpace || prevTextEndedWithSpace,
                        }
                    };
                    SplitFirstLetterIfPending(textRuns);
                    LayoutInlineRuns(textRuns, box, null, ref inlineX, ref childY, ref inlineLineHeight,
                        inlineWrapWidth, style, fontSize, floatCtx.HasFloats ? floatCtx : null, floatOriginY,
                        LineStartFor(childY, firstLineIndentY, firstLineIndent));

                    char ilLastChar = textNode.Data.Length > 0 ? textNode.Data[textNode.Data.Length - 1] : '\0';
                    prevTextEndedWithSpace = ilLastChar != NonBreakingSpace && char.IsWhiteSpace(ilLastChar);
                    prevInlineElementEndedWithSpace = false;
                    continue;
                }

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
                            InlineLineTop = childY,
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
                    bool fatFirstWordHasLeadingSpace = StartsWithCollapsibleWhitespace(textNode.Data) || prevTextEndedWithSpace;
                    int fatWordIndex = -1;
                    foreach (var word in fatWords)
                    {
                        fatWordIndex++;
                        bool fatAtLineStart = inlineX == 0;
                        if (fatAtLineStart)
                        {
                            float absY = floatOriginY + box.PaddingTop + childY;
                            float startX = floatCtx.GetContentStartX(absY, fatLineHeight, absContainerLeft);
                            float inset = startX - absContainerLeft;
                            if (inset > 0) inlineX = inset;
                        }

                        bool fatSpaceBefore = !fatAtLineStart && (fatWordIndex > 0 || fatFirstWordHasLeadingSpace);
                        var wordWithSpace = (fatSpaceBefore ? " " : "") + word;
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
                    prevInlineElementEndedWithSpace = false;
                    continue;
                }

                var fontFamily = style.FontFamily;
                var fontWeight = style.FontWeight;
                var fontStyle = style.Get("font-style");
                float lineHeight = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"),
                    fontFamily, fontWeight, fontStyle, textNode.Data);
                var textData = preserveWhitespace ? textNode.Data : TrimHtmlText(textNode.Data);

                if (preserveWhitespace && textData.IndexOf('\t') >= 0)
                {
                    var tabSizeStr = style.Get("tab-size");
                    int tabSize = DefaultTabSize;
                    if (!string.IsNullOrEmpty(tabSizeStr) &&
                        int.TryParse(tabSizeStr, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int ts) && ts > 0)
                        tabSize = ts;
                    textData = TextMeasurer.ExpandTabs(textData, tabSize);
                }

                var overflowWrap = style.Get("overflow-wrap") ?? style.Get("word-wrap");
                var wordBreak = style.Get("word-break");
                bool breakWord = overflowWrap == "break-word" || overflowWrap == "anywhere" ||
                                 wordBreak == "break-all" || wordBreak == "break-word";
                bool enableHyphenation = style.Get("hyphens") == "auto";
                float blockLetterSpacing = ResolveLength(style.Get("letter-spacing"), 0, fontSize);

                textData = ApplyTextTransformForMeasure(textData, style);

                float balanceWidth = childContainingWidth;
                var textWrapProp = style.Get("text-wrap");
                if (textWrapProp == "balance")
                {
                    float totalTextWidth = TextMeasurer.MeasureWidth(textData, fontSize, fontFamily, fontWeight, fontStyle);
                    var preWrapLines = TextMeasurer.WrapText(textData, fontSize, fontFamily, fontWeight, fontStyle,
                        childContainingWidth, whiteSpaceProp, breakWord, enableHyphenation);
                    if (TextWrapBalance.ShouldBalance(textWrapProp, preWrapLines.Count))
                        balanceWidth = TextWrapBalance.CalculateBalancedWidth(totalTextWidth, childContainingWidth, preWrapLines.Count);
                }

                float firstLineWidth = balanceWidth - textIndent;
                var lines = TextMeasurer.WrapText(textData, fontSize, fontFamily, fontWeight, fontStyle,
                    firstLineWidth > 0 ? firstLineWidth : balanceWidth, whiteSpaceProp, breakWord, enableHyphenation, blockLetterSpacing);

                if (textIndent != 0f && lines.Count > 1)
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

                if (lineClamp > 0 && lines.Count > lineClamp)
                {
                    while (lines.Count > lineClamp) lines.RemoveAt(lines.Count - 1);
                    const string ellipsis = "\u2026";
                    var lastLine = lines[lines.Count - 1];
                    float ellipsisWidth = TextMeasurer.MeasureWidth(ellipsis, fontSize, fontFamily, fontWeight, fontStyle);
                    float maxLastLineWidth = childContainingWidth - ellipsisWidth;
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
                    while (trimLen > 0 && lastLine[trimLen - 1] == ' ') trimLen--;
                    lines[lines.Count - 1] = lastLine.Substring(0, trimLen) + ellipsis;
                }

                float hangOffset = 0f;
                var hangPunct = style.Get("hanging-punctuation");
                if (!string.IsNullOrEmpty(hangPunct) &&
                    hangPunct.IndexOf("first", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !string.IsNullOrEmpty(textData))
                {
                    char firstChar = textData[0];
                    if (firstChar == '\u0022' || firstChar == '\u0027' ||
                        firstChar == '\u2018' || firstChar == '\u2019' ||
                        firstChar == '\u201C' || firstChar == '\u201D' ||
                        firstChar == '\u00AB' || firstChar == '\u2039' ||
                        firstChar == '(' || firstChar == '[' || firstChar == '{')
                    {
                        hangOffset = -TextMeasurer.MeasureWidth(
                            firstChar.ToString(), fontSize, fontFamily, fontWeight, fontStyle);
                    }
                }

                bool isFirstLine = true;
                int lineIndex = 0;
                foreach (var line in lines)
                {
                    bool isLastLine = lineIndex++ == lines.Count - 1;
                    float lineX = box.X + box.PaddingLeft;
                    if (isFirstLine)
                        lineX += textIndent;
                    if (isFirstLine && hangOffset != 0f)
                        lineX += hangOffset;

                    bool applyFirstLine = isFirstLine && !firstBlockLineEmitted && firstLineStyle != null;
                    bool hasInitialLetter = initialLetterScale > 0f;
                    bool applyFirstLetter = isFirstLine && !firstBlockLineEmitted &&
                        (firstLetterStyle != null || hasInitialLetter) && !string.IsNullOrEmpty(line);

                    if (applyFirstLetter)
                    {
                        int letterLength = FirstLetterLength(line);
                        var letterChar = line.Substring(0, letterLength);
                        var remainder = line.Substring(letterLength);

                        float flFontSize;
                        float flLineHeight;
                        ComputedStyle flStyle;

                        if (hasInitialLetter)
                        {
                            flFontSize = fontSize * initialLetterScale;
                            flLineHeight = lineHeight * initialLetterScale;
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
                                Text = remainder,
                                Justification = isLastLine ? LineJustification.NotJustifiable : LineJustification.Justifiable
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
                            Width = isFirstLine ? childContainingWidth - textIndent : childContainingWidth,
                            Height = lineHeight,
                            ContentWidth = TextMeasurer.MeasureWidth(line, fontSize, fontFamily, fontWeight, fontStyle, blockLetterSpacing),
                            ContentHeight = lineHeight,
                            Text = line,
                            Justification = isLastLine ? LineJustification.NotJustifiable : LineJustification.Justifiable
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

        string? afterContent = afterPseudoStyle != null ? ResolvePseudoElementContent(element, afterPseudoStyle) : null;
        if (afterContent != null && afterIsInline)
        {
            LayoutInlinePseudoElement(afterContent, afterPseudoStyle!, prevTextEndedWithSpace || prevInlineElementEndedWithSpace, box, ref inlineX, ref childY,
                ref inlineLineHeight, inlineWrapWidth, style, fontSize, floatCtx.HasFloats ? floatCtx : null, floatOriginY,
                LineStartFor(childY, firstLineIndentY, firstLineIndent));
        }

        if (LineHasContent())
        {
            childY += inlineLineHeight;
        }

        if (lineClamp > 0)
            ClampInlineLines(box, lineClamp, childContainingWidth, ref childY);

        if (!deferInlineAlignment)
            FinishInlineLines(box, childContainingWidth, floatCtx.HasFloats ? floatCtx : null, floatOriginY);

        if (afterContent != null && !afterIsInline)
            childY = LayoutPseudoElementLine(afterContent, afterPseudoStyle!, box, childY, childContainingWidth, fontSize);
        else if (afterContent == null && IsBlockLevelEmptyStringPseudoElement(afterPseudoStyle))
            childY = LayoutEmptyPseudoElementBox(afterPseudoStyle!, box, childY, childContainingWidth, fontSize);

        if (counterCtx != null)
            counterCtx.PopReset(style.Get("counter-reset"));

        if (style.Display == "table" && style.Get("caption-side") == "bottom")
        {
            float rowBottom = box.Y + box.PaddingTop;
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
                child.Y = rowBottom;
            }
        }

        if ((IsTableRow(style.Display) || actsAsImplicitTableRow) && childY > 0)
        {
            float rowHeight = childY;
            for (int ci = 0; ci < box.Children.Count; ci++)
            {
                var cell = box.Children[ci];
                if (cell.Element == null || RowSpanOf(cell.Element) > 1) continue;

                float contentHeight = cell.ContentHeight;
                cell.Height = rowHeight;

                var vAlign = cell.Style.Get("vertical-align") ?? "top";
                float offset = 0;
                if (vAlign == "middle")
                    offset = (rowHeight - cell.PaddingTop - cell.PaddingBottom - contentHeight) / 2f;
                else if (vAlign == "bottom")
                    offset = rowHeight - cell.PaddingTop - cell.PaddingBottom - contentHeight;

                if (offset > 0)
                {
                    for (int gi = 0; gi < cell.Children.Count; gi++)
                        cell.Children[gi].Y += offset;
                }
            }
        }

        if (style.Display == "list-item")
        {
            var markerCascadeRes = _threadCascadeResolver;
            var markerStyle = markerCascadeRes?.ResolvePseudoElement(element, "marker", style);

            var listStyleType = style.Get("list-style-type") ?? (parentStyle?.Get("list-style-type")) ?? "disc";
            string markerText = GetListMarkerText(listStyleType, element, parent.Element, counterCtx);

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

        if (element.TagName == "table")
            childY += ApplyRowSpans(box);

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
            if (isMultiColumn && box.Children.Count > 0)
            {
                var (colCount, colWidth, colGap) = MultiColumnLayout.ResolveColumns(style, box.ContentWidth, fontSize);
                if (colCount > 1)
                {
                    var childList = new List<LayoutBox>(box.Children.Count);
                    foreach (var c in box.Children)
                        if (c is LayoutBox lb) childList.Add(lb);

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
                            if (segment.Count > 0)
                            {
                                var columns = MultiColumnLayout.DistributeIntoColumns(
                                    segment, colCount, colWidth, colGap, containerX, currentY,
                                    _viewportHeight > 0 ? _viewportHeight : float.PositiveInfinity);
                                float segHeight = 0;
                                foreach (var col in columns)
                                {
                                    box.Children.Add(col);
                                    segHeight = Math.Max(segHeight, col.Y - currentY + col.Height);
                                }
                                currentY += segHeight;
                                totalHeight += segHeight;
                                segment.Clear();
                            }

                            if (isSpanning)
                            {
                                child!.Width = box.ContentWidth;
                                child.ContentWidth = box.ContentWidth - child.PaddingLeft - child.PaddingRight;
                                if (child.ContentWidth < 0) child.ContentWidth = 0;
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

            if (!isMultiColumn && hasBlockChild && childY == childYAfterLastBlock && prevMarginBottom != 0f)
            {
                bool collapsesThroughBottom = collapsesBottomWithChildren && box.PaddingBottom == 0f &&
                                              string.IsNullOrEmpty(style.Get("min-height"));
                if (collapsesThroughBottom)
                    box.MarginBottom = CollapseMargins(box.MarginBottom, prevMarginBottom);
                else
                    childY += prevMarginBottom;
            }
            box.ContentHeight = childY;
            box.Height = childY + box.PaddingTop + box.PaddingBottom;
        }
        if (marginCollapsedThroughTop != 0f)
            box.MarginTop = CollapseMargins(box.MarginTop, marginCollapsedThroughTop);

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

        float? minHeight = ResolveOptionalLength(style.Get("min-height"), 0, fontSize);
        float? maxHeight = ResolveOptionalLength(style.Get("max-height"), 0, fontSize);

        if (minHeight.HasValue && box.Height < minHeight.Value)
            box.Height = minHeight.Value;
        if (maxHeight.HasValue && box.Height > maxHeight.Value)
            box.Height = maxHeight.Value;

        LayoutAbsoluteChildren(absChildren, box, position, containingWidth, parent, fontSize, resolver, style);

        if (position == "relative" || position == "sticky")
            box.Y += RelativeOffsetY(style, fontSize);

        return box;
    }

    private static (float fontSize, bool borderBox, string? position) ResolveBoxModel(
        LayoutBox box, HtmlElement element, ComputedStyle style, LayoutBox parent,
        float containingWidth, ComputedStyle? parentStyle)
    {
        float parentFontSize = parentStyle != null ? ResolveFontSize(parentStyle.FontSize, DefaultFontSize) : DefaultFontSize;
        float fontSize = ResolveFontSize(style.FontSize, parentFontSize);

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
        if (!BordersCollapse(element, style) && !IsPaintedFormControl(element))
        {
            box.PaddingTop += VisibleBorderWidth(style, "top", fontSize);
            box.PaddingRight += VisibleBorderWidth(style, "right", fontSize);
            box.PaddingBottom += VisibleBorderWidth(style, "bottom", fontSize);
            box.PaddingLeft += VisibleBorderWidth(style, "left", fontSize);
        }

        bool borderBox = style.Get("box-sizing") == "border-box";

        bool isTableCell = element.TagName == "td" || element.TagName == "th";
        float? specifiedWidth = isTableCell ? null : ResolveOptionalLength(style.Width, containingWidth, fontSize);
        if (specifiedWidth.HasValue)
        {
            if (borderBox)
            {
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
            box.Width = containingWidth - box.MarginLeft - box.MarginRight;
            box.ContentWidth = box.Width - box.PaddingLeft - box.PaddingRight;
        }

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

        box.X = parent.X + parent.PaddingLeft + box.MarginLeft;

        var position = style.Get("position");
        if (position == "relative")
            box.X += RelativeOffsetX(style, fontSize);

        return (fontSize, borderBox, position);
    }

    private static LayoutBox LayoutFlexContainer(LayoutBox box, HtmlElement element, ComputedStyle style,
        LayoutBox parent, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        ComputedStyle? parentStyle, string? position, float fontSize, bool borderBox)
    {
        FlexLayout.LayoutFlex(box, element, style, containingWidth, resolver, parentStyle);
        return FinishItemContainer(box, element, style, parent, containingWidth, resolver, position, fontSize, borderBox);
    }

    private static LayoutBox LayoutGridContainer(LayoutBox box, HtmlElement element, ComputedStyle style,
        LayoutBox parent, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        ComputedStyle? parentStyle, string? position, float fontSize, bool borderBox)
    {
        GridLayout.LayoutGrid(box, element, style, containingWidth, resolver, parentStyle);
        return FinishItemContainer(box, element, style, parent, containingWidth, resolver, position, fontSize, borderBox);
    }

    private static LayoutBox FinishItemContainer(LayoutBox box, HtmlElement element, ComputedStyle style,
        LayoutBox parent, float containingWidth, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        string? position, float fontSize, bool borderBox)
    {
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

        float? minHeight = ResolveOptionalLength(style.Get("min-height"), 0, fontSize);
        float? maxHeight = ResolveOptionalLength(style.Get("max-height"), 0, fontSize);
        if (minHeight.HasValue && box.Height < minHeight.Value)
            box.Height = minHeight.Value;
        if (maxHeight.HasValue && box.Height > maxHeight.Value)
            box.Height = maxHeight.Value;

        if (position == "relative")
            box.Y += RelativeOffsetY(style, fontSize);

        var absChildren = CollectAbsoluteChildren(element, style, resolver);
        if (absChildren.Count > 0)
            LayoutAbsoluteChildren(absChildren, box, position, containingWidth, parent, fontSize, resolver, style);

        return box;
    }
}
