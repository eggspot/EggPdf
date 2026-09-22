using System.Collections.Generic;
using System.Text;

namespace EggPdf.Text;

/// <summary>
/// Arabic contextual shaping: picks each letter's isolated/initial/medial/final form from its
/// neighbours' joining behaviour (Unicode ArabicShaping.txt) and maps it onto the Unicode Arabic
/// Presentation Forms (U+FB50..FDFF, U+FE70..FEFF), including the mandatory lam-alef ligatures.
/// A pure string transform on logical-order text -- it must run before bidi reordering, since
/// joining depends on logical neighbours -- with no font dependency; callers fall back to the
/// base letter (<see cref="TryGetBaseForm"/>) for fonts that lack presentation-form glyphs.
///
/// Covers the standard Arabic letters (U+0621..U+064A) plus the common Persian/Urdu-adjacent
/// letters peh, tcheh, jeh, keheh, gaf and farsi yeh. Other Arabic-script letters pass through
/// unshaped (and break joining). Indic scripts and GPOS mark positioning are out of scope.
/// </summary>
public static class ArabicShaper
{
    private readonly struct Forms
    {
        public readonly char Isolated, Final, Initial, Medial;
        public Forms(int iso, int fin, int init, int med)
        {
            Isolated = (char)iso; Final = (char)fin; Initial = (char)init; Medial = (char)med;
        }
        /// <summary>Dual-joining letters have an initial form; right-joining ones only iso/final.</summary>
        public bool JoinsForward => Initial != 0;
        public bool AcceptsJoin => Final != 0;
    }

    private static readonly Dictionary<char, Forms> Table = BuildTable();
    private static readonly Dictionary<char, (char iso, char fin)> LamAlef = new()
    {
        ['آ'] = ('ﻵ', 'ﻶ'),
        ['أ'] = ('ﻷ', 'ﻸ'),
        ['إ'] = ('ﻹ', 'ﻺ'),
        ['ا'] = ('ﻻ', 'ﻼ'),
    };
    private static readonly Dictionary<int, string> Reverse = BuildReverse();
    private static readonly Dictionary<int, (string baseText, ArabicForm[] forms)> FormInfo = BuildFormInfo();

    /// <summary>The positional form a presentation-form codepoint stands for.</summary>
    public enum ArabicForm { Isolated, Final, Initial, Medial }

    /// <summary>
    /// For an Arabic presentation-form codepoint, the base letter(s) and the positional form each
    /// takes -- how a font that lacks presentation-form glyphs is shaped through its own
    /// isol/init/medi/fina GSUB features instead. A lam-alef ligature expands to lam + alef.
    /// </summary>
    public static bool TryGetFormInfo(int codepoint, out string baseText, out ArabicForm[] forms)
    {
        if (FormInfo.TryGetValue(codepoint, out var info)) { baseText = info.baseText; forms = info.forms; return true; }
        baseText = ""; forms = System.Array.Empty<ArabicForm>();
        return false;
    }

    private static Dictionary<int, (string, ArabicForm[])> BuildFormInfo()
    {
        var map = new Dictionary<int, (string, ArabicForm[])>();
        foreach (var kv in Table)
        {
            string b = kv.Key.ToString();
            var f = kv.Value;
            map[f.Isolated] = (b, new[] { ArabicForm.Isolated });
            if (f.Final != 0) map[f.Final] = (b, new[] { ArabicForm.Final });
            if (f.Initial != 0) map[f.Initial] = (b, new[] { ArabicForm.Initial });
            if (f.Medial != 0) map[f.Medial] = (b, new[] { ArabicForm.Medial });
        }
        foreach (var kv in LamAlef)
        {
            string pair = "ل" + kv.Key;
            map[kv.Value.iso] = (pair, new[] { ArabicForm.Initial, ArabicForm.Final });
            map[kv.Value.fin] = (pair, new[] { ArabicForm.Medial, ArabicForm.Final });
        }
        return map;
    }

