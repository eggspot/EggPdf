using System;
using System.Collections.Generic;

namespace EggPdf.Text;

/// <summary>
/// Simplified Unicode Bidirectional Algorithm (UAX #9).
/// Determines the visual ordering of characters in mixed LTR/RTL text.
/// Supports Arabic, Hebrew, and other RTL scripts alongside LTR text.
/// </summary>
public static class BidiAlgorithm
{
    /// <summary>Bidi character types (simplified subset).</summary>
    public enum BidiType { L, R, AL, EN, AN, ES, ET, CS, WS, ON, NSM, BN }

    /// <summary>
    /// Reorder a string for visual display.
    /// Returns the visually-ordered string and an array mapping visual position to logical position.
    /// </summary>
    public static (string visual, int[] logicalOrder) Reorder(string text, bool baseRTL = false)
    {
        if (string.IsNullOrEmpty(text))
            return ("", Array.Empty<int>());

        int len = text.Length;
        var types = new BidiType[len];
        var levels = new int[len];

        // Step 1: Classify characters
        for (int i = 0; i < len; i++)
            types[i] = ClassifyChar(text[i]);

        // Step 2: Determine paragraph embedding level
        int paragraphLevel = baseRTL ? 1 : 0;
        // Auto-detect: find first strong character
        for (int i = 0; i < len; i++)
        {
            if (types[i] == BidiType.L) { paragraphLevel = 0; break; }
            if (types[i] == BidiType.R || types[i] == BidiType.AL) { paragraphLevel = 1; break; }
        }

        // Step 3: Assign embedding levels (simplified: no explicit embeddings)
        int currentLevel = paragraphLevel;
        for (int i = 0; i < len; i++)
        {
            switch (types[i])
            {
                case BidiType.L:
                    levels[i] = currentLevel % 2 == 0 ? currentLevel : currentLevel + 1;
                    break;
                case BidiType.R:
                case BidiType.AL:
                    levels[i] = currentLevel % 2 == 1 ? currentLevel : currentLevel + 1;
                    break;
                case BidiType.EN:
                case BidiType.AN:
                    levels[i] = currentLevel % 2 == 0 ? currentLevel : currentLevel + 1;
                    // European/Arabic numbers are LTR in display
                    if (currentLevel % 2 == 1) levels[i] = currentLevel + 1;
                    break;
                default:
                    levels[i] = currentLevel;
                    break;
            }
        }

        // Step 4: Reverse runs at each level (visual reordering)
        int maxLevel = 0;
        for (int i = 0; i < len; i++)
            if (levels[i] > maxLevel) maxLevel = levels[i];

        // Create logical order array
        var order = new int[len];
        for (int i = 0; i < len; i++) order[i] = i;

        // Reverse subsequences at each level from maxLevel down to 1
        for (int level = maxLevel; level >= 1; level--)
        {
            int runStart = -1;
            for (int i = 0; i <= len; i++)
            {
                if (i < len && levels[i] >= level)
                {
                    if (runStart < 0) runStart = i;
                }
                else
                {
                    if (runStart >= 0)
                    {
                        // Reverse run [runStart, i-1]
                        int lo = runStart, hi = i - 1;
                        while (lo < hi)
                        {
                            int tmp = order[lo]; order[lo] = order[hi]; order[hi] = tmp;
                            lo++; hi--;
                        }
                        runStart = -1;
                    }
                }
            }
        }

        // Build visual string
        var visual = new char[len];
        for (int i = 0; i < len; i++)
            visual[i] = MirrorIfNeeded(text[order[i]], levels[order[i]]);

        return (new string(visual), order);
    }

    /// <summary>Check if a character is RTL.</summary>
    public static bool IsRTL(char c)
    {
        var t = ClassifyChar(c);
        return t == BidiType.R || t == BidiType.AL;
    }

    /// <summary>Check if a string contains any RTL characters.</summary>
    public static bool ContainsRTL(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < text.Length; i++)
            if (IsRTL(text[i])) return true;
        return false;
    }

    private static BidiType ClassifyChar(char c)
    {
        // Arabic block: U+0600-U+06FF, U+0750-U+077F, U+08A0-U+08FF
        if ((c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F) ||
            (c >= 0x08A0 && c <= 0x08FF) || (c >= 0xFB50 && c <= 0xFDFF) ||
            (c >= 0xFE70 && c <= 0xFEFF))
            return BidiType.AL;

        // Hebrew block: U+0590-U+05FF, U+FB1D-U+FB4F
        if ((c >= 0x0590 && c <= 0x05FF) || (c >= 0xFB1D && c <= 0xFB4F))
            return BidiType.R;

        // RTL: Thaana, Syriac, NKo, etc.
        if (c >= 0x0700 && c <= 0x074F) return BidiType.AL; // Syriac
        if (c >= 0x0780 && c <= 0x07BF) return BidiType.AL; // Thaana
        if (c >= 0x07C0 && c <= 0x07FF) return BidiType.R;  // NKo

        // Numbers
        if (c >= '0' && c <= '9') return BidiType.EN;
        if (c >= 0x0660 && c <= 0x0669) return BidiType.AN; // Arabic-Indic digits
        if (c >= 0x06F0 && c <= 0x06F9) return BidiType.EN; // Extended Arabic-Indic digits

        // Whitespace
        if (c == ' ' || c == '\t' || c == '\n' || c == '\r') return BidiType.WS;

        // Common punctuation
        if (c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}')
            return BidiType.ON;

        // Number separators
        if (c == '.' || c == ',' || c == ':') return BidiType.CS;
        if (c == '+' || c == '-') return BidiType.ES;
        if (c == '%' || c == '$' || c == '#') return BidiType.ET;

        // Latin, CJK, etc. = LTR
        return BidiType.L;
    }

    private static char MirrorIfNeeded(char c, int level)
    {
        // Mirror brackets in RTL context
        if (level % 2 == 0) return c;

        switch (c)
        {
            case '(': return ')';
            case ')': return '(';
            case '[': return ']';
            case ']': return '[';
            case '{': return '}';
            case '}': return '{';
            case '<': return '>';
            case '>': return '<';
            default: return c;
        }
    }
}
