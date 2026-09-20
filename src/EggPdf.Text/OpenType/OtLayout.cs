using System;
using System.Collections.Generic;

namespace EggPdf.Text.OpenType;

/// <summary>One glyph in the shaping buffer, plus the positioning state GPOS builds up.</summary>
internal struct ShapedGlyph
{
    public ushort Id;
    /// <summary>UTF-16 index of the first source character this glyph came from.</summary>
    public int Cluster;
    /// <summary>Originating codepoint (used to synthesize mark classes when the font has no GDEF).</summary>
    public int Codepoint;
    public int XAdvance, YAdvance, XOffset, YOffset;
    /// <summary>Index of the base/mark glyph this mark attaches to, or -1.</summary>
    public int AttachTo;
    public int AttachDx, AttachDy;
    /// <summary>Number of components when this glyph is a ligature (0 otherwise).</summary>
    public int LigComponents;
    /// <summary>For a mark following a ligature: which component it belongs to (-1 = unknown/last).</summary>
    public int LigComp;
    /// <summary>Per-glyph feature mask bits (script shapers restrict some features to some glyphs).</summary>
    public uint Mask;
    public bool IsMarkHint;
    /// <summary>Parent glyph index + 1 for cursive attachment (0 = none); y-offset follows the parent's.</summary>
    public int CursiveParentPlusOne;
    public int CursiveDy;
    /// <summary>Indic syllable role (see <see cref="IndicRole"/>); 0 for glyphs outside Indic syllables.</summary>
    public byte Role;
    /// <summary>Syllable index within the run, or -1.</summary>
    public int Syl;
}

/// <summary>Growable glyph sequence with helpers the lookup appliers share.</summary>
internal sealed class GlyphBuffer
{
    public readonly List<ShapedGlyph> Glyphs = new();
    public int Count => Glyphs.Count;
    public ShapedGlyph this[int i] { get => Glyphs[i]; set => Glyphs[i] = value; }
}

/// <summary>A ScriptList language system: which features it enables.</summary>
internal sealed class OtLangSys
{
    public int RequiredFeature = -1;
    public int[] Features = Array.Empty<int>();
}

internal sealed class OtScript
{
    public OtLangSys? Default;
    public readonly Dictionary<string, OtLangSys> LangSys = new();
}

internal sealed class OtFeature
{
    public string Tag = "";
    public int[] Lookups = Array.Empty<int>();
}

/// <summary>A lookup's header plus its lazily parsed subtables.</summary>
internal sealed class OtLookup
{
    public int Type;
    public ushort Flag;
    public int FilterSet = -1;
    public int[] SubtableOffsets = Array.Empty<int>();
    public LookupSubtable?[]? Parsed;
}

/// <summary>A parsed lookup subtable able to apply itself at one buffer position.</summary>
internal abstract class LookupSubtable
{
    /// <summary>
    /// Try to apply at <paramref name="pos"/>. On success returns true and sets
    /// <paramref name="next"/> to the index the driver should examine next.
    /// </summary>
    public abstract bool Apply(OtEngine eng, GlyphBuffer buf, int pos, out int next);
}

/// <summary>
/// Shared parse of a GSUB or GPOS table header: script/language systems, features and lookup
/// list. Subtables are parsed on first use of each lookup, so shaping one script in a large font
/// never pays for the rest.
/// </summary>
internal sealed class OtLayout
{
    public readonly OtReader R;
    public readonly bool IsGpos;
    public readonly Dictionary<string, OtScript> Scripts = new();
    public OtFeature[] Features = Array.Empty<OtFeature>();
    private int[] _lookupOffsets = Array.Empty<int>();
    private OtLookup?[] _lookups = Array.Empty<OtLookup?>();

    private OtLayout(OtReader r, bool isGpos) { R = r; IsGpos = isGpos; }

    public int LookupCount => _lookupOffsets.Length;

