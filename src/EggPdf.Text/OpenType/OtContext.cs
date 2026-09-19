using System;
using System.Collections.Generic;

namespace EggPdf.Text.OpenType;

internal sealed class ContextRule
{
    public ushort[] Backtrack = Array.Empty<ushort>();   // nearest preceding glyph first
    public ushort[] Input = Array.Empty<ushort>();       // excludes the first glyph
    public ushort[] Lookahead = Array.Empty<ushort>();
    public (int seq, int lookup)[] Records = Array.Empty<(int, int)>();
}

/// <summary>
/// Contextual and chaining-contextual lookups (GSUB 5/6, GPOS 7/8), formats 1 (glyph rules),
/// 2 (class rules) and 3 (coverage rules). Matching honours the lookup's ignore flags; each
/// matched rule then runs its nested lookups at the matched input positions.
/// </summary>
internal sealed class ContextSubtable : LookupSubtable
{
    private int _format;
    private Coverage? _coverage;
    private ContextRule[][]? _ruleSets;
    private ClassDef? _btClass, _inClass, _laClass;
    private Coverage?[]? _btCov, _inCov, _laCov;
    private (int seq, int lookup)[] _records3 = Array.Empty<(int, int)>();

    public static ContextSubtable? Parse(OtReader r, int off, bool chain)
    {
        int format = r.U16(off);
        var st = new ContextSubtable { _format = format };
        switch (format)
        {
            case 1:
            case 2:
                st._coverage = Coverage.Parse(r, off + r.U16(off + 2));
                if (st._coverage == null) return null;
                int setCountPos;
                if (format == 1)
                {
                    setCountPos = off + 4;
                }
                else if (chain)
                {
                    st._btClass = ClassDef.Parse(r, OffsetOrZero(off, r.U16(off + 4)));
                    st._inClass = ClassDef.Parse(r, OffsetOrZero(off, r.U16(off + 6)));
                    st._laClass = ClassDef.Parse(r, OffsetOrZero(off, r.U16(off + 8)));
                    setCountPos = off + 10;
                }
                else
                {
                    st._inClass = ClassDef.Parse(r, OffsetOrZero(off, r.U16(off + 4)));
                    setCountPos = off + 6;
                }
                int setCount = r.U16(setCountPos);
                st._ruleSets = new ContextRule[setCount][];
                for (int i = 0; i < setCount; i++)
                {
                    int setOff = r.U16(setCountPos + 2 + 2 * i);
                    st._ruleSets[i] = setOff == 0 ? Array.Empty<ContextRule>() : ParseRuleSet(r, off + setOff, chain);
                }
                return st;

            case 3:
                if (chain)
                {
                    int p = off + 2;
                    int bt = r.U16(p); p += 2;
                    st._btCov = ReadCoverages(r, off, p, bt); p += 2 * bt;
                    int inp = r.U16(p); p += 2;
                    st._inCov = ReadCoverages(r, off, p, inp); p += 2 * inp;
                    int la = r.U16(p); p += 2;
                    st._laCov = ReadCoverages(r, off, p, la); p += 2 * la;
                    st._records3 = ReadRecords(r, p);
                }
                else
                {
                    int glyphCount = r.U16(off + 2);
                    int substCount = r.U16(off + 4);
                    st._btCov = Array.Empty<Coverage?>();
                    st._laCov = Array.Empty<Coverage?>();
                    st._inCov = ReadCoverages(r, off, off + 6, glyphCount);
                    st._records3 = ReadRecords(r, off + 6 + 2 * glyphCount, substCount);
                }
                return st;
        }
        return null;
    }

    private static int OffsetOrZero(int baseOff, int rel) => rel == 0 ? 0 : baseOff + rel;

    private static Coverage?[] ReadCoverages(OtReader r, int baseOff, int pos, int count)
    {
        var arr = new Coverage?[count];
        for (int i = 0; i < count; i++)
        {
            int rel = r.U16(pos + 2 * i);
            arr[i] = rel == 0 ? null : Coverage.Parse(r, baseOff + rel);
        }
        return arr;
    }

    private static (int seq, int lookup)[] ReadRecords(OtReader r, int pos, int count = -1)
    {
        if (count < 0) { count = r.U16(pos); pos += 2; }
        var recs = new (int, int)[count];
        for (int i = 0; i < count; i++)
            recs[i] = (r.U16(pos + 4 * i), r.U16(pos + 4 * i + 2));
        return recs;
    }

    private static ContextRule[] ParseRuleSet(OtReader r, int setOff, bool chain)
    {
        int n = r.U16(setOff);
        var rules = new ContextRule[n];
        for (int i = 0; i < n; i++)
            rules[i] = ParseRule(r, setOff + r.U16(setOff + 2 + 2 * i), chain);
        return rules;
    }

    private static ushort[] ReadU16Array(OtReader r, int pos, int count)
    {
        var a = new ushort[count];
        for (int i = 0; i < count; i++) a[i] = r.U16(pos + 2 * i);
        return a;
    }

