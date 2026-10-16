using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using EggPdf.Text.TrueType;

namespace EggPdf.Text.Fallback;

/// <summary>A fallback font face and the code points it was chosen to supply.</summary>
public sealed class FallbackFont
{
    internal FallbackFont(FontData font, string path, int faceIndex, IReadOnlyList<int> codepoints)
    {
        Font = font;
        Path = path;
        FaceIndex = faceIndex;
        Codepoints = codepoints;
    }

    /// <summary>The parsed font face (for a collection, a standalone copy of the chosen face).</summary>
    public FontData Font { get; }

    /// <summary>The installed font file the face came from.</summary>
    public string Path { get; }

    /// <summary>Face index inside a TrueType Collection (0 for a single font).</summary>
    public int FaceIndex { get; }

    /// <summary>The requested code points this font covers.</summary>
    public IReadOnlyList<int> Codepoints { get; }
}

/// <summary>Outcome of resolving fallback fonts for a set of code points.</summary>
public sealed class FontFallbackResult
{
    internal FontFallbackResult(IReadOnlyList<FallbackFont> fonts, IReadOnlyList<int> uncovered)
    {
        Fonts = fonts;
        Uncovered = uncovered;
    }

    /// <summary>Fonts to embed, each with the code points it supplies; every requested covered code point appears in exactly one.</summary>
    public IReadOnlyList<FallbackFont> Fonts { get; }

    /// <summary>Visible code points that no installed font covers (they will paint as the missing-glyph box).</summary>
    public IReadOnlyList<int> Uncovered { get; }
}

/// <summary>
/// Chooses installed fonts for characters the document's own font cannot draw — the equivalent of a
/// browser's per-character font fallback. The decision is driven by coverage, never by a hard-coded
/// language: for each code point it walks the <see cref="FontFallbackCatalog"/> candidates for the
/// code point's script, then the universal candidates, then every installed font (largest coverage
/// first), and takes the first face whose cmap actually maps the code point. The result for a code
/// point depends only on the code point, the weight/style request and the Han variant, so layout
/// measurement and PDF embedding always agree. Coverage is read from the cmap table alone and cached
/// for the process lifetime (like <see cref="SystemFontLocator"/>'s file list).
/// </summary>
public sealed class FontFallbackResolver
{
    private static readonly Lazy<FontFallbackResolver> DefaultInstance = new Lazy<FontFallbackResolver>(
        () => new FontFallbackResolver(() => SystemFontLocator.GetAllFontFiles(), FontFallbackCatalog.Default));

    /// <summary>The resolver over the system's installed fonts and <see cref="FontFallbackCatalog.Default"/>.</summary>
    public static FontFallbackResolver Default => DefaultInstance.Value;

    private sealed class Face
    {
        public Face(string path, int index, FontCoverage coverage) { Path = path; Index = index; Coverage = coverage; Id = path + "|" + index; }
        public string Id { get; }
        public string Path { get; }
        public int Index { get; }
        public FontCoverage Coverage { get; }
    }

    private readonly Func<IReadOnlyList<string>> _fontFiles;
    private readonly FontFallbackCatalog _catalog;

