using System;
using System.Globalization;
using EggPdf.Css;
using EggPdf.Html.Dom;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    /// <summary>Collect all text content from an element (no whitespace trimming beyond basic collapse).</summary>
    private static string CollectText(HtmlElement elem)
    {
        var sb = new System.Text.StringBuilder();
        CollectTextRecursive(elem, sb);
        return sb.ToString().Trim();
    }

    /// <summary>Collect text runs from an inline element tree, preserving style per segment.</summary>
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

            // <rt> and <rp> are handled by LayoutRubyInline — skip them in normal inline flow
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

    /// <summary>
    /// Resolve a ::before/::after pseudo-element for <paramref name="element"/>: its computed
    /// style and the text produced by its `content` property. Returns false when no cascade
    /// context is active, the pseudo-element has no matching rule, or `content` yields nothing; in
    /// the last case <paramref name="pseudoStyle"/> is still set, so callers can lay out an empty box.
    /// </summary>
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

    /// <summary>Layout inline runs as word-level boxes with style-aware wrapping.</summary>
    private static void LayoutInlineRuns(List<InlineRun> runs, LayoutBox box, HtmlElement? wrapperElement,
        ref float inlineX, ref float childY, ref float inlineLineHeight, float containerWidth,
        ComputedStyle parentStyle, float parentFontSize, FloatContext? floats = null, float floatOriginY = 0f,
        float lineStartX = 0f)
    {
        bool elementAssigned = false;
        bool prevRunTrailingSpace = false;

        // Every word-fragment this call produces belongs to the same wrapperElement (see the
        // call site in BlockLayout.cs -- one LayoutInlineRuns call per inline child element).
        // currentSpan accumulates the union rect of fragments on one line; childY changing means
        // a new line, so it starts a fresh span rather than extending across the line-break gap.
        InlineElementSpan? currentSpan = null;
        float currentSpanLineY = float.NaN;

        // Float-aware wrapping (including shape-outside): when floats are active, the left
        // inset and right wrap boundary are queried per line at that line's absolute Y
        // instead of the flat containerWidth used otherwise. atLineStart tracks "nothing
        // placed on the current line yet" explicitly rather than inferring it from
        // inlineX > 0, since a float-inset line legitimately starts with inlineX > 0 (the
        // classic case this whole mechanism exists for: text starting indented beside a
        // floated image). When floats is null (the overwhelming majority of documents,
        // which don't use float at all), every helper below degenerates to exactly the
        // original flat-width behavior.
        bool floatsActive = floats != null;
        float lineStart = lineStartX;
        bool atLineStart = inlineX <= lineStart;
        float absContainerLeft = box.X + box.PaddingLeft;
        float absContainerRight = absContainerLeft + containerWidth;

        // Local functions can't capture the ref parameters inlineX/childY, so the current
        // value is passed in explicitly and (for the inset) returned back to the caller.
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
                // A run that normalizes to nothing was pure whitespace -- e.g. the newline +
                // indentation HTML source formatting often leaves as its own text node between
                // two sibling inline elements. CSS still collapses that into a single boundary
                // space, so the NEXT run must still know a space belongs before it (mirrors the
                // trailing-space bookkeeping below, which this skip would otherwise bypass).
                char lastRunCharSkip = run.Text.Length > 0 ? run.Text[run.Text.Length - 1] : '\0';
                prevRunTrailingSpace = lastRunCharSkip != NonBreakingSpace && char.IsWhiteSpace(lastRunCharSkip);
                continue;
            }

            // Measure the transformed text (uppercase is wider); the paint-time
            // transform is idempotent so word boxes may carry it too.
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

                // Apply the float-based left inset before measuring/placing, if this is a
                // fresh, not-yet-indented line (no-op when floats is null).
                inlineX = ApplyLeftInsetIfNeeded(Math.Max(inlineLineHeight, lhRun), inlineX, childY);

                // A boundary space also comes from the PREVIOUS run's trailing
                // whitespace ("đến <strong>bản</strong>": the space belongs to the
                // text run, not the strong run).
                bool notAtLineStart = floatsActive ? !atLineStart : inlineX > lineStart;
                bool needSpace = notAtLineStart && !joinNoSpace && (!firstWord || run.HasLeadingSpace || prevRunTrailingSpace);
                var wordText = needSpace ? " " + word : word;
                float wordWidth = TextMeasurer.MeasureWidth(wordText, run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                float rightLimit = RightLimit(Math.Max(inlineLineHeight, lhRun), childY);
                bool lastWord = pieces != null ? pieceIndex >= pieces.Count : OnlySpacesFrom(text, wPos);
                float leadInset = firstWord ? run.StartInset : 0f;
                float trailInset = lastWord ? run.EndInset : 0f;
                float leadMargin = firstWord ? run.StartMargin : 0f;

                if (!runNoWrap && notAtLineStart && inlineX + leadMargin + wordWidth + leadInset + trailInset > rightLimit)
                {
                    if (joinNoSpace && previousEndsAtSoftHyphen && previousPieceBox != null)
                        AppendVisibleHyphen(previousPieceBox, run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                    childY += inlineLineHeight;
                    inlineX = 0;
                    lineStart = 0;
                    inlineLineHeight = 0;
                    atLineStart = true;
                    inlineX = ApplyLeftInsetIfNeeded(lhRun, inlineX, childY);
                    wordText = word; // no space prefix after wrap
                    wordWidth = TextMeasurer.MeasureWidth(wordText, run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                    rightLimit = RightLimit(lhRun, childY);
                }

                // break-all / break-word: a word wider than the line splits into
                // character chunks that each fit (e.g. long URLs in captions). Uses the
                // flat containerWidth even when floats are active -- a narrow enough edge
                // case (long unbreakable text beside a float) that per-chunk float-aware
                // re-querying isn't worth the added complexity here.
                if (runBreakWord && wordWidth > containerWidth && wordText.Length > 1)
                {
                    int start = 0;
                    while (start < wordText.Length)
                    {
                        int len = 1;
                        float chunkWidth = TextMeasurer.MeasureWidth(wordText.Substring(start, 1),
                            run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                        while (start + len < wordText.Length)
                        {
                            float nextWidth = TextMeasurer.MeasureWidth(wordText.Substring(start, len + 1),
                                run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                            if (inlineX + nextWidth > containerWidth) break;
                            len++;
                            chunkWidth = nextWidth;
                        }

                        var chunkBox = new LayoutBox
                        {
                            Element = (!elementAssigned && wrapperElement != null) ? wrapperElement : null,
                            Style = run.Style,
                            InlineLineTop = childY,
                            X = box.X + box.PaddingLeft + inlineX,
                            // box.Y here is often still provisional (0) -- a block's final page
                            // position is applied by its parent after this recursion returns, so
                            // a negative shift can leave a momentarily-negative Y. That's fine: the
                            // parent's later shift brings it back to a valid position except in the
                            // (rare) case this content is genuinely the very first thing on page 1,
                            // which PdfRenderer's page-assignment guards against dropping instead.
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
