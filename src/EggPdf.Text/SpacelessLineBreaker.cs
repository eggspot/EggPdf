using System.Collections.Generic;

namespace EggPdf.Text;

/// <summary>
/// Break opportunities inside scripts written without spaces between words: Thai, Lao, Khmer and
/// Myanmar. Like <see cref="ThaiLineBreaker"/> this is a dictionary-free, orthographic heuristic --
/// it breaks only at syllable starts it can identify, never splits a syllable's combining marks,
/// coeng/virama clusters or coda consonants from their base, and so guarantees that long paragraphs
/// wrap instead of overflowing. It does not find true word boundaries (that needs a dictionary, as
/// in ICU), so lines may end at syllable rather than word boundaries.
/// </summary>
public static class SpacelessLineBreaker
{
    private const int LaoToThai = 0x80; // the Lao block mirrors Thai's letter/vowel/tone layout 0x80 higher

    /// <summary>Whether the text holds any Thai, Lao, Khmer or Myanmar character.</summary>
    public static bool Contains(string? text)
    {
        if (text == null) return false;
        for (int i = 0; i < text.Length; i++)
            if (IsThai(text[i]) || IsLao(text[i]) || IsKhmer(text[i]) || IsMyanmar(text[i])) return true;
        return false;
    }

    private static bool IsThai(char c) => c >= 'ก' && c <= '๛';
    private static bool IsLao(char c) => c >= '຀' && c <= 'ໟ';
    private static bool IsKhmer(char c) => c >= 'ក' && c <= '៿';
    private static bool IsMyanmar(char c) => c >= 'က' && c <= '႟';

    /// <summary>
    /// Split a space-free word into segments at syllable-start break opportunities. Segments are
    /// meant to be joined without any space. Returns the word itself when it has no opportunities.
    /// </summary>
    public static List<string> Split(string word)
    {
        if (ThaiLineBreaker.ContainsThai(word)) return ThaiLineBreaker.Split(word);

        bool lao = false, khmer = false, myanmar = false;
        for (int i = 0; i < word.Length; i++)
        {
            lao |= IsLao(word[i]);
            khmer |= IsKhmer(word[i]);
            myanmar |= IsMyanmar(word[i]);
        }

        if (lao) return SplitLao(word);
        if (!khmer && !myanmar) return new List<string> { word };

        var parts = new List<string>();
        int start = 0;
        for (int i = 1; i < word.Length; i++)
        {
            if (!CanBreakBefore(word, i)) continue;
            parts.Add(word.Substring(start, i - start));
            start = i;
        }
        parts.Add(word.Substring(start));
        return parts;
    }

    /// <summary>Lao follows Thai's syllable structure exactly one block higher: map, split with the Thai rules, map back.</summary>
    private static List<string> SplitLao(string word)
    {
        var mapped = new char[word.Length];
        for (int i = 0; i < word.Length; i++)
            mapped[i] = IsLao(word[i]) ? (char)(word[i] - LaoToThai) : word[i];

        var parts = new List<string>();
        int offset = 0;
        foreach (var segment in ThaiLineBreaker.Split(new string(mapped)))
        {
            parts.Add(word.Substring(offset, segment.Length));
            offset += segment.Length;
        }
        return parts;
    }

    private static bool CanBreakBefore(string text, int index)
    {
        char prev = text[index - 1], cur = text[index];
        bool prevKhmer = IsKhmer(prev), curKhmer = IsKhmer(cur);
        bool prevMyanmar = IsMyanmar(prev), curMyanmar = IsMyanmar(cur);

        if (curKhmer && prevKhmer) return KhmerBreak(text, index);
        if (curMyanmar && prevMyanmar) return MyanmarBreak(text, index);

        // Script <-> Latin letters/digits. Punctuation stays attached so a line never starts with it.
        bool prevScript = prevKhmer || prevMyanmar, curScript = curKhmer || curMyanmar;
        if (prevScript == curScript) return false;
        char other = prevScript ? cur : prev;
        return char.IsLetterOrDigit(other);
    }

    // ── Khmer ────────────────────────────────────────────────────────────────

    private const char Coeng = '្';
    private static bool IsKhmerConsonant(char c) => c >= 'ក' && c <= 'អ';
    private static bool IsKhmerIndependentVowel(char c) => c >= 'ឣ' && c <= 'ឳ';
    private static bool IsKhmerDependentVowel(char c) => c >= 'ា' && c <= 'ៅ';
    private static bool IsKhmerSign(char c) => (c >= 'ំ' && c <= '៑') || c == '៓' || c == '៝';

    private static bool KhmerBreak(string text, int index)
    {
        char prev = text[index - 1], cur = text[index];
        if (prev == Coeng) return false; // the subscript consonant belongs to the coeng before it

        // A syllable ends on a dependent vowel or a sign (nikahit, bantoc, ...)
        bool prevEndsSyllable = IsKhmerDependentVowel(prev) || IsKhmerSign(prev);
        if (!prevEndsSyllable) return false;

        if (IsKhmerIndependentVowel(cur)) return true;
        if (!IsKhmerConsonant(cur) || index + 1 >= text.Length) return false;

        // A consonant after a completed syllable starts a new one only when it visibly carries its
        // own coeng or vowel; otherwise it is the previous syllable's coda ("ក្នុង": ង).
        char next = text[index + 1];
        return next == Coeng || IsKhmerDependentVowel(next);
    }

    // ── Myanmar ──────────────────────────────────────────────────────────────

    private const char Asat = '်', Virama = '္';
    private static bool IsMyanmarConsonant(char c) => c >= 'က' && c <= 'အ';
    private static bool IsMyanmarIndependentVowel(char c) => c >= 'ဣ' && c <= 'ဪ';
    private static bool IsMyanmarVowelSign(char c) => c >= 'ါ' && c <= 'ဲ';
    private static bool IsMyanmarToneOrSign(char c) => c >= 'ံ' && c <= 'း';

    private static bool MyanmarBreak(string text, int index)
    {
        char prev = text[index - 1], cur = text[index];
        if (prev == Virama) return false; // the stacked consonant belongs to the virama before it

        bool prevEndsSyllable = prev == Asat || IsMyanmarVowelSign(prev) || IsMyanmarToneOrSign(prev);
        if (!prevEndsSyllable) return false;

        if (IsMyanmarIndependentVowel(cur)) return true;
        if (!IsMyanmarConsonant(cur) || index + 1 >= text.Length) return false;

        // A consonant followed by an asat is the previous syllable's killed final ("ကျွန်"); one that
        // is followed by a virama starts a stacked cluster and one followed by a vowel or medial starts a syllable.
        char next = text[index + 1];
        return next != Asat;
    }
}