    private readonly ConcurrentDictionary<string, FontCoverage?[]> _coverage = new ConcurrentDictionary<string, FontCoverage?[]>(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string?> _located = new ConcurrentDictionary<string, string?>(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, Face?> _decisions = new ConcurrentDictionary<long, Face?>();
    private readonly ConcurrentDictionary<int, string[]> _candidates = new ConcurrentDictionary<int, string[]>();
    private readonly ConcurrentDictionary<string, FontData?> _fonts = new ConcurrentDictionary<string, FontData?>(StringComparer.Ordinal);
    private readonly object _scanLock = new object();
    private List<Face>? _scan;
    private int _catalogVersion;

    /// <param name="fontFiles">Provides the installed font files (.ttf/.otf/.ttc paths) to search.</param>
    /// <param name="catalog">Per-script candidate lists.</param>
    public FontFallbackResolver(Func<IReadOnlyList<string>> fontFiles, FontFallbackCatalog catalog)
    {
        _fontFiles = fontFiles;
        _catalog = catalog;
        _catalogVersion = catalog.Version;
    }

    /// <summary>
    /// Assign every visible code point to an installed font that covers it. Code points no font covers
    /// are returned in <see cref="FontFallbackResult.Uncovered"/> so the caller can warn instead of
    /// silently painting missing-glyph boxes.
    /// </summary>
    /// <param name="bold">Prefer bold faces where a candidate has one (otherwise the regular face is used).</param>
    /// <param name="italic">Prefer italic faces where a candidate has one.</param>
    /// <param name="han">Regional preference for ideographs (see <see cref="HanVariant"/>).</param>
    public FontFallbackResult Resolve(IEnumerable<int> codepoints, bool bold = false, bool italic = false, HanVariant han = HanVariant.Auto)
    {
        var sorted = new List<int>();
        var seen = new HashSet<int>();
        foreach (var cp in codepoints)
            if (!UnicodeScripts.IsInvisible(cp) && seen.Add(cp)) sorted.Add(cp);
        sorted.Sort();

        // Decisions return a fresh Face per (code point), so group by the face's identity (file + index)
        var order = new List<Face>();
        var byFace = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var uncovered = new List<int>();
        foreach (var cp in sorted)
        {
            var face = Decide(cp, bold, italic, han);
            if (face == null) { uncovered.Add(cp); continue; }
            if (!byFace.TryGetValue(face.Id, out var list))
            {
                byFace[face.Id] = list = new List<int>();
                order.Add(face);
            }
            list.Add(cp);
        }

        var fonts = new List<FallbackFont>(order.Count);
        foreach (var face in order)
        {
            var font = LoadFont(face);
            if (font == null) uncovered.AddRange(byFace[face.Id]);
            else fonts.Add(new FallbackFont(font, face.Path, face.Index, byFace[face.Id]));
        }
        uncovered.Sort();
        return new FontFallbackResult(fonts, uncovered);
    }

    /// <summary>The font that would supply one code point, or null when no installed font covers it.</summary>
    public FontData? FindFont(int codepoint, bool bold = false, bool italic = false, HanVariant han = HanVariant.Auto)
    {
        var face = Decide(codepoint, bold, italic, han, requireBasicLatin: false);
        return face == null ? null : LoadFont(face);
    }

    /// <summary>
    /// A font that covers <paramref name="codepoint"/> and also basic Latin letters and digits, or null when
    /// no installed font does. For text shaped as a whole with one font (Thai, Indic, Arabic with marks), a
    /// script-only font would turn every Latin letter in the same run — "Invoice नमस्ते" — into a missing glyph.
    /// </summary>
    public FontData? FindFontWithBasicLatin(int codepoint, bool bold = false, bool italic = false, HanVariant han = HanVariant.Auto)
    {
        var face = Decide(codepoint, bold, italic, han, requireBasicLatin: true);
        return face == null ? null : LoadFont(face);
    }

    /// <summary>The advance (in em) of the glyph the fallback font supplies for a code point, for layout measurement.</summary>
    public bool TryGetAdvanceEm(int codepoint, bool bold, bool italic, HanVariant han, out float advanceEm)
    {
        advanceEm = 0;
        var font = FindFont(codepoint, bold, italic, han);
        if (font == null || font.UnitsPerEm <= 0) return false;
        var gid = font.GetGlyphId(codepoint);
        if (gid == 0) return false;
        advanceEm = font.GetAdvanceWidth(gid) / (float)font.UnitsPerEm;
        return true;
    }

    // ── decision ────────────────────────────────────────────────────────────

    private Face? Decide(int cp, bool bold, bool italic, HanVariant han, bool requireBasicLatin = false)
    {
        if (_catalogVersion != _catalog.Version)
        {
            // The candidate lists changed (FontFallbackCatalog.Register): earlier decisions may be stale
            _decisions.Clear();
            _candidates.Clear();
            _catalogVersion = _catalog.Version;
        }

        long key = ((long)cp << 9) | (uint)((bold ? 1 : 0) | (italic ? 2 : 0) | ((int)han << 2) | (requireBasicLatin ? 256 : 0));
        if (_decisions.TryGetValue(key, out var cached)) return cached;

        var script = UnicodeScripts.Of(cp);
        var face = Find(CandidatesFor(script, han), cp, bold, italic, requireBasicLatin)
                   ?? Find(CandidatesFor(UnicodeScript.Other, han), cp, bold, italic, requireBasicLatin)
                   ?? FindInInstalledFonts(cp, requireBasicLatin);
        _decisions[key] = face;
        return face;
    }

    private static bool CoversBasicLatin(FontCoverage coverage)
    {
        for (int c = '0'; c <= '9'; c++) if (!coverage.Contains(c)) return false;
        for (int c = 'A'; c <= 'Z'; c++) if (!coverage.Contains(c)) return false;
        for (int c = 'a'; c <= 'z'; c++) if (!coverage.Contains(c)) return false;
        return true;
    }

    private string[] CandidatesFor(UnicodeScript script, HanVariant han)
    {
        return _candidates.GetOrAdd(((int)script << 4) | (int)han, _ =>
        {
            if (script == UnicodeScript.Other)
            {
                var universal = _catalog.GetUniversalCandidates();
                var copy = new string[universal.Count];
                for (int i = 0; i < copy.Length; i++) copy[i] = universal[i];
                return copy;
            }
            var list = _catalog.GetCandidates(script, han);
            var result = new string[list.Count];
            for (int i = 0; i < result.Length; i++) result[i] = list[i];
            return result;
        });
    }

    private Face? Find(string[] names, int cp, bool bold, bool italic, bool requireBasicLatin)
    {
        foreach (var name in names)
        {
            var path = Locate(name, bold, italic);
            if (path == null) continue;

            var faces = CoverageOf(path);
            for (int i = 0; i < faces.Length; i++)
            {
                var coverage = faces[i];
                if (coverage != null && coverage.HasOutlines && coverage.Contains(cp) && (!requireBasicLatin || CoversBasicLatin(coverage)))
                    return new Face(path, i, coverage);
            }
        }
        return null;
    }

    private Face? FindInInstalledFonts(int cp, bool requireBasicLatin)
    {
        foreach (var face in InstalledFaces())
            if (face.Coverage.Contains(cp) && (!requireBasicLatin || CoversBasicLatin(face.Coverage))) return face;
        return null;
    }

    /// <summary>Every drawable face of every installed font, broadest coverage first (built once, from cmap tables only).</summary>
    private List<Face> InstalledFaces()
    {
        var scan = _scan;
        if (scan != null) return scan;
        lock (_scanLock)
        {
            if (_scan != null) return _scan;
            var faces = new List<Face>();
            foreach (var path in _fontFiles())
            {
                var coverage = CoverageOf(path);
                for (int i = 0; i < coverage.Length; i++)
                    if (coverage[i] != null && coverage[i]!.HasOutlines) faces.Add(new Face(path, i, coverage[i]!));
            }
            faces.Sort((a, b) =>
            {
                int byCount = b.Coverage.Count.CompareTo(a.Coverage.Count);
                if (byCount != 0) return byCount;
                int byPath = string.CompareOrdinal(a.Path, b.Path);
                return byPath != 0 ? byPath : a.Index.CompareTo(b.Index);
            });
            _scan = faces;
            return faces;
        }
    }

    private FontCoverage?[] CoverageOf(string path)
        => _coverage.GetOrAdd(path, p => FontCoverageReader.ReadFaces(p));

    /// <summary>Find the installed file for a candidate name, preferring the requested bold/italic variant.</summary>
    private string? Locate(string name, bool bold, bool italic)
    {
        return _located.GetOrAdd(name + "|" + (bold ? "B" : "") + (italic ? "I" : ""), _ =>
        {
            var files = _fontFiles();
            if (bold || italic)
            {
                foreach (var variant in FontResolver.VariantNames(name, bold, italic))
                {
                    var found = SystemFontLocator.FindFontIn(files, variant);
                    if (found != null) return found;
                }
            }
            return SystemFontLocator.FindFontIn(files, name);
        });
    }

    private FontData? LoadFont(Face face)
    {
        return _fonts.GetOrAdd(face.Id, _ =>
        {
            try
            {
                var bytes = File.ReadAllBytes(face.Path);
                if (face.Index > 0)
                {
                    bytes = FontCollection.ExtractFace(bytes, face.Index);
                    if (bytes == null) return null;
                }
                return TtfParser.Parse(bytes);
            }
            catch
            {
                return null;
            }
        });
    }
}
