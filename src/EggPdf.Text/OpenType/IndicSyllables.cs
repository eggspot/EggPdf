using System;
using System.Collections.Generic;

namespace EggPdf.Text.OpenType;

internal static class IndicRole
{
    public const byte None = 0, Reph = 1, RephHalant = 2, PreMatra = 3, Base = 4, Consonant = 5,
        Halant = 6, Nukta = 7, MatraAboveBelow = 8, MatraPost = 9, Modifier = 10, Vowel = 11, Joiner = 12;
}

/// <summary>One codepoint in an Indic run, with the role it plays in its syllable.</summary>
internal struct IndicChar
{
    public int Cp, Cluster, Syl;
    public byte Role;
}

/// <summary>
/// Splits Indic text into syllables and applies the pre-GSUB reordering shared by the Indic
/// scripts: two-part vowel signs are decomposed, a syllable-initial "RA + virama" is flagged as a
/// reph candidate, and pre-base vowel signs move to the front of their syllable (after the reph
/// candidate). Later GSUB features and the final reph placement rely on the roles assigned here.
/// </summary>
internal static class IndicSyllables
{
    // Canonical decompositions of two-part vowel signs whose left part is written before the base.
    private static readonly Dictionary<int, int[]> TwoPart = new()
    {
        [0x09CB] = new[] { 0x09C7, 0x09BE }, [0x09CC] = new[] { 0x09C7, 0x09D7 },
        [0x0B4B] = new[] { 0x0B47, 0x0B3E }, [0x0B4C] = new[] { 0x0B47, 0x0B57 },
        [0x0BCA] = new[] { 0x0BC6, 0x0BBE }, [0x0BCB] = new[] { 0x0BC7, 0x0BBE }, [0x0BCC] = new[] { 0x0BC6, 0x0BD7 },
        [0x0D4A] = new[] { 0x0D46, 0x0D3E }, [0x0D4B] = new[] { 0x0D47, 0x0D3E }, [0x0D4C] = new[] { 0x0D46, 0x0D57 },
        [0x0DDA] = new[] { 0x0DD9, 0x0DCA }, [0x0DDC] = new[] { 0x0DD9, 0x0DCF },
        [0x0DDD] = new[] { 0x0DD9, 0x0DCF, 0x0DCA }, [0x0DDE] = new[] { 0x0DD9, 0x0DDF },
    };

    private static bool IsJoiner(int cp) => cp == 0x200C || cp == 0x200D;

    /// <summary>Matras drawn above or below the base (placed before the reph); all other matras are right-side.</summary>
    private static bool IsAboveBelowMatra(IndicScriptInfo s, int cp)
    {
        int o = cp - s.Base;
        if (s.Base == 0x0900) return o >= 0x41 && o <= 0x48;
        if (s.Base == 0x0980) return o >= 0x41 && o <= 0x44;
        if (s.Base == 0x0A80) return (o >= 0x41 && o <= 0x45) || o == 0x47 || o == 0x48;
        if (s.Base == 0x0B00) return (o >= 0x41 && o <= 0x44) || o == 0x56;
        return false;
    }

    private static bool IsOtherMark(int cp)
    {
        var cat = char.GetUnicodeCategory((char)cp);
        return cat == System.Globalization.UnicodeCategory.NonSpacingMark ||
               cat == System.Globalization.UnicodeCategory.EnclosingMark;
    }

