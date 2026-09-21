using System.Collections.Generic;

namespace EggPdf.Text;

public static partial class BidiAlgorithm
{
    private const int MaxBracketStack = 63;

    /// <summary>
    /// Paired brackets (Bidi_Paired_Bracket_Type) as code points: opening, closing, opening, closing, ...
    /// Numeric on purpose -- these glyphs look alike, and 2329/232A are canonically equivalent to 3008/3009.
    /// </summary>
    private static readonly char[] BracketPairs =
    {
        (char)0x28, (char)0x29, (char)0x5B, (char)0x5D, (char)0x7B, (char)0x7D,
        (char)0x0F3A, (char)0x0F3B, (char)0x0F3C, (char)0x0F3D, (char)0x169B, (char)0x169C,
        (char)0x2045, (char)0x2046, (char)0x207D, (char)0x207E, (char)0x208D, (char)0x208E,
        (char)0x2308, (char)0x2309, (char)0x230A, (char)0x230B,
        (char)0x2768, (char)0x2769, (char)0x276A, (char)0x276B, (char)0x276C, (char)0x276D,
        (char)0x276E, (char)0x276F, (char)0x2770, (char)0x2771, (char)0x2772, (char)0x2773, (char)0x2774, (char)0x2775,
        (char)0x27C5, (char)0x27C6, (char)0x27E6, (char)0x27E7, (char)0x27E8, (char)0x27E9,
        (char)0x27EA, (char)0x27EB, (char)0x27EC, (char)0x27ED, (char)0x27EE, (char)0x27EF,
        (char)0x2983, (char)0x2984, (char)0x2985, (char)0x2986, (char)0x2987, (char)0x2988,
        (char)0x2989, (char)0x298A, (char)0x298B, (char)0x298C, (char)0x298D, (char)0x298E,
        (char)0x298F, (char)0x2990, (char)0x2991, (char)0x2992, (char)0x2993, (char)0x2994,
        (char)0x2995, (char)0x2996, (char)0x2997, (char)0x2998,
        (char)0x29D8, (char)0x29D9, (char)0x29DA, (char)0x29DB, (char)0x29FC, (char)0x29FD,
        (char)0x2E22, (char)0x2E23, (char)0x2E24, (char)0x2E25, (char)0x2E26, (char)0x2E27, (char)0x2E28, (char)0x2E29,
        (char)0x3008, (char)0x3009, (char)0x300A, (char)0x300B, (char)0x300C, (char)0x300D, (char)0x300E, (char)0x300F,
        (char)0x3010, (char)0x3011, (char)0x3014, (char)0x3015, (char)0x3016, (char)0x3017,
        (char)0x3018, (char)0x3019, (char)0x301A, (char)0x301B,
        (char)0xFE59, (char)0xFE5A, (char)0xFE5B, (char)0xFE5C, (char)0xFE5D, (char)0xFE5E,
        (char)0xFF08, (char)0xFF09, (char)0xFF3B, (char)0xFF3D, (char)0xFF5B, (char)0xFF5D,
        (char)0xFF5F, (char)0xFF60, (char)0xFF62, (char)0xFF63,
    };

    /// <summary>Characters mirrored at odd levels that are not paired brackets: angle brackets, guillemets, comparisons, set relations.</summary>
    private static readonly char[] MirrorOnlyPairs =
    {
        (char)0x3C, (char)0x3E, (char)0xAB, (char)0xBB, (char)0x2039, (char)0x203A,
        (char)0x2264, (char)0x2265, (char)0x226A, (char)0x226B, (char)0x2208, (char)0x220B,
        (char)0x2282, (char)0x2283, (char)0x2286, (char)0x2287,
    };

    private const char AngleOpenAlt = (char)0x2329, AngleCloseAlt = (char)0x232A;
    private const char AngleOpen = (char)0x3008, AngleClose = (char)0x3009;

    private static char Canonical(char c) => c == AngleOpenAlt ? AngleOpen : c == AngleCloseAlt ? AngleClose : c;

