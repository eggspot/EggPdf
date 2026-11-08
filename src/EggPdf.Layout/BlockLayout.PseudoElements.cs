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

    private static bool PlainTextKeepsItsFeaturesInInlineFlow(ComputedStyle style,
        ComputedStyle? firstLineStyle, ComputedStyle? firstLetterStyle)
    {
        if (firstLineStyle != null || firstLetterStyle != null) return false;
        if (style.Get("hyphens") == "auto") return false;
        if (style.Get("line-clamp") != null || style.Get("-webkit-line-clamp") != null) return false;
        if (style.Get("initial-letter") != null) return false;
        if (style.Get("text-indent")?.TrimStart().StartsWith("-", System.StringComparison.Ordinal) == true) return false;

        var textWrap = style.Get("text-wrap");
        if (textWrap == "balance" || textWrap == "pretty") return false;

        var hangingPunctuation = style.Get("hanging-punctuation");
        return string.IsNullOrEmpty(hangingPunctuation) || hangingPunctuation == "none";
    }

    private static float PositiveTextIndent(ComputedStyle style, float containingWidth, float fontSize)
    {
        var textIndent = style.Get("text-indent");
        if (string.IsNullOrEmpty(textIndent)) return 0f;
        float indent = ResolveLength(textIndent, containingWidth, fontSize);
        return indent > 0f ? indent : 0f;
    }

    private static float LineStartFor(float childY, float firstLineIndentY, float firstLineIndent) =>
        childY == firstLineIndentY ? firstLineIndent : 0f;

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
        ComputedStyle parentStyle, float parentFontSize, FloatContext? floats, float floatOriginY, float lineStartX)
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
            containerWidth, parentStyle, parentFontSize, floats, floatOriginY, lineStartX);
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
        float paddingTop = ResolveLength(pseudoStyle.Get("padding-top"), containingWidth, fontSize)
                           + VisibleBorderWidth(pseudoStyle, "top", fontSize);
        float paddingBottom = ResolveLength(pseudoStyle.Get("padding-bottom"), containingWidth, fontSize)
                              + VisibleBorderWidth(pseudoStyle, "bottom", fontSize);
        float boxHeight = height + paddingTop + paddingBottom;
        if (boxHeight <= 0) return childY;

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
        return childY + marginTop + boxHeight + marginBottom;
    }

    private const float ThinBorderWidth = 1f;
    private const float MediumBorderWidth = 3f;
    private const float ThickBorderWidth = 5f;

    private static bool BordersCollapse(HtmlElement element, ComputedStyle style)
    {
        var tag = element.TagName;
        return (tag == "td" || tag == "th" || tag == "tr" || tag == "table" || tag == "thead" || tag == "tbody" || tag == "tfoot") &&
               style.Get("border-collapse") == "collapse";
    }

    internal static float VisibleBorderWidth(ComputedStyle style, string side, float fontSize)
    {
        if (!IsVisibleBorderStyle(style.Get("border-" + side + "-style") ?? style.Get("border-style"))) return 0f;
        var width = style.Get("border-" + side + "-width") ?? style.Get("border-width");
        if (width != null && width.IndexOf(' ') > 0) width = width.Split(' ')[0];
        return BorderWidthKeyword(width, fontSize);
    }

    internal static float BorderWidthKeyword(string? width, float fontSize)
    {
        switch (width)
        {
            case null:
            case "":
            case "medium": return MediumBorderWidth;
            case "thin": return ThinBorderWidth;
            case "thick": return ThickBorderWidth;
            default: return ResolveLength(width, 0, fontSize);
        }
    }

    private static bool IsBlockLevelEmptyStringPseudoElement(ComputedStyle? pseudoStyle)
    {
        if (pseudoStyle == null) return false;
        var display = pseudoStyle.Display;
        if (display == null || !IsBlockLevel(display)) return false;
        if (IsOutOfFlow(pseudoStyle)) return false;
        var content = pseudoStyle.Get("content")?.Trim();
        return content == "''" || content == "\"\"";
    }

    private static bool IsVisibleBorderStyle(string? borderStyle) =>
        !string.IsNullOrEmpty(borderStyle) && borderStyle != "none" && borderStyle != "hidden";

    private static string? ResolvePseudoElementContent(HtmlElement element, ComputedStyle pseudoStyle) =>
        _threadCounterCtx?.ResolveContent(pseudoStyle.Get("content"), element, pseudoStyle);
}
