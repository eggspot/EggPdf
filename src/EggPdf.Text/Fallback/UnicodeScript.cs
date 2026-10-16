namespace EggPdf.Text.Fallback;

/// <summary>
/// Coarse Unicode script classes used to pick font-fallback candidates. This is a font-selection
/// aid, not a full UAX #24 implementation: related blocks that the same fonts cover are grouped
/// (e.g. every CJK ideograph block is <see cref="Han"/>), and format/punctuation characters that any
/// font may carry are <see cref="Common"/>.
/// </summary>
#pragma warning disable CS1591 // members named after their script need no further description
public enum UnicodeScript
{
    /// <summary>Code point outside every known block (or Private Use): only the universal list and a full scan can cover it.</summary>
    Other,
    /// <summary>Punctuation, digits, spaces, combining marks and other characters shared by many scripts.</summary>
    Common,
    Latin,
    Greek,
    Cyrillic,
    Armenian,
    Hebrew,
    Arabic,
    Syriac,
    Thaana,
    NKo,
    Devanagari,
    Bengali,
    Gurmukhi,
    Gujarati,
    Oriya,
    Tamil,
    Telugu,
    Kannada,
    Malayalam,
    Sinhala,
    Thai,
    Lao,
    Tibetan,
    Myanmar,
    Georgian,
    Hangul,
    Ethiopic,
    Cherokee,
    CanadianAboriginal,
    Ogham,
    Runic,
    Khmer,
    Mongolian,
    /// <summary>Hiragana and Katakana (including half-width forms).</summary>
    Kana,
    Bopomofo,
    /// <summary>CJK ideographs plus CJK punctuation, enclosed/compatibility forms and full-width forms.</summary>
    Han,
    Yi,
    /// <summary>Arrows, technical, box drawing, geometric shapes, dingbats and similar symbol blocks.</summary>
    Symbols,
    Math,
    Braille,
    /// <summary>Emoji and pictographs (Supplementary Multilingual Plane symbol blocks).</summary>
    Emoji,
}
#pragma warning restore CS1591

/// <summary>Classifies code points into <see cref="UnicodeScript"/> classes.</summary>
public static class UnicodeScripts
{
    // Sorted, non-overlapping [start, end] ranges; a code point in a gap is UnicodeScript.Other.
    private static readonly int[] Starts;
    private static readonly int[] Ends;
    private static readonly UnicodeScript[] Scripts;

