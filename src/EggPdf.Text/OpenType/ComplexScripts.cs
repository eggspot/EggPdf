using System;

namespace EggPdf.Text.OpenType;

internal enum ScriptKind { Common, Latin, Arabic, Thai, Indic, Tibetan, Khmer, Myanmar }

/// <summary>Where a formed reph is placed relative to the rest of its syllable.</summary>
internal enum RephPosition { AfterMain, BeforeSub, AfterSub, BeforePost, AfterPost }

/// <summary>Indic scripts handled by the syllable shaper, with per-script character behaviour.</summary>
internal class IndicScriptInfo
{
    public int Base;                  // first codepoint of the Unicode block
    public string[] Tags = Array.Empty<string>();   // OpenType script tags, newest first
    public int[] PreBaseMatras = Array.Empty<int>();
    public bool HasReph;
    public RephPosition RephPos = RephPosition.BeforePost;
    /// <summary>True when only "RA + virama + ZWJ" forms a reph (Telugu, Malayalam); otherwise "RA + virama + consonant".</summary>
    public bool RephNeedsZwj;
    public int Ra;                    // RA consonant codepoint (0 = none)
    /// <summary>A dedicated one-character reph (Malayalam dot reph U+0D4E); 0 = none.</summary>
    public int RephChar;
    public int YaPostBase;            // YA that forms a post-base shape after a halant (0 = none)

    public virtual bool IsConsonant(int cp)
    {
        int o = cp - Base;
        return (o >= 0x15 && o <= 0x39) || (o >= 0x58 && o <= 0x5F && Base == 0x0900);
    }
    public virtual bool IsVowel(int cp) { int o = cp - Base; return o >= 0x05 && o <= 0x14; }
    public virtual bool IsNukta(int cp) => cp - Base == 0x3C;
    public virtual bool IsVirama(int cp) => cp - Base == 0x4D;
    public virtual bool IsMatra(int cp) { int o = cp - Base; return o >= 0x3E && o <= 0x4C; }
    public virtual bool IsModifier(int cp) { int o = cp - Base; return o >= 0x00 && o <= 0x03; }
    public bool IsPreBaseMatra(int cp)
    {
        foreach (var m in PreBaseMatras) if (m == cp) return true;
        return false;
    }
}

