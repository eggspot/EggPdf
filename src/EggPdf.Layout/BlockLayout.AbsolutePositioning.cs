using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private const float FallbackPageHeight = 841.89f;

    private static float FindPageHeight(LayoutBox parent) =>
        parent.Height > 0 ? parent.Height : FallbackPageHeight;

    private static System.Collections.Generic.List<(HtmlElement elem, ComputedStyle style, string pos)> CollectAbsoluteChildren(
        HtmlElement element, ComputedStyle style, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver)
    {
        var result = new System.Collections.Generic.List<(HtmlElement elem, ComputedStyle style, string pos)>();
        foreach (var childNode in element.ChildNodes)
        {
            if (!(childNode is HtmlElement childElem))
                continue;
            var childStyle = resolver(childElem, style);
            if (childStyle.Display == "none")
                continue;
            var childPosition = childStyle.Get("position");
            if (childPosition == "absolute" || childPosition == "fixed")
                result.Add((childElem, childStyle, childPosition!));
        }
        return result;
    }

    private static void LayoutAbsoluteChildren(
        System.Collections.Generic.List<(HtmlElement elem, ComputedStyle style, string pos)> absChildren,
        LayoutBox box, string? position, float containingWidth, LayoutBox parent, float fontSize,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, ComputedStyle style)
    {
        for (int ai = 0; ai < absChildren.Count; ai++)
        {
            var absEntry = absChildren[ai];
            var absElem = absEntry.elem;
            var absStyle = absEntry.style;
            var absPos = absEntry.pos;
            var absBox = CreateBox(absElem, absStyle, box, box.ContentWidth, resolver, style);
            absBox.IsAbsolutelyPositioned = true;

            float cbX, cbY, cbWidth, cbHeight;

            if (absPos == "fixed" && IsSheetScopedFixed(absStyle))
            {
                cbX = 0;
                cbY = 0;
                cbWidth = _fullPageWidthPx > 0 ? _fullPageWidthPx : containingWidth;
                cbHeight = _fullPageHeightPx > 0 ? _fullPageHeightPx : FindPageHeight(parent);
            }
            else if (absPos == "fixed")
            {
                cbX = 0;
                cbY = 0;
                cbWidth = _viewportWidth > 0 ? _viewportWidth : containingWidth;
                cbHeight = _viewportHeight > 0 ? _viewportHeight : FindPageHeight(parent);
            }
            else
            {
                if (position == "relative" || position == "absolute" || position == "fixed" || position == "sticky")
                {
                    float cbBorderLeft = VisibleBorderWidth(style, "left", fontSize);
                    float cbBorderRight = VisibleBorderWidth(style, "right", fontSize);
                    float cbBorderTop = VisibleBorderWidth(style, "top", fontSize);
                    float cbBorderBottom = VisibleBorderWidth(style, "bottom", fontSize);
                    cbX = box.X + cbBorderLeft;
                    cbY = box.Y + cbBorderTop;
                    cbWidth = box.Width - cbBorderLeft - cbBorderRight;
                    cbHeight = box.Height - cbBorderTop - cbBorderBottom;
                }
                else
                {
                    cbX = 0;
                    cbY = 0;
                    cbWidth = containingWidth;
                    cbHeight = FindPageHeight(parent);
                }
            }

            float absFontSize = ResolveFontSize(absStyle.FontSize, fontSize);

            float? topVal = ResolveOptionalLength(absStyle.Get("top"), cbHeight, absFontSize);
            float? rightVal = ResolveOptionalLength(absStyle.Get("right"), cbWidth, absFontSize);
            float? bottomVal = ResolveOptionalLength(absStyle.Get("bottom"), cbHeight, absFontSize);
            float? leftVal = ResolveOptionalLength(absStyle.Get("left"), cbWidth, absFontSize);

            if (leftVal.HasValue && rightVal.HasValue && absStyle.Width == null)
            {
                float newWidth = cbWidth - leftVal.Value - rightVal.Value
                    - absBox.MarginLeft - absBox.MarginRight;
                if (newWidth > 0)
                {
                    absBox.Width = newWidth;
                    absBox.ContentWidth = newWidth - absBox.PaddingLeft - absBox.PaddingRight;
                }
            }

            if (topVal.HasValue && bottomVal.HasValue && absStyle.Height == null)
            {
                float newHeight = cbHeight - topVal.Value - bottomVal.Value
                    - absBox.MarginTop - absBox.MarginBottom;
                if (newHeight > 0)
                {
                    absBox.Height = newHeight;
                    absBox.ContentHeight = newHeight - absBox.PaddingTop - absBox.PaddingBottom;
                }
            }

            float targetX;
            if (leftVal.HasValue)
                targetX = cbX + leftVal.Value + absBox.MarginLeft;
            else if (rightVal.HasValue)
                targetX = cbX + cbWidth - rightVal.Value - absBox.Width - absBox.MarginRight;
            else
                targetX = cbX + absBox.MarginLeft;

            float targetY;
            if (topVal.HasValue)
                targetY = cbY + topVal.Value + absBox.MarginTop;
            else if (bottomVal.HasValue)
                targetY = cbY + cbHeight - bottomVal.Value - absBox.Height - absBox.MarginBottom;
            else
                targetY = cbY + absBox.MarginTop;

            float absDeltaX = targetX - absBox.X;
            absBox.X = targetX;
            absBox.Y = targetY;
            if (Math.Abs(absDeltaX) > 0.01f)
                FlexLayout.OffsetChildren(absBox, absDeltaX, 0);

            box.Children.Add(absBox);
        }
    }

    private const string FixedScopeProperty = "-eggpdf-fixed-scope";
    private const string FixedScopeSheet = "sheet";

    /// <summary>
    /// True when a <c>position: fixed</c> box is placed against the whole physical sheet: an <c>@page</c>
    /// margin box, or a box marked <c>-eggpdf-fixed-scope: sheet</c> (the fluent builder's header, footer and
    /// watermark). Every other fixed box is placed against the page area inside the <c>@page</c> margins.
    /// </summary>
    public static bool IsSheetScopedFixed(ComputedStyle style) =>
        style.Get(FixedScopeProperty) == FixedScopeSheet || style.Get("-eggpdf-margin-box-position") != null;
}