    public static OtLayout? Parse(byte[] data, int tableOffset, bool isGpos)
    {
        var r = new OtReader(data);
        if (tableOffset <= 0 || tableOffset >= data.Length) return null;
        if (r.U16(tableOffset) != 1) return null;

        var layout = new OtLayout(r, isGpos);
        int scriptListOff = tableOffset + r.U16(tableOffset + 4);
        int featureListOff = tableOffset + r.U16(tableOffset + 6);
        int lookupListOff = tableOffset + r.U16(tableOffset + 8);

        int scriptCount = r.U16(scriptListOff);
        for (int i = 0; i < scriptCount; i++)
        {
            string tag = r.Tag(scriptListOff + 2 + 6 * i);
            int scriptOff = scriptListOff + r.U16(scriptListOff + 2 + 6 * i + 4);
            var script = new OtScript();
            int defOff = r.U16(scriptOff);
            if (defOff > 0) script.Default = ParseLangSys(r, scriptOff + defOff);
            int lsCount = r.U16(scriptOff + 2);
            for (int j = 0; j < lsCount; j++)
            {
                string lsTag = r.Tag(scriptOff + 4 + 6 * j);
                int lsOff = scriptOff + r.U16(scriptOff + 4 + 6 * j + 4);
                script.LangSys[lsTag] = ParseLangSys(r, lsOff);
            }
            layout.Scripts[tag] = script;
        }

        int featureCount = r.U16(featureListOff);
        layout.Features = new OtFeature[featureCount];
        for (int i = 0; i < featureCount; i++)
        {
            string tag = r.Tag(featureListOff + 2 + 6 * i);
            int fOff = featureListOff + r.U16(featureListOff + 2 + 6 * i + 4);
            int n = r.U16(fOff + 2);
            var lookups = new int[n];
            for (int k = 0; k < n; k++) lookups[k] = r.U16(fOff + 4 + 2 * k);
            layout.Features[i] = new OtFeature { Tag = tag, Lookups = lookups };
        }

        int lookupCount = r.U16(lookupListOff);
        layout._lookupOffsets = new int[lookupCount];
        layout._lookups = new OtLookup?[lookupCount];
        for (int i = 0; i < lookupCount; i++)
            layout._lookupOffsets[i] = lookupListOff + r.U16(lookupListOff + 2 + 2 * i);
        return layout;
    }

    private static OtLangSys ParseLangSys(OtReader r, int off)
    {
        var ls = new OtLangSys();
        int req = r.U16(off + 2);
        ls.RequiredFeature = req == 0xFFFF ? -1 : req;
        int n = r.U16(off + 4);
        ls.Features = new int[n];
        for (int i = 0; i < n; i++) ls.Features[i] = r.U16(off + 6 + 2 * i);
        return ls;
    }

    /// <summary>First script present among <paramref name="tags"/> (case-sensitive OpenType tags).</summary>
    public OtScript? FindScript(params string[] tags)
    {
        foreach (var t in tags)
            if (Scripts.TryGetValue(t, out var s)) return s;
        return null;
    }

    /// <summary>Lookup indices a language system enables for a feature tag, ascending and de-duplicated.</summary>
    public List<int> LookupsFor(OtLangSys? ls, string featureTag)
    {
        var result = new List<int>();
        if (ls == null) return result;
        void Collect(int fi)
        {
            if (fi < 0 || fi >= Features.Length || Features[fi].Tag != featureTag) return;
            foreach (var li in Features[fi].Lookups)
                if (!result.Contains(li)) result.Add(li);
        }
        Collect(ls.RequiredFeature);
        foreach (var fi in ls.Features) Collect(fi);
        result.Sort();
        return result;
    }

    public bool HasFeature(OtLangSys? ls, string featureTag)
    {
        if (ls == null) return false;
        if (ls.RequiredFeature >= 0 && ls.RequiredFeature < Features.Length && Features[ls.RequiredFeature].Tag == featureTag) return true;
        foreach (var fi in ls.Features)
            if (fi >= 0 && fi < Features.Length && Features[fi].Tag == featureTag) return true;
        return false;
    }

