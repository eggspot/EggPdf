using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    /// <summary>
    /// Lay out a &lt;ruby&gt; element: base text flows inline at the parent font size,
    /// the &lt;rt&gt; annotation is placed above it at 50% font size.
    /// </summary>
    private static void LayoutRubyInline(
        HtmlElement rubyElem, ComputedStyle rubyStyle, LayoutBox parentBox,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        ComputedStyle containerStyle, float containerFontSize,
        ref float inlineX, ref float childY, ref float inlineLineHeight, float containerWidth)
    {
        float baseFontSize = ResolveFontSize(rubyStyle.FontSize, containerFontSize);
        float baseLineHeight = TextMeasurer.GetLineHeight(baseFontSize, rubyStyle.Get("line-height"));

        // Collect base text (non-rt, non-rp children)
        var sb = new System.Text.StringBuilder();
        string? rtText = null;
        ComputedStyle? rtStyle = null;
        foreach (var child in rubyElem.ChildNodes)
        {
            if (child is HtmlTextNode tn)
            {
                sb.Append(TrimHtmlText(tn.Data));
            }
            else if (child is HtmlElement ce)
            {
                if (ce.TagName == "rt")
                {
                    if (rtText == null)
                    {
                        rtStyle = resolver(ce, rubyStyle);
                        rtText = CollectText(ce);
                    }
                }
                else if (ce.TagName != "rp")
                {
                    var ceStyle = resolver(ce, rubyStyle);
                    if (ceStyle.Display != "none")
                        sb.Append(CollectText(ce));
                }
            }
        }
        string baseText = sb.ToString();

        // Measure base and annotation
        float baseWidth = string.IsNullOrEmpty(baseText) ? 0f :
            TextMeasurer.MeasureWidth(baseText, baseFontSize, rubyStyle.FontFamily, rubyStyle.FontWeight, rubyStyle.Get("font-style"));

        float rtFontSize = rtStyle != null ? ResolveFontSize(rtStyle.FontSize, baseFontSize) : baseFontSize * 0.5f;
        float rtLineHeight = TextMeasurer.GetLineHeight(rtFontSize, rtStyle?.Get("line-height"));
        float rtWidth = (!string.IsNullOrEmpty(rtText)) ?
            TextMeasurer.MeasureWidth(rtText, rtFontSize, rubyStyle.FontFamily, rubyStyle.FontWeight, null) : 0f;

        float totalWidth = Math.Max(baseWidth, rtWidth);

        // Wrap to next line if needed
        if (inlineX > 0 && inlineX + totalWidth > containerWidth)
        {
            childY += inlineLineHeight;
            inlineX = 0;
            inlineLineHeight = 0;
        }

        float startX = parentBox.X + parentBox.PaddingLeft + inlineX;
        float startY = parentBox.Y + parentBox.PaddingTop + childY;

        // ruby-position: over (default) = annotation above base; under = annotation below base
        var rubyPosition = rubyStyle.Get("ruby-position") ?? "over";
        bool annotationUnder = rubyPosition.IndexOf("under", StringComparison.OrdinalIgnoreCase) >= 0;

        // ruby-align: center (default), start, end, space-around, space-between
        var rubyAlign = rubyStyle.Get("ruby-align") ?? "center";

        float CalcAnnotationX()
        {
            switch (rubyAlign.ToLowerInvariant())
            {
                case "start":    return startX;
                case "end":      return startX + totalWidth - rtWidth;
                case "space-around":
                {
                    float spacing = rtWidth < totalWidth ? (totalWidth - rtWidth) / 2f : 0f;
                    return startX + spacing;
                }
                case "space-between":
                    return startX;
                default: // center
                    return startX + (totalWidth - rtWidth) / 2f;
            }
        }

        if (!annotationUnder)
        {
            // over: rt sits at startY, base sits at startY + rtLineHeight
            if (!string.IsNullOrEmpty(rtText))
            {
                float rtX = CalcAnnotationX();
                var rtBox = new LayoutBox
                {
                    Element = null,
                    Style = rtStyle ?? rubyStyle,
                    X = rtX,
                    Y = startY,
                    Width = rtWidth,
                    Height = rtLineHeight,
                    ContentWidth = rtWidth,
                    ContentHeight = rtLineHeight,
                    Text = rtText
                };
                parentBox.Children.Add(rtBox);
            }

            float baseOffsetYOver = (!string.IsNullOrEmpty(rtText)) ? rtLineHeight : 0f;
            if (!string.IsNullOrEmpty(baseText))
            {
                float baseX = startX + (totalWidth - baseWidth) / 2f;
                var baseBoxOver = new LayoutBox
                {
                    Element = rubyElem,
                    Style = rubyStyle,
                    X = baseX,
                    Y = startY + baseOffsetYOver,
                    Width = baseWidth,
                    Height = baseLineHeight,
                    ContentWidth = baseWidth,
                    ContentHeight = baseLineHeight,
                    Text = baseText
                };
                parentBox.Children.Add(baseBoxOver);
            }

            float totalHeight = baseOffsetYOver + baseLineHeight;
            inlineX += totalWidth;
            if (totalHeight > inlineLineHeight) inlineLineHeight = totalHeight;
            return;
        }

        // under: base sits at startY, rt sits at startY + baseLineHeight
        if (!string.IsNullOrEmpty(baseText))
        {
            float baseX = startX + (totalWidth - baseWidth) / 2f;
            var baseBox = new LayoutBox
            {
                Element = rubyElem,
                Style = rubyStyle,
                X = baseX,
                Y = startY,
                Width = baseWidth,
                Height = baseLineHeight,
                ContentWidth = baseWidth,
                ContentHeight = baseLineHeight,
                Text = baseText
            };
            parentBox.Children.Add(baseBox);
        }

        if (!string.IsNullOrEmpty(rtText))
        {
            float rtX = CalcAnnotationX();
            var rtBox = new LayoutBox
            {
                Element = null,
                Style = rtStyle ?? rubyStyle,
                X = rtX,
                Y = startY + baseLineHeight,
                Width = rtWidth,
                Height = rtLineHeight,
                ContentWidth = rtWidth,
                ContentHeight = rtLineHeight,
                Text = rtText
            };
            parentBox.Children.Add(rtBox);
        }

        float baseOffsetY = (!string.IsNullOrEmpty(rtText)) ? rtLineHeight : 0f;
        float totalHeightUnder = baseLineHeight + baseOffsetY;
        inlineX += totalWidth;
        if (totalHeightUnder > inlineLineHeight) inlineLineHeight = totalHeightUnder;
    }

}
