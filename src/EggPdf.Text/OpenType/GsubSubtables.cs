using System;
using System.Collections.Generic;

namespace EggPdf.Text.OpenType;

/// <summary>GSUB Lookup Type 1: one glyph replaced by one glyph.</summary>
internal sealed class SingleSubst : LookupSubtable
{
    private Coverage _cov = null!;
    private int _format;
    private short _delta;
    private ushort[] _subs = Array.Empty<ushort>();

    public static SingleSubst? Parse(OtReader r, int off)
    {
        var st = new SingleSubst { _format = r.U16(off) };
        var cov = Coverage.Parse(r, off + r.U16(off + 2));
        if (cov == null) return null;
        st._cov = cov;
        if (st._format == 1) { st._delta = r.I16(off + 4); return st; }
        if (st._format != 2) return null;
        int n = r.U16(off + 4);
        st._subs = new ushort[n];
        for (int i = 0; i < n; i++) st._subs[i] = r.U16(off + 6 + 2 * i);
        return st;
    }

    public override bool Apply(OtEngine eng, GlyphBuffer buf, int pos, out int next)
    {
        next = pos + 1;
        var g = buf.Glyphs[pos];
        int idx = _cov.IndexOf(g.Id);
        if (idx < 0) return false;
        ushort id = _format == 1 ? unchecked((ushort)(g.Id + _delta)) : (idx < _subs.Length ? _subs[idx] : g.Id);
        g.Id = id;
        buf.Glyphs[pos] = g;
        return true;
    }
}

/// <summary>GSUB Lookup Type 2 (one glyph to a sequence) and Type 3 (alternates; the first alternate is used).</summary>
internal sealed class MultipleSubst : LookupSubtable
{
    private Coverage _cov = null!;
    private ushort[][] _seqs = Array.Empty<ushort[]>();
    private bool _alternate;

    public static MultipleSubst? Parse(OtReader r, int off, bool alternate)
    {
        if (r.U16(off) != 1) return null;
        var cov = Coverage.Parse(r, off + r.U16(off + 2));
        if (cov == null) return null;
        int n = r.U16(off + 4);
        var seqs = new ushort[n][];
        for (int i = 0; i < n; i++)
        {
            int so = off + r.U16(off + 6 + 2 * i);
            int cnt = r.U16(so);
            var arr = new ushort[cnt];
            for (int k = 0; k < cnt; k++) arr[k] = r.U16(so + 2 + 2 * k);
            seqs[i] = arr;
        }
        return new MultipleSubst { _cov = cov, _seqs = seqs, _alternate = alternate };
    }

    public override bool Apply(OtEngine eng, GlyphBuffer buf, int pos, out int next)
    {
        next = pos + 1;
        var g = buf.Glyphs[pos];
        int idx = _cov.IndexOf(g.Id);
        if (idx < 0 || idx >= _seqs.Length) return false;
        var seq = _seqs[idx];

        if (_alternate)
        {
            if (seq.Length == 0) return false;
            g.Id = seq[0];
            buf.Glyphs[pos] = g;
            return true;
        }

        if (seq.Length == 0)
        {
            buf.Glyphs.RemoveAt(pos);
            next = pos;
            return true;
        }

        g.Id = seq[0];
        buf.Glyphs[pos] = g;
        for (int k = 1; k < seq.Length; k++)
        {
            var extra = g;
            extra.Id = seq[k];
            buf.Glyphs.Insert(pos + k, extra);
        }
        next = pos + seq.Length;
        return true;
    }
}

/// <summary>GSUB Lookup Type 4: several glyphs replaced by one ligature glyph.</summary>
internal sealed class LigatureSubst : LookupSubtable
{
    private struct Ligature { public ushort Glyph; public ushort[] Components; }

    private Coverage _cov = null!;
    private Ligature[][] _sets = Array.Empty<Ligature[]>();

    public static LigatureSubst? Parse(OtReader r, int off)
    {
        if (r.U16(off) != 1) return null;
        var cov = Coverage.Parse(r, off + r.U16(off + 2));
        if (cov == null) return null;
        int setCount = r.U16(off + 4);
        var sets = new Ligature[setCount][];
        for (int i = 0; i < setCount; i++)
        {
            int so = off + r.U16(off + 6 + 2 * i);
            int ligCount = r.U16(so);
            var ligs = new Ligature[ligCount];
            for (int j = 0; j < ligCount; j++)
            {
                int lo = so + r.U16(so + 2 + 2 * j);
                int compCount = r.U16(lo + 2);
                var comps = new ushort[Math.Max(compCount - 1, 0)];
                for (int c = 0; c < comps.Length; c++) comps[c] = r.U16(lo + 4 + 2 * c);
                ligs[j] = new Ligature { Glyph = r.U16(lo), Components = comps };
            }
            sets[i] = ligs;
        }
        return new LigatureSubst { _cov = cov, _sets = sets };
    }

    public override bool Apply(OtEngine eng, GlyphBuffer buf, int pos, out int next)
    {
        next = pos + 1;
        var first = buf.Glyphs[pos];
        int idx = _cov.IndexOf(first.Id);
        if (idx < 0 || idx >= _sets.Length) return false;

        var filter = eng.Filter;
        foreach (var lig in _sets[idx])
        {
            var matched = new List<int>(lig.Components.Length + 1) { pos };
            int cur = pos;
            bool ok = true;
            foreach (var comp in lig.Components)
            {
                cur = filter.Next(buf, cur);
                if (cur < 0 || buf.Glyphs[cur].Id != comp) { ok = false; break; }
                matched.Add(cur);
            }
            if (!ok) continue;

            // Marks skipped between components stay after the ligature; remember which
            // component each followed so mark-to-ligature positioning can pick the right anchor.
            for (int k = 0; k < matched.Count - 1; k++)
                for (int j = matched[k] + 1; j < matched[k + 1]; j++)
                {
                    var mk = buf.Glyphs[j];
                    mk.LigComp = k;
                    buf.Glyphs[j] = mk;
                }

            var head = buf.Glyphs[pos];
            head.Id = lig.Glyph;
            head.LigComponents = matched.Count;
            for (int k = 1; k < matched.Count; k++)
                head.Cluster = Math.Min(head.Cluster, buf.Glyphs[matched[k]].Cluster);
            buf.Glyphs[pos] = head;

            for (int k = matched.Count - 1; k >= 1; k--)
                buf.Glyphs.RemoveAt(matched[k]);
            next = pos + 1;
            return true;
        }
        return false;
    }
}