    static UnicodeScripts()
    {
        var table = new (int start, int end, UnicodeScript script)[]
        {
            (0x0000, 0x0040, UnicodeScript.Common),
            (0x0041, 0x005A, UnicodeScript.Latin),
            (0x005B, 0x0060, UnicodeScript.Common),
            (0x0061, 0x007A, UnicodeScript.Latin),
            (0x007B, 0x00A9, UnicodeScript.Common),
            (0x00AA, 0x00AA, UnicodeScript.Latin),
            (0x00AB, 0x00B9, UnicodeScript.Common),
            (0x00BA, 0x00BA, UnicodeScript.Latin),
            (0x00BB, 0x00BF, UnicodeScript.Common),
            (0x00C0, 0x02AF, UnicodeScript.Latin),
            (0x02B0, 0x036F, UnicodeScript.Common),
            (0x0370, 0x03FF, UnicodeScript.Greek),
            (0x0400, 0x052F, UnicodeScript.Cyrillic),
            (0x0530, 0x058F, UnicodeScript.Armenian),
            (0x0590, 0x05FF, UnicodeScript.Hebrew),
            (0x0600, 0x06FF, UnicodeScript.Arabic),
            (0x0700, 0x074F, UnicodeScript.Syriac),
            (0x0750, 0x077F, UnicodeScript.Arabic),
            (0x0780, 0x07BF, UnicodeScript.Thaana),
            (0x07C0, 0x07FF, UnicodeScript.NKo),
            (0x0870, 0x08FF, UnicodeScript.Arabic),
            (0x0900, 0x097F, UnicodeScript.Devanagari),
            (0x0980, 0x09FF, UnicodeScript.Bengali),
            (0x0A00, 0x0A7F, UnicodeScript.Gurmukhi),
            (0x0A80, 0x0AFF, UnicodeScript.Gujarati),
            (0x0B00, 0x0B7F, UnicodeScript.Oriya),
            (0x0B80, 0x0BFF, UnicodeScript.Tamil),
            (0x0C00, 0x0C7F, UnicodeScript.Telugu),
            (0x0C80, 0x0CFF, UnicodeScript.Kannada),
            (0x0D00, 0x0D7F, UnicodeScript.Malayalam),
            (0x0D80, 0x0DFF, UnicodeScript.Sinhala),
            (0x0E00, 0x0E7F, UnicodeScript.Thai),
            (0x0E80, 0x0EFF, UnicodeScript.Lao),
            (0x0F00, 0x0FFF, UnicodeScript.Tibetan),
            (0x1000, 0x109F, UnicodeScript.Myanmar),
            (0x10A0, 0x10FF, UnicodeScript.Georgian),
            (0x1100, 0x11FF, UnicodeScript.Hangul),
            (0x1200, 0x139F, UnicodeScript.Ethiopic),
            (0x13A0, 0x13FF, UnicodeScript.Cherokee),
            (0x1400, 0x167F, UnicodeScript.CanadianAboriginal),
            (0x1680, 0x169F, UnicodeScript.Ogham),
            (0x16A0, 0x16FF, UnicodeScript.Runic),
            (0x1780, 0x17FF, UnicodeScript.Khmer),
            (0x1800, 0x18AF, UnicodeScript.Mongolian),
            (0x18B0, 0x18FF, UnicodeScript.CanadianAboriginal),
            (0x19E0, 0x19FF, UnicodeScript.Khmer),
            (0x1C80, 0x1C8F, UnicodeScript.Cyrillic),
            (0x1C90, 0x1CBF, UnicodeScript.Georgian),
            (0x1D00, 0x1DBF, UnicodeScript.Latin),
            (0x1DC0, 0x1DFF, UnicodeScript.Common),
            (0x1E00, 0x1EFF, UnicodeScript.Latin),
            (0x1F00, 0x1FFF, UnicodeScript.Greek),
            (0x2000, 0x218F, UnicodeScript.Common),
            (0x2190, 0x21FF, UnicodeScript.Symbols),
            (0x2200, 0x22FF, UnicodeScript.Math),
            (0x2300, 0x27BF, UnicodeScript.Symbols),
            (0x27C0, 0x27EF, UnicodeScript.Math),
            (0x27F0, 0x27FF, UnicodeScript.Symbols),
            (0x2800, 0x28FF, UnicodeScript.Braille),
            (0x2900, 0x297F, UnicodeScript.Symbols),
            (0x2980, 0x2AFF, UnicodeScript.Math),
            (0x2B00, 0x2BFF, UnicodeScript.Symbols),
            (0x2C60, 0x2C7F, UnicodeScript.Latin),
            (0x2D00, 0x2D2F, UnicodeScript.Georgian),
            (0x2D80, 0x2DDF, UnicodeScript.Ethiopic),
            (0x2DE0, 0x2DFF, UnicodeScript.Cyrillic),
            (0x2E00, 0x2E7F, UnicodeScript.Common),
            (0x2E80, 0x2FFF, UnicodeScript.Han),
            (0x3000, 0x303F, UnicodeScript.Han),
            (0x3040, 0x30FF, UnicodeScript.Kana),
            (0x3100, 0x312F, UnicodeScript.Bopomofo),
            (0x3130, 0x318F, UnicodeScript.Hangul),
            (0x3190, 0x319F, UnicodeScript.Han),
            (0x31A0, 0x31BF, UnicodeScript.Bopomofo),
            (0x31C0, 0x31EF, UnicodeScript.Han),
            (0x31F0, 0x31FF, UnicodeScript.Kana),
            (0x3200, 0x4DBF, UnicodeScript.Han),
            (0x4DC0, 0x4DFF, UnicodeScript.Symbols),
            (0x4E00, 0x9FFF, UnicodeScript.Han),
            (0xA000, 0xA4CF, UnicodeScript.Yi),
            (0xA640, 0xA69F, UnicodeScript.Cyrillic),
            (0xA720, 0xA7FF, UnicodeScript.Latin),
            (0xA8E0, 0xA8FF, UnicodeScript.Devanagari),
            (0xA960, 0xA97F, UnicodeScript.Hangul),
            (0xA9E0, 0xA9FF, UnicodeScript.Myanmar),
            (0xAA60, 0xAA7F, UnicodeScript.Myanmar),
            (0xAB00, 0xAB2F, UnicodeScript.Ethiopic),
            (0xAB30, 0xAB6F, UnicodeScript.Latin),
            (0xAB70, 0xABBF, UnicodeScript.Cherokee),
            (0xAC00, 0xD7FF, UnicodeScript.Hangul),
            (0xF900, 0xFAFF, UnicodeScript.Han),
            (0xFB00, 0xFB12, UnicodeScript.Latin),
            (0xFB13, 0xFB1C, UnicodeScript.Armenian),
            (0xFB1D, 0xFB4F, UnicodeScript.Hebrew),
            (0xFB50, 0xFDFF, UnicodeScript.Arabic),
            (0xFE00, 0xFE0F, UnicodeScript.Common),
            (0xFE10, 0xFE1F, UnicodeScript.Han),
            (0xFE20, 0xFE2F, UnicodeScript.Common),
            (0xFE30, 0xFE6F, UnicodeScript.Han),
            (0xFE70, 0xFEFF, UnicodeScript.Arabic),
            (0xFF00, 0xFF65, UnicodeScript.Han),
            (0xFF66, 0xFF9F, UnicodeScript.Kana),
            (0xFFA0, 0xFFDF, UnicodeScript.Hangul),
            (0xFFE0, 0xFFEF, UnicodeScript.Han),
            (0xFFF0, 0xFFFF, UnicodeScript.Common),
            (0x1B000, 0x1B16F, UnicodeScript.Kana),
            (0x1D400, 0x1D7FF, UnicodeScript.Math),
            (0x1F000, 0x1FAFF, UnicodeScript.Emoji),
            (0x20000, 0x323AF, UnicodeScript.Han),
        };

        Starts = new int[table.Length];
        Ends = new int[table.Length];
        Scripts = new UnicodeScript[table.Length];
        for (int i = 0; i < table.Length; i++)
        {
            Starts[i] = table[i].start;
            Ends[i] = table[i].end;
            Scripts[i] = table[i].script;
        }
    }

