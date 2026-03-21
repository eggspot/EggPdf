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

        // Width
        float? specifiedWidth = ResolveOptionalLength(style.Width, containingWidth, fontSize);
        if (specifiedWidth.HasValue)
        {
            box.ContentWidth = specifiedWidth.Value;
            box.Width = specifiedWidth.Value + box.PaddingLeft + box.PaddingRight;
        }
        else
        {
            // Auto width: fill containing block minus margins
            box.Width = containingWidth - box.MarginLeft - box.MarginRight;
            box.ContentWidth = box.Width - box.PaddingLeft - box.PaddingRight;
        }

        // Position
        box.X = parent.X + parent.PaddingLeft + box.MarginLeft;

        // Layout children
        float childY = 0;
        float childContainingWidth = box.ContentWidth;

        foreach (var childNode in element.ChildNodes)
        {
            if (childNode is HtmlElement childElem)
            {
                var childStyle = resolver.Resolve(childElem, style);

                if (childStyle.Display == "none")
                    continue;

                if (IsBlockLevel(childStyle.Display))
                {
                    var childBox = CreateBox(childElem, childStyle, box, childContainingWidth, resolver, style);
                    childBox.Y = box.Y + box.PaddingTop + childY + childBox.MarginTop;
                    childBox.X = box.X + box.PaddingLeft + childBox.MarginLeft;

                    box.Children.Add(childBox);
                    childY += childBox.MarginTop + childBox.Height + childBox.MarginBottom;
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
                // Text content
                float textHeight = fontSize * DefaultLineHeight;
                var textBox = new LayoutBox
                {
                    Style = style,
                    X = box.X + box.PaddingLeft,
                    Y = box.Y + box.PaddingTop + childY,
                    Width = childContainingWidth,
                    Height = textHeight,
                    ContentWidth = childContainingWidth,
                    ContentHeight = textHeight,
                    Text = textNode.Data.Trim()
                };
                box.Children.Add(textBox);
                childY += textHeight;
            }
        }

        // Height
        float? specifiedHeight = ResolveOptionalLength(style.Height, 0, fontSize);
        if (specifiedHeight.HasValue)
        {
            box.ContentHeight = specifiedHeight.Value;
            box.Height = specifiedHeight.Value + box.PaddingTop + box.PaddingBottom;
        }
        else
        {
            // Auto height: sum of children
            box.ContentHeight = childY;
            box.Height = childY + box.PaddingTop + box.PaddingBottom;
        }

        return box;
    }

    private static bool IsBlockLevel(string display)
    {
        return display == "block" || display == "list-item" ||
               display == "table" || display == "flex" || display == "grid";
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
}