    public OtLookup? GetLookup(int index)
    {
        if (index < 0 || index >= _lookupOffsets.Length) return null;
        var cached = _lookups[index];
        if (cached != null) return cached;

        int off = _lookupOffsets[index];
        int type = R.U16(off);
        ushort flag = R.U16(off + 2);
        int subCount = R.U16(off + 4);
        var offsets = new int[subCount];
        for (int i = 0; i < subCount; i++) offsets[i] = off + R.U16(off + 6 + 2 * i);
        int filterSet = (flag & 0x10) != 0 ? R.U16(off + 6 + 2 * subCount) : -1;

        // Extension lookups (GSUB 7 / GPOS 9): every subtable is a 32-bit-offset indirection to
        // the real subtable, whose type is the extension's declared type.
        int extType = IsGpos ? 9 : 7;
        if (type == extType && subCount > 0)
        {
            int realType = R.U16(offsets[0] + 2);
            for (int i = 0; i < subCount; i++)
                offsets[i] = offsets[i] + (int)R.U32(offsets[i] + 4);
            type = realType;
        }

        var lookup = new OtLookup { Type = type, Flag = flag, FilterSet = filterSet, SubtableOffsets = offsets };
        _lookups[index] = lookup;
        return lookup;
    }
}

/// <summary>Decides which buffer glyphs a lookup ignores (LookupFlag + GDEF classes).</summary>
internal readonly struct LookupFilter
{
    private readonly Gdef? _gdef;
    private readonly ushort _flag;
    private readonly int _filterSet;

    public LookupFilter(Gdef? gdef, OtLookup lookup)
    {
        _gdef = gdef; _flag = lookup.Flag; _filterSet = lookup.FilterSet;
    }

    /// <summary>GDEF class of a buffer glyph, synthesized from its source codepoint when the font has no GDEF classes.</summary>
    public static int ClassOf(Gdef? gdef, in ShapedGlyph g)
    {
        if (gdef?.GlyphClasses != null)
        {
            int c = gdef.GlyphClasses.GetClass(g.Id);
            if (c != 0) return c;
        }
        return g.IsMarkHint ? Gdef.ClassMark : Gdef.ClassBase;
    }

    /// <summary>The lookup's RightToLeft flag (cursive attachment chains child-to-parent by it).</summary>
    public bool RightToLeftLookup => (_flag & 0x01) != 0;

    public bool Skip(in ShapedGlyph g)
    {
        if ((_flag & 0x000E) == 0 && (_flag & 0xFF00) == 0 && (_flag & 0x10) == 0) return false;
        int cls = ClassOf(_gdef, g);
        if (cls == Gdef.ClassMark)
        {
            if ((_flag & 0x08) != 0) return true;
            int attachType = _flag >> 8;
            if (attachType != 0 && (_gdef?.MarkAttachClasses?.GetClass(g.Id) ?? 0) != attachType) return true;
            if ((_flag & 0x10) != 0)
            {
                var sets = _gdef?.MarkGlyphSets;
                if (sets == null || _filterSet < 0 || _filterSet >= sets.Length) return true;
                var cov = sets[_filterSet];
                if (cov == null || !cov.Contains(g.Id)) return true;
            }
        }
        else if (cls == Gdef.ClassBase && (_flag & 0x02) != 0) return true;
        else if (cls == Gdef.ClassLigature && (_flag & 0x04) != 0) return true;
        return false;
    }

    /// <summary>Next non-skipped index strictly after <paramref name="from"/>, or -1.</summary>
    public int Next(GlyphBuffer buf, int from)
    {
        for (int i = from + 1; i < buf.Count; i++)
            if (!Skip(buf.Glyphs[i])) return i;
        return -1;
    }

    /// <summary>Previous non-skipped index strictly before <paramref name="from"/>, or -1.</summary>
    public int Previous(GlyphBuffer buf, int from)
    {
        for (int i = from - 1; i >= 0; i--)
            if (!Skip(buf.Glyphs[i])) return i;
        return -1;
    }
}
