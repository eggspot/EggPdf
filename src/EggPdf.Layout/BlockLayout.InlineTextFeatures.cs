using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private static readonly string LineClampEllipsis = ((char)0x2026).ToString();
    private const int BalanceSearchSteps = 12;
    private const float BalanceWidthSlack = 1f;

    private static int FirstLetterLength(string text)
    {
        int i = 0;
        while (i < text.Length && char.IsPunctuation(text[i])) i++;
        if (i < text.Length) i++;
        if (i < text.Length && char.IsLowSurrogate(text[i])) i++;
        return i;
    }

    private static float InitialLetterScale(ComputedStyle style)
    {
        var initialLetter = style.Get("initial-letter");
        if (string.IsNullOrEmpty(initialLetter) || initialLetter == "normal") return 0f;
        if (!TryParseFirstToken(initialLetter!, out float scale)) return 0f;
        return scale < 1f ? 1f : scale;
    }

    private static bool SplitFirstLetterRun(List<InlineRun> runs, ComputedStyle blockStyle,
        ComputedStyle? firstLetterStyle, float initialLetterScale)
    {
        for (int i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.Text == "\n") return false;

            int start = 0;
            while (start < run.Text.Length && char.IsWhiteSpace(run.Text[start])) start++;
            if (start >= run.Text.Length) continue;

            var visible = run.Text.Substring(start);
            int length = FirstLetterLength(visible);
            var letterStyle = FirstLetterStyle(run.Style, blockStyle, firstLetterStyle);
            float letterFontSize = initialLetterScale > 0f
                ? run.FontSize * initialLetterScale
                : ResolveFontSize(letterStyle.FontSize, run.FontSize);
            if (initialLetterScale > 0f)
                letterStyle.Set("font-size", letterFontSize.ToString(CultureInfo.InvariantCulture) + "px");

            var letterRun = run;
            letterRun.Text = visible.Substring(0, length);
            letterRun.Style = letterStyle;
            letterRun.FontSize = letterFontSize;

            if (length == visible.Length)
            {
                runs[i] = letterRun;
                return true;
            }

            letterRun.EndInset = 0f;
            letterRun.EndMargin = 0f;
            var restRun = run;
            restRun.Text = visible.Substring(length);
            restRun.HasLeadingSpace = false;
            restRun.StartInset = 0f;
            restRun.StartMargin = 0f;
            runs[i] = letterRun;
            runs.Insert(i + 1, restRun);
            return true;
        }
        return false;
    }

    private static ComputedStyle FirstLetterStyle(ComputedStyle runStyle, ComputedStyle blockStyle, ComputedStyle? firstLetterStyle)
    {
        var style = new ComputedStyle();
        foreach (var kv in runStyle.All)
            style.Set(kv.Key, kv.Value);
        if (firstLetterStyle == null) return style;
        foreach (var kv in firstLetterStyle.All)
            if (blockStyle.Get(kv.Key) != kv.Value)
                style.Set(kv.Key, kv.Value);
        return style;
    }

    private static int LineClampCount(ComputedStyle style)
    {
        var value = style.Get("line-clamp") ?? style.Get("-webkit-line-clamp");
        if (string.IsNullOrEmpty(value) || value == "none") return 0;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lines) && lines > 0 ? lines : 0;
    }

    private static void ClampInlineLines(LayoutBox box, int maxLines, float containerWidth, ref float childY)
    {
        var children = box.Children;
        int linesSeen = 0;
        float lineTop = float.NaN, lastKeptTop = float.NaN, cutTop = float.NaN;
        for (int i = 0; i < children.Count; i++)
        {
            var item = children[i];
            if (!item.IsInlineFlowItem || item.InlineLineTop == lineTop) continue;
            lineTop = item.InlineLineTop;
            linesSeen++;
            if (linesSeen > maxLines)
            {
                cutTop = lineTop;
                break;
            }
            lastKeptTop = lineTop;
        }
        if (float.IsNaN(cutTop)) return;

        float contentTop = box.Y + box.PaddingTop;
        children.RemoveAll(c => c.IsInlineFlowItem ? c.InlineLineTop >= cutTop : c.Y - contentTop >= cutTop);
        EndLineWithEllipsis(box, lastKeptTop, box.X + box.PaddingLeft + containerWidth);
        childY = cutTop;
    }

    private static void EndLineWithEllipsis(LayoutBox box, float lineTop, float lineRightLimit)
    {
        var children = box.Children;
        while (true)
        {
            int lastIndex = -1, itemsOnLine = 0;
            float lineRight = float.MinValue;
            for (int i = 0; i < children.Count; i++)
            {
                var item = children[i];
                if (!item.IsInlineFlowItem || item.InlineLineTop != lineTop) continue;
                itemsOnLine++;
                if (item.Text != null) lastIndex = i;
                if (item.X + item.Width > lineRight) lineRight = item.X + item.Width;
            }
            if (lastIndex < 0) return;

            var last = children[lastIndex];
            float fontSize = ResolveFontSize(last.Style.FontSize, DefaultFontSize);
            float letterSpacing = ResolveLength(last.Style.Get("letter-spacing"), 0, fontSize);
            float ellipsisWidth = TextMeasurer.MeasureWidth(LineClampEllipsis, fontSize,
                last.Style.FontFamily, last.Style.FontWeight, last.Style.Get("font-style"), letterSpacing);

            if (lineRight + ellipsisWidth > lineRightLimit && itemsOnLine > 1)
            {
                last.InlineSpan?.RemoveFragment(last);
                children.RemoveAt(lastIndex);
                continue;
            }

            float maxTextWidth = lineRightLimit - ellipsisWidth - last.X - last.PaddingLeft - last.PaddingRight;
            var text = last.Text!.TrimEnd();
            while (text.Length > 1 && TextMeasurer.MeasureWidth(text, fontSize, last.Style.FontFamily,
                       last.Style.FontWeight, last.Style.Get("font-style"), letterSpacing) > maxTextWidth)
                text = text.Substring(0, text.Length - 1).TrimEnd();

            last.Text = text + LineClampEllipsis;
            last.ContentWidth = TextMeasurer.MeasureWidth(last.Text, fontSize, last.Style.FontFamily,
                last.Style.FontWeight, last.Style.Get("font-style"), letterSpacing);
            last.Width = last.ContentWidth + last.PaddingLeft + last.PaddingRight;
            last.Justification = LineJustification.NotJustifiable;
            return;
        }
    }

    private static float BalancedInlineWrapWidth(HtmlElement element, ComputedStyle style, float containerWidth,
        float fontSize, Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver)
    {
        if (style.Get("text-wrap") != "balance") return containerWidth;

        var words = new List<(float Width, float SpaceBefore, bool BreaksBefore)>();
        bool pendingSpace = false, pendingBreak = false;
        if (!CollectBalanceWords(element, style, fontSize, resolver, words, ref pendingSpace, ref pendingBreak))
            return containerWidth;

        int lines = CountGreedyLines(words, containerWidth);
        if (!TextWrapBalance.ShouldBalance("balance", lines)) return containerWidth;

        float low = 0f, high = containerWidth;
        for (int step = 0; step < BalanceSearchSteps; step++)
        {
            float mid = (low + high) / 2f;
            if (CountGreedyLines(words, mid) <= lines) high = mid;
            else low = mid;
        }
        return Math.Min(containerWidth, high + BalanceWidthSlack);
    }

    private static bool CollectBalanceWords(HtmlElement element, ComputedStyle style, float fontSize,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver,
        List<(float Width, float SpaceBefore, bool BreaksBefore)> words, ref bool pendingSpace, ref bool pendingBreak)
    {
        foreach (var node in element.ChildNodes)
        {
            if (node is HtmlTextNode textNode)
            {
                AddBalanceWords(textNode.Data, style, fontSize, words, ref pendingSpace, ref pendingBreak);
                continue;
            }
            if (!(node is HtmlElement child)) continue;
            if (child.TagName == "br")
            {
                pendingBreak = true;
                pendingSpace = false;
                continue;
            }

            var childStyle = resolver(child, style);
            if (childStyle.Display == "none") continue;
            if (childStyle.Display != "inline" || child.TagName == "img" || child.TagName == "svg" ||
                child.TagName == "picture" || child.TagName == "ruby")
                return false;

            float childFontSize = ResolveFontSize(childStyle.FontSize, fontSize);
            if (!CollectBalanceWords(child, childStyle, childFontSize, resolver, words, ref pendingSpace, ref pendingBreak))
                return false;
        }
        return true;
    }

    private static void AddBalanceWords(string text, ComputedStyle style, float fontSize,
        List<(float Width, float SpaceBefore, bool BreaksBefore)> words, ref bool pendingSpace, ref bool pendingBreak)
    {
        var family = style.FontFamily;
        var weight = style.FontWeight;
        var fontStyle = style.Get("font-style");
        float letterSpacing = ResolveLength(style.Get("letter-spacing"), 0, fontSize);
        float spaceWidth = TextMeasurer.MeasureWidth(" ", fontSize, family, weight, fontStyle, letterSpacing);
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]) && text[i] != NonBreakingSpace)
            {
                pendingSpace = true;
                i++;
                continue;
            }
            int end = i;
            while (end < text.Length && !(char.IsWhiteSpace(text[end]) && text[end] != NonBreakingSpace)) end++;
            float width = TextMeasurer.MeasureWidth(text.Substring(i, end - i), fontSize, family, weight, fontStyle, letterSpacing);
            words.Add((width, pendingSpace && words.Count > 0 ? spaceWidth : 0f, pendingBreak));
            pendingSpace = false;
            pendingBreak = false;
            i = end;
        }
    }

    private static int CountGreedyLines(List<(float Width, float SpaceBefore, bool BreaksBefore)> words, float width)
    {
        int lines = 1;
        float lineWidth = 0f;
        bool lineEmpty = true;
        foreach (var word in words)
        {
            if (word.BreaksBefore && !lineEmpty)
            {
                lines++;
                lineWidth = 0f;
                lineEmpty = true;
            }
            if (lineEmpty)
            {
                lineWidth = word.Width;
                lineEmpty = false;
            }
            else if (lineWidth + word.SpaceBefore + word.Width > width)
            {
                lines++;
                lineWidth = word.Width;
            }
            else
            {
                lineWidth += word.SpaceBefore + word.Width;
            }
        }
        return lines;
    }
}
