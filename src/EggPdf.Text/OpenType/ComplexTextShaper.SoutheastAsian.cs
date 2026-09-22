using System.Collections.Generic;

namespace EggPdf.Text.OpenType;

public static partial class ComplexTextShaper
{
    // ── Khmer ───────────────────────────────────────────────────────────────────

    private const int Coeng = 0x17D2, KhmerRo = 0x179A;

    private static bool IsKhmerConsonant(int c) => c >= 0x1780 && c <= 0x17A2;
    private static bool IsKhmerIndependentVowel(int c) => c >= 0x17A3 && c <= 0x17B3;
    private static bool IsKhmerPreVowel(int c) => c >= 0x17C1 && c <= 0x17C3;

    /// <summary>Marks and vowel signs that trail a Khmer base within its syllable.</summary>
    private static bool IsKhmerDependent(int c)
        => (c >= 0x17B4 && c <= 0x17D1) || c == 0x17D3 || c == 0x17DD;

    /// <summary>
    /// Khmer syllable reordering: pre-base vowel signs and a subscript RO (coeng + RO) are written
    /// before the base, so they move to the front of the syllable (vowels first), and two-part
    /// vowel signs gain their left-hand 17C1 part. GSUB then forms subscripts and ligatures.
    /// </summary>
    private static void ReorderKhmer(List<int> cps, List<int> clusters)
    {
        var outCps = new List<int>(cps.Count + 4);
        var outClusters = new List<int>(cps.Count + 4);
        int n = cps.Count, i = 0;
        while (i < n)
        {
            int c = cps[i];
            if (!(IsKhmerConsonant(c) || IsKhmerIndependentVowel(c)))
            {
                outCps.Add(c); outClusters.Add(clusters[i]); i++;
                continue;
            }

            int start = i, j = i + 1;
            while (j < n)
            {
                int d = cps[j];
                if (d == Coeng && j + 1 < n && IsKhmerConsonant(cps[j + 1])) { j += 2; continue; }
                if (IsKhmerDependent(d) || d == 0x200C || d == 0x200D) { j++; continue; }
                break;
            }

            var preVowels = new List<(int cp, int cl)>();
            var coengRo = new List<(int cp, int cl)>();
            var rest = new List<(int cp, int cl)>();
            rest.Add((cps[start], clusters[start]));
            for (int k = start + 1; k < j; k++)
            {
                int d = cps[k];
                if (d == Coeng && k + 1 < j && cps[k + 1] == KhmerRo)
                {
                    coengRo.Add((d, clusters[k]));
                    coengRo.Add((cps[k + 1], clusters[k + 1]));
                    k++;
                }
                else if (d == 0x17BE || d == 0x17BF || d == 0x17C0 || d == 0x17C4 || d == 0x17C5)
                {
                    preVowels.Add((0x17C1, clusters[k]));
                    rest.Add((d, clusters[k]));
                }
                else if (IsKhmerPreVowel(d))
                {
                    preVowels.Add((d, clusters[k]));
                }
                else
                {
                    rest.Add((d, clusters[k]));
                }
            }

            foreach (var v in preVowels) { outCps.Add(v.cp); outClusters.Add(v.cl); }
            foreach (var v in coengRo) { outCps.Add(v.cp); outClusters.Add(v.cl); }
            foreach (var v in rest) { outCps.Add(v.cp); outClusters.Add(v.cl); }
            i = j;
        }
        cps.Clear(); cps.AddRange(outCps);
        clusters.Clear(); clusters.AddRange(outClusters);
    }

    // ── Myanmar ─────────────────────────────────────────────────────────────────

    private static bool IsMyanmarBase(int c)
        => (c >= 0x1000 && c <= 0x102A) || c == 0x103F || (c >= 0x1050 && c <= 0x1055) ||
           (c >= 0x105A && c <= 0x105D) || c == 0x1061 || c == 0x1065 || c == 0x1066 ||
           (c >= 0x106E && c <= 0x1070) || (c >= 0x1075 && c <= 0x1081) || c == 0x108E;

    private static bool IsMyanmarConsonant(int c) => c >= 0x1000 && c <= 0x1021;

