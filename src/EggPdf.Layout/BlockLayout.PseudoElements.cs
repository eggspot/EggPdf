using System.Collections.Generic;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private static ComputedStyle? ResolvePseudoElementStyle(HtmlElement element, string pseudo, ComputedStyle parentStyle) =>
        _threadCascadeResolver?.ResolvePseudoElement(element, pseudo, parentStyle);

    private static bool PreservesWhitespace(ComputedStyle style)
    {
        var whiteSpace = style.Get("white-space");
        return whiteSpace == "pre" || whiteSpace == "pre-wrap" || whiteSpace == "pre-line" || whiteSpace == "break-spaces";
    }

    private static bool PlainTextKeepsItsFeaturesInInlineFlow(ComputedStyle style, FloatContext floats,
        ComputedStyle? firstLineStyle, ComputedStyle? firstLetterStyle)
    {
        if (floats.HasFloats || firstLineStyle != null || firstLetterStyle != null) return false;
        if (style.Get("text-align") == "justify") return false;
        if (style.Get("hyphens") == "auto") return false;
        if (style.Get("line-clamp") != null || style.Get("-webkit-line-clamp") != null) return false;
        if (style.Get("initial-letter") != null) return false;

        var textIndent = style.Get("text-indent");
        if (!string.IsNullOrEmpty(textIndent) && textIndent != "0" && textIndent != "0px") return false;

        var textWrap = style.Get("text-wrap");
        if (textWrap == "balance" || textWrap == "pretty") return false;

        var hangingPunctuation = style.Get("hanging-punctuation");
        if (!string.IsNullOrEmpty(hangingPunctuation) && hangingPunctuation != "none") return false;

        var overflowWrap = style.Get("overflow-wrap") ?? style.Get("word-wrap");
        if (overflowWrap == "break-word" || overflowWrap == "anywhere") return false;
        var wordBreak = style.Get("word-break");
        return wordBreak != "break-all" && wordBreak != "break-word";
    }

    private static bool HasFloatedChild(HtmlElement element, ComputedStyle style,
        System.Func<HtmlElement, ComputedStyle?, ComputedStyle>? resolver)
    {
        if (resolver == null) return false;
        foreach (var child in element.ChildNodes)
        {
            if (child is HtmlElement childElement && IsFloated(resolver(childElement, style)))
                return true;
        }
        return false;
    }

    private static bool GeneratesInlineBox(ComputedStyle? pseudoStyle)
    {
        if (pseudoStyle == null) return false;

        var content = pseudoStyle.Get("content");
        if (string.IsNullOrEmpty(content) || content == "none" || content == "normal") return false;

        var display = pseudoStyle.Display;
        if (display == "none" || (display != null && IsBlockLevel(display))) return false;

        return !IsOutOfFlow(pseudoStyle);
    }

    private static bool IsOutOfFlow(ComputedStyle pseudoStyle)
    {
        var position = pseudoStyle.Get("position");
        if (position == "absolute" || position == "fixed") return true;
        var floatValue = pseudoStyle.Get("float");
        return floatValue == "left" || floatValue == "right";
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

    private static float LayoutEmptyPseudoElementBox(ComputedStyle pseudoStyle, LayoutBox box, float childY,
        float containingWidth, float parentFontSize)
    {
        float fontSize = ResolveFontSize(pseudoStyle.FontSize, parentFontSize);
        float height = ResolveLength(pseudoStyle.Get("height"), 0, fontSize);
        float paddingTop = ResolveLength(pseudoStyle.Get("padding-top"), containingWidth, fontSize);
        float paddingBottom = ResolveLength(pseudoStyle.Get("padding-bottom"), containingWidth, fontSize);
        float boxHeight = height + paddingTop + paddingBottom;
        if (boxHeight <= 0 && !HasVisibleBorder(pseudoStyle)) return childY;

        float width = ResolveOptionalLength(pseudoStyle.Get("width"), containingWidth, fontSize) ?? containingWidth;
        float marginTop = ResolveLength(pseudoStyle.Get("margin-top"), containingWidth, fontSize);
        float marginBottom = ResolveLength(pseudoStyle.Get("margin-bottom"), containingWidth, fontSize);
        float marginLeft = ResolveLength(pseudoStyle.Get("margin-left"), containingWidth, fontSize);
        box.Children.Add(new LayoutBox
        {
            Style = pseudoStyle,
            X = box.X + box.PaddingLeft + marginLeft,
            Y = box.Y + box.PaddingTop + childY + marginTop,
            Width = width,
            Height = boxHeight,
            ContentWidth = width,
            ContentHeight = height,
            PaddingTop = paddingTop,
            PaddingBottom = paddingBottom,
        });
        float borders = VisibleBorderWidth(pseudoStyle, "top", fontSize) + VisibleBorderWidth(pseudoStyle, "bottom", fontSize);
        return childY + marginTop + boxHeight + borders + marginBottom;
    }

    private static float VisibleBorderWidth(ComputedStyle style, string side, float fontSize) =>
        IsVisibleBorderStyle(style.Get("border-" + side + "-style"))
            ? ResolveLength(style.Get("border-" + side + "-width"), 0, fontSize)
            : 0f;

    private static bool IsBlockLevelEmptyStringPseudoElement(ComputedStyle? pseudoStyle)
    {
        if (pseudoStyle == null) return false;
        var display = pseudoStyle.Display;
        if (display == null || !IsBlockLevel(display)) return false;
        if (IsOutOfFlow(pseudoStyle)) return false;
        var content = pseudoStyle.Get("content")?.Trim();
        return content == "''" || content == "\"\"";
    }

    private static bool HasVisibleBorder(ComputedStyle style) =>
        IsVisibleBorderStyle(style.Get("border-top-style")) || IsVisibleBorderStyle(style.Get("border-right-style")) ||
        IsVisibleBorderStyle(style.Get("border-bottom-style")) || IsVisibleBorderStyle(style.Get("border-left-style"));

    private static bool IsVisibleBorderStyle(string? borderStyle) =>
        !string.IsNullOrEmpty(borderStyle) && borderStyle != "none" && borderStyle != "hidden";

    private static string? ResolvePseudoElementContent(HtmlElement element, ComputedStyle pseudoStyle) =>
        _threadCounterCtx?.ResolveContent(pseudoStyle.Get("content"), element, pseudoStyle);
}
