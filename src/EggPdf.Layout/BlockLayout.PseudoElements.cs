using System.Collections.Generic;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private static ComputedStyle? ResolvePseudoElementStyle(HtmlElement element, string pseudo, ComputedStyle parentStyle) =>
        _threadCascadeResolver?.ResolvePseudoElement(element, pseudo, parentStyle);

    private static bool GeneratesInlineBox(ComputedStyle? pseudoStyle)
    {
        if (pseudoStyle == null) return false;

        var content = pseudoStyle.Get("content");
        if (string.IsNullOrEmpty(content) || content == "none" || content == "normal") return false;

        var display = pseudoStyle.Display;
        if (display == "none" || (display != null && IsBlockLevel(display))) return false;

        var position = pseudoStyle.Get("position");
        if (position == "absolute" || position == "fixed") return false;

        var floatValue = pseudoStyle.Get("float");
        return floatValue != "left" && floatValue != "right";
    }

    private static void LayoutInlinePseudoElement(string content, ComputedStyle pseudoStyle, bool hasLeadingSpace,
        LayoutBox box, ref float inlineX, ref float childY, ref float inlineLineHeight, float containerWidth,
        ComputedStyle parentStyle, float parentFontSize, FloatContext? floats, float floatOriginY)
    {
        var runs = new List<InlineRun>(1)
        {
            new InlineRun
            {
                Text = content,
                Style = pseudoStyle,
                Element = null,
                FontSize = ResolveFontSize(pseudoStyle.FontSize, parentFontSize),
                HasLeadingSpace = hasLeadingSpace || StartsWithCollapsibleWhitespace(content),
            }
        };
        LayoutInlineRuns(runs, box, null, ref inlineX, ref childY, ref inlineLineHeight,
            containerWidth, parentStyle, parentFontSize, floats, floatOriginY);
    }

    private static float LayoutPseudoElementLine(string content, ComputedStyle pseudoStyle, LayoutBox box,
        float childY, float containingWidth, float parentFontSize)
    {
        float fontSize = ResolveFontSize(pseudoStyle.FontSize, parentFontSize);
        float lineHeight = TextMeasurer.GetLineHeight(fontSize, pseudoStyle.Get("line-height"));
        box.Children.Add(new LayoutBox
        {
            Style = pseudoStyle,
            X = box.X + box.PaddingLeft,
            Y = box.Y + box.PaddingTop + childY,
            Width = containingWidth,
            Height = lineHeight,
            ContentWidth = TextMeasurer.MeasureWidth(content, fontSize,
                pseudoStyle.FontFamily, pseudoStyle.FontWeight, pseudoStyle.Get("font-style")),
            ContentHeight = lineHeight,
            Text = content
        });
        return childY + lineHeight;
    }

    private static string? ResolvePseudoElementContent(HtmlElement element, ComputedStyle pseudoStyle) =>
        _threadCounterCtx?.ResolveContent(pseudoStyle.Get("content"), element, pseudoStyle);
}
