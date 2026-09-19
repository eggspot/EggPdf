using System.Collections.Generic;

namespace EggPdf.Text;

/// <summary>
/// Break opportunities inside Thai text, which has no spaces between words. This is a
/// dictionary-free heuristic: it never splits a character cluster (consonant + vowel/tone marks)
/// or a leading vowel from its consonant, and only breaks at syllable starts it can identify
/// from orthography -- before leading vowels, and before a consonant that begins a vowel-marked
/// syllable following a completed one. It guarantees long Thai paragraphs wrap instead of
/// overflowing; it does not find true word boundaries (that needs a dictionary, as in ICU).
/// </summary>
public static class ThaiLineBreaker
{
    public static bool ContainsThai(string? text)
    {
        if (text == null) return false;
        for (int i = 0; i < text.Length; i++)
            if (text[i] >= 'ก' && text[i] <= '๛') return true;
        return false;
    }

    private static bool IsThai(char c) => c >= 'ก' && c <= '๛';
    private static bool IsConsonant(char c) => c >= 'ก' && c <= 'ฮ';
    private static bool IsLeadingVowel(char c) => c >= 'เ' && c <= 'ไ';

    /// <summary>Vowels/marks that complete an open syllable (sara a, aa, am, lakkhangyao, maiyamok).</summary>
    private static bool IsSyllableFinalVowel(char c)
        => c == 'ะ' || c == 'า' || c == 'ำ' || c == 'ๅ' || c == 'ๆ';

    /// <summary>Combining vowel, tone and sign characters that must stay with the preceding consonant.</summary>
    private static bool IsDependent(char c)
        => c == 'ั' || (c >= 'ิ' && c <= 'ฺ') || (c >= '็' && c <= '๎') || IsSyllableFinalVowel(c);

    private static bool IsAboveBelowVowelOrTone(char c)
        => c == 'ั' || (c >= 'ิ' && c <= 'ฺ') || (c >= '็' && c <= '๎');

    /// <summary>Whether a line may break between text[index - 1] and text[index].</summary>
    private static bool CanBreakBefore(string text, int index)
    {
        char prev = text[index - 1];
        char cur = text[index];

        bool prevThai = IsThai(prev), curThai = IsThai(cur);
        if (prevThai != curThai)
            return true; // Thai <-> Latin/digits/punctuation

        if (!curThai) return false;
        if (IsLeadingVowel(prev)) return false;      // leading vowel stays with its consonant
        if (IsDependent(cur)) return false;           // marks/vowels stay with their consonant

        if (IsLeadingVowel(cur))
            return true;                              // a leading vowel begins a new syllable

        if (!IsConsonant(cur)) return false;

        // A consonant begins a new syllable after a completed one. Only trust that when the
        // previous syllable visibly ended (open-syllable vowel or above/below vowel/tone) and this
        // consonant is itself followed by a vowel/tone mark or leading-vowel context, so codas
        // ("กิน" -> ก ิ | น) and consonant clusters are not split.
        bool prevEndsSyllable = IsSyllableFinalVowel(prev) || IsAboveBelowVowelOrTone(prev);
        if (!prevEndsSyllable) return false;
        if (index + 1 >= text.Length) return false;
        char next = text[index + 1];
        return IsDependent(next);
    }

    /// <summary>
    /// Split a space-free word into segments at the break opportunities above. Segments are
    /// meant to be joined without any space. Returns the word itself when it has no opportunities.
    /// </summary>
    public static List<string> Split(string word)
    {
        var parts = new List<string>();
        int start = 0;
        for (int i = 1; i < word.Length; i++)
        {
            if (CanBreakBefore(word, i))
            {
                parts.Add(word.Substring(start, i - start));
                start = i;
            }
        }
        parts.Add(word.Substring(start));
        return parts;
    }
}
