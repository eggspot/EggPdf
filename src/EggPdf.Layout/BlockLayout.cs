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

        var bodyStyle = resolveStyle(document.Body, null);
        var bodyBox = CreateBox(document.Body, bodyStyle, root, pageWidth, resolveStyle, null);
        root.Children.Add(bodyBox);

        // Post-layout pass: convert all Y coordinates to absolute
        ResolveAbsolutePositions(root, 0, 0);

        return root;
    }

    private static LayoutBox CreateBox(HtmlElement element, ComputedStyle style,
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

        // Layout children using inline formatting context awareness
        float childY = 0;
        float childContainingWidth = box.ContentWidth;
        float prevMarginBottom = 0; // for margin collapsing
        float inlineX = 0; // current X offset within the inline line
        float inlineLineHeight = 0; // max height of current inline line

        foreach (var childNode in element.ChildNodes)
        {
            if (childNode is HtmlElement childElem)
            {
                var childStyle = resolver(childElem, style);

                if (childStyle.Display == "none")
                    continue;

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
                        float colWidth = totalColumns > 0 ? childContainingWidth / totalColumns : childContainingWidth;
                        int colOffset = CountPreviousColumns(element, childElem);
                        int colspan = GetColspan(childElem);

                        float cellWidth = colWidth * colspan;
                        var childBox = CreateBox(childElem, childStyle, box, cellWidth, resolver, style);
                        childBox.Width = cellWidth;
                        childBox.ContentWidth = cellWidth - childBox.PaddingLeft - childBox.PaddingRight;
                        childBox.Y = box.Y + box.PaddingTop;
                        childBox.X = box.X + box.PaddingLeft + (colOffset * colWidth);

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
                    else
                    {
                        childY += lineHeight;
                    }
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
                else
                {
                    // Inline elements: lay out horizontally on the same line
                    var inlineText = GetTextContent(childElem);
                    float inlineFontSize = ResolveFontSize(childStyle.FontSize, fontSize);
                    float inlineHeight = inlineFontSize * DefaultLineHeight;
                    float inlineWidth = 0;

                    if (!string.IsNullOrEmpty(inlineText))
                    {
                        inlineWidth = TextMeasurer.MeasureWidth(inlineText, inlineFontSize,
                            childStyle.FontFamily ?? style.FontFamily,
                            childStyle.FontWeight ?? style.FontWeight,
                            childStyle.Get("font-style") ?? style.Get("font-style"));
                    }

                    // Wrap to next line if inline element doesn't fit
                    if (inlineX > 0 && inlineWidth > 0 && inlineX + inlineWidth > childContainingWidth)
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
                        Width = inlineWidth > 0 ? inlineWidth : childContainingWidth,
                        Height = inlineHeight,
                        ContentWidth = inlineWidth,
                        ContentHeight = inlineHeight,
                        Text = inlineText
                    };
                    box.Children.Add(childBox);

                    if (inlineWidth > 0)
                    {
                        inlineX += inlineWidth;
                        if (inlineHeight > inlineLineHeight)
                            inlineLineHeight = inlineHeight;
                    }
                    else if (!string.IsNullOrEmpty(inlineText))
                    {
                        childY += inlineHeight;
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

                // For text-indent, reduce first line's available width
                float firstLineWidth = textIndent > 0 ? childContainingWidth - textIndent : childContainingWidth;
                var lines = TextMeasurer.WrapText(textData, fontSize, fontFamily, fontWeight, fontStyle,
                    firstLineWidth > 0 ? firstLineWidth : childContainingWidth, whiteSpaceProp);

                // If indent caused wrapping and there are remaining lines, re-wrap with full width
                if (textIndent > 0 && lines.Count > 1)
                {
                    var firstLine = lines[0];
                    var remaining = textData.Substring(firstLine.Length).TrimStart();
                    lines = new System.Collections.Generic.List<string> { firstLine };
                    if (!string.IsNullOrEmpty(remaining))
                    {
                        var moreLines = TextMeasurer.WrapText(remaining, fontSize, fontFamily, fontWeight, fontStyle, childContainingWidth, whiteSpaceProp);
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
            }
        }

        // Flush any remaining inline content
        if (inlineX > 0)
        {
            childY += inlineLineHeight;
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

        // Apply relative position Y offset
        if (position == "relative")
        {
            float offsetTop = ResolveLength(style.Get("top"), 0, fontSize);
            box.Y += offsetTop;
        }

        return box;
    }

    private static bool IsTableRow(string display)
        => display == "table-row";

    private static bool IsTableCell(string display)
        => display == "table-cell";

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
        foreach (var child in element.ChildNodes)
        {
            if (child is HtmlTextNode text)
                return text.Data.Trim();
        }
        return null;
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

    private static float ResolveFontSize(string? value, float parentFontSize)
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

    /// <summary>
    /// Post-layout pass: resolve children with relative Y coordinates to absolute.
    /// Children created by CreateBox have Y relative to parent's content area,
    /// but the parent's final Y is set after CreateBox returns.
    /// </summary>
    private static void ResolveAbsolutePositions(LayoutBox box, float offsetX, float offsetY)
    {
        foreach (var child in box.Children)
        {
            // List markers are already absolutely positioned
            if (child.IsListMarker)
            {
                ResolveAbsolutePositions(child, child.X, child.Y);
                continue;
            }

            // A child's Y is relative if it's less than its parent's Y
            // and the parent has a non-zero Y position
            bool needsYFix = box.Y > 0 && child.Y < box.Y;
            bool needsXFix = box.X > 0 && child.X < box.X;

            if (needsYFix)
                child.Y += box.Y;

            if (needsXFix)
                child.X = Math.Max(child.X + box.X - 8, child.X); // adjust but don't double-count body margin

            // Recurse into children
            ResolveAbsolutePositions(child, child.X, child.Y);
        }
    }
}
