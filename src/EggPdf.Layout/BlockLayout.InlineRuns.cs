using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    private static string CollectText(HtmlElement elem)
    {
        var sb = new System.Text.StringBuilder();
        CollectTextRecursive(elem, sb);
        return sb.ToString().Trim();
    }

    private static void CollectInlineRuns(HtmlNode node, ComputedStyle parentStyle, float parentFontSize,
        Func<HtmlElement, ComputedStyle?, ComputedStyle> resolver, List<InlineRun> runs)
    {
        if (node is HtmlTextNode textNode)
        {
            var data = textNode.Data;
            if (PreservesWhitespace(parentStyle))
            {
                AddPreservedTextRuns(data, parentStyle, parentFontSize, runs);
            }
            else if (!string.IsNullOrEmpty(data))
            {
                runs.Add(new InlineRun
                {
                    Text = data,
                    Style = parentStyle,
                    Element = null,
                    FontSize = parentFontSize,
                    HasLeadingSpace = data.Length > 0 && data[0] != NonBreakingSpace && char.IsWhiteSpace(data[0])
                });
            }
            return;
        }

        if (node is HtmlElement elem)
        {
            if (elem.TagName == "br")
            {
                runs.Add(new InlineRun { Text = "\n", Style = parentStyle, Element = elem, FontSize = parentFontSize });
                return;
            }

            if (elem.TagName == "rt" || elem.TagName == "rp") return;

            var style = resolver(elem, parentStyle);
            if (style.Display == "none") return;
            float fontSize = ResolveFontSize(style.FontSize, parentFontSize);
            int firstRunOfElement = runs.Count;

            if (TryResolvePseudoContent(elem, "before", style, out var beforeStyle, out var beforeContent))
            {
                float bfs = ResolveFontSize(beforeStyle!.FontSize, fontSize);
                runs.Add(new InlineRun { Text = beforeContent!, Style = beforeStyle, Element = elem, FontSize = bfs, HasLeadingSpace = StartsWithCollapsibleWhitespace(beforeContent!) });
            }

            foreach (var child in elem.ChildNodes)
                CollectInlineRuns(child, style, fontSize, resolver, runs);

            if (TryResolvePseudoContent(elem, "after", style, out var afterStyle, out var afterContent))
            {
                float afs = ResolveFontSize(afterStyle!.FontSize, fontSize);
                runs.Add(new InlineRun { Text = afterContent!, Style = afterStyle, Element = null, FontSize = afs, HasLeadingSpace = StartsWithCollapsibleWhitespace(afterContent!) });
            }

            ApplyInlineBoxInsets(runs, firstRunOfElement, style, fontSize);
        }
    }

    private static void ApplyInlineBoxInsets(List<InlineRun> runs, int firstRunOfElement, ComputedStyle style, float fontSize)
    {
        float start = InlineEdgeInset(style, "left", fontSize);
        float end = InlineEdgeInset(style, "right", fontSize);
        float startMargin = ResolveLength(style.Get("margin-left"), 0, fontSize);
        float endMargin = ResolveLength(style.Get("margin-right"), 0, fontSize);
        if (start <= 0f && end <= 0f && startMargin == 0f && endMargin == 0f) return;

        int first = firstRunOfElement;
        while (first < runs.Count && runs[first].Text == "\n") first++;
        int last = runs.Count - 1;
        while (last >= first && runs[last].Text == "\n") last--;
        if (first > last) return;

        var firstRun = runs[first];
        firstRun.StartInset += start;
        firstRun.StartMargin += startMargin;
        runs[first] = firstRun;
        var lastRun = runs[last];
        lastRun.EndInset += end;
        lastRun.EndMargin += endMargin;
        runs[last] = lastRun;
    }

    private static float InlineEdgeInset(ComputedStyle style, string side, float fontSize)
    {
        var padding = side == "left" ? style.PaddingLeft : style.PaddingRight;
        return Math.Max(0f, ResolveLength(padding, 0, fontSize)) + VisibleBorderWidth(style, side, fontSize);
    }

    private static bool PaintsInlineDecoration(ComputedStyle style)
    {
        var background = style.Get("background-color");
        if (!string.IsNullOrEmpty(background) && background != "transparent" && background != "rgba(0, 0, 0, 0)") return true;
        var backgroundImage = style.Get("background-image");
        if (!string.IsNullOrEmpty(backgroundImage) && backgroundImage != "none") return true;
        var decoration = style.Get("text-decoration-line") ?? style.Get("text-decoration");
        return !string.IsNullOrEmpty(decoration) && decoration != "none";
    }

    private static bool OnlySpacesFrom(string text, int index)
    {
        for (int i = index; i < text.Length; i++)
            if (text[i] != ' ') return false;
        return true;
    }

    internal static bool TryResolvePseudoContent(HtmlElement element, string pseudo, ComputedStyle parentStyle,
        out ComputedStyle? pseudoStyle, out string? content)
    {
        pseudoStyle = null;
        content = null;

        var cascadeRes = _threadCascadeResolver;
        var counterCtx = _threadCounterCtx;
        if (cascadeRes == null || counterCtx == null) return false;

        var resolved = cascadeRes.ResolvePseudoElement(element, pseudo, parentStyle);
        if (resolved == null) return false;

        var text = counterCtx.ResolveContent(resolved.Get("content"), element, resolved);
        pseudoStyle = resolved;
        if (text == null) return false;

        content = text;
        return true;
    }

    private static void LayoutInlineRuns(List<InlineRun> runs, LayoutBox box, HtmlElement? wrapperElement,
        ref float inlineX, ref float childY, ref float inlineLineHeight, float containerWidth,
        ComputedStyle parentStyle, float parentFontSize, FloatContext? floats = null, float floatOriginY = 0f,
        float lineStartX = 0f)
    {
        bool elementAssigned = false;
        bool prevRunTrailingSpace = false;

        InlineElementSpan? currentSpan = null;
        float currentSpanLineY = float.NaN;

        bool floatsActive = floats != null;
        float lineStart = lineStartX;
        bool atLineStart = inlineX <= lineStart;
        float absContainerLeft = box.X + box.PaddingLeft;
        float absContainerRight = absContainerLeft + containerWidth;

        float RightLimit(float lineHeight, float currentChildY)
        {
            if (!floatsActive) return containerWidth;
            float absY = floatOriginY + box.PaddingTop + currentChildY;
            float rightOffset = floats!.GetRightOffset(absY, lineHeight, absContainerRight);
            float limit = containerWidth - rightOffset;
            return limit > 0 ? limit : 0;
        }

        float ApplyLeftInsetIfNeeded(float lineHeight, float currentInlineX, float currentChildY)
        {
            if (!floatsActive || !atLineStart || currentInlineX > lineStart) return currentInlineX;
            float absY = floatOriginY + box.PaddingTop + currentChildY;
            float startX = floats!.GetContentStartX(absY, lineHeight, absContainerLeft);
            float inset = startX - absContainerLeft;
            return (inset > 0 ? inset : 0f) + lineStart;
        }

        float parentBaseline = TextMeasurer.GetBaselineOffset(parentFontSize,
            TextMeasurer.GetLineHeight(parentFontSize, parentStyle.Get("line-height")),
            parentStyle.FontFamily, parentStyle.FontWeight, parentStyle.Get("font-style"));

        for (int ri = 0; ri < runs.Count; ri++)
        {
            var run = runs[ri];

            if (run.Text == "\n")
            {
                float lh = TextMeasurer.GetLineHeight(run.FontSize, run.Style.Get("line-height"));
                if (!(floatsActive ? atLineStart : inlineX <= lineStart))
                {
                    MarkForcedLineEnd(box);
                    childY += Math.Max(inlineLineHeight, lh);
                    inlineLineHeight = 0;
                }
                else
                {
                    childY += lh;
                }
                inlineX = 0;
                lineStart = 0;
                atLineStart = true;
                prevRunTrailingSpace = false;
                continue;
            }

            var text = run.KeepsSpaces ? run.Text : NormalizeInlineWhitespace(run.Text);

            if (text.Length == 0)
            {
                char lastRunCharSkip = run.Text.Length > 0 ? run.Text[run.Text.Length - 1] : '\0';
                prevRunTrailingSpace = lastRunCharSkip != NonBreakingSpace && char.IsWhiteSpace(lastRunCharSkip);
                continue;
            }

            text = ApplyTextTransformForMeasure(text, run.Style);

            var fontFamily = run.Style.FontFamily;
            var fontWeight = run.Style.FontWeight;
            var fontStyle = run.Style.Get("font-style");
            float runLetterSpacing = ResolveLength(run.Style.Get("letter-spacing"), 0, run.FontSize);
            float lhRun = TextMeasurer.GetLineHeight(run.FontSize, run.Style.Get("line-height"),
                fontFamily, fontWeight, fontStyle, text);

            var runOverflowWrap = run.Style.Get("overflow-wrap") ?? run.Style.Get("word-wrap");
            var runWordBreak = run.Style.Get("word-break");
            bool runNoWrap = SuppressesLineWrap(run.Style);
            bool runBreakWord = !runNoWrap &&
                                (runOverflowWrap == "break-word" || runOverflowWrap == "anywhere" ||
                                 runWordBreak == "break-all" || runWordBreak == "break-word");
            bool runHyphenates = !runNoWrap && !run.KeepsSpaces && run.Style.Get("hyphens") == "auto";
            float visibleHyphenWidth = 0f;

            float baselineShift = run.FontSize == parentFontSize
                ? 0f
                : parentBaseline - TextMeasurer.GetBaselineOffset(run.FontSize, lhRun, fontFamily, fontWeight, fontStyle, text);

            var pieces = run.KeepsSpaces
                ? new List<(string Text, bool JoinsPrevious, bool EndsAtSoftHyphen)> { (text, true, false) }
                : NeedsBreakablePieces(text) ? SplitIntoBreakablePieces(text) : null;
            int pieceIndex = 0;
            int wPos = 0;
            bool firstWord = true;
            LayoutBox? previousPieceBox = null;
            bool previousEndsAtSoftHyphen = false;
            while (pieces != null ? pieceIndex < pieces.Count : wPos < text.Length)
            {
                string word;
                bool joinNoSpace = false;
                bool endsAtSoftHyphen = false;
                if (pieces != null)
                {
                    (word, joinNoSpace, endsAtSoftHyphen) = pieces[pieceIndex++];
                }
                else
                {
                    while (wPos < text.Length && text[wPos] == ' ') wPos++;
                    if (wPos >= text.Length) break;
                    int wEnd = text.IndexOf(' ', wPos);
                    if (wEnd < 0) wEnd = text.Length;
                    word = text.Substring(wPos, wEnd - wPos);
                    wPos = wEnd;
                }

                inlineX = ApplyLeftInsetIfNeeded(Math.Max(inlineLineHeight, lhRun), inlineX, childY);

                bool notAtLineStart = floatsActive ? !atLineStart : inlineX > lineStart;
                bool needSpace = notAtLineStart && !joinNoSpace && (!firstWord || run.HasLeadingSpace || prevRunTrailingSpace);
                var wordText = needSpace ? " " + word : word;
                float wordWidth = TextMeasurer.MeasureWidth(wordText, run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                float rightLimit = RightLimit(Math.Max(inlineLineHeight, lhRun), childY);
                bool lastWord = pieces != null ? pieceIndex >= pieces.Count : OnlySpacesFrom(text, wPos);
                float leadInset = firstWord ? run.StartInset : 0f;
                float trailInset = lastWord ? run.EndInset : 0f;
                float leadMargin = firstWord ? run.StartMargin : 0f;
                if ((endsAtSoftHyphen || runHyphenates) && visibleHyphenWidth == 0f)
                    visibleHyphenWidth = TextMeasurer.MeasureWidth(LineBreakOpportunities.VisibleHyphen, run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                float hyphenReserve = endsAtSoftHyphen ? visibleHyphenWidth : 0f;
                bool overflows = inlineX + leadMargin + wordWidth + leadInset + trailInset + hyphenReserve > rightLimit;
                bool mayHyphenate = runHyphenates && !joinNoSpace && !endsAtSoftHyphen;

                if (mayHyphenate && overflows && HyphenationSegments(word) is { } segmentsHere)
                {
                    string firstPiece = (needSpace ? " " : "") + segmentsHere[0].Text;
                    float firstPieceWidth = TextMeasurer.MeasureWidth(firstPiece, run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                    if (!notAtLineStart || inlineX + leadMargin + leadInset + firstPieceWidth + visibleHyphenWidth <= rightLimit)
                    {
                        ReplaceWordWithSegments(segmentsHere, text, wPos, ref pieces, ref pieceIndex);
                        continue;
                    }
                }

                if (!runNoWrap && notAtLineStart && overflows)
                {
                    if (joinNoSpace && previousEndsAtSoftHyphen && previousPieceBox != null)
                        AppendVisibleHyphen(previousPieceBox, run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                    childY += inlineLineHeight;
                    inlineX = 0;
                    lineStart = 0;
                    inlineLineHeight = 0;
                    atLineStart = true;
                    inlineX = ApplyLeftInsetIfNeeded(lhRun, inlineX, childY);
                    wordText = word;
                    wordWidth = TextMeasurer.MeasureWidth(wordText, run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                    rightLimit = RightLimit(lhRun, childY);
                    if (mayHyphenate && inlineX + wordWidth + leadInset + trailInset > rightLimit &&
                        HyphenationSegments(word) is { } segmentsAfterWrap)
                    {
                        ReplaceWordWithSegments(segmentsAfterWrap, text, wPos, ref pieces, ref pieceIndex);
                        continue;
                    }
                }

                if (runBreakWord && inlineX + wordWidth > rightLimit && wordText.Length > 1)
                {
                    int start = 0;
                    while (start < wordText.Length)
                    {
                        float chunkLimit = RightLimit(lhRun, childY);
                        int len = 1;
                        float chunkWidth = TextMeasurer.MeasureWidth(wordText.Substring(start, 1),
                            run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                        while (start + len < wordText.Length)
                        {
                            float nextWidth = TextMeasurer.MeasureWidth(wordText.Substring(start, len + 1),
                                run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                            if (inlineX + nextWidth > chunkLimit) break;
                            len++;
                            chunkWidth = nextWidth;
                        }

                        var chunkBox = new LayoutBox
                        {
                            Element = (!elementAssigned && wrapperElement != null) ? wrapperElement : null,
                            Style = run.Style,
                            InlineLineTop = childY,
                            X = box.X + box.PaddingLeft + inlineX,
                            Y = box.Y + box.PaddingTop + childY + baselineShift,
                            Width = chunkWidth,
                            Height = lhRun,
                            ContentWidth = chunkWidth,
                            ContentHeight = lhRun,
                            Text = wordText.Substring(start, len)
                        };
                        if (!elementAssigned && wrapperElement != null)
                            elementAssigned = true;
                        if (wrapperElement != null)
                        {
                            if (currentSpan == null || childY != currentSpanLineY)
                            {
                                currentSpan = new InlineElementSpan(wrapperElement, chunkBox);
                                currentSpanLineY = childY;
                            }
                            else
                            {
                                currentSpan.AddFragment(chunkBox);
                            }
                            chunkBox.InlineSpan = currentSpan;
                        }
                        box.Children.Add(chunkBox);
                        inlineX += chunkWidth;
                        atLineStart = false;
                        if (lhRun > inlineLineHeight)
                            inlineLineHeight = lhRun;

                        start += len;
                        if (start < wordText.Length)
                        {
                            childY += inlineLineHeight > 0 ? inlineLineHeight : lhRun;
                            inlineX = 0;
                            lineStart = 0;
                            inlineLineHeight = 0;
                            atLineStart = true;
                            inlineX = ApplyLeftInsetIfNeeded(lhRun, inlineX, childY);
                        }
                    }
                    firstWord = false;
                    previousPieceBox = null;
                    previousEndsAtSoftHyphen = false;
                    continue;
                }

                inlineX += leadMargin;
                if (firstWord && wordText.Length > word.Length && (leadInset > 0f || PaintsInlineDecoration(run.Style)))
                {
                    float wordOnlyWidth = TextMeasurer.MeasureWidth(word, run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                    inlineX += wordWidth - wordOnlyWidth;
                    wordText = word;
                    wordWidth = wordOnlyWidth;
                }

                var textBox = new LayoutBox
                {
                    Element = (!elementAssigned && wrapperElement != null) ? wrapperElement : null,
                    Style = run.Style,
                    InlineLineTop = childY,
                    X = box.X + box.PaddingLeft + inlineX,
                    Y = box.Y + box.PaddingTop + childY + baselineShift,
                    Width = wordWidth + leadInset + trailInset,
                    Height = lhRun,
                    PaddingLeft = leadInset,
                    PaddingRight = trailInset,
                    ContentWidth = wordWidth,
                    ContentHeight = lhRun,
                    Text = wordText
                };

                if (!elementAssigned && wrapperElement != null)
                    elementAssigned = true;

                if (wrapperElement != null)
                {
                    if (currentSpan == null || childY != currentSpanLineY)
                    {
                        currentSpan = new InlineElementSpan(wrapperElement, textBox);
                        currentSpanLineY = childY;
                    }
                    else
                    {
                        currentSpan.AddFragment(textBox);
                    }
                    textBox.InlineSpan = currentSpan;
                }

                box.Children.Add(textBox);
                inlineX += textBox.Width + (lastWord ? run.EndMargin : 0f);
                atLineStart = false;
                if (lhRun > inlineLineHeight)
                    inlineLineHeight = lhRun;
                firstWord = false;
                previousPieceBox = textBox;
                previousEndsAtSoftHyphen = endsAtSoftHyphen;
            }

            char lastRunChar = run.Text.Length > 0 ? run.Text[run.Text.Length - 1] : '\0';
            prevRunTrailingSpace = !run.KeepsSpaces && lastRunChar != NonBreakingSpace && char.IsWhiteSpace(lastRunChar);
        }
    }

    private const int PreservedTabSize = 8;

    private static void AddPreservedTextRuns(string data, ComputedStyle style, float fontSize, List<InlineRun> runs)
    {
        if (string.IsNullOrEmpty(data)) return;
        bool keepsSpaces = style.Get("white-space") != "pre-line";
        var lines = data.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                runs.Add(new InlineRun { Text = "\n", Style = style, Element = null, FontSize = fontSize });
            var line = keepsSpaces ? TextMeasurer.ExpandTabs(lines[i], PreservedTabSize) : lines[i];
            if (line.Length == 0) continue;
            runs.Add(new InlineRun
            {
                Text = line,
                Style = style,
                Element = null,
                FontSize = fontSize,
                KeepsSpaces = keepsSpaces,
                HasLeadingSpace = !keepsSpaces && StartsWithCollapsibleWhitespace(line),
            });
        }
    }

    private static void AppendVisibleHyphen(LayoutBox wordBox, float fontSize, string? fontFamily, string? fontWeight,
        string? fontStyle, float letterSpacing)
    {
        float hyphenWidth = TextMeasurer.MeasureWidth(LineBreakOpportunities.VisibleHyphen, fontSize, fontFamily, fontWeight, fontStyle, letterSpacing);
        wordBox.Text += LineBreakOpportunities.VisibleHyphen;
        wordBox.Width += hyphenWidth;
        wordBox.ContentWidth += hyphenWidth;
    }

    private static List<(string Text, bool JoinsPrevious, bool EndsAtSoftHyphen)>? HyphenationSegments(string word)
    {
        var points = TextMeasurer.HyphenationPoints(word);
        if (points.Length == 0) return null;

        var segments = new List<(string Text, bool JoinsPrevious, bool EndsAtSoftHyphen)>(points.Length + 1);
        int start = 0;
        for (int i = 0; i < points.Length; i++)
        {
            segments.Add((word.Substring(start, points[i] - start), i > 0, true));
            start = points[i];
        }
        segments.Add((word.Substring(start), true, false));
        return segments;
    }

    private static void ReplaceWordWithSegments(List<(string Text, bool JoinsPrevious, bool EndsAtSoftHyphen)> segments,
        string runText, int nextWordPosition,
        ref List<(string Text, bool JoinsPrevious, bool EndsAtSoftHyphen)>? pieces, ref int pieceIndex)
    {
        if (pieces == null)
        {
            foreach (var rest in runText.Substring(nextWordPosition).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                segments.Add((rest, false, false));
            pieces = segments;
            pieceIndex = 0;
        }
        else
        {
            pieceIndex--;
            pieces.RemoveAt(pieceIndex);
            pieces.InsertRange(pieceIndex, segments);
        }
    }

    private static bool NeedsBreakablePieces(string text) =>
        EggPdf.Text.SpacelessLineBreaker.Contains(text) || LineBreakOpportunities.HasBreaksInsideWords(text);

    private static List<(string Text, bool JoinsPrevious, bool EndsAtSoftHyphen)> SplitIntoBreakablePieces(string text)
    {
        var pieces = new List<(string Text, bool JoinsPrevious, bool EndsAtSoftHyphen)>();
        foreach (var word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!EggPdf.Text.SpacelessLineBreaker.Contains(word))
            {
                LineBreakOpportunities.AppendSegments(word, false, pieces);
                continue;
            }
            var syllables = EggPdf.Text.SpacelessLineBreaker.Split(word);
            for (int i = 0; i < syllables.Count; i++)
                LineBreakOpportunities.AppendSegments(syllables[i], i > 0, pieces);
        }
        return pieces;
    }

}
