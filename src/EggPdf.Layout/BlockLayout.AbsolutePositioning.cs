using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    /// <summary>Walk up parent chain to find page height.</summary>
    private static float FindPageHeight(LayoutBox parent)
    {
        // The root box has Height set to page height and no Element
        if (parent.Element == null && parent.Height > 0)
            return parent.Height;
        // Default A4 page height in CSS px
        return parent.Height > 0 ? parent.Height : 841.89f;
    }

    /// <summary>
    /// Collect absolutely/fixed positioned element children (out-of-flow).
    /// Used by flex and grid containers, whose item collectors skip them.
    /// </summary>
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

    /// <summary>
    /// Position absolutely/fixed positioned children relative to their containing
    /// block (this box when positioned, else the page root). Must run after the
    /// container's final size is known. Appends the boxes to box.Children.
    /// </summary>
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

            // Determine containing block:
            // - position:fixed -> page root (0,0 with page dimensions)
            // - position:absolute -> nearest positioned ancestor (position != static) or root
            float cbX, cbY, cbWidth, cbHeight;

            if (absPos == "fixed")
            {
                // Fixed: relative to the full physical page (not the content area), so
                // "bottom: 0" / "right: 0" can reach the literal page edge and occupy the
                // @page margin area reserved for it, matching how print CSS is normally used.
                cbX = 0;
                cbY = 0;
                cbWidth = _fullPageWidthPx > 0 ? _fullPageWidthPx : containingWidth;
                cbHeight = _fullPageHeightPx > 0 ? _fullPageHeightPx : FindPageHeight(parent);
            }
            else
            {
                // Absolute: relative to nearest positioned ancestor or this box if positioned
                if (position == "relative" || position == "absolute" || position == "fixed" || position == "sticky")
                {
                    // This box is the containing block — its PADDING box (CSS 2.1
                    // §10.1), so offsets count from inside the border.
                    float cbBorderLeft = ResolveLength(style.Get("border-left-width"), 0, fontSize);
                    float cbBorderRight = ResolveLength(style.Get("border-right-width"), 0, fontSize);
                    float cbBorderTop = ResolveLength(style.Get("border-top-width"), 0, fontSize);
                    float cbBorderBottom = ResolveLength(style.Get("border-bottom-width"), 0, fontSize);
                    cbX = box.X + cbBorderLeft;
                    cbY = box.Y + cbBorderTop;
                    cbWidth = box.Width - cbBorderLeft - cbBorderRight;
                    cbHeight = box.Height - cbBorderTop - cbBorderBottom;
                }
                else
                {
                    // No positioned ancestor at this level -- use page root
                    cbX = 0;
                    cbY = 0;
                    cbWidth = containingWidth;
                    cbHeight = FindPageHeight(parent);
                }
            }

            float absFontSize = ResolveFontSize(absStyle.FontSize, fontSize);

            // Resolve top/right/bottom/left
            float? topVal = ResolveOptionalLength(absStyle.Get("top"), cbHeight, absFontSize);
            float? rightVal = ResolveOptionalLength(absStyle.Get("right"), cbWidth, absFontSize);
            float? bottomVal = ResolveOptionalLength(absStyle.Get("bottom"), cbHeight, absFontSize);
            float? leftVal = ResolveOptionalLength(absStyle.Get("left"), cbWidth, absFontSize);

            // If both left and right are set, calculate width from them (when no explicit width)
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

            // If both top and bottom are set, calculate height from them (when no explicit height)
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

            // Position X: offsets move the box AND its already-laid-out descendants
            float targetX;
            if (leftVal.HasValue)
                targetX = cbX + leftVal.Value + absBox.MarginLeft;
            else if (rightVal.HasValue)
                targetX = cbX + cbWidth - rightVal.Value - absBox.Width - absBox.MarginRight;
            else
                targetX = cbX + absBox.MarginLeft; // default: top-left of containing block

            // Position Y
            float targetY;
            if (topVal.HasValue)
                targetY = cbY + topVal.Value + absBox.MarginTop;
            else if (bottomVal.HasValue)
                targetY = cbY + cbHeight - bottomVal.Value - absBox.Height - absBox.MarginBottom;
            else
                targetY = cbY + absBox.MarginTop; // default: top-left of containing block

            // X is absolute (shift descendants); Y is parent-relative and resolved
            // in the post-layout pass.
            float absDeltaX = targetX - absBox.X;
            absBox.X = targetX;
            absBox.Y = targetY;
            if (Math.Abs(absDeltaX) > 0.01f)
                FlexLayout.OffsetChildren(absBox, absDeltaX, 0);

            box.Children.Add(absBox);
        }
    }

}
