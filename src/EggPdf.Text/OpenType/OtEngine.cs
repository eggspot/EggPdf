using System;
using System.Collections.Generic;

namespace EggPdf.Text.OpenType;

/// <summary>
/// Applies GSUB or GPOS lookups to a glyph buffer. Owns lookup-subtable parsing dispatch, the
/// per-lookup glyph filter (LookupFlag + GDEF) and nested-lookup recursion for contextual rules.
/// </summary>
internal sealed class OtEngine
{
    public readonly OtLayout Layout;
    public readonly Gdef? Gdef;

    /// <summary>Filter of the lookup currently being applied (nested lookups push/pop their own).</summary>
    public LookupFilter Filter;
    /// <summary>Shaping direction of the current run (cursive attachment aligns exit/entry anchors by it).</summary>
    public bool Rtl;
    private int _depth;
    private const int MaxDepth = 6;

    public OtEngine(OtLayout layout, Gdef? gdef) { Layout = layout; Gdef = gdef; }

    private LookupSubtable?[] GetSubtables(OtLookup lookup)
    {
        if (lookup.Parsed != null) return lookup.Parsed;
        var parsed = new LookupSubtable?[lookup.SubtableOffsets.Length];
        for (int i = 0; i < parsed.Length; i++)
            parsed[i] = ParseSubtable(lookup.Type, lookup.SubtableOffsets[i]);
        lookup.Parsed = parsed;
        return parsed;
    }

    private LookupSubtable? ParseSubtable(int type, int off)
    {
        var r = Layout.R;
        if (Layout.IsGpos)
        {
            switch (type)
            {
                case 1: return SinglePos.Parse(r, off);
                case 2: return PairPos.Parse(r, off);
                case 4: return MarkBasePos.Parse(r, off, MarkBasePos.Kind.Base);
                case 5: return MarkBasePos.Parse(r, off, MarkBasePos.Kind.Ligature);
                case 6: return MarkBasePos.Parse(r, off, MarkBasePos.Kind.Mark);
                case 7: return ContextSubtable.Parse(r, off, chain: false);
                case 8: return ContextSubtable.Parse(r, off, chain: true);
                case 3: return CursivePos.Parse(r, off);
                default: return null;
            }
        }
        switch (type)
        {
            case 1: return SingleSubst.Parse(r, off);
            case 2: return MultipleSubst.Parse(r, off, alternate: false);
            case 3: return MultipleSubst.Parse(r, off, alternate: true);
            case 4: return LigatureSubst.Parse(r, off);
            case 5: return ContextSubtable.Parse(r, off, chain: false);
            case 6: return ContextSubtable.Parse(r, off, chain: true);
            default: return null; // reverse chaining single (8) is not applied
        }
    }

    /// <summary>
    /// Apply a lookup across the whole buffer. A glyph is a candidate start only when its Mask
    /// shares a bit with <paramref name="mask"/> (script shapers use this to confine a feature to
    /// particular glyphs, e.g. reph forms).
    /// </summary>
    public void ApplyLookup(int lookupIndex, GlyphBuffer buf, uint mask = uint.MaxValue)
    {
        var lookup = Layout.GetLookup(lookupIndex);
        if (lookup == null) return;
        var subtables = GetSubtables(lookup);
        var saved = Filter;
        Filter = new LookupFilter(Gdef, lookup);

        int i = 0;
        int guard = 0, guardMax = buf.Count * 8 + 256;
        while (i < buf.Count && guard++ < guardMax)
        {
            var g = buf.Glyphs[i];
            if (Filter.Skip(g) || (g.Mask & mask) == 0) { i++; continue; }

            int next = i + 1;
            bool applied = false;
            foreach (var st in subtables)
            {
                if (st != null && st.Apply(this, buf, i, out next)) { applied = true; break; }
            }
            // A subtable reports where to resume (past a ligature, or the same index after a
            // deletion); the guard above bounds any pathological non-advancing font.
            i = applied ? Math.Max(next, 0) : i + 1;
        }
        Filter = saved;
    }

    /// <summary>Apply a nested lookup at exactly one position (contextual rules). Returns the buffer length change.</summary>
    public int ApplyNested(int lookupIndex, GlyphBuffer buf, int pos)
    {
        if (_depth >= MaxDepth || pos < 0 || pos >= buf.Count) return 0;
        var lookup = Layout.GetLookup(lookupIndex);
        if (lookup == null) return 0;
        var subtables = GetSubtables(lookup);

        int before = buf.Count;
        var saved = Filter;
        Filter = new LookupFilter(Gdef, lookup);
        _depth++;
        try
        {
            foreach (var st in subtables)
                if (st != null && st.Apply(this, buf, pos, out _)) break;
        }
        finally
        {
            _depth--;
            Filter = saved;
        }
        return buf.Count - before;
    }
}
