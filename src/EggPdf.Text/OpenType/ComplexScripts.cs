using System;

namespace EggPdf.Text.OpenType;

internal enum ScriptKind { Common, Latin, Arabic, Thai, Indic }

/// <summary>Indic scripts handled by the syllable shaper, with per-script character behaviour.</summary>
internal sealed class IndicScriptInfo
{
    public int Base;                  // first codepoint of the Unicode block
    public string[] Tags = Array.Empty<string>();   // OpenType script tags, newest first
    public int[] PreBaseMatras = Array.Empty<int>();
    public bool HasReph;
    public int Ra;                    // RA consonant codepoint (0 = none)
    public int YaPostBase;            // YA that forms a post-base shape after a halant (0 = none)

    public bool IsConsonant(int cp)
    {
        int o = cp - Base;
        return (o >= 0x15 && o <= 0x39) || (o >= 0x58 && o <= 0x5F && Base == 0x0900);
    }
    public bool IsVowel(int cp) { int o = cp - Base; return o >= 0x05 && o <= 0x14; }
    public bool IsNukta(int cp) => cp - Base == 0x3C;
    public bool IsVirama(int cp) => cp - Base == 0x4D;
    public bool IsMatra(int cp) { int o = cp - Base; return o >= 0x3E && o <= 0x4C; }
    public bool IsModifier(int cp) { int o = cp - Base; return o >= 0x00 && o <= 0x03; }
    public bool IsPreBaseMatra(int cp)
    {
        foreach (var m in PreBaseMatras) if (m == cp) return true;
        return false;
    }
}

/// <summary>Script classification for the complex-text shaping path.</summary>
internal static class ComplexScripts
{
    public static readonly IndicScriptInfo Devanagari = new()
    {
        Base = 0x0900, Tags = new[] { "dev2", "deva" }, PreBaseMatras = new[] { 0x093F },
        HasReph = true, Ra = 0x0930,
    };
    public static readonly IndicScriptInfo Bengali = new()
    {
        Base = 0x0980, Tags = new[] { "bng2", "beng" }, PreBaseMatras = new[] { 0x09BF, 0x09C7, 0x09C8 },
        HasReph = true, Ra = 0x09B0, YaPostBase = 0x09AF,
    };
    public static readonly IndicScriptInfo Gurmukhi = new()
    {
        Base = 0x0A00, Tags = new[] { "gur2", "guru" }, PreBaseMatras = new[] { 0x0A3F },
    };
    public static readonly IndicScriptInfo Gujarati = new()
    {
        Base = 0x0A80, Tags = new[] { "gjr2", "gujr" }, HasReph = true, Ra = 0x0AB0,
    };
    public static readonly IndicScriptInfo Oriya = new()
    {
        Base = 0x0B00, Tags = new[] { "ory2", "orya" }, PreBaseMatras = new[] { 0x0B47 },
        HasReph = true, Ra = 0x0B30,
    };
    public static readonly IndicScriptInfo Tamil = new()
    {
        Base = 0x0B80, Tags = new[] { "tml2", "taml" }, PreBaseMatras = new[] { 0x0BC6, 0x0BC7, 0x0BC8 },
    };
    public static readonly IndicScriptInfo Telugu = new()
    {
        Base = 0x0C00, Tags = new[] { "tel2", "telu" },
    };
    public static readonly IndicScriptInfo Kannada = new()
    {
        Base = 0x0C80, Tags = new[] { "knd2", "knda" },
    };
    public static readonly IndicScriptInfo Malayalam = new()
    {
        Base = 0x0D00, Tags = new[] { "mlm2", "mlym" }, PreBaseMatras = new[] { 0x0D46, 0x0D47, 0x0D48 },
    };

    public static IndicScriptInfo? GetIndic(int cp)
    {
        if (cp < 0x0900 || cp > 0x0D7F) return null;
        if (cp < 0x0980) return Devanagari;
        if (cp < 0x0A00) return Bengali;
        if (cp < 0x0A80) return Gurmukhi;
        if (cp < 0x0B00) return Gujarati;
        if (cp < 0x0B80) return Oriya;
        if (cp < 0x0C00) return Tamil;
        if (cp < 0x0C80) return Telugu;
        if (cp < 0x0D00) return Kannada;
        return Malayalam;
    }

    public static bool IsThaiOrLao(int cp) => (cp >= 0x0E00 && cp <= 0x0EFF);

    /// <summary>Arabic-block combining marks (harakat, superscript alef, Quranic marks) that need GPOS placement.</summary>
    public static bool IsArabicMark(int cp)
        => (cp >= 0x064B && cp <= 0x065F) || cp == 0x0670 || (cp >= 0x0610 && cp <= 0x061A) ||
           (cp >= 0x06D6 && cp <= 0x06DC) || (cp >= 0x06DF && cp <= 0x06E4) || cp == 0x06E7 || cp == 0x06E8 ||
           (cp >= 0x06EA && cp <= 0x06ED);

    public static ScriptKind KindOf(int cp)
    {
        if (cp >= 0x0900 && cp <= 0x0D7F) return ScriptKind.Indic;
        if (IsThaiOrLao(cp)) return ScriptKind.Thai;
        if ((cp >= 0x0600 && cp <= 0x06FF) || (cp >= 0xFB50 && cp <= 0xFDFF) || (cp >= 0xFE70 && cp <= 0xFEFF))
            return ScriptKind.Arabic;
        if (cp <= 0x0040 || (cp >= 0x005B && cp <= 0x0060) || (cp >= 0x007B && cp <= 0x00BF) ||
            (cp >= 0x2000 && cp <= 0x206F) || cp == 0x200C || cp == 0x200D)
            return ScriptKind.Common;
        return ScriptKind.Latin;
    }

    /// <summary>
    /// True when the text must go through the glyph-level shaper: any Indic/Thai/Lao character,
    /// or Arabic text carrying combining marks (letters alone are handled by ArabicShaper).
    /// </summary>
    public static bool NeedsShaping(string? text)
    {
        if (text == null) return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < 'ؐ') continue;
            if ((c >= 'ऀ' && c <= 'ൿ') || (c >= '฀' && c <= '໿')) return true;
            if (IsArabicMark(c)) return true;
        }
        return false;
    }

    public static bool HasStrongComplexScript(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if ((c >= 'ऀ' && c <= 'ൿ') || (c >= '฀' && c <= '໿')) return true;
        }
        return false;
    }
}
