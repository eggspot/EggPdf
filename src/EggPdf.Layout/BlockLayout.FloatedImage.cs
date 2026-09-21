using System;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    /// <summary>
    /// Lay out a <c>float: left|right</c> image: honour <c>clear</c>, pin it to the container edge inside its margins,
    /// and register its exclusion (margin box, or its <c>shape-outside</c>) so text laid out afterwards wraps around it.
    /// </summary>
    private static void AddFloatedImage(LayoutBox box, HtmlElement element, ComputedStyle style, string side,
        float width, float height, string? source, float containingWidth, float fontSize, FloatContext floatCtx,
        float floatOriginY, ref float childY, ref float leftFloatBottom, ref float rightFloatBottom)
    {
        float imgFontSize = ResolveFontSize(style.FontSize, fontSize);
        var marginShort = style.Get("margin");
        float mLeft = ResolveLength(style.MarginLeft ?? marginShort, containingWidth, imgFontSize);
        float mRight = ResolveLength(style.MarginRight ?? marginShort, containingWidth, imgFontSize);
        float mTop = ResolveLength(style.MarginTop ?? marginShort, containingWidth, imgFontSize);
        float mBottom = ResolveLength(style.MarginBottom ?? marginShort, containingWidth, imgFontSize);

        var clear = style.Get("clear");
        if (clear == "both" || clear == "left") childY = Math.Max(childY, leftFloatBottom);
        if (clear == "both" || clear == "right") childY = Math.Max(childY, rightFloatBottom);

        var image = new LayoutBox
        {
            Element = element,
            Style = style,
            Width = width, Height = height,
            ContentWidth = width, ContentHeight = height,
            MarginLeft = mLeft, MarginRight = mRight, MarginTop = mTop, MarginBottom = mBottom,
            ImageSource = source,
            IsFloat = true,
        };
        image.Y = box.Y + box.PaddingTop + childY + mTop;
        image.X = side == "right"
            ? box.X + box.PaddingLeft + box.ContentWidth - width - mRight
            : box.X + box.PaddingLeft + mLeft;
        box.Children.Add(image);

        // Exclusion: the margin box for a plain float; a shape is defined on the border box
        var shape = ShapeOutsideParser.Parse(style.Get("shape-outside"), width, height, imgFontSize,
            ShapeOutsideParser.ParseThreshold(style.Get("shape-image-threshold")));
        float regY = floatOriginY + box.PaddingTop + childY + mTop;
        float regHeight = height + mBottom;
        if (side == "left")
            floatCtx.AddLeftFloat(image.X, regY, width + mRight, regHeight, shape);
        else
            floatCtx.AddRightFloat(image.X - (shape.HasValue ? 0f : mLeft), regY, width + (shape.HasValue ? 0f : mLeft), regHeight, shape);

        // Where later `clear` content must start (relative to the content area), incl. shape-margin
        float bottom = image.Y + height + mBottom - (box.Y + box.PaddingTop);
        var shapeMargin = style.Get("shape-margin");
        if (!string.IsNullOrEmpty(shapeMargin))
        {
            float extra = ResolveLength(shapeMargin, box.ContentWidth, imgFontSize);
            if (extra > 0f) bottom += extra;
        }
        if (side == "left") leftFloatBottom = Math.Max(leftFloatBottom, bottom);
        else rightFloatBottom = Math.Max(rightFloatBottom, bottom);
    }
}