    public static List<IndicChar> Prepare(IndicScriptInfo s, IList<int> cps, IList<int> clusters)
    {
        // Decompose two-part vowel signs first so pre-base parts can move independently.
        var chars = new List<IndicChar>(cps.Count + 4);
        for (int i = 0; i < cps.Count; i++)
        {
            if (TwoPart.TryGetValue(cps[i], out var parts))
                foreach (var p in parts) chars.Add(new IndicChar { Cp = p, Cluster = clusters[i] });
            else
                chars.Add(new IndicChar { Cp = cps[i], Cluster = clusters[i] });
        }

        var result = new List<IndicChar>(chars.Count);
        int n = chars.Count, i2 = 0, syl = 0;
        while (i2 < n)
        {
            int start = i2;
            int cp = chars[i2].Cp;
            var syllable = new List<IndicChar>();
            int prematraInsertAt = 0;

            bool dotReph = s.RephChar != 0 && cp == s.RephChar && i2 + 1 < n && s.IsConsonant(chars[i2 + 1].Cp);
            if (s.IsConsonant(cp) || dotReph)
            {
                int j = i2;
                var cons = new List<int>();          // indices into syllable of consonants (excluding reph)
                if (dotReph)
                {
                    syllable.Add(WithRole(chars[j], IndicRole.Reph));
                    j++;
                    prematraInsertAt = 1;
                }
                else if (s.HasReph && s.Ra != 0 && cp == s.Ra && j + 1 < n && s.IsVirama(chars[j + 1].Cp))
                {
                    // Implicit-reph scripts form a reph from RA + virama before a consonant;
                    // Telugu and Malayalam only from RA + virama + ZWJ.
                    bool zwjFollows = j + 2 < n && chars[j + 2].Cp == 0x200D;
                    bool rephForms = s.RephNeedsZwj ? zwjFollows : (j + 2 < n && s.IsConsonant(chars[j + 2].Cp));
                    if (rephForms)
                    {
                        syllable.Add(WithRole(chars[j], IndicRole.Reph));
                        syllable.Add(WithRole(chars[j + 1], IndicRole.RephHalant));
                        j += 2;
                        if (s.RephNeedsZwj) { syllable.Add(WithRole(chars[j], IndicRole.RephHalant)); j++; }
                        prematraInsertAt = syllable.Count;
                    }
                }

                while (j < n && s.IsConsonant(chars[j].Cp))
                {
                    cons.Add(syllable.Count);
                    syllable.Add(WithRole(chars[j], IndicRole.Consonant));
                    j++;
                    while (j < n && s.IsNukta(chars[j].Cp)) { syllable.Add(WithRole(chars[j], IndicRole.Nukta)); j++; }
                    if (j < n && s.IsVirama(chars[j].Cp))
                    {
                        syllable.Add(WithRole(chars[j], IndicRole.Halant));
                        j++;
                        while (j < n && IsJoiner(chars[j].Cp)) { syllable.Add(WithRole(chars[j], IndicRole.Joiner)); j++; }
                        if (j < n && s.IsConsonant(chars[j].Cp)) continue;   // conjunct continues
                    }
                    break;
                }

                // Base consonant: the last one, skipping RA / post-base YA that follow a halant.
                if (cons.Count > 0)
                {
                    int b = cons.Count - 1;
                    while (b > 0)
                    {
                        int cpB = syllable[cons[b]].Cp;
                        // RA/YA subjoined through "virama ZWJ RA" (Sinhala) count as after-halant too.
                        int prev = cons[b] - 1;
                        while (prev >= 0 && syllable[prev].Role == IndicRole.Joiner) prev--;
                        bool afterHalant = prev >= 0 && syllable[prev].Role == IndicRole.Halant;
                        if (afterHalant && (cpB == s.Ra || (s.YaPostBase != 0 && cpB == s.YaPostBase))) b--;
                        else break;
                    }
                    var bc = syllable[cons[b]];
                    bc.Role = IndicRole.Base;
                    syllable[cons[b]] = bc;
                }

                j = ConsumeTrailing(s, chars, j, n, syllable);
                i2 = j;
            }
            else if (s.IsVowel(cp))
            {
                syllable.Add(WithRole(chars[i2], IndicRole.Vowel));
                int j = i2 + 1;
                while (j < n && s.IsNukta(chars[j].Cp)) { syllable.Add(WithRole(chars[j], IndicRole.Nukta)); j++; }
                j = ConsumeTrailing(s, chars, j, n, syllable);
                i2 = j;
            }
            else
            {
                syllable.Add(chars[i2]);
                i2++;
            }

            // Move pre-base vowel signs to the front of the syllable (after a reph candidate).
            for (int k = prematraInsertAt; k < syllable.Count; k++)
            {
                if (syllable[k].Role == IndicRole.PreMatra)
                {
                    var m = syllable[k];
                    syllable.RemoveAt(k);
                    syllable.Insert(prematraInsertAt, m);
                    prematraInsertAt++;
                }
            }

            for (int k = 0; k < syllable.Count; k++)
            {
                var c = syllable[k];
                c.Syl = syl;
                result.Add(c);
            }
            syl++;
            if (i2 == start) i2++;
        }
        return result;
    }

    private static IndicChar WithRole(IndicChar c, byte role) { c.Role = role; return c; }

    /// <summary>Consume matras, nukta, modifiers and other combining marks that trail a consonant/vowel.</summary>
    private static int ConsumeTrailing(IndicScriptInfo s, List<IndicChar> chars, int j, int n, List<IndicChar> syllable)
    {
        while (j < n)
        {
            int cp = chars[j].Cp;
            byte role;
            if (s.IsNukta(cp)) role = IndicRole.Nukta;
            else if (s.IsPreBaseMatra(cp)) role = IndicRole.PreMatra;
            else if (s.IsMatra(cp) || cp == 0x09D7 || cp == 0x0B57 || cp == 0x0BD7 || cp == 0x0D57)
                role = IsAboveBelowMatra(s, cp) ? IndicRole.MatraAboveBelow : IndicRole.MatraPost;
            else if (s.IsVirama(cp)) role = IndicRole.Halant;
            else if (s.IsModifier(cp) || IsOtherMark(cp)) role = IndicRole.Modifier;
            else if (IsJoiner(cp)) role = IndicRole.Joiner;
            else break;
            syllable.Add(WithRole(chars[j], role));
            j++;
        }
        return j;
    }
}
