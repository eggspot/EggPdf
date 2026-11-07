using System;
using EggPdf.Css;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private const float MinAlignmentShift = 0.01f;

    internal static void AlignInlineLines(LayoutBox box, float availableWidth, FloatContext? floats, float floatOriginY)
    {
        var align = LogicalPropertyResolver.PhysicalTextAlign(box.Style);
        if (align != "center" && align != "right") return;

        var children = box.Children;
        int lineStart = 0;
        while (lineStart < children.Count)
        {
            if (!children[lineStart].IsInlineFlowItem)
            {
                lineStart++;
                continue;
            }

            float lineTop = children[lineStart].InlineLineTop;
            float left = float.MaxValue, right = float.MinValue, height = 0f;
            int lineEnd = lineStart;
            while (lineEnd < children.Count && children[lineEnd].InlineLineTop == lineTop)
            {
                var item = children[lineEnd];
                if (item.X < left) left = item.X;
                if (item.X + item.Width > right) right = item.X + item.Width;
                if (item.Height > height) height = item.Height;
                lineEnd++;
            }

            float dx = LineAlignmentShift(box, align, availableWidth, floats, floatOriginY, lineTop, left, right, height);
            if (Math.Abs(dx) >= MinAlignmentShift)
                OffsetLineItems(box, lineStart, lineEnd, dx);
            lineStart = lineEnd;
        }
    }

    private static float LineAlignmentShift(LayoutBox box, string align, float availableWidth, FloatContext? floats,
        float floatOriginY, float lineTop, float lineLeft, float lineRight, float lineHeight)
    {
        float bandLeft = box.X + box.PaddingLeft;
        float bandRight = bandLeft + availableWidth;
        if (floats != null)
        {
            float absLineTop = floatOriginY + box.PaddingTop + lineTop;
            bandLeft = floats.GetContentStartX(absLineTop, lineHeight, bandLeft);
            bandRight -= floats.GetRightOffset(absLineTop, lineHeight, bandRight);
        }

        float freeSpace = bandRight - bandLeft - (lineRight - lineLeft);
        if (freeSpace <= 0f) return 0f;

        float targetLeft = align == "center" ? bandLeft + freeSpace / 2f : bandLeft + freeSpace;
        return targetLeft - lineLeft;
    }

    private static void OffsetLineItems(LayoutBox box, int lineStart, int lineEnd, float dx)
    {
        InlineElementSpan? lastOffsetSpan = null;
        for (int i = lineStart; i < lineEnd; i++)
        {
            var item = box.Children[i];
            item.X += dx;
            FlexLayout.OffsetChildren(item, dx, 0f);
            var span = item.InlineSpan;
            if (span != null && span != lastOffsetSpan)
            {
                span.OffsetX(dx);
                lastOffsetSpan = span;
            }
        }
    }
}
