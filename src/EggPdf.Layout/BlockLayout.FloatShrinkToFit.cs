using System;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private const float ShrinkToFitRoundingAllowancePx = 1f;

    private static float FloatShrinkToFitContainingWidth(HtmlElement element, ComputedStyle style, float availableWidth,
        float parentFontSize, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver)
    {
        if (!string.IsNullOrEmpty(style.Width) && style.Width != "auto")
            return availableWidth;

        float fontSize = ResolveFontSize(style.FontSize, parentFontSize);
        float horizontalExtras =
            ResolveLength(style.Get("padding-left"), availableWidth, fontSize) +
            ResolveLength(style.Get("padding-right"), availableWidth, fontSize) +
            ResolveLength(style.Get("margin-left"), availableWidth, fontSize) +
            ResolveLength(style.Get("margin-right"), availableWidth, fontSize);
        float preferred = FlexLayout.MeasureMaxContentWidth(element, style, resolver, fontSize)
                          + horizontalExtras + ShrinkToFitRoundingAllowancePx;
        return preferred < availableWidth ? preferred : availableWidth;
    }
}