    /// <summary>Signs that trail a Myanmar base within its syllable (medials, vowels, tone marks, asat).</summary>
    private static bool IsMyanmarDependent(int c)
        => (c >= 0x102B && c <= 0x103E) || (c >= 0x1056 && c <= 0x1059) || (c >= 0x105E && c <= 0x1060) ||
           (c >= 0x1062 && c <= 0x1064) || (c >= 0x1067 && c <= 0x106D) || (c >= 0x1071 && c <= 0x1074) ||
           (c >= 0x1082 && c <= 0x108D) || (c >= 0x108F && c <= 0x109D);

    /// <summary>
    /// Myanmar syllable reordering: the e-vowel (U+1031) and medial ra (U+103C) are written to the
    /// left of the consonant, so they move ahead of the base; a leading kinzi moves behind it.
    /// </summary>
    private static void ReorderMyanmar(List<int> cps, List<int> clusters)
    {
        var outCps = new List<int>(cps.Count);
        var outClusters = new List<int>(cps.Count);
        int n = cps.Count, i = 0;
        while (i < n)
        {
            int start = i;
            var kinzi = new List<(int cp, int cl)>();
            // Kinzi: NGA + asat + virama before the base consonant.
            if (cps[i] == 0x1004 && i + 3 < n && cps[i + 1] == 0x103A && cps[i + 2] == 0x1039 && IsMyanmarBase(cps[i + 3]))
            {
                for (int k = 0; k < 3; k++) kinzi.Add((cps[i + k], clusters[i + k]));
                i += 3;
            }

            if (!IsMyanmarBase(cps[i]))
            {
                if (kinzi.Count > 0) { foreach (var k in kinzi) { outCps.Add(k.cp); outClusters.Add(k.cl); } }
                else { outCps.Add(cps[i]); outClusters.Add(clusters[i]); i++; }
                continue;
            }

            int baseIdx = i, j = i + 1;
            while (j < n)
            {
                int d = cps[j];
                if (d == 0x1039 && j + 1 < n && IsMyanmarConsonant(cps[j + 1])) { j += 2; continue; }
                if (IsMyanmarDependent(d) || d == 0x200C || d == 0x200D) { j++; continue; }
                break;
            }

            var eVowel = new List<(int cp, int cl)>();
            var medialRa = new List<(int cp, int cl)>();
            var rest = new List<(int cp, int cl)> { (cps[baseIdx], clusters[baseIdx]) };
            for (int k = baseIdx + 1; k < j; k++)
            {
                int d = cps[k];
                if (d == 0x1031) eVowel.Add((d, clusters[k]));
                else if (d == 0x103C) medialRa.Add((d, clusters[k]));
                else rest.Add((d, clusters[k]));
            }

            // Order: e-vowel, medial ra, base, kinzi, remaining signs. The kinzi follows the base so
            // GPOS mark-to-base attachment (which needs the base first) can seat it above the consonant.
            foreach (var v in eVowel) { outCps.Add(v.cp); outClusters.Add(v.cl); }
            foreach (var v in medialRa) { outCps.Add(v.cp); outClusters.Add(v.cl); }
            outCps.Add(rest[0].cp); outClusters.Add(rest[0].cl);
            foreach (var v in kinzi) { outCps.Add(v.cp); outClusters.Add(v.cl); }
            for (int r = 1; r < rest.Count; r++) { outCps.Add(rest[r].cp); outClusters.Add(rest[r].cl); }
            i = j;
            if (i == start) i++;
        }
        cps.Clear(); cps.AddRange(outCps);
        clusters.Clear(); clusters.AddRange(outClusters);
    }

    // ── GSUB feature sequences ──────────────────────────────────────────────────

    /// <summary>Khmer/Myanmar: basic shaping features one at a time, then the presentation set.</summary>
    private static void ApplySoutheastAsianFeatures(ScriptKind kind, OtEngine gsub, OtLangSys ls, GlyphBuffer buf)
    {
        ApplyStage(gsub, ls, buf, new[] { "locl", "ccmp" }, uint.MaxValue);
        string[] basic = kind == ScriptKind.Khmer
            ? new[] { "pref", "blwf", "abvf", "pstf", "cfar" }
            : new[] { "rphf", "pref", "blwf", "pstf" };
        foreach (var f in basic)
            ApplyStage(gsub, ls, buf, new[] { f }, uint.MaxValue);
        ApplyStage(gsub, ls, buf, new[] { "pres", "abvs", "blws", "psts", "clig", "calt", "liga" }, uint.MaxValue);
    }
}
