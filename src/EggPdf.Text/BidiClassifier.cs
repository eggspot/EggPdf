using System.Globalization;

namespace EggPdf.Text;

/// <summary>Bidi_Class values from UAX #9 table 4.</summary>
public enum BidiType
{
    L, R, AL,
    EN, ES, ET, AN, CS, NSM, BN,
    B, S, WS, ON,
    LRE, LRO, RLE, RLO, PDF,
    LRI, RLI, FSI, PDI,
}

/// <summary>
/// Character to Bidi_Class mapping, built from Unicode general categories plus the script blocks
/// and punctuation ranges where the class differs from what the category implies. The BCL carries
/// no Bidi_Class table, so this is a compact approximation of the UCD data: exact for ASCII,
/// Latin-1, General Punctuation, Hebrew, Arabic (incl. presentation forms), Syriac, Thaana, NKo,
/// the fullwidth forms, and category-derived (letters L, marks NSM, symbols/punctuation ON,
/// currency ET) everywhere else. Code points are written numerically on purpose: invisible
/// directional formatting characters must never appear literally in source.
/// </summary>
internal static class BidiClassifier
{
    /// <summary>Class of a BMP code unit.</summary>
    public static BidiType Classify(char c)
    {
        if (c < 0x80) return Ascii(c);
        if (c < 0x100) return Latin1(c);

        // Fullwidth ASCII variants classify like the ASCII characters they mirror
        if (c >= 0xFF01 && c <= 0xFF5E) return Ascii((char)(c - 0xFEE0));

        if (c >= 0x2000 && c <= 0x2BFF) return GeneralPunctuationAndSymbols(c);

        if (c >= 0x0590 && c <= 0x08FF) return RtlBlocks(c);
        if (c >= 0xFB1D && c <= 0xFDFF) return PresentationFormsA(c);
        if (c >= 0xFE70 && c <= 0xFEFF) return c == 0xFEFF ? BidiType.BN : BidiType.AL;

        return FromCategory(c);
    }

    /// <summary>Class of the supplementary-plane code point starting at <paramref name="index"/> (ON unless in an RTL script block).</summary>
    public static BidiType ClassifySupplementary(string text, int index)
    {
        int cp = char.ConvertToUtf32(text[index], text[index + 1]);
        if ((cp >= 0x10800 && cp <= 0x10CFF) || (cp >= 0x10E80 && cp <= 0x10FFF)) return BidiType.R;
        if (cp >= 0x1E800 && cp <= 0x1EFFF) return BidiType.R;   // Mende Kikakui, Adlam, Arabic math
        if (cp >= 0x10D00 && cp <= 0x10D3F) return BidiType.AL;  // Hanifi Rohingya
        if (cp >= 0x1D7CE && cp <= 0x1D7FF) return BidiType.EN;  // mathematical digits
        if (cp >= 0xE0001 && cp <= 0xE007F) return BidiType.BN;  // tags

        switch (char.GetUnicodeCategory(text, index))
        {
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.EnclosingMark: return BidiType.NSM;
            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.OtherLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.SpacingCombiningMark:
            case UnicodeCategory.DecimalDigitNumber:
            case UnicodeCategory.LetterNumber: return BidiType.L;
            default: return BidiType.ON; // emoji, symbols, private use
        }
    }

    private static BidiType Ascii(char c)
    {
        switch ((int)c)
        {
            case 0x09: case 0x0B: case 0x1F: return BidiType.S;
            case 0x0A: case 0x0D: case 0x1C: case 0x1D: case 0x1E: return BidiType.B;
            case 0x0C: case 0x20: return BidiType.WS;
            case 0x23: case 0x24: case 0x25: return BidiType.ET;     // # $ %
            case 0x2B: case 0x2D: return BidiType.ES;                // + -
            case 0x2C: case 0x2E: case 0x2F: case 0x3A: return BidiType.CS; // , . / :
        }
        if (c <= 0x1B || c == 0x7F) return BidiType.BN;
        if (c >= '0' && c <= '9') return BidiType.EN;
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return BidiType.L;
        return BidiType.ON;
    }

    private static BidiType Latin1(char c)
    {
        if (c <= 0x84 || (c >= 0x86 && c <= 0x9F) || c == 0xAD) return BidiType.BN;
        switch ((int)c)
        {
            case 0x85: return BidiType.B;
            case 0xA0: return BidiType.CS;                                   // no-break space
            case 0xA2: case 0xA3: case 0xA4: case 0xA5: case 0xB0: case 0xB1: return BidiType.ET;
            case 0xB2: case 0xB3: case 0xB9: return BidiType.EN;             // superscript digits
            case 0xAA: case 0xB5: case 0xBA: return BidiType.L;
        }
        if (c >= 0xC0 && c != 0xD7 && c != 0xF7) return BidiType.L;
        return BidiType.ON;
    }