    private static Dictionary<char, Forms> BuildTable()
    {
        // base, isolated, final, initial, medial (0 = form does not exist)
        int[][] rows =
        {
            new[] { 0x0621, 0xFE80, 0, 0, 0 },
            new[] { 0x0622, 0xFE81, 0xFE82, 0, 0 },
            new[] { 0x0623, 0xFE83, 0xFE84, 0, 0 },
            new[] { 0x0624, 0xFE85, 0xFE86, 0, 0 },
            new[] { 0x0625, 0xFE87, 0xFE88, 0, 0 },
            new[] { 0x0626, 0xFE89, 0xFE8A, 0xFE8B, 0xFE8C },
            new[] { 0x0627, 0xFE8D, 0xFE8E, 0, 0 },
            new[] { 0x0628, 0xFE8F, 0xFE90, 0xFE91, 0xFE92 },
            new[] { 0x0629, 0xFE93, 0xFE94, 0, 0 },
            new[] { 0x062A, 0xFE95, 0xFE96, 0xFE97, 0xFE98 },
            new[] { 0x062B, 0xFE99, 0xFE9A, 0xFE9B, 0xFE9C },
            new[] { 0x062C, 0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0 },
            new[] { 0x062D, 0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4 },
            new[] { 0x062E, 0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8 },
            new[] { 0x062F, 0xFEA9, 0xFEAA, 0, 0 },
            new[] { 0x0630, 0xFEAB, 0xFEAC, 0, 0 },
            new[] { 0x0631, 0xFEAD, 0xFEAE, 0, 0 },
            new[] { 0x0632, 0xFEAF, 0xFEB0, 0, 0 },
            new[] { 0x0633, 0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4 },
            new[] { 0x0634, 0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8 },
            new[] { 0x0635, 0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC },
            new[] { 0x0636, 0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0 },
            new[] { 0x0637, 0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4 },
            new[] { 0x0638, 0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8 },
            new[] { 0x0639, 0xFEC9, 0xFECA, 0xFECB, 0xFECC },
            new[] { 0x063A, 0xFECD, 0xFECE, 0xFECF, 0xFED0 },
            new[] { 0x0641, 0xFED1, 0xFED2, 0xFED3, 0xFED4 },
            new[] { 0x0642, 0xFED5, 0xFED6, 0xFED7, 0xFED8 },
            new[] { 0x0643, 0xFED9, 0xFEDA, 0xFEDB, 0xFEDC },
            new[] { 0x0644, 0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0 },
            new[] { 0x0645, 0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4 },
            new[] { 0x0646, 0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8 },
            new[] { 0x0647, 0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC },
            new[] { 0x0648, 0xFEED, 0xFEEE, 0, 0 },
            new[] { 0x0649, 0xFEEF, 0xFEF0, 0, 0 },
            new[] { 0x064A, 0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4 },
            // Persian / Urdu-adjacent letters (Presentation Forms-A)
            new[] { 0x067E, 0xFB56, 0xFB57, 0xFB58, 0xFB59 },
            new[] { 0x0686, 0xFB7A, 0xFB7B, 0xFB7C, 0xFB7D },
            new[] { 0x0698, 0xFB8A, 0xFB8B, 0, 0 },
            new[] { 0x06A9, 0xFB8E, 0xFB8F, 0xFB90, 0xFB91 },
            new[] { 0x06AF, 0xFB92, 0xFB93, 0xFB94, 0xFB95 },
            new[] { 0x06CC, 0xFBFC, 0xFBFD, 0xFBFE, 0xFBFF },
        };
        var table = new Dictionary<char, Forms>(rows.Length);
        foreach (var r in rows)
            table[(char)r[0]] = new Forms(r[1], r[2], r[3], r[4]);
        return table;
    }