/// <summary>Sinhala does not follow the ISCII block layout the other Indic scripts share.</summary>
internal sealed class SinhalaScriptInfo : IndicScriptInfo
{
    public override bool IsConsonant(int cp) => cp >= 0x0D9A && cp <= 0x0DC6;
    public override bool IsVowel(int cp) => cp >= 0x0D85 && cp <= 0x0D96;
    public override bool IsNukta(int cp) => false;
    public override bool IsVirama(int cp) => cp == 0x0DCA;
    public override bool IsMatra(int cp)
        => (cp >= 0x0DCF && cp <= 0x0DD4) || cp == 0x0DD6 || (cp >= 0x0DD8 && cp <= 0x0DDF) || cp == 0x0DF2 || cp == 0x0DF3;
    public override bool IsModifier(int cp) => cp == 0x0D81 || cp == 0x0D82 || cp == 0x0D83;
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
        HasReph = true, RephPos = RephPosition.AfterSub, Ra = 0x09B0, YaPostBase = 0x09AF,
    };
    public static readonly IndicScriptInfo Gurmukhi = new()
    {
        Base = 0x0A00, Tags = new[] { "gur2", "guru" }, PreBaseMatras = new[] { 0x0A3F },
        HasReph = true, RephPos = RephPosition.BeforeSub, Ra = 0x0A30,
    };
    public static readonly IndicScriptInfo Gujarati = new()
    {
        Base = 0x0A80, Tags = new[] { "gjr2", "gujr" }, HasReph = true, Ra = 0x0AB0,
    };
    public static readonly IndicScriptInfo Oriya = new()
    {
        Base = 0x0B00, Tags = new[] { "ory2", "orya" }, PreBaseMatras = new[] { 0x0B47 },
        HasReph = true, RephPos = RephPosition.AfterMain, Ra = 0x0B30,
    };
    public static readonly IndicScriptInfo Tamil = new()
    {
        Base = 0x0B80, Tags = new[] { "tml2", "taml" }, PreBaseMatras = new[] { 0x0BC6, 0x0BC7, 0x0BC8 },
        HasReph = true, RephPos = RephPosition.AfterPost, Ra = 0x0BB0,
    };
    public static readonly IndicScriptInfo Telugu = new()
    {
        Base = 0x0C00, Tags = new[] { "tel2", "telu" },
        HasReph = true, RephPos = RephPosition.AfterPost, RephNeedsZwj = true, Ra = 0x0C30,
    };
    public static readonly IndicScriptInfo Kannada = new()
    {
        Base = 0x0C80, Tags = new[] { "knd2", "knda" },
        HasReph = true, RephPos = RephPosition.AfterPost, Ra = 0x0CB0,
    };
    public static readonly IndicScriptInfo Malayalam = new()
    {
        Base = 0x0D00, Tags = new[] { "mlm2", "mlym" }, PreBaseMatras = new[] { 0x0D46, 0x0D47, 0x0D48 },
        HasReph = true, RephPos = RephPosition.AfterMain, RephChar = 0x0D4E,
    };

    public static readonly IndicScriptInfo Sinhala = new SinhalaScriptInfo
    {
        Base = 0x0D80, Tags = new[] { "sinh" }, PreBaseMatras = new[] { 0x0DD9 },
        HasReph = true, RephPos = RephPosition.AfterPost, RephNeedsZwj = true, Ra = 0x0DBB, YaPostBase = 0x0DBA,
    };

    public static IndicScriptInfo? GetIndic(int cp)
    {
        if (cp >= 0x0D80 && cp <= 0x0DFF) return Sinhala;
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

    public static bool IsTibetan(int cp) => cp >= 0x0F00 && cp <= 0x0FFF;
    public static bool IsKhmer(int cp) => (cp >= 0x1780 && cp <= 0x17FF) || (cp >= 0x19E0 && cp <= 0x19FF);
    public static bool IsMyanmar(int cp) => cp >= 0x1000 && cp <= 0x109F;

    public static ScriptKind KindOf(int cp)
    {
        if (cp >= 0x0900 && cp <= 0x0DFF) return ScriptKind.Indic;
        if (IsTibetan(cp)) return ScriptKind.Tibetan;
        if (IsKhmer(cp)) return ScriptKind.Khmer;
        if (IsMyanmar(cp)) return ScriptKind.Myanmar;
        if (IsThaiOrLao(cp)) return ScriptKind.Thai;
        if ((cp >= 0x0600 && cp <= 0x06FF) || (cp >= 0xFB50 && cp <= 0xFDFF) || (cp >= 0xFE70 && cp <= 0xFEFF))
            return ScriptKind.Arabic;
        if (cp <= 0x0040 || (cp >= 0x005B && cp <= 0x0060) || (cp >= 0x007B && cp <= 0x00BF) ||
            (cp >= 0x2000 && cp <= 0x206F) || cp == 0x200C || cp == 0x200D)
            return ScriptKind.Common;
        return ScriptKind.Latin;
    }

    public static bool IsArabicLetterBlock(int c)
        => (c >= 0x0600 && c <= 0x06FF) || (c >= 0xFB50 && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF);

    /// <summary>
    /// True when the text must go through the glyph-level shaper: any Arabic, Indic, Thai, Lao,
    /// Tibetan, Khmer or Myanmar character.
    /// </summary>
    public static bool NeedsShaping(string? text)
    {
        if (text == null) return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < '؀') continue;
            if ((c >= 'ऀ' && c <= '෿') || (c >= '฀' && c <= '໿') ||
                (c >= 'ༀ' && c <= '႟') || (c >= 'ក' && c <= '៿')) return true;
            if (IsArabicLetterBlock(c)) return true;
        }
        return false;
    }
}