    /// <summary>The script class of a code point (<see cref="UnicodeScript.Other"/> when it is in no known block).</summary>
    public static UnicodeScript Of(int codepoint)
    {
        int lo = 0, hi = Starts.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (codepoint < Starts[mid]) hi = mid - 1;
            else if (codepoint > Ends[mid]) lo = mid + 1;
            else return Scripts[mid];
        }
        return UnicodeScript.Other;
    }

    /// <summary>
    /// True for zero-width format characters (joiners, direction marks, variation selectors, BOM, soft
    /// hyphen) that never draw anything: a font lacking them is not a missing-glyph problem and they
    /// occupy no space, so a renderer may drop them.
    /// </summary>
    public static bool IsDefaultIgnorable(int codepoint)
    {
        switch (codepoint)
        {
            case 0x00AD:                                      // soft hyphen
            case 0x034F:                                      // combining grapheme joiner
            case 0x061C:                                      // Arabic letter mark
            case 0x180E:
            case 0xFEFF:                                      // BOM / zero width no-break space
                return true;
        }
        return (codepoint >= 0x200B && codepoint <= 0x200F)   // zero-width space/joiners, direction marks
            || (codepoint >= 0x202A && codepoint <= 0x202E)   // bidi embeddings
            || (codepoint >= 0x2060 && codepoint <= 0x206F)   // word joiner, invisible operators, bidi isolates
            || (codepoint >= 0xFE00 && codepoint <= 0xFE0F)   // variation selectors
            || (codepoint >= 0xE0100 && codepoint <= 0xE01EF);// variation selectors supplement
    }

    /// <summary>
    /// True for characters that show no ink (controls, whitespace, line separators and the
    /// <see cref="IsDefaultIgnorable"/> set): font fallback never needs to find a glyph for them.
    /// </summary>
    public static bool IsInvisible(int codepoint)
    {
        return codepoint <= 0x20
            || (codepoint >= 0x7F && codepoint <= 0xA0)
            || (codepoint >= 0x2000 && codepoint <= 0x200A)   // en/em/thin spaces
            || codepoint == 0x2028 || codepoint == 0x2029
            || IsDefaultIgnorable(codepoint);
    }

    /// <summary>A human-readable script name (e.g. "Canadian Aboriginal") for diagnostics.</summary>
    public static string DisplayName(UnicodeScript script)
    {
        var name = script.ToString();
        var sb = new System.Text.StringBuilder(name.Length + 2);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }
}
