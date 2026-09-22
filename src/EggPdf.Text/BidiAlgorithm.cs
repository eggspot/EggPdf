using System;
using System.Collections.Generic;

namespace EggPdf.Text;

/// <summary>
/// The Unicode Bidirectional Algorithm (UAX #9): explicit embeddings, overrides and isolates
/// (X1-X10), weak-type resolution (W1-W7), paired-bracket handling (N0), neutral resolution
/// (N1-N2), implicit levels (I1-I2), line rules (L1) and reordering/mirroring (L2, L4) for a
/// single line. The paragraph direction is the caller's (CSS <c>direction</c>), never guessed
/// from the first strong character, matching browsers' <c>unicode-bidi: normal</c>.
/// </summary>
public static partial class BidiAlgorithm
{
    private const int MaxDepth = 125;

    /// <summary>
    /// Reorder a string for visual display. Returns the visually-ordered string (mirrored glyphs
    /// applied) and, for each visual position, the logical index it came from.
    /// </summary>
    public static (string visual, int[] logicalOrder) Reorder(string text, bool baseRTL = false)
    {
        if (string.IsNullOrEmpty(text))
            return ("", Array.Empty<int>());

        var levels = ResolveLevels(text, baseRTL);
        var order = VisualOrder(text, levels);

        var visual = new char[text.Length];
        for (int i = 0; i < order.Length; i++)
        {
            int logical = order[i];
            visual[i] = (levels[logical] & 1) == 1 ? Mirror(text[logical]) : text[logical];
        }
        return (new string(visual), order);
    }

    /// <summary>Whether a character is strongly right-to-left (Hebrew, Arabic, Syriac, Thaana, ...).</summary>
    public static bool IsRTL(char c)
    {
        var t = BidiClassifier.Classify(c);
        return t == BidiType.R || t == BidiType.AL;
    }

