using System;
using System.Collections.Generic;

namespace EggPdf.Text.Hyphenation;

/// <summary>
/// Implements the Liang/Knuth hyphenation algorithm (same as TeX).
/// Uses pattern-based lookup to find valid hyphenation points in words.
/// Odd numbers at a position indicate a valid break point.
/// </summary>
public class Hyphenator
{
    private readonly Dictionary<string, byte[]> _patterns = new();
    private readonly HashSet<string> _exceptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int[]> _exceptionPoints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Minimum characters before first hyphen (default: 2).</summary>
    public int LeftMin { get; set; } = 2;

    /// <summary>Minimum characters after last hyphen (default: 3).</summary>
    public int RightMin { get; set; } = 3;

    /// <summary>
    /// Add a hyphenation pattern (e.g., ".hy1p", "he2n").
    /// Digits indicate break priority at that position. Odd = break, even = no break.
    /// </summary>
    public void AddPattern(string pattern)
    {
        // Extract the text (letters only) and the numeric values at each position
        var text = new System.Text.StringBuilder();
        var values = new List<byte>();
        values.Add(0); // value before first letter

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c >= '0' && c <= '9')
            {
                // Replace the last added value with this digit
                if (values.Count > 0)
                    values[values.Count - 1] = (byte)(c - '0');
                else
                    values.Add((byte)(c - '0'));
            }
            else
            {
                text.Append(char.ToLowerInvariant(c));
                values.Add(0); // default value after this letter
            }
        }

        _patterns[text.ToString()] = values.ToArray();
    }

    /// <summary>
    /// Add a hyphenation exception (pre-defined hyphenation).
    /// Format: "hy-phen-ation" where hyphens mark break points.
    /// </summary>
    public void AddException(string exception)
    {
        var clean = exception.Replace("-", "");
        _exceptions.Add(clean);

        var points = new int[clean.Length + 1];
        int pos = 0;
        for (int i = 0; i < exception.Length; i++)
        {
            if (exception[i] == '-')
                points[pos] = 1;
            else
                pos++;
        }
        _exceptionPoints[clean] = points;
    }

    /// <summary>
    /// Find hyphenation points in a word.
    /// Returns array of indices where hyphens can be inserted (0-based, between characters).
    /// Example: "hyphenation" -> [2, 5, 8] meaning "hy-phen-ation" or "hyphen-ation".
    /// </summary>
    public int[] Hyphenate(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length < LeftMin + RightMin)
            return Array.Empty<int>();

        var lower = word.ToLowerInvariant();

        // Check exceptions first
        if (_exceptionPoints.TryGetValue(lower, out var exPoints))
        {
            var result = new List<int>();
            for (int i = LeftMin; i <= lower.Length - RightMin; i++)
            {
                if (exPoints[i] % 2 == 1)
                    result.Add(i);
            }
            return result.ToArray();
        }

        // Apply patterns using Liang/Knuth algorithm
        // Surround word with boundary markers
        string work = "." + lower + ".";
        var levels = new int[work.Length + 1];

        // Try all substrings and apply matching patterns
        for (int i = 0; i < work.Length; i++)
        {
            for (int len = 1; len <= work.Length - i; len++)
            {
                string sub = work.Substring(i, len);
                if (_patterns.TryGetValue(sub, out var values))
                {
                    // Apply max of pattern values at each position
                    for (int k = 0; k < values.Length; k++)
                    {
                        int pos = i + k;
                        if (pos < levels.Length && values[k] > levels[pos])
                            levels[pos] = values[k];
                    }
                }
            }
        }

        // Extract break points (odd values indicate break)
        // Offset by 1 because of the leading "." boundary marker
        var breaks = new List<int>();
        for (int i = LeftMin; i <= lower.Length - RightMin; i++)
        {
            // levels[i+1] corresponds to the position between character i-1 and i in the original word
            if (levels[i + 1] % 2 == 1)
                breaks.Add(i);
        }

        return breaks.ToArray();
    }

    /// <summary>
    /// Get an English hyphenator with built-in patterns.
    /// </summary>
    public static Hyphenator CreateEnglish()
    {
        var h = new Hyphenator();
        LoadEnglishPatterns(h);
        return h;
    }

    /// <summary>Load a curated set of English hyphenation patterns (subset of TeX hyph-en).</summary>
    private static void LoadEnglishPatterns(Hyphenator h)
    {
        // Core English hyphenation patterns (curated subset covering common words)
        // Full TeX English patterns have ~4,500 entries; this is a practical subset
        var patterns = new[]
        {
            // Prefix patterns
            ".ab1s", ".ac1q", ".ad2", ".af1t", ".al3t", ".am1", ".an3",
            ".ap1", ".ar1", ".as1", ".at1", ".au1",
            ".be1", ".bi2", ".bu2",
            ".ca2", ".ci2", ".co2", ".com1", ".con1", ".cu2",
            ".de1", ".di2s", ".dis1", ".do2", ".du2",
            ".en1", ".ex1",
            ".fi2", ".fo2", ".fu2",
            ".ge2", ".hy1p",
            ".in1", ".im1",
            ".mi2", ".mis1",
            ".no2", ".non1",
            ".or2", ".out1", ".over1",
            ".pre1", ".pro1",
            ".re1",
            ".semi1", ".sub1", ".su2", ".super1",
            ".un1", ".under1",

            // Common interior patterns
            "a1b", "a1c", "a1d", "a1g", "a1l", "a1m", "a1n", "a1p", "a1r", "a1t",
            "ab2l", "ac1u", "ad3e", "ag1", "al1", "am1", "an2d", "an2t",
            "ap2", "ar1", "as1t", "at2", "au1",
            "b2l", "b2r",
            "c2h", "c2k", "c2l", "c2r", "ck1",
            "d2r",
            "e1b", "e1c", "e1d", "e1f", "e1g", "e1l", "e1m", "e1n", "e1p", "e1r", "e1s", "e1t", "e1v",
            "en1t", "er1", "es1",
            "f2l", "f2r",
            "g2l", "g2r",
            "i1a", "i1b", "i1c", "i1d", "i1f", "i1g", "i1l", "i1m", "i1n", "i1p", "i1s", "i1t", "i1v", "i1z",
            "id1", "il1", "im1", "in1", "ir1", "is1", "it1",
            "k2",
            "l2", "l1i",
            "m2", "m1i", "m1e",
            "n2", "n1i", "n1o",
            "o1b", "o1c", "o1d", "o1f", "o1g", "o1l", "o1m", "o1n", "o1p", "o1r", "o1s", "o1t", "o1v",
            "p2h", "p2l", "p2r",
            "qu2",
            "r2", "r1i",
            "s2c", "s2h", "s2k", "s2l", "s2m", "s2n", "s2p", "s2t", "s2w",
            "sh2", "st2",
            "t2h", "t2r", "t2w",
            "th2",
            "u1a", "u1b", "u1c", "u1d", "u1f", "u1g", "u1l", "u1m", "u1n", "u1p", "u1r", "u1s", "u1t",
            "v2",
            "w2",
            "x2",
            "y1", "y2l",
            "z2",

            // Suffix patterns
            "1tion", "1sion", "1ment", "1ness", "1ly.", "1ful", "1less", "1able", "1ible",
            "1ing.", "1tion.", "1sion.", "1ment.", "1ness.", "1ful.", "1less.",
            "2ble.", "2ting.", "2ding.", "2ning.", "2ring.", "2ling.",
            "a1tion", "e1ment", "i1ty.", "al1ly.",

            // Common word patterns
            "1phen", "phe2n", "hen3a", "1na1t",
            "com1pu", "pu1ter",
            "1de1vel", "vel1op",
            "1pro1gram",
            "1man1age",
        };

        foreach (var p in patterns)
            h.AddPattern(p);

        // Common exceptions
        h.AddException("as-so-ci-ate");
        h.AddException("as-so-ci-ates");
        h.AddException("project");
        h.AddException("projects");
    }
}