    /// <summary>The bracket paired with <paramref name="c"/> and whether <paramref name="c"/> opens the pair.</summary>
    private static bool TryGetBracket(char c, out char partner, out bool opening)
    {
        c = Canonical(c);
        int index = System.Array.IndexOf(BracketPairs, c);
        if (index < 0) { partner = c; opening = false; return false; }
        opening = index % 2 == 0;
        partner = opening ? BracketPairs[index + 1] : BracketPairs[index - 1];
        return true;
    }

    /// <summary>The glyph-mirrored counterpart of a character at an odd level (L4), or the character itself.</summary>
    private static char Mirror(char c)
    {
        if (c == AngleOpenAlt) return AngleCloseAlt;
        if (c == AngleCloseAlt) return AngleOpenAlt;

        int index = System.Array.IndexOf(BracketPairs, c);
        if (index >= 0) return index % 2 == 0 ? BracketPairs[index + 1] : BracketPairs[index - 1];

        index = System.Array.IndexOf(MirrorOnlyPairs, c);
        if (index >= 0) return index % 2 == 0 ? MirrorOnlyPairs[index + 1] : MirrorOnlyPairs[index - 1];
        return c;
    }

    /// <summary>The strong direction N0 sees for a resolved type (numbers count as R), or ON when neutral.</summary>
    private static BidiType StrongForBrackets(BidiType t)
    {
        if (t == BidiType.L) return BidiType.L;
        if (t == BidiType.R || t == BidiType.EN || t == BidiType.AN) return BidiType.R;
        return BidiType.ON;
    }

    /// <summary>
    /// N0: resolve paired brackets (BD14-BD16) as a unit. A pair takes the embedding direction when
    /// it encloses any strong text of that direction; if it only encloses the opposite direction it
    /// takes that direction when the context before it agrees, else the embedding direction.
    /// </summary>
    private static void ApplyBracketRules(string text, List<int> seq, BidiType[] original, BidiType[] t, int level, BidiType sos)
    {
        int count = seq.Count;

        // BD16: pair up brackets with a bounded stack
        var pairs = new List<(int open, int close)>();
        var stack = new List<(char closer, int pos)>();
        for (int i = 0; i < count; i++)
        {
            if (t[i] != BidiType.ON) continue;
            if (!TryGetBracket(text[seq[i]], out char partner, out bool opening)) continue;

            if (opening)
            {
                if (stack.Count == MaxBracketStack) break;
                stack.Add((partner, i));
            }
            else
            {
                char self = Canonical(text[seq[i]]);
                for (int s = stack.Count - 1; s >= 0; s--)
                {
                    if (stack[s].closer != self) continue;
                    pairs.Add((stack[s].pos, i));
                    stack.RemoveRange(s, stack.Count - s);
                    break;
                }
            }
        }
        if (pairs.Count == 0) return;
        pairs.Sort((a, b) => a.open.CompareTo(b.open));

        var embedding = level % 2 == 1 ? BidiType.R : BidiType.L;
        var opposite = embedding == BidiType.L ? BidiType.R : BidiType.L;

        foreach (var (open, close) in pairs)
        {
            bool foundEmbedding = false, foundOpposite = false;
            for (int k = open + 1; k < close && !foundEmbedding; k++)
            {
                var strong = StrongForBrackets(t[k]);
                if (strong == embedding) foundEmbedding = true;
                else if (strong != BidiType.ON) foundOpposite = true;
            }

            BidiType resolved;
            if (foundEmbedding) resolved = embedding;
            else if (foundOpposite)
            {
                var context = sos;
                for (int k = open - 1; k >= 0; k--)
                {
                    var strong = StrongForBrackets(t[k]);
                    if (strong != BidiType.ON) { context = strong; break; }
                }
                resolved = context == opposite ? opposite : embedding;
            }
            else continue; // nothing strong inside: leave the brackets to N1/N2

            SetBracket(t, original, seq, open, resolved);
            SetBracket(t, original, seq, close, resolved);
        }
    }

    /// <summary>Set a bracket's type, and that of the combining marks (originally NSM) that follow it.</summary>
    private static void SetBracket(BidiType[] t, BidiType[] original, List<int> seq, int position, BidiType resolved)
    {
        t[position] = resolved;
        for (int k = position + 1; k < seq.Count && original[seq[k]] == BidiType.NSM; k++)
            t[k] = resolved;
    }
}
