using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace EggPdf.Text.Fallback;

/// <summary>Which regional Han/CJK font family to prefer when ideographs are unmarked (glyph shapes differ per region).</summary>
public enum HanVariant
{
    /// <summary>No preference known: Simplified Chinese fonts first (the most widely installed).</summary>
    Auto,
    /// <summary>Japanese glyph shapes (<c>lang="ja"</c>, or kana in the text).</summary>
    Japanese,
    /// <summary>Korean glyph shapes (<c>lang="ko"</c>, or Hangul in the text).</summary>
    Korean,
    /// <summary>Simplified Chinese glyph shapes (<c>lang="zh"</c>, <c>zh-Hans</c>, <c>zh-CN</c>).</summary>
    SimplifiedChinese,
    /// <summary>Traditional Chinese glyph shapes (<c>zh-Hant</c>, <c>zh-TW</c>, <c>zh-HK</c>).</summary>
    TraditionalChinese,
}

/// <summary>
/// Ordered, per-platform font names to try (by file name or family name, matched by
/// <see cref="SystemFontLocator"/>) when a document's own font lacks a script's glyphs. Lists are
/// data, not logic: the resolver accepts a candidate only if its cmap actually covers the code point,
/// so a missing or wrong name costs nothing, and anything not listed is still found by the resolver's
/// full scan of installed fonts. Applications can put their own fonts first with <see cref="Register"/>.
/// </summary>
public sealed class FontFallbackCatalog
{
    /// <summary>The catalog the renderer uses.</summary>
    public static FontFallbackCatalog Default { get; } = new FontFallbackCatalog();

    private readonly object _lock = new object();
    private Dictionary<UnicodeScript, string[]> _custom = new Dictionary<UnicodeScript, string[]>();
    private string[] _customUniversal = Array.Empty<string>();
    private int _version;

    private readonly Dictionary<UnicodeScript, string[]> _builtIn;
    private readonly string[] _universal;
    private readonly string[] _japanese, _korean, _simplified, _traditional, _panCjk;

    /// <summary>Incremented whenever <see cref="Register"/> changes the lists, so resolvers can drop cached decisions.</summary>
    internal int Version => _version;

    internal FontFallbackCatalog()
    {
        var platform = Platform();
        _builtIn = new Dictionary<UnicodeScript, string[]>();

        // CJK pools: platform fonts first, then the Noto/Source Han/open-source families common on Linux
        _japanese = Concat(platform.Japanese, L("NotoSansCJKjp", "NotoSerifCJKjp", "NotoSansJP", "SourceHanSans", "IPAexGothic", "IPAGothic", "ipag",
            "TakaoGothic", "VL-Gothic", "kochi-gothic", "Sazanami"));
        _korean = Concat(platform.Korean, L("NotoSansCJKkr", "NotoSerifCJKkr", "NotoSansKR", "NanumGothic", "NanumBarunGothic", "UnDotum",
            "Baekmuk", "UnBatang"));
        _simplified = Concat(platform.Simplified, L("NotoSansCJKsc", "NotoSerifCJKsc", "NotoSansSC", "WenQuanYi", "wqy-zenhei", "wqy-microhei",
            "uming", "ukai", "ZenHei"));
        _traditional = Concat(platform.Traditional, L("NotoSansCJKtc", "NotoSansCJKhk", "NotoSansTC", "NotoSansHK", "uming", "ukai"));
        _panCjk = Concat(platform.PanCjk, L("NotoSansCJK", "NotoSerifCJK", "SourceHanSans", "SourceHanSerif", "DroidSansFallbackFull",
            "DroidSansFallback", "wqy-zenhei", "unifont"));

        // Latin-derived scripts share one list: any broad Latin/Greek/Cyrillic text font will do
        var latinLike = Concat(platform.Latin, L("NotoSans", "DejaVuSans", "LiberationSans", "FreeSans", "NotoSerif", "DejaVuSerif", "LiberationSerif",
            "FreeSerif", "Roboto"));
        foreach (var script in new[] { UnicodeScript.Common, UnicodeScript.Latin, UnicodeScript.Greek, UnicodeScript.Cyrillic })
            _builtIn[script] = latinLike;

        foreach (var kv in platform.Scripts)
            _builtIn[kv.Key] = Concat(kv.Value, NotoFor(kv.Key));
        foreach (UnicodeScript script in Enum.GetValues(typeof(UnicodeScript)))
        {
            if (!_builtIn.ContainsKey(script) && NotoFor(script).Length > 0)
                _builtIn[script] = NotoFor(script);
        }

        _builtIn[UnicodeScript.Symbols] = Concat(platform.Symbols, L("NotoSansSymbols", "NotoSansSymbols2", "DejaVuSans", "Symbola", "FreeSerif", "FreeSans", "unifont"));
        _builtIn[UnicodeScript.Math] = Concat(platform.Math, L("NotoSansMath", "STIXTwoMath", "STIXGeneral", "XITSMath", "LatinModernMath", "DejaVuSans", "Symbola"));
        _builtIn[UnicodeScript.Emoji] = Concat(platform.Emoji, L("TwemojiMozilla", "Twemoji", "NotoEmoji", "EmojiOne", "Symbola", "NotoSansSymbols2", "DejaVuSans"));
        _builtIn[UnicodeScript.Braille] = Concat(platform.Braille, L("NotoSansSymbols2", "DejaVuSans", "FreeSerif", "unifont"));

        _universal = Concat(platform.Universal, L("NotoSans", "DejaVuSans", "FreeSans", "FreeSerif", "NotoSansSymbols2", "NotoSansSymbols", "Symbola",
            "unifont", "Code2000", "LiberationSans"));
    }

