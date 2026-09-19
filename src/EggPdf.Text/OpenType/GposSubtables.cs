using System;

namespace EggPdf.Text.OpenType;

internal struct ValueRecord
{
    public int XPlacement, YPlacement, XAdvance, YAdvance;

    public bool IsZero => XPlacement == 0 && YPlacement == 0 && XAdvance == 0 && YAdvance == 0;

    /// <summary>Size in bytes of a ValueRecord with the given ValueFormat flags.</summary>
    public static int SizeOf(int format)
    {
        int n = 0;
        for (int f = format & 0xFF; f != 0; f &= f - 1) n += 2;
        return n;
    }

    public static ValueRecord Read(OtReader r, int off, int format)
    {
        var v = new ValueRecord();
        int p = off;
        if ((format & 0x01) != 0) { v.XPlacement = r.I16(p); p += 2; }
        if ((format & 0x02) != 0) { v.YPlacement = r.I16(p); p += 2; }
        if ((format & 0x04) != 0) { v.XAdvance = r.I16(p); p += 2; }
        if ((format & 0x08) != 0) { v.YAdvance = r.I16(p); }
        return v; // device-table offsets (0x10..0x80) are size-accounted by SizeOf but ignored
    }

    public void ApplyTo(ref ShapedGlyph g)
    {
        g.XOffset += XPlacement;
        g.YOffset += YPlacement;
        g.XAdvance += XAdvance;
        g.YAdvance += YAdvance;
    }
}

/// <summary>GPOS Lookup Type 1: adjust a single glyph.</summary>
internal sealed class SinglePos : LookupSubtable
{
    private Coverage _cov = null!;
    private ValueRecord[] _values = Array.Empty<ValueRecord>();
    private bool _shared;

    public static SinglePos? Parse(OtReader r, int off)
    {
        int format = r.U16(off);
        var cov = Coverage.Parse(r, off + r.U16(off + 2));
        if (cov == null) return null;
        int vf = r.U16(off + 4);
        if (format == 1)
            return new SinglePos { _cov = cov, _shared = true, _values = new[] { ValueRecord.Read(r, off + 6, vf) } };
        if (format != 2) return null;
        int n = r.U16(off + 6);
        int size = ValueRecord.SizeOf(vf);
        var vals = new ValueRecord[n];
        for (int i = 0; i < n; i++) vals[i] = ValueRecord.Read(r, off + 8 + size * i, vf);
        return new SinglePos { _cov = cov, _values = vals };
    }

    public override bool Apply(OtEngine eng, GlyphBuffer buf, int pos, out int next)
    {
        next = pos + 1;
        var g = buf.Glyphs[pos];
        int idx = _cov.IndexOf(g.Id);
        if (idx < 0) return false;
        var v = _shared ? _values[0] : (idx < _values.Length ? _values[idx] : default);
        v.ApplyTo(ref g);
        buf.Glyphs[pos] = g;
        return true;
    }
}

/// <summary>GPOS Lookup Type 2: pair adjustment (kerning), by glyph pair (format 1) or class pair (format 2).</summary>
internal sealed class PairPos : LookupSubtable
{
    private OtReader _r;
    private int _off, _format, _vf1, _vf2;
    private Coverage _cov = null!;
    private ClassDef? _class1, _class2;
    private int _class1Count, _class2Count;

    public static PairPos? Parse(OtReader r, int off)
    {
        int format = r.U16(off);
        var cov = Coverage.Parse(r, off + r.U16(off + 2));
        if (cov == null) return null;
        var st = new PairPos { _r = r, _off = off, _format = format, _cov = cov, _vf1 = r.U16(off + 4), _vf2 = r.U16(off + 6) };
        if (format == 2)
        {
            st._class1 = ClassDef.Parse(r, off + r.U16(off + 8));
            st._class2 = ClassDef.Parse(r, off + r.U16(off + 10));
            st._class1Count = r.U16(off + 12);
            st._class2Count = r.U16(off + 14);
        }
        else if (format != 1) return null;
        return st;
    }

    public override bool Apply(OtEngine eng, GlyphBuffer buf, int pos, out int next)
    {
        next = pos + 1;
        var first = buf.Glyphs[pos];
        int ci = _cov.IndexOf(first.Id);
        if (ci < 0) return false;

        int j = eng.Filter.Next(buf, pos);
        if (j < 0) return false;
        var second = buf.Glyphs[j];

        int size1 = ValueRecord.SizeOf(_vf1), size2 = ValueRecord.SizeOf(_vf2);
        ValueRecord v1, v2;
        if (_format == 1)
        {
            int setOff = _off + _r.U16(_off + 10 + 2 * ci);
            int count = _r.U16(setOff);
            int recSize = 2 + size1 + size2;
            int lo = 0, hi = count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ushort sg = _r.U16(setOff + 2 + recSize * mid);
                if (sg == second.Id) { found = mid; break; }
                if (sg < second.Id) lo = mid + 1; else hi = mid - 1;
            }
            if (found < 0) return false;
            int rec = setOff + 2 + recSize * found + 2;
            v1 = ValueRecord.Read(_r, rec, _vf1);
            v2 = ValueRecord.Read(_r, rec + size1, _vf2);
        }
        else
        {
            int c1 = _class1?.GetClass(first.Id) ?? 0;
            int c2 = _class2?.GetClass(second.Id) ?? 0;
            if (c1 >= _class1Count || c2 >= _class2Count) return false;
            int rec = _off + 16 + (c1 * _class2Count + c2) * (size1 + size2);
            v1 = ValueRecord.Read(_r, rec, _vf1);
            v2 = ValueRecord.Read(_r, rec + size1, _vf2);
        }

