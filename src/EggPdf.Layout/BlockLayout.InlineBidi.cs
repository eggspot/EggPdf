using System.Collections.Generic;
using System.Text;
using EggPdf.Text;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private const char ObjectReplacementCharacter = (char)0xFFFC;

    private static void ReorderInlineLinesByBidiLevel(LayoutBox box)
    {
        bool baseRtl = box.Style.Get("direction") == "rtl";
        var children = box.Children;
        if (!baseRtl && !AnyInlineItemContainsRtl(children)) return;

        int lineStart = 0;
        while (lineStart < children.Count)
        {
            if (!children[lineStart].IsInlineFlowItem)
            {
                lineStart++;
                continue;
            }

            float lineTop = children[lineStart].InlineLineTop;
            int lineEnd = lineStart;
            while (lineEnd < children.Count && children[lineEnd].IsInlineFlowItem && children[lineEnd].InlineLineTop == lineTop)
                lineEnd++;
            if (lineEnd - lineStart > 1)
                ReorderLineByBidiLevel(children, lineStart, lineEnd, baseRtl);
            lineStart = lineEnd;
        }
    }

    private static bool AnyInlineItemContainsRtl(List<LayoutBox> children)
    {
        for (int i = 0; i < children.Count; i++)
        {
            var item = children[i];
            if (item.IsInlineFlowItem && item.Text != null && BidiAlgorithm.ContainsRTL(item.Text))
                return true;
        }
        return false;
    }

    private static void ReorderLineByBidiLevel(List<LayoutBox> children, int lineStart, int lineEnd, bool baseRtl)
    {
        int count = lineEnd - lineStart;
        var starts = new int[count + 1];
        var lineText = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            starts[i] = lineText.Length;
            var text = children[lineStart + i].Text;
            if (string.IsNullOrEmpty(text)) lineText.Append(ObjectReplacementCharacter);
            else lineText.Append(text);
        }
        starts[count] = lineText.Length;

        var lineCharacters = lineText.ToString();
        var charLevels = BidiAlgorithm.ResolveLevels(lineCharacters, baseRtl);
        var levels = new int[count];
        int highest = 0, lowestOdd = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            levels[i] = FragmentLevel(lineCharacters, charLevels, starts[i], starts[i + 1]);
            if (levels[i] > highest) highest = levels[i];
            int odd = levels[i] % 2 == 1 ? levels[i] : levels[i] + 1;
            if (odd < lowestOdd) lowestOdd = odd;
        }

        for (int level = highest; level >= lowestOdd; level--)
        {
            int i = 0;
            while (i < count)
            {
                if (levels[i] < level)
                {
                    i++;
                    continue;
                }
                int runEnd = i;
                while (runEnd < count && levels[runEnd] >= level) runEnd++;
                if (runEnd - i > 1)
                    MirrorFragmentsWithinTheirExtent(children, lineStart + i, lineStart + runEnd);
                i = runEnd;
            }
        }
    }

    private static int FragmentLevel(string lineText, int[] charLevels, int start, int end)
    {
        for (int i = start; i < end; i++)
            if (char.IsLetterOrDigit(lineText[i]))
                return charLevels[i];
        return charLevels[start];
    }

    private static void MirrorFragmentsWithinTheirExtent(List<LayoutBox> children, int from, int to)
    {
        float left = float.MaxValue, right = float.MinValue;
        for (int i = from; i < to; i++)
        {
            var item = children[i];
            if (item.X < left) left = item.X;
            if (item.X + item.Width > right) right = item.X + item.Width;
        }

        for (int i = from; i < to; i++)
        {
            var item = children[i];
            float mirroredX = left + right - (item.X + item.Width);
            float dx = mirroredX - item.X;
            item.X = mirroredX;
            if (System.Math.Abs(dx) >= MinAlignmentShift)
                FlexLayout.OffsetChildren(item, dx, 0f);
        }
    }
}