    /// <summary>
    /// Put <paramref name="families"/> (font file or family names) ahead of the built-in candidates for a
    /// script — e.g. a corporate Korean font installed in a non-standard directory under a known name.
    /// Takes effect for subsequent renders; names that match no installed font are ignored.
    /// </summary>
    public void Register(UnicodeScript script, params string[] families)
    {
        if (families == null || families.Length == 0) return;
        lock (_lock)
        {
            var next = new Dictionary<UnicodeScript, string[]>(_custom);
            next[script] = next.TryGetValue(script, out var existing) ? Concat(families, existing) : families;
            _custom = next;
            _version++;
        }
    }

    /// <summary>Like <see cref="Register"/> but for the universal list tried for every script before the full font scan.</summary>
    public void RegisterUniversal(params string[] families)
    {
        if (families == null || families.Length == 0) return;
        lock (_lock)
        {
            _customUniversal = Concat(families, _customUniversal);
            _version++;
        }
    }

    /// <summary>Candidate names for a script in priority order: registered, platform, then cross-platform (Noto, DejaVu, ...).</summary>
    public IReadOnlyList<string> GetCandidates(UnicodeScript script, HanVariant variant = HanVariant.Auto)
    {
        string[] custom;
        lock (_lock) custom = _custom.TryGetValue(script, out var c) ? c : Array.Empty<string>();

        string[] builtIn;
        switch (script)
        {
            case UnicodeScript.Han:
                builtIn = variant == HanVariant.Japanese ? Concat(_japanese, _panCjk, _simplified, _traditional, _korean)
                        : variant == HanVariant.Korean ? Concat(_korean, _panCjk, _simplified, _traditional, _japanese)
                        : variant == HanVariant.TraditionalChinese ? Concat(_traditional, _panCjk, _simplified, _japanese, _korean)
                        : Concat(_simplified, _panCjk, _traditional, _japanese, _korean);
                break;
            case UnicodeScript.Kana:
                builtIn = Concat(_japanese, _panCjk, _simplified, _traditional);
                break;
            case UnicodeScript.Hangul:
                builtIn = Concat(_korean, _panCjk, _japanese, _simplified);
                break;
            case UnicodeScript.Bopomofo:
                builtIn = Concat(_traditional, _panCjk, _simplified);
                break;
            default:
                builtIn = _builtIn.TryGetValue(script, out var list) ? list : Array.Empty<string>();
                break;
        }
        return Concat(custom, builtIn);
    }

    /// <summary>Candidates tried for every script (after its own list) before scanning all installed fonts.</summary>
    public IReadOnlyList<string> GetUniversalCandidates()
    {
        string[] custom;
        lock (_lock) custom = _customUniversal;
        return Concat(custom, _universal);
    }