        if (v1.IsZero && v2.IsZero) return false;
        v1.ApplyTo(ref first);
        buf.Glyphs[pos] = first;
        if (!v2.IsZero)
        {
            v2.ApplyTo(ref second);
            buf.Glyphs[j] = second;
            next = j + 1;
        }
        else
        {
            next = j;
        }
        return true;
    }
}

/// <summary>
/// GPOS Lookup Types 4, 5 and 6: attach a mark to a base glyph, a ligature component, or another
/// mark, via anchor points. The attachment is recorded on the mark and resolved into final
/// x/y offsets once advances are known (see <see cref="MarkPlacement"/>).
/// </summary>
internal sealed class MarkBasePos : LookupSubtable
{
    public enum Kind { Base, Ligature, Mark }

    private OtReader _r;
    private Kind _kind;
    private Coverage _markCov = null!, _targetCov = null!;
    private int _markClassCount, _markArrayOff, _targetArrayOff;

    public static MarkBasePos? Parse(OtReader r, int off, Kind kind)
    {
        if (r.U16(off) != 1) return null;
        var markCov = Coverage.Parse(r, off + r.U16(off + 2));
        var targetCov = Coverage.Parse(r, off + r.U16(off + 4));
        if (markCov == null || targetCov == null) return null;
        return new MarkBasePos
        {
            _r = r, _kind = kind, _markCov = markCov, _targetCov = targetCov,
            _markClassCount = r.U16(off + 6),
            _markArrayOff = off + r.U16(off + 8),
            _targetArrayOff = off + r.U16(off + 10),
        };
    }

    private bool ReadAnchor(int anchorOff, out int x, out int y)
    {
        x = y = 0;
        if (anchorOff == 0) return false;
        int format = _r.U16(anchorOff);
        if (format < 1 || format > 3) return false;
        x = _r.I16(anchorOff + 2);
        y = _r.I16(anchorOff + 4);
        return true;
    }

    public override bool Apply(OtEngine eng, GlyphBuffer buf, int pos, out int next)
    {
        next = pos + 1;
        var mark = buf.Glyphs[pos];
        int mi = _markCov.IndexOf(mark.Id);
        if (mi < 0) return false;

        // Find the glyph the mark attaches to.
        int target;
        if (_kind == Kind.Mark)
        {
            target = eng.Filter.Previous(buf, pos);
            if (target < 0 || LookupFilter.ClassOf(eng.Gdef, buf.Glyphs[target]) != Gdef.ClassMark) return false;
        }
        else
        {
            target = pos - 1;
            while (target >= 0 && LookupFilter.ClassOf(eng.Gdef, buf.Glyphs[target]) == Gdef.ClassMark) target--;
            if (target < 0) return false;
        }

        var tg = buf.Glyphs[target];
        int ti = _targetCov.IndexOf(tg.Id);
        if (ti < 0) return false;

        int markRec = _markArrayOff + 2 + 4 * mi;
        int markClass = _r.U16(markRec);
        if (markClass >= _markClassCount) return false;
        int markAnchorRel = _r.U16(markRec + 2);
        if (!ReadAnchor(markAnchorRel == 0 ? 0 : _markArrayOff + markAnchorRel, out int mx, out int my))
            return false;

        int anchorOff;
        if (_kind == Kind.Ligature)
        {
            int ligAttach = _targetArrayOff + _r.U16(_targetArrayOff + 2 + 2 * ti);
            int compCount = _r.U16(ligAttach);
            if (compCount == 0) return false;
            int comp = mark.LigComp >= 0 ? Math.Min(mark.LigComp, compCount - 1) : compCount - 1;
            int rel = _r.U16(ligAttach + 2 + 2 * (_markClassCount * comp + markClass));
            anchorOff = rel == 0 ? 0 : ligAttach + rel;
        }
        else
        {
            // Base array (type 4) and Mark2 array (type 6) share a layout: count, then rows of
            // markClassCount anchor offsets relative to the array.
            int rel = _r.U16(_targetArrayOff + 2 + 2 * (_markClassCount * ti + markClass));
            anchorOff = rel == 0 ? 0 : _targetArrayOff + rel;
        }
        if (!ReadAnchor(anchorOff, out int bx, out int by)) return false;

        mark.AttachTo = target;
        mark.AttachDx = bx - mx;
        mark.AttachDy = by - my;
        buf.Glyphs[pos] = mark;
        return true;
    }
}