    private static Dictionary<int, string> BuildReverse()
    {
        var reverse = new Dictionary<int, string>();
        foreach (var kv in Table)
        {
            string baseText = kv.Key.ToString();
            var f = kv.Value;
            reverse[f.Isolated] = baseText;
            if (f.Final != 0) reverse[f.Final] = baseText;
            if (f.Initial != 0) reverse[f.Initial] = baseText;
            if (f.Medial != 0) reverse[f.Medial] = baseText;
        }
        foreach (var kv in LamAlef)
        {
            string pair = "ل" + kv.Key;
            reverse[kv.Value.iso] = pair;
            reverse[kv.Value.fin] = pair;
        }
        return reverse;
    }

    /// <summary>True if the text contains any character in the Arabic block (cheap early-exit scan).</summary>
    public static bool ContainsArabic(string? text)
    {
        if (text == null) return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c >= '؀' && c <= 'ۿ') return true;
        }
        return false;
    }

    /// <summary>
    /// Map a presentation-form codepoint back to the base letter(s) it was shaped from -- used
    /// when a font lacks presentation-form glyphs so text degrades to the unshaped letters
    /// (disconnected but legible) rather than .notdef boxes. A lam-alef ligature expands to two.
    /// </summary>
    public static bool TryGetBaseForm(int codepoint, out string baseText)
        => Reverse.TryGetValue(codepoint, out baseText!);

    /// <summary>
    /// Shape logical-order text: substitute each Arabic letter with its contextual presentation
    /// form. Already-shaped text and non-Arabic characters pass through unchanged (idempotent).
    /// </summary>
    public static string Shape(string text)
    {
        if (!ContainsArabic(text)) return text;

        var sb = new StringBuilder(text.Length);
        int n = text.Length;
        for (int i = 0; i < n; i++)
        {
            char c = text[i];
            if (!Table.TryGetValue(c, out var forms))
            {
                sb.Append(c);
                continue;
            }

            bool prevJoins = PreviousConnectsForward(text, i);

            // Mandatory lam-alef ligature (alef must directly follow the lam).
            if (c == 'ل' && i + 1 < n && LamAlef.TryGetValue(text[i + 1], out var lig))
            {
                sb.Append(prevJoins ? lig.fin : lig.iso);
                i++;
                continue;
            }

            bool nextAccepts = forms.JoinsForward && NextAcceptsJoin(text, i);

            char shaped;
            if (forms.JoinsForward)
                shaped = prevJoins ? (nextAccepts ? forms.Medial : forms.Final)
                                   : (nextAccepts ? forms.Initial : forms.Isolated);
            else
                shaped = prevJoins && forms.AcceptsJoin ? forms.Final : forms.Isolated;

            sb.Append(shaped);
        }
        return sb.ToString();
    }

    private static bool PreviousConnectsForward(string text, int index)
    {
        for (int k = index - 1; k >= 0; k--)
        {
            char p = text[k];
            if (IsTransparent(p)) continue;
            return CanJoinForward(p);
        }
        return false;
    }

    private static bool NextAcceptsJoin(string text, int index)
    {
        for (int k = index + 1; k < text.Length; k++)
        {
            char q = text[k];
            if (IsTransparent(q)) continue;
            return AcceptsJoinFromPrevious(q);
        }
        return false;
    }

    private static bool CanJoinForward(char c)
        => c == 'ـ' || c == '‍' || (Table.TryGetValue(c, out var f) && f.JoinsForward);

    private static bool AcceptsJoinFromPrevious(char c)
        => c == 'ـ' || c == '‍' || (Table.TryGetValue(c, out var f) && f.AcceptsJoin);

    /// <summary>Combining marks (harakat, superscript alef, Quranic marks) do not affect joining.</summary>
    private static bool IsTransparent(char c)
        => (c >= 'ً' && c <= 'ٟ') || c == 'ٰ' ||
           (c >= 'ؐ' && c <= 'ؚ') ||
           (c >= 'ۖ' && c <= 'ۜ') || (c >= '۟' && c <= 'ۤ') ||
           c == 'ۧ' || c == 'ۨ' || (c >= '۪' && c <= 'ۭ');
}