    /// <summary>A font worth installing for a script, for diagnostics ("Noto Sans CJK", "Noto Sans Arabic", ...).</summary>
    public static string InstallHint(UnicodeScript script)
    {
        switch (script)
        {
            case UnicodeScript.Han: case UnicodeScript.Kana: case UnicodeScript.Hangul: case UnicodeScript.Bopomofo:
                return "Noto Sans CJK";
            case UnicodeScript.Emoji: return "Twemoji (COLR) or Noto Emoji";
            case UnicodeScript.Symbols: return "Noto Sans Symbols 2";
            case UnicodeScript.Math: return "Noto Sans Math";
            case UnicodeScript.Other: case UnicodeScript.Common: case UnicodeScript.Latin: case UnicodeScript.Greek: case UnicodeScript.Cyrillic:
                return "Noto Sans";
            default:
                return "Noto Sans " + UnicodeScripts.DisplayName(script);
        }
    }

    // ── data ────────────────────────────────────────────────────────────────

    private static string[] L(params string[] names) => names;

    private static string[] Concat(params string[][] lists)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var list in lists)
            foreach (var name in list)
                if (seen.Add(name)) result.Add(name);
        return result.ToArray();
    }

    /// <summary>Noto Sans/Serif file-name stems for a script (the cross-platform baseline, e.g. <c>NotoSansBengali</c>).</summary>
    private static string[] NotoFor(UnicodeScript script)
    {
        switch (script)
        {
            case UnicodeScript.Other: case UnicodeScript.Common: case UnicodeScript.Latin: case UnicodeScript.Greek: case UnicodeScript.Cyrillic:
            case UnicodeScript.Han: case UnicodeScript.Kana: case UnicodeScript.Hangul: case UnicodeScript.Bopomofo:
            case UnicodeScript.Symbols: case UnicodeScript.Math: case UnicodeScript.Emoji: case UnicodeScript.Braille:
                return Array.Empty<string>();
        }
        string name = script.ToString();
        return new[] { "NotoSans" + name, "NotoSerif" + name, "Noto Sans " + UnicodeScripts.DisplayName(script) };
    }

    private sealed class PlatformLists
    {
        public string[] Japanese = Array.Empty<string>(), Korean = Array.Empty<string>(), Simplified = Array.Empty<string>(),
            Traditional = Array.Empty<string>(), PanCjk = Array.Empty<string>();
        public string[] Latin = Array.Empty<string>(), Symbols = Array.Empty<string>(), Math = Array.Empty<string>(),
            Emoji = Array.Empty<string>(), Braille = Array.Empty<string>(), Universal = Array.Empty<string>();
        public Dictionary<UnicodeScript, string[]> Scripts = new Dictionary<UnicodeScript, string[]>();
    }

    private static PlatformLists Platform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Windows();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return MacOS();
        return Linux();
    }

    // Windows file names (C:\Windows\Fonts): matched by stem, so "malgun" finds malgun.ttf, "msyh" finds msyh.ttc
    private static PlatformLists Windows() => new PlatformLists
    {
        Japanese = L("YuGothR", "YuGothM", "meiryo", "msgothic", "msmincho", "BIZ-UDGothicR"),
        Korean = L("malgun", "gulim", "batang", "dotum", "gungsuh"),
        Simplified = L("msyh", "simsun", "simhei", "simkai", "SIMFANG", "Dengl"),
        Traditional = L("msjh", "mingliu", "kaiu"),
        PanCjk = L("ArialUni", "Arial Unicode MS"),
        Latin = L("arial", "segoeui", "tahoma", "times", "verdana", "calibri"),
        Symbols = L("seguisym", "segoeui", "arialuni", "cambria"),
        Math = L("cambria", "seguisym"),
        Emoji = L("seguiemj", "seguisym"),
        Braille = L("seguisym"),
        Universal = L("segoeui", "arial", "tahoma", "seguisym", "ArialUni"),
        Scripts =
        {
            [UnicodeScript.Hebrew] = L("arial", "segoeui", "david", "miriam", "tahoma", "times"),
            [UnicodeScript.Arabic] = L("arial", "segoeui", "tahoma", "times", "trado", "majalla"),
            [UnicodeScript.Syriac] = L("estre", "segoeui"),
            [UnicodeScript.Thaana] = L("mvboli", "segoeui"),
            [UnicodeScript.NKo] = L("ebrima", "segoeui"),
            [UnicodeScript.Armenian] = L("sylfaen", "segoeui", "arial"),
            [UnicodeScript.Georgian] = L("sylfaen", "segoeui", "arial"),
            [UnicodeScript.Devanagari] = L("Nirmala", "mangal", "Aparajita", "utsaah", "kokila"),
            [UnicodeScript.Bengali] = L("Nirmala", "vrinda", "Shonar"),
            [UnicodeScript.Gurmukhi] = L("Nirmala", "raavi"),
            [UnicodeScript.Gujarati] = L("Nirmala", "shruti"),
            [UnicodeScript.Oriya] = L("Nirmala", "kalinga"),
            [UnicodeScript.Tamil] = L("Nirmala", "latha", "vijaya"),
            [UnicodeScript.Telugu] = L("Nirmala", "gautami", "vani"),
            [UnicodeScript.Kannada] = L("Nirmala", "tunga", "kartika"),
            [UnicodeScript.Malayalam] = L("Nirmala", "kartika"),
            [UnicodeScript.Sinhala] = L("Nirmala", "iskpota"),
            [UnicodeScript.Thai] = L("LeelawUI", "leelawad", "tahoma", "angsa", "cordia"),
            [UnicodeScript.Lao] = L("LaoUI", "dokchamp", "LeelawUI"),
            [UnicodeScript.Khmer] = L("khmerui", "DaunPenh", "LeelawUI"),
            [UnicodeScript.Myanmar] = L("mmrtext", "Myanmar Text"),
            [UnicodeScript.Tibetan] = L("himalaya"),
            [UnicodeScript.Ethiopic] = L("nyala", "ebrima"),
            [UnicodeScript.Cherokee] = L("plantc"),
            [UnicodeScript.CanadianAboriginal] = L("euphemia"),
            [UnicodeScript.Mongolian] = L("monbaiti"),
            [UnicodeScript.Yi] = L("msyi"),
        },
    };

    // macOS: /System/Library/Fonts, /System/Library/Fonts/Supplemental, /Library/Fonts
    private static PlatformLists MacOS() => new PlatformLists
    {
        Japanese = L("ヒラギノ角ゴシック W3", "ヒラギノ角ゴ", "Hiragino Sans", "HiraginoSans", "Osaka", "YuGothic"),
        Korean = L("AppleSDGothicNeo", "AppleGothic", "AppleMyungjo"),
        Simplified = L("PingFang", "Hiragino Sans GB", "STHeiti Light", "STHeiti", "Songti", "STSong"),
        Traditional = L("PingFang", "STHeiti", "Songti", "LiGothic", "LiSong Pro", "BiauKai"),
        PanCjk = L("Arial Unicode"),
        Latin = L("Helvetica", "HelveticaNeue", "Arial", "Times New Roman", "Geneva", "Lucida Grande", "Menlo"),
        Symbols = L("Apple Symbols", "Menlo", "ZapfDingbats", "STIXGeneral", "Arial Unicode"),
        Math = L("STIXGeneral", "Apple Symbols", "Cambria Math"),
        Emoji = L("Apple Symbols"),
        Braille = L("Apple Braille"),
        Universal = L("Arial Unicode", "Apple Symbols", "Helvetica", "Lucida Grande", "Menlo"),
        Scripts =
        {
            [UnicodeScript.Hebrew] = L("ArialHB", "Arial Hebrew", "Raanana", "New Peninim MT", "Lucida Grande", "Arial", "Times New Roman"),
            [UnicodeScript.Arabic] = L("GeezaPro", "Geeza Pro", "Al Nile", "Damascus", "Baghdad", "DecoTypeNaskh", "KufiStandardGK", "Arial", "Times New Roman"),
            [UnicodeScript.Armenian] = L("Mshtakan", "Arial Unicode"),
            [UnicodeScript.Georgian] = L("Arial Unicode", "Menlo"),
            [UnicodeScript.Devanagari] = L("Kohinoor", "Kohinoor Devanagari", "KohinoorDevanagari", "DevanagariMT", "Devanagari Sangam MN", "ITFDevanagari"),
            [UnicodeScript.Bengali] = L("KohinoorBangla", "Bangla Sangam MN", "BanglaMN"),
            [UnicodeScript.Gurmukhi] = L("Gurmukhi MN", "Gurmukhi Sangam MN", "GurmukhiMT"),
            [UnicodeScript.Gujarati] = L("Gujarati Sangam MN", "GujaratiMT", "KohinoorGujarati"),
            [UnicodeScript.Oriya] = L("Oriya Sangam MN", "OriyaMN"),
            [UnicodeScript.Tamil] = L("Tamil Sangam MN", "TamilMN", "InaiMathi"),
            [UnicodeScript.Telugu] = L("Telugu Sangam MN", "TeluguMN", "KohinoorTelugu"),
            [UnicodeScript.Kannada] = L("Kannada Sangam MN", "KannadaMN"),
            [UnicodeScript.Malayalam] = L("Malayalam Sangam MN", "MalayalamMN"),
            [UnicodeScript.Sinhala] = L("Sinhala Sangam MN", "SinhalaMN"),
            [UnicodeScript.Thai] = L("Thonburi", "Ayuthaya", "Sathu", "Silom", "Krungthep", "Arial Unicode"),
            [UnicodeScript.Lao] = L("Lao Sangam MN", "LaoMN"),
            [UnicodeScript.Khmer] = L("Khmer Sangam MN", "KhmerMN"),
            [UnicodeScript.Myanmar] = L("Myanmar Sangam MN", "MyanmarMN"),
            [UnicodeScript.Tibetan] = L("Kailasa", "Tibetan Machine Uni"),
            [UnicodeScript.Ethiopic] = L("Kefa"),
            [UnicodeScript.Cherokee] = L("Plantagenet Cherokee"),
            [UnicodeScript.CanadianAboriginal] = L("Euphemia UCAS"),
            [UnicodeScript.Mongolian] = L("Mongolian Baiti", "Arial Unicode"),
            [UnicodeScript.Yi] = L("Arial Unicode"),
        },
    };

    // Linux: the Noto/DejaVu/Liberation baseline is appended for every platform, so this adds the
    // distribution-specific families (fonts-wqy-*, fonts-nanum, fonts-takao, Lohit, TLWG, SIL, ...)
    private static PlatformLists Linux() => new PlatformLists
    {
        Japanese = L("TakaoPGothic", "TakaoGothic", "IPAPGothic", "ipagp"),
        Korean = L("NanumGothic", "NanumMyeongjo", "UnDotum"),
        Simplified = L("wqy-microhei", "wqy-zenhei"),
        Traditional = L("wqy-microhei", "uming"),
        PanCjk = L("DroidSansFallbackFull"),
        Latin = L("LiberationSans", "DejaVuSans", "Arimo", "Cantarell", "Ubuntu"),
        Symbols = L("DejaVuSans", "NotoSansSymbols2"),
        Math = L("DejaVuSans"),
        Emoji = L("NotoEmoji"),
        Braille = L("DejaVuSans"),
        Universal = L("DejaVuSans", "FreeSans"),
        Scripts =
        {
            [UnicodeScript.Hebrew] = L("DejaVuSans", "FreeSans", "Culmus", "SBL_Hbrw", "Ezra", "Frank", "DavidLibre", "Alef"),
            [UnicodeScript.Arabic] = L("DejaVuSans", "KacstOne", "Amiri", "Scheherazade", "FreeSerif", "DroidArabicNaskh"),
            [UnicodeScript.Armenian] = L("DejaVuSans", "FreeSerif"),
            [UnicodeScript.Georgian] = L("DejaVuSans", "FreeSerif"),
            [UnicodeScript.Devanagari] = L("Lohit-Devanagari", "Lohit Devanagari", "Mukta", "Sarai", "Gargi", "Chandas"),
            [UnicodeScript.Bengali] = L("Lohit-Bengali", "Lohit Bengali"),
            [UnicodeScript.Gurmukhi] = L("Lohit-Gurmukhi", "Lohit Gurmukhi"),
            [UnicodeScript.Gujarati] = L("Lohit-Gujarati", "Lohit Gujarati"),
            [UnicodeScript.Oriya] = L("Lohit-Odia", "Lohit-Oriya"),
            [UnicodeScript.Tamil] = L("Lohit-Tamil", "Lohit Tamil"),
            [UnicodeScript.Telugu] = L("Lohit-Telugu", "Lohit Telugu"),
            [UnicodeScript.Kannada] = L("Lohit-Kannada", "Lohit Kannada"),
            [UnicodeScript.Malayalam] = L("Lohit-Malayalam", "Lohit Malayalam"),
            [UnicodeScript.Thai] = L("Garuda", "Loma", "Norasi", "Waree", "Kinnari", "Umpush", "Sarabun", "Tlwg"),
            [UnicodeScript.Khmer] = L("KhmerOS", "Khmer OS"),
            [UnicodeScript.Myanmar] = L("Padauk"),
            [UnicodeScript.Tibetan] = L("Jomolhari", "TibetanMachineUni"),
            [UnicodeScript.Ethiopic] = L("AbyssinicaSIL", "Abyssinica SIL"),
        },
    };
}
