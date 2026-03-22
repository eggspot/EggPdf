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
    /// Lay out an entire document into a layout tree.
    /// </summary>
    public static LayoutBox LayoutDocument(HtmlDocument document, float pageWidth, float pageHeight)
    {
        var resolver = new BasicStyleResolver();
        var root = new LayoutBox
        {
            X = 0, Y = 0,
            Width = pageWidth, Height = pageHeight,
            ContentWidth = pageWidth, ContentHeight = pageHeight
        };

        if (document.Body == null) return root;

        var bodyStyle = resolver.Resolve(document.Body, null);
        var bodyBox = CreateBox(document.Body, bodyStyle, root, pageWidth, resolver, null);
        root.Children.Add(bodyBox);

        // Post-layout pass: convert all Y coordinates to absolute
        ResolveAbsolutePositions(root, 0, 0);

        return root;
    }

    private static LayoutBox CreateBox(HtmlElement element, ComputedStyle style,
        LayoutBox parent, float containingWidth, BasicStyleResolver resolver, ComputedStyle? parentStyle)
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

        // Layout children
        float childY = 0;
        float childContainingWidth = box.ContentWidth;
        float prevMarginBottom = 0; // for margin collapsing

        foreach (var childNode in element.ChildNodes)
        {
            if (childNode is HtmlElement childElem)
            {
                var childStyle = resolver.Resolve(childElem, style);

                if (childStyle.Display == "none")
                    continue;

                if (IsBlockLevel(childStyle.Display))
                {
                    // Table row layout: cells go side-by-side (horizontal)
                    if (IsTableRow(style.Display) && IsTableCell(childStyle.Display))
                    {
                        int cellCount = CountTableCells(element);
                        float cellWidth = cellCount > 0 ? childContainingWidth / cellCount : childContainingWidth;
                        int cellIndex = CountPreviousCells(element, childElem);

                        var childBox = CreateBox(childElem, childStyle, box, cellWidth, resolver, style);
                        childBox.Width = cellWidth;
                        childBox.ContentWidth = cellWidth - childBox.PaddingLeft - childBox.PaddingRight;
                        childBox.Y = box.Y + box.PaddingTop;
                        childBox.X = box.X + box.PaddingLeft + (cellIndex * cellWidth);

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
                else
                {
                    // Inline elements: create a simple box with text height
                    var childBox = new LayoutBox
                    {
                        Element = childElem,
                        Style = childStyle,
                        X = box.X + box.PaddingLeft,
                        Y = box.Y + box.PaddingTop + childY,
                        Width = childContainingWidth,
                        Height = fontSize * DefaultLineHeight,
                        ContentWidth = childContainingWidth,
                        ContentHeight = fontSize * DefaultLineHeight,
                        Text = GetTextContent(childElem)
                    };
                    box.Children.Add(childBox);

                    if (!string.IsNullOrEmpty(childBox.Text))
                        childY += childBox.Height;
                }
            }
            else if (childNode is HtmlTextNode textNode && !string.IsNullOrWhiteSpace(textNode.Data))
            {
                // Text content with line wrapping
                var fontFamily = style.FontFamily;
                float lineHeight = TextMeasurer.GetLineHeight(fontSize, style.Get("line-height"));
                var lines = TextMeasurer.WrapText(textNode.Data.Trim(), fontSize, fontFamily, childContainingWidth);

                foreach (var line in lines)
                {
                    var textBox = new LayoutBox
                    {
                        Style = style,
                        X = box.X + box.PaddingLeft,
                        Y = box.Y + box.PaddingTop + childY,
                        Width = childContainingWidth,
                        Height = lineHeight,
                        ContentWidth = TextMeasurer.MeasureWidth(line, fontSize, fontFamily),
                        ContentHeight = lineHeight,
                        Text = line
                    };
                    box.Children.Add(textBox);
                    childY += lineHeight;
                }
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

    private static int CountTableCells(HtmlElement row)
    {
        int count = 0;
        foreach (var child in row.ChildNodes)
            if (child is HtmlElement e && (e.TagName == "td" || e.TagName == "th"))
                count++;
        return count;
    }

    private static int CountPreviousCells(HtmlElement row, HtmlElement currentCell)
    {
        int index = 0;
        foreach (var child in row.ChildNodes)
        {
            if (child == currentCell) return index;
            if (child is HtmlElement e && (e.TagName == "td" || e.TagName == "th"))
                index++;
        }
        return index;
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