    private static ContextRule ParseRule(OtReader r, int off, bool chain)
    {
        var rule = new ContextRule();
        int p = off;
        if (chain)
        {
            int bt = r.U16(p); p += 2;
            rule.Backtrack = ReadU16Array(r, p, bt); p += 2 * bt;
            int inp = r.U16(p); p += 2;
            rule.Input = ReadU16Array(r, p, Math.Max(inp - 1, 0)); p += 2 * Math.Max(inp - 1, 0);
            int la = r.U16(p); p += 2;
            rule.Lookahead = ReadU16Array(r, p, la); p += 2 * la;
            rule.Records = ReadRecords(r, p);
        }
        else
        {
            int glyphCount = r.U16(p);
            int substCount = r.U16(p + 2);
            p += 4;
            rule.Input = ReadU16Array(r, p, Math.Max(glyphCount - 1, 0)); p += 2 * Math.Max(glyphCount - 1, 0);
            rule.Records = ReadRecords(r, p, substCount);
        }
        return rule;
    }

    public override bool Apply(OtEngine eng, GlyphBuffer buf, int pos, out int next)
    {
        next = pos + 1;
        var first = buf.Glyphs[pos];

        if (_format == 3)
        {
            var matched = new List<int> { pos };
            if (_inCov == null || _inCov.Length == 0 || _inCov[0] == null || !_inCov[0]!.Contains(first.Id)) return false;
            return TryMatch(eng, buf, pos, matched, _btCov!, _inCov, _laCov!, null, null, null,
                (cov, ord, g) => ((Coverage?[])cov)[ord]?.Contains(g.Id) ?? false, _records3, ref next);
        }

        int ci = _coverage!.IndexOf(first.Id);
        if (ci < 0) return false;

        ContextRule[] rules;
        if (_format == 1)
        {
            if (ci >= _ruleSets!.Length) return false;
            rules = _ruleSets[ci];
        }
        else
        {
            int cls = _inClass?.GetClass(first.Id) ?? 0;
            if (cls >= _ruleSets!.Length) return false;
            rules = _ruleSets[cls];
        }

        foreach (var rule in rules)
        {
            var matched = new List<int> { pos };
            bool ok = _format == 1
                ? TryMatch(eng, buf, pos, matched, rule.Backtrack, rule.Input, rule.Lookahead, null, null, null,
                    (arr, ord, g) => ((ushort[])arr)[ord] == g.Id, rule.Records, ref next)
                : TryMatch(eng, buf, pos, matched, rule.Backtrack, rule.Input, rule.Lookahead, _btClass, _inClass, _laClass,
                    (arr, ord, g) => false, rule.Records, ref next);
            if (ok) return true;
        }
        return false;
    }

    /// <summary>
    /// Shared matcher. <paramref name="bt"/>/<paramref name="inp"/>/<paramref name="la"/> are either
    /// glyph/class arrays (formats 1-2) or coverage arrays (format 3); <paramref name="test"/> is
    /// used for glyph and coverage comparisons, while class comparisons use the ClassDefs.
    /// </summary>
    private bool TryMatch(OtEngine eng, GlyphBuffer buf, int pos, List<int> matched,
        object bt, object inp, object la, ClassDef? btClass, ClassDef? inClass, ClassDef? laClass,
        Func<object, int, ShapedGlyph, bool> test, (int seq, int lookup)[] records, ref int next)
    {
        var filter = eng.Filter;
        bool useClasses = _format == 2;

        bool Test(object seq, int ord, ClassDef? cd, in ShapedGlyph g)
        {
            if (useClasses) return cd != null ? cd.GetClass(g.Id) == ((ushort[])seq)[ord] : ((ushort[])seq)[ord] == 0;
            return test(seq, ord, g);
        }

        int inputLen = inp is ushort[] ia ? ia.Length : ((Coverage?[])inp).Length - 1;
        int inputStart = inp is ushort[] ? 0 : 1; // format 3 stores the first glyph's coverage too

        int cur = pos;
        for (int k = 0; k < inputLen; k++)
        {
            cur = filter.Next(buf, cur);
            if (cur < 0) return false;
            var g = buf.Glyphs[cur];
            if (!Test(inp, k + inputStart, inClass, g)) return false;
            matched.Add(cur);
        }

        int btLen = bt is ushort[] ba ? ba.Length : ((Coverage?[])bt).Length;
        cur = pos;
        for (int k = 0; k < btLen; k++)
        {
            cur = filter.Previous(buf, cur);
            if (cur < 0) return false;
            if (!Test(bt, k, btClass, buf.Glyphs[cur])) return false;
        }

        int laLen = la is ushort[] la1 ? la1.Length : ((Coverage?[])la).Length;
        cur = matched[matched.Count - 1];
        for (int k = 0; k < laLen; k++)
        {
            cur = filter.Next(buf, cur);
            if (cur < 0) return false;
            if (!Test(la, k, laClass, buf.Glyphs[cur])) return false;
        }

        foreach (var (seq, lookup) in records)
        {
            if (seq < 0 || seq >= matched.Count) continue;
            int delta = eng.ApplyNested(lookup, buf, matched[seq]);
            if (delta != 0)
                for (int m = seq + 1; m < matched.Count; m++) matched[m] += delta;
        }
        next = Math.Max(matched[matched.Count - 1] + 1, pos + 1);
        return true;
    }
}
