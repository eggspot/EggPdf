using System;
using EggPdf.Css;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private const float MinAlignmentShift = 0.01f;

    private static void MarkForcedLineEnd(LayoutBox box)
    {
        if (box.Children.Count == 0) return;
        var last = box.Children[box.Children.Count - 1];
        if (last.IsInlineFlowItem) last.EndsLineAtForcedBreak = true;
    }

    internal static void AlignInlineLines(LayoutBox box, float availableWidth, FloatContext? floats, float floatOriginY)
    {
        var align = LogicalPropertyResolver.PhysicalTextAlign(box.Style);
        bool justify = align == "justify";
        if (align != "center" && align != "right" && !justify) return;

        var children = box.Children;
        int lastLineStart = justify ? LastInlineLineStart(children) : -1;
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

            if (justify)
            {
                bool justifiable = lineStart != lastLineStart && !children[lineEnd - 1].EndsLineAtForcedBreak;
                if (justifiable)
                    JustifyLine(box, lineStart, lineEnd, availableWidth, floats, floatOriginY, lineTop, left, right, height);
            }
            else
            {
                float dx = LineAlignmentShift(box, align, availableWidth, floats, floatOriginY, lineTop, left, right, height);
                if (Math.Abs(dx) >= MinAlignmentShift)
                    OffsetLineItems(box, lineStart, lineEnd, dx);
            }
            lineStart = lineEnd;
        }
    }

    private static int LastInlineLineStart(System.Collections.Generic.List<LayoutBox> children)
    {
        for (int i = children.Count - 1; i >= 0; i--)
        {
            if (!children[i].IsInlineFlowItem) continue;
            float lineTop = children[i].InlineLineTop;
            int start = i;
            while (start > 0 && children[start - 1].IsInlineFlowItem && children[start - 1].InlineLineTop == lineTop)
                start--;
            return start;
        }
        return -1;
    }

    private static void JustifyLine(LayoutBox box, int lineStart, int lineEnd, float availableWidth, FloatContext? floats,
        float floatOriginY, float lineTop, float lineLeft, float lineRight, float lineHeight)
    {
        int gaps = 0;
        for (int i = lineStart + 1; i < lineEnd; i++)
            if (StartsWithJustifiableSpace(box.Children[i])) gaps++;
        if (gaps == 0) return;

        var (bandLeft, bandRight) = LineBand(box, availableWidth, floats, floatOriginY, lineTop, lineHeight);
        float freeSpace = bandRight - lineRight;
        if (freeSpace <= MinAlignmentShift) return;

        float perGap = freeSpace / gaps;
        float shift = 0f;
        for (int i = lineStart + 1; i < lineEnd; i++)
        {
            var item = box.Children[i];
            if (StartsWithJustifiableSpace(item)) shift += perGap;
            if (shift >= MinAlignmentShift)
            {
                item.X += shift;
                FlexLayout.OffsetChildren(item, shift, 0f);
            }
        }
    }

    private static bool StartsWithJustifiableSpace(LayoutBox item) =>
        !string.IsNullOrEmpty(item.Text) && item.Text![0] == ' ';

    private static (float Left, float Right) LineBand(LayoutBox box, float availableWidth, FloatContext? floats,
        float floatOriginY, float lineTop, float lineHeight)
    {
        float bandLeft = box.X + box.PaddingLeft;
        float bandRight = bandLeft + availableWidth;
        if (floats != null)
        {
            float absLineTop = floatOriginY + box.PaddingTop + lineTop;
            bandLeft = floats.GetContentStartX(absLineTop, lineHeight, bandLeft);
            bandRight -= floats.GetRightOffset(absLineTop, lineHeight, bandRight);
        }
        return (bandLeft, bandRight);
    }

    private static float LineAlignmentShift(LayoutBox box, string align, float availableWidth, FloatContext? floats,
        float floatOriginY, float lineTop, float lineLeft, float lineRight, float lineHeight)
    {
        var (bandLeft, bandRight) = LineBand(box, availableWidth, floats, floatOriginY, lineTop, lineHeight);
        float freeSpace = bandRight - bandLeft - (lineRight - lineLeft);
        if (freeSpace <= 0f) return 0f;

        float targetLeft = align == "center" ? bandLeft + freeSpace / 2f : bandLeft + freeSpace;
        return targetLeft - lineLeft;
    }

    private static void OffsetLineItems(LayoutBox box, int lineStart, int lineEnd, float dx)
    {
        for (int i = lineStart; i < lineEnd; i++)
        {
            var item = box.Children[i];
            item.X += dx;
            FlexLayout.OffsetChildren(item, dx, 0f);
        }
    }
}
