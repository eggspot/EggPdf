using System;
using System.Collections.Generic;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class FlexLayout
{
    private const int MaxMinContentDepth = 64;

    private static float? AutomaticMinimumWidth(HtmlElement element, ComputedStyle style,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, float containerFontSize, float? specifiedWidth, float? maxWidth,
        float horizontalInsets)
    {
        var overflow = style.Get("overflow-x") ?? style.Get("overflow");
        if (!string.IsNullOrEmpty(overflow) && overflow != "visible" && overflow != "clip") return null;

        float fontSize = BlockLayout.ResolveFontSize(style.FontSize, containerFontSize);
        float minimum = element.TagName == "img" || element.TagName == "svg"
            ? BlockLayout.ResolveReplacedElementWidth(style, element, fontSize)
            : MeasureMinContentWidth(element, style, resolver, fontSize, 0);
        minimum += horizontalInsets;
        if (specifiedWidth.HasValue) minimum = Math.Min(minimum, specifiedWidth.Value);
        if (maxWidth.HasValue) minimum = Math.Min(minimum, maxWidth.Value);
        return minimum > 0f ? minimum : null;
    }

    private static float MeasureMinContentWidth(HtmlElement element, ComputedStyle style,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, float fontSize, int depth)
    {
        if (depth > MaxMinContentDepth) return 0f;
        var whiteSpace = style.Get("white-space");
        if (whiteSpace == "nowrap" || whiteSpace == "pre")
            return MeasureMaxContentWidth(element, style, resolver, fontSize);

        float letterSpacing = BlockLayout.ResolveLength(style.Get("letter-spacing"), 0, fontSize);
        bool uppercase = style.Get("text-transform") == "uppercase";
        float widest = 0f;
        for (int i = 0; i < element.ChildNodes.Count; i++)
        {
            var child = element.ChildNodes[i];
            if (child is HtmlTextNode textNode)
            {
                foreach (var word in textNode.Data.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
                {
                    var measured = uppercase ? word.ToUpperInvariant() : word;
                    widest = Math.Max(widest, TextMeasurer.MeasureWidth(measured, fontSize,
                        style.FontFamily, style.FontWeight, style.Get("font-style"), letterSpacing));
                }
            }
            else if (child is HtmlElement childElement)
            {
                var childStyle = resolver(childElement, style);
                var position = childStyle.Get("position");
                if (childStyle.Display == "none" || position == "absolute" || position == "fixed") continue;
                float childFontSize = BlockLayout.ResolveFontSize(childStyle.FontSize, fontSize);
                float childWidth;
                var explicitWidth = childStyle.Width;
                if (!string.IsNullOrEmpty(explicitWidth) && explicitWidth != "auto" && explicitWidth!.IndexOf('%') < 0 &&
                    BlockLayout.ResolveOptionalLength(explicitWidth, 0, childFontSize) is float fixedWidth)
                    childWidth = fixedWidth;
                else if (childElement.TagName == "img" || childElement.TagName == "svg")
                    childWidth = BlockLayout.ResolveReplacedElementWidth(childStyle, childElement, childFontSize);
                else
                    childWidth = MeasureMinContentWidth(childElement, childStyle, resolver, childFontSize, depth + 1);
                childWidth += HorizontalSpacing(childStyle, childFontSize);
                widest = Math.Max(widest, childWidth);
            }
        }
        return widest;
    }

    private static readonly char[] WordSeparators = { ' ', '\t', '\n', '\r', '\f' };

    private static float WithMainAxisInsets(float baseSize, HtmlElement element, ComputedStyle style, LayoutBox box,
        bool isRow, bool specifiedSize)
    {
        float insets = isRow ? box.PaddingLeft + box.PaddingRight : box.PaddingTop + box.PaddingBottom;
        if (specifiedSize) return style.Get("box-sizing") == "border-box" ? baseSize : baseSize + insets;
        if (!isRow) return baseSize;
        bool replaced = element.TagName == "img" || element.TagName == "svg";
        return replaced ? box.Width : baseSize + insets;
    }

    private static float HorizontalSpacing(ComputedStyle style, float fontSize) =>
        BlockLayout.ResolveLength(style.Get("padding-left"), 0, fontSize) +
        BlockLayout.ResolveLength(style.Get("padding-right"), 0, fontSize) +
        BlockLayout.ResolveLength(style.Get("margin-left"), 0, fontSize) +
        BlockLayout.ResolveLength(style.Get("margin-right"), 0, fontSize);

    private static void ShrinkItemsHonoringMinimums(List<FlexItem> items, float overflow)
    {
        var frozen = new bool[items.Count];
        for (int pass = 0; pass <= items.Count; pass++)
        {
            float weightedShrink = 0f, removedByFrozen = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                if (frozen[i]) removedByFrozen += items[i].HypotheticalMainSize - items[i].MainSize;
                else weightedShrink += items[i].FlexShrink * items[i].HypotheticalMainSize;
            }

            float remaining = overflow - removedByFrozen;
            bool anyClamped = false;
            for (int i = 0; i < items.Count; i++)
            {
                if (frozen[i]) continue;
                var item = items[i];
                float reduction = weightedShrink > 0f && item.FlexShrink > 0f
                    ? remaining * item.FlexShrink * item.HypotheticalMainSize / weightedShrink
                    : 0f;
                item.MainSize = item.HypotheticalMainSize - reduction;
                if (item.MainSize < item.MinMain)
                {
                    item.MainSize = item.MinMain;
                    frozen[i] = true;
                    anyClamped = true;
                }
                else if (item.MainSize > item.MaxMain)
                {
                    item.MainSize = item.MaxMain;
                    frozen[i] = true;
                    anyClamped = true;
                }
            }
            if (!anyClamped) return;
        }
    }
}
