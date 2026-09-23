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
            if (!string.IsNullOrEmpty(data))
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

            // ::before pseudo-element injection for inline elements
            if (TryResolvePseudoContent(elem, "before", style, out var beforeStyle, out var beforeContent))
            {
                float bfs = ResolveFontSize(beforeStyle!.FontSize, fontSize);
                runs.Add(new InlineRun { Text = beforeContent!, Style = beforeStyle, Element = elem, FontSize = bfs, HasLeadingSpace = false });
            }

            foreach (var child in elem.ChildNodes)
                CollectInlineRuns(child, style, fontSize, resolver, runs);

            // ::after pseudo-element injection for inline elements
            if (TryResolvePseudoContent(elem, "after", style, out var afterStyle, out var afterContent))
            {
                float afs = ResolveFontSize(afterStyle!.FontSize, fontSize);
                runs.Add(new InlineRun { Text = afterContent!, Style = afterStyle, Element = null, FontSize = afs, HasLeadingSpace = false });
            }
        }
    }

    /// <summary>
    /// Resolve a ::before/::after pseudo-element for <paramref name="element"/>: its computed
    /// style and the text produced by its `content` property. Returns false when no cascade
    /// context is active, the pseudo-element has no matching rule, or `content` yields nothing.
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
        if (text == null) return false;

        pseudoStyle = resolved;
        content = text;
        return true;
    }

    /// <summary>Layout inline runs as word-level boxes with style-aware wrapping.</summary>
    private static void LayoutInlineRuns(List<InlineRun> runs, LayoutBox box, HtmlElement? wrapperElement,
        ref float inlineX, ref float childY, ref float inlineLineHeight, float containerWidth,
        ComputedStyle parentStyle, float parentFontSize, FloatContext? floats = null, float floatOriginY = 0f)
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
        bool atLineStart = inlineX <= 0f;
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
            if (!floatsActive || !atLineStart || currentInlineX > 0) return currentInlineX;
            float absY = floatOriginY + box.PaddingTop + currentChildY;
            float startX = floats!.GetContentStartX(absY, lineHeight, absContainerLeft);
            float inset = startX - absContainerLeft;
            return inset > 0 ? inset : currentInlineX;
        }

        for (int ri = 0; ri < runs.Count; ri++)
        {
            var run = runs[ri];

            if (run.Text == "\n")
            {
                float lh = TextMeasurer.GetLineHeight(run.FontSize, run.Style.Get("line-height"));
                if (!(floatsActive ? atLineStart : inlineX <= 0))
                {
                    childY += Math.Max(inlineLineHeight, lh);
                    inlineX = 0;
                    inlineLineHeight = 0;
                    atLineStart = true;
                }
                else
                {
                    childY += lh;
                }
                prevRunTrailingSpace = false;
                continue;
            }

            // Normalize whitespace: \n \r \t become spaces, runs of spaces
            // collapse, edges trim — single pass (the old Replace loop
            // reallocated the string once per collapsed pair).
            var text = NormalizeInlineWhitespace(run.Text);

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
            bool runBreakWord = runOverflowWrap == "break-word" || runOverflowWrap == "anywhere" ||
                                runWordBreak == "break-all" || runWordBreak == "break-word";

            // vertical-align: baseline (default) — every run sits on the parent line's
            // baseline, not the line top (ascent approximated at 0.8em). A smaller run
            // shifts down (positive); a larger run shifts up (negative) so its baseline
            // still meets the smaller surrounding text instead of hanging below it.
            float baselineShift = (parentFontSize - run.FontSize) * 0.8f;

            // Iterate words inline — avoids allocating a string[] upfront. Thai has no spaces
            // between words, so a Thai run is pre-split into syllable-boundary pieces that rejoin
            // without a space (the same heuristic the plain-text wrapper uses).
            var thaiPieces = EggPdf.Text.SpacelessLineBreaker.Contains(text) ? SplitSpacelessWords(text) : null;
            int thaiIndex = 0;
            int wPos = 0;
            bool firstWord = true;
            while (thaiPieces != null ? thaiIndex < thaiPieces.Count : wPos < text.Length)
            {
                string word;
                bool joinNoSpace = false;
                if (thaiPieces != null)
                {
                    (word, joinNoSpace) = thaiPieces[thaiIndex++];
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
                bool notAtLineStart = floatsActive ? !atLineStart : inlineX > 0;
                bool needSpace = notAtLineStart && !joinNoSpace && (!firstWord || run.HasLeadingSpace || prevRunTrailingSpace);
                var wordText = needSpace ? " " + word : word;
                float wordWidth = TextMeasurer.MeasureWidth(wordText, run.FontSize, fontFamily, fontWeight, fontStyle, runLetterSpacing);
                float rightLimit = RightLimit(Math.Max(inlineLineHeight, lhRun), childY);

                // Wrap to next line if doesn't fit
                if (notAtLineStart && inlineX + wordWidth > rightLimit)
                {
                    childY += inlineLineHeight;
                    inlineX = 0;
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
                                currentSpan = new InlineElementSpan(wrapperElement, chunkBox.X, chunkBox.Y, chunkBox.Width, chunkBox.Height);
                                currentSpanLineY = childY;
                            }
                            else
                            {
                                currentSpan.Union(chunkBox.X, chunkBox.Y, chunkBox.Width, chunkBox.Height);
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
                            inlineLineHeight = 0;
                            atLineStart = true;
                        }
                    }
                    firstWord = false;
                    continue;
                }

                var textBox = new LayoutBox
                {
                    Element = (!elementAssigned && wrapperElement != null) ? wrapperElement : null,
                    Style = run.Style,
                    X = box.X + box.PaddingLeft + inlineX,
                    // See the chunkBox comment above about box.Y being provisional here.
                    Y = box.Y + box.PaddingTop + childY + baselineShift,
                    Width = wordWidth,
                    Height = lhRun,
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
                        currentSpan = new InlineElementSpan(wrapperElement, textBox.X, textBox.Y, textBox.Width, textBox.Height);
                        currentSpanLineY = childY;
                    }
                    else
                    {
                        currentSpan.Union(textBox.X, textBox.Y, textBox.Width, textBox.Height);
                    }
                    textBox.InlineSpan = currentSpan;
                }

                box.Children.Add(textBox);
                inlineX += wordWidth;
                atLineStart = false;
                if (lhRun > inlineLineHeight)
                    inlineLineHeight = lhRun;
                firstWord = false;
            }

            // NBSP is rendered content, not a collapsible boundary space
            char lastRunChar = run.Text.Length > 0 ? run.Text[run.Text.Length - 1] : '\0';
            prevRunTrailingSpace = lastRunChar != NonBreakingSpace && char.IsWhiteSpace(lastRunChar);
        }
    }


    /// <summary>
    /// Split a run into words on spaces, then split Thai words at syllable-boundary break
    /// opportunities. Pieces after the first of a Thai word carry <c>noSpace</c> so they rejoin
    /// the previous piece without a space.
    /// </summary>
    private static string[] ExpandSpacelessWords(string[] words, out bool[] noSpace)
    {
        var pieces = new List<string>(words.Length + 8);
        var flags = new List<bool>(words.Length + 8);
        foreach (var word in words)
        {
            if (!EggPdf.Text.SpacelessLineBreaker.Contains(word)) { pieces.Add(word); flags.Add(false); continue; }
            var segments = EggPdf.Text.SpacelessLineBreaker.Split(word);
            for (int i = 0; i < segments.Count; i++) { pieces.Add(segments[i]); flags.Add(i > 0); }
        }
        noSpace = flags.ToArray();
        return pieces.ToArray();
    }

    /// <summary>As <see cref="ExpandSpacelessWords"/> but for one run's text: split on spaces first.</summary>
    private static List<(string word, bool noSpace)> SplitSpacelessWords(string text)
    {
        var pieces = new List<(string, bool)>();
        foreach (var word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!EggPdf.Text.SpacelessLineBreaker.Contains(word)) { pieces.Add((word, false)); continue; }
            var segments = EggPdf.Text.SpacelessLineBreaker.Split(word);
            for (int i = 0; i < segments.Count; i++) pieces.Add((segments[i], i > 0));
        }
        return pieces;
    }

}