    /// <summary>Whether a string contains any strongly right-to-left character.</summary>
    public static bool ContainsRTL(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < text.Length; i++)
            if (IsRTL(text[i])) return true;
        return false;
    }

    /// <summary>
    /// Resolved embedding level of every UTF-16 code unit (rules X1-L1). Characters removed by rule
    /// X9 (explicit formatting and boundary neutrals) take the level of the character before them.
    /// </summary>
    public static int[] ResolveLevels(string text, bool baseRTL)
    {
        int n = text.Length;
        var levels = new int[n];
        if (n == 0) return levels;

        int paraLevel = baseRTL ? 1 : 0;
        var original = ClassifyAll(text);
        var types = (BidiType[])original.Clone();

        var matchingPdi = new int[n];
        var matchingInitiator = new int[n];
        MatchIsolates(original, matchingPdi, matchingInitiator);

        ResolveExplicit(text, original, types, levels, paraLevel, matchingPdi);

        var removed = new bool[n];
        for (int i = 0; i < n; i++) removed[i] = IsRemovedByX9(original[i]);

        var runOf = new int[n];
        var runs = BuildLevelRuns(levels, removed, runOf);
        var sequences = BuildIsolatingRunSequences(runs, original, matchingPdi, matchingInitiator, runOf);

        foreach (var seq in sequences)
            ResolveSequence(text, seq, original, types, levels, removed, paraLevel);

        // Removed characters adopt the level of the character before them (or the paragraph level)
        int prevLevel = paraLevel;
        for (int i = 0; i < n; i++)
        {
            if (removed[i]) levels[i] = prevLevel; else prevLevel = levels[i];
        }

        ApplyL1(original, levels, removed, paraLevel);
        return levels;
    }

    // ── Classification ───────────────────────────────────────────────────────

    private static BidiType[] ClassifyAll(string text)
    {
        var types = new BidiType[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                var t = BidiClassifier.ClassifySupplementary(text, i);
                types[i] = t;
                types[i + 1] = t;
                i++;
            }
            else
            {
                types[i] = BidiClassifier.Classify(c);
            }
        }
        return types;
    }

    private static bool IsIsolateInitiator(BidiType t) => t == BidiType.LRI || t == BidiType.RLI || t == BidiType.FSI;

    private static bool IsRemovedByX9(BidiType t)
        => t == BidiType.LRE || t == BidiType.RLE || t == BidiType.LRO || t == BidiType.RLO || t == BidiType.PDF || t == BidiType.BN;

    /// <summary>Neutral or isolate formatting character (N1/N2's NI set).</summary>
    private static bool IsNeutralOrIsolate(BidiType t)
        => t == BidiType.B || t == BidiType.S || t == BidiType.WS || t == BidiType.ON || IsIsolateInitiator(t) || t == BidiType.PDI;

    // ── BD9: matching isolate initiators and PDIs ────────────────────────────

    private static void MatchIsolates(BidiType[] original, int[] matchingPdi, int[] matchingInitiator)
    {
        int n = original.Length;
        for (int i = 0; i < n; i++) { matchingPdi[i] = n; matchingInitiator[i] = -1; }

        var stack = new Stack<int>();
        for (int i = 0; i < n; i++)
        {
            if (IsIsolateInitiator(original[i])) stack.Push(i);
            else if (original[i] == BidiType.PDI && stack.Count > 0)
            {
                int start = stack.Pop();
                matchingPdi[start] = i;
                matchingInitiator[i] = start;
            }
        }
    }

    // ── X1-X8: explicit levels ───────────────────────────────────────────────

    private struct Status
    {
        public int Level;
        public BidiType Override; // ON = neutral, L or R = directional override
        public bool Isolate;
    }

    private static void ResolveExplicit(string text, BidiType[] original, BidiType[] types, int[] levels,
        int paraLevel, int[] matchingPdi)
    {
        int n = original.Length;
        var stack = new List<Status>(MaxDepth + 2) { new Status { Level = paraLevel, Override = BidiType.ON, Isolate = false } };
        int overflowIsolates = 0, overflowEmbeddings = 0, validIsolates = 0;

        for (int i = 0; i < n; i++)
        {
            var t = original[i];
            var top = stack[stack.Count - 1];

            switch (t)
            {
                case BidiType.RLE: case BidiType.LRE: case BidiType.RLO: case BidiType.LRO:
                {
                    levels[i] = top.Level;
                    bool rtl = t == BidiType.RLE || t == BidiType.RLO;
                    int next = NextLevel(top.Level, rtl);
                    if (next <= MaxDepth && overflowIsolates == 0 && overflowEmbeddings == 0)
                    {
                        var ov = t == BidiType.RLO ? BidiType.R : t == BidiType.LRO ? BidiType.L : BidiType.ON;
                        stack.Add(new Status { Level = next, Override = ov, Isolate = false });
                    }
                    else if (overflowIsolates == 0)
                    {
                        overflowEmbeddings++;
                    }
                    break;
                }
                case BidiType.RLI: case BidiType.LRI: case BidiType.FSI:
                {
                    levels[i] = top.Level;
                    if (top.Override != BidiType.ON) types[i] = top.Override;

                    bool rtl = t == BidiType.RLI || (t == BidiType.FSI && FirstStrongIsRtl(original, i + 1, matchingPdi[i]));
                    int next = NextLevel(top.Level, rtl);
                    if (next <= MaxDepth && overflowIsolates == 0 && overflowEmbeddings == 0)
                    {
                        validIsolates++;
                        stack.Add(new Status { Level = next, Override = BidiType.ON, Isolate = true });
                    }
                    else
                    {
                        overflowIsolates++;
                    }
                    break;
                }
                case BidiType.PDI:
                {
                    if (overflowIsolates > 0) overflowIsolates--;
                    else if (validIsolates > 0)
                    {
                        overflowEmbeddings = 0;
                        while (!stack[stack.Count - 1].Isolate) stack.RemoveAt(stack.Count - 1);
                        stack.RemoveAt(stack.Count - 1);
                        validIsolates--;
                    }
                    top = stack[stack.Count - 1];
                    levels[i] = top.Level;
                    if (top.Override != BidiType.ON) types[i] = top.Override;
                    break;
                }
                case BidiType.PDF:
                {
                    levels[i] = top.Level;
                    if (overflowIsolates > 0) { }
                    else if (overflowEmbeddings > 0) overflowEmbeddings--;
                    else if (!top.Isolate && stack.Count >= 2) stack.RemoveAt(stack.Count - 1);
                    break;
                }
                case BidiType.B:
                    levels[i] = paraLevel;
                    break;
                case BidiType.BN:
                    levels[i] = top.Level;
                    break;
                default:
                    levels[i] = top.Level;
                    if (top.Override != BidiType.ON) types[i] = top.Override;
                    break;
            }
        }
    }

    private static int NextLevel(int level, bool rtl)
        => rtl ? (level % 2 == 0 ? level + 1 : level + 2) : (level % 2 == 0 ? level + 2 : level + 1);

    /// <summary>P2/P3 over an isolate's content: the direction of the first strong character not inside a nested isolate.</summary>
    private static bool FirstStrongIsRtl(BidiType[] original, int start, int end)
    {
        int depth = 0;
        for (int i = start; i < end && i < original.Length; i++)
        {
            var t = original[i];
            if (IsIsolateInitiator(t)) depth++;
            else if (t == BidiType.PDI) { if (depth > 0) depth--; }
            else if (depth == 0)
            {
                if (t == BidiType.L) return false;
                if (t == BidiType.R || t == BidiType.AL) return true;
            }
        }
        return false;
    }

    // ── X10: level runs and isolating run sequences ──────────────────────────

    private static List<List<int>> BuildLevelRuns(int[] levels, bool[] removed, int[] runOf)
    {
        var runs = new List<List<int>>();
        List<int>? current = null;
        int currentLevel = -1;
        for (int i = 0; i < levels.Length; i++)
        {
            if (removed[i]) { runOf[i] = -1; continue; }
            if (current == null || levels[i] != currentLevel)
            {
                current = new List<int>();
                runs.Add(current);
                currentLevel = levels[i];
            }
            current.Add(i);
            runOf[i] = runs.Count - 1;
        }
        return runs;
    }

    private static List<List<int>> BuildIsolatingRunSequences(List<List<int>> runs, BidiType[] original,
        int[] matchingPdi, int[] matchingInitiator, int[] runOf)
    {
        var sequences = new List<List<int>>();
        int n = original.Length;
        foreach (var run in runs)
        {
            int first = run[0];
            if (original[first] == BidiType.PDI && matchingInitiator[first] != -1) continue; // continues an earlier sequence

            var sequence = new List<int>();
            var current = run;
            while (true)
            {
                sequence.AddRange(current);
                int last = current[current.Count - 1];
                if (IsIsolateInitiator(original[last]) && matchingPdi[last] != n && runOf[matchingPdi[last]] >= 0)
                    current = runs[runOf[matchingPdi[last]]];
                else
                    break;
            }
            sequences.Add(sequence);
        }
        return sequences;
    }

    // ── W, N, I rules over one isolating run sequence ────────────────────────

    private static void ResolveSequence(string text, List<int> seq, BidiType[] original, BidiType[] types,
        int[] levels, bool[] removed, int paraLevel)
    {
        int count = seq.Count;
        int level = levels[seq[0]];

        // sos / eos from the levels of the neighbouring non-removed characters
        int before = PreviousKept(removed, seq[0]);
        int after = NextKept(removed, seq[count - 1]);
        int prevLevel = before >= 0 ? levels[before] : paraLevel;
        int nextLevel = after >= 0 && !IsIsolateInitiator(original[seq[count - 1]]) ? levels[after] : paraLevel;
        var sos = Math.Max(level, prevLevel) % 2 == 1 ? BidiType.R : BidiType.L;
        var eos = Math.Max(level, nextLevel) % 2 == 1 ? BidiType.R : BidiType.L;

        var t = new BidiType[count];
        for (int i = 0; i < count; i++) t[i] = types[seq[i]];

        ApplyWeakRules(t, sos);
        ApplyBracketRules(text, seq, original, t, level, sos);
        ApplyNeutralRules(t, sos, eos, level);

        // I1 / I2
        for (int i = 0; i < count; i++)
        {
            int idx = seq[i];
            if (level % 2 == 0)
            {
                if (t[i] == BidiType.R) levels[idx] = level + 1;
                else if (t[i] == BidiType.AN || t[i] == BidiType.EN) levels[idx] = level + 2;
                else levels[idx] = level;
            }
            else
            {
                levels[idx] = t[i] == BidiType.L || t[i] == BidiType.EN || t[i] == BidiType.AN ? level + 1 : level;
            }
        }
    }

    private static int PreviousKept(bool[] removed, int index)
    {
        for (int i = index - 1; i >= 0; i--) if (!removed[i]) return i;
        return -1;
    }

    private static int NextKept(bool[] removed, int index)
    {
        for (int i = index + 1; i < removed.Length; i++) if (!removed[i]) return i;
        return -1;
    }

    private static void ApplyWeakRules(BidiType[] t, BidiType sos)
    {
        int n = t.Length;

        // W1: NSM takes the previous type (ON after an isolate initiator or PDI)
        for (int i = 0; i < n; i++)
        {
            if (t[i] != BidiType.NSM) continue;
            if (i == 0) t[i] = sos;
            else t[i] = IsIsolateInitiator(t[i - 1]) || t[i - 1] == BidiType.PDI ? BidiType.ON : t[i - 1];
        }

        // W2: EN after AL becomes AN
        var lastStrong = sos;
        for (int i = 0; i < n; i++)
        {
            if (t[i] == BidiType.L || t[i] == BidiType.R || t[i] == BidiType.AL) lastStrong = t[i];
            else if (t[i] == BidiType.EN && lastStrong == BidiType.AL) t[i] = BidiType.AN;
        }

        // W3: AL becomes R
        for (int i = 0; i < n; i++) if (t[i] == BidiType.AL) t[i] = BidiType.R;

        // W4: a single separator between two numbers of the same kind joins them
        for (int i = 1; i < n - 1; i++)
        {
            if (t[i] == BidiType.ES && t[i - 1] == BidiType.EN && t[i + 1] == BidiType.EN) t[i] = BidiType.EN;
            else if (t[i] == BidiType.CS && t[i - 1] == BidiType.EN && t[i + 1] == BidiType.EN) t[i] = BidiType.EN;
            else if (t[i] == BidiType.CS && t[i - 1] == BidiType.AN && t[i + 1] == BidiType.AN) t[i] = BidiType.AN;
        }

        // W5: a run of ET next to an EN becomes EN
        for (int i = 0; i < n; i++)
        {
            if (t[i] != BidiType.ET) continue;
            int end = i;
            while (end < n && t[end] == BidiType.ET) end++;
            bool adjacentEn = (i > 0 && t[i - 1] == BidiType.EN) || (end < n && t[end] == BidiType.EN);
            if (adjacentEn) for (int k = i; k < end; k++) t[k] = BidiType.EN;
            i = end - 1;
        }

        // W6: remaining separators and terminators are neutral
        for (int i = 0; i < n; i++)
            if (t[i] == BidiType.ES || t[i] == BidiType.ET || t[i] == BidiType.CS) t[i] = BidiType.ON;

        // W7: EN after L becomes L
        lastStrong = sos;
        for (int i = 0; i < n; i++)
        {
            if (t[i] == BidiType.L || t[i] == BidiType.R) lastStrong = t[i];
            else if (t[i] == BidiType.EN && lastStrong == BidiType.L) t[i] = BidiType.L;
        }
    }

    private static void ApplyNeutralRules(BidiType[] t, BidiType sos, BidiType eos, int level)
    {
        int n = t.Length;
        var embedding = level % 2 == 1 ? BidiType.R : BidiType.L;

        for (int i = 0; i < n; i++)
        {
            if (!IsNeutralOrIsolate(t[i])) continue;
            int end = i;
            while (end < n && IsNeutralOrIsolate(t[end])) end++;

            var leading = i == 0 ? sos : StrongOf(t[i - 1]);
            var trailing = end == n ? eos : StrongOf(t[end]);
            var resolved = leading == trailing ? leading : embedding; // N1, else N2
            for (int k = i; k < end; k++) t[k] = resolved;
            i = end - 1;
        }
    }

    /// <summary>Numbers count as R for neutral resolution (N1).</summary>
    private static BidiType StrongOf(BidiType t) => t == BidiType.L ? BidiType.L : BidiType.R;

    // ── L1 and L2 ────────────────────────────────────────────────────────────

    private static void ApplyL1(BidiType[] original, int[] levels, bool[] removed, int paraLevel)
    {
        bool trailing = true; // the end of the line counts as a separator
        for (int i = original.Length - 1; i >= 0; i--)
        {
            var t = original[i];
            if (t == BidiType.S || t == BidiType.B)
            {
                levels[i] = paraLevel;
                trailing = true;
            }
            else if (t == BidiType.WS || IsIsolateInitiator(t) || t == BidiType.PDI || removed[i])
            {
                if (trailing) levels[i] = paraLevel;
            }
            else
            {
                trailing = false;
            }
        }
    }

    private static int[] VisualOrder(string text, int[] levels)
    {
        int n = levels.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;

        int highest = 0, lowestOdd = int.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (levels[i] > highest) highest = levels[i];
            if ((levels[i] & 1) == 1 && levels[i] < lowestOdd) lowestOdd = levels[i];
        }

        for (int level = highest; level >= lowestOdd && level >= 1; level--)
        {
            int start = -1;
            for (int i = 0; i <= n; i++)
            {
                bool inRun = i < n && levels[order[i]] >= level;
                if (inRun) { if (start < 0) start = i; }
                else if (start >= 0)
                {
                    Array.Reverse(order, start, i - start);
                    start = -1;
                }
            }
        }

        // Reversal flips surrogate pairs; put each pair back in high-then-low order
        for (int i = 0; i + 1 < n; i++)
        {
            if (char.IsLowSurrogate(text[order[i]]) && char.IsHighSurrogate(text[order[i + 1]]) && order[i + 1] == order[i] - 1)
            {
                int tmp = order[i]; order[i] = order[i + 1]; order[i + 1] = tmp;
                i++;
            }
        }
        return order;
    }
}