    private static BidiType GeneralPunctuationAndSymbols(char c)
    {
        if (c >= 0x2000 && c <= 0x200A) return BidiType.WS;
        switch ((int)c)
        {
            case 0x200B: case 0x200C: case 0x200D: return BidiType.BN;
            case 0x200E: return BidiType.L;                                  // LRM
            case 0x200F: return BidiType.R;                                  // RLM
            case 0x2028: return BidiType.WS;
            case 0x2029: return BidiType.B;
            case 0x202A: return BidiType.LRE;
            case 0x202B: return BidiType.RLE;
            case 0x202C: return BidiType.PDF;
            case 0x202D: return BidiType.LRO;
            case 0x202E: return BidiType.RLO;
            case 0x202F: return BidiType.CS;
            case 0x205F: return BidiType.WS;
            case 0x2066: return BidiType.LRI;
            case 0x2067: return BidiType.RLI;
            case 0x2068: return BidiType.FSI;
            case 0x2069: return BidiType.PDI;
            case 0x2044: return BidiType.CS;                                 // fraction slash
            case 0x207A: case 0x207B: case 0x208A: case 0x208B: return BidiType.ES;
            case 0x2212: return BidiType.ES;                                 // minus sign
            case 0x2213: return BidiType.ET;
        }
        if (c >= 0x2030 && c <= 0x2034) return BidiType.ET;                  // per mille, prime
        if (c >= 0x2060 && c <= 0x2064) return BidiType.BN;
        if (c >= 0x206A && c <= 0x206F) return BidiType.BN;
        if (c == 0x2070 || (c >= 0x2074 && c <= 0x2079) || (c >= 0x2080 && c <= 0x2089)) return BidiType.EN;
        if (c >= 0x20A0 && c <= 0x20CF) return BidiType.ET;                  // currency symbols
        if (c >= 0x2488 && c <= 0x249B) return BidiType.EN;                  // digit full stop
        return FromCategory(c);
    }

    /// <summary>Hebrew, Arabic, Syriac, Thaana, NKo, Samaritan, Mandaic and the Arabic supplements/extended blocks.</summary>
    private static BidiType RtlBlocks(char c)
    {
        var cat = CharUnicodeInfo.GetUnicodeCategory(c);
        if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.EnclosingMark) return BidiType.NSM;

        // Arabic block specifics: numbers, separators and symbols inside the letter range
        if (c >= 0x0600 && c <= 0x06FF)
        {
            if (c <= 0x0605 || (c >= 0x0660 && c <= 0x0669) || c == 0x066B || c == 0x066C || c == 0x06DD) return BidiType.AN;
            if (c >= 0x06F0 && c <= 0x06F9) return BidiType.EN;
            if (c == 0x0609 || c == 0x060A || c == 0x066A) return BidiType.ET;
            if (c == 0x060C) return BidiType.CS;
            if (c == 0x0606 || c == 0x0607 || c == 0x060E || c == 0x060F || c == 0x06DE || c == 0x06E9) return BidiType.ON;
            return BidiType.AL;
        }
        if (c == 0x08E2) return BidiType.AN;

        // Hebrew (0590-05FF), NKo (07C0-07FF), Samaritan (0800-083F), Mandaic (0840-085F) are R;
        // Arabic-script blocks and Syriac/Thaana (0700-07BF) and 0860-08FF are AL.
        if (c < 0x0600) return BidiType.R;
        if (c >= 0x07C0 && c <= 0x085F)
            return (c >= 0x07F6 && c <= 0x07F9) ? BidiType.ON : BidiType.R;
        return BidiType.AL;
    }

    private static BidiType PresentationFormsA(char c)
    {
        if (c >= 0xFB1D && c <= 0xFB4F)
        {
            if (c == 0xFB1E) return BidiType.NSM;
            if (c == 0xFB29) return BidiType.ES;
            return BidiType.R;
        }
        if (c == 0xFD3E || c == 0xFD3F || c == 0xFDFD) return BidiType.ON;
        if (c >= 0xFDD0 && c <= 0xFDEF) return BidiType.BN; // noncharacters
        return BidiType.AL;
    }

    private static BidiType FromCategory(char c)
    {
        switch (CharUnicodeInfo.GetUnicodeCategory(c))
        {
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.EnclosingMark: return BidiType.NSM;
            case UnicodeCategory.Format:
            case UnicodeCategory.Control: return BidiType.BN;
            case UnicodeCategory.SpaceSeparator: return BidiType.WS;
            case UnicodeCategory.LineSeparator: return BidiType.WS;
            case UnicodeCategory.ParagraphSeparator: return BidiType.B;
            case UnicodeCategory.CurrencySymbol: return BidiType.ET;
            case UnicodeCategory.ConnectorPunctuation:
            case UnicodeCategory.DashPunctuation:
            case UnicodeCategory.OpenPunctuation:
            case UnicodeCategory.ClosePunctuation:
            case UnicodeCategory.InitialQuotePunctuation:
            case UnicodeCategory.FinalQuotePunctuation:
            case UnicodeCategory.OtherPunctuation:
            case UnicodeCategory.MathSymbol:
            case UnicodeCategory.ModifierSymbol:
            case UnicodeCategory.OtherSymbol:
            case UnicodeCategory.OtherNumber:
            case UnicodeCategory.OtherNotAssigned: return BidiType.ON;
            case UnicodeCategory.PrivateUse:
            case UnicodeCategory.Surrogate: return BidiType.L;
            default: return BidiType.L; // letters, spacing marks, decimal digits of other scripts, letter numbers
        }
    }
}
