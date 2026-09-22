using System;
using System.Collections.Generic;

namespace EggPdf.Text.OpenType;

/// <summary>OpenType Coverage table: maps a glyph ID to its index in a sorted glyph set (or -1).</summary>
internal sealed class Coverage
{
    private readonly ushort[]? _glyphs;
    private readonly ushort[]? _rangeStart, _rangeEnd, _rangeStartIndex;

    private Coverage(ushort[] glyphs) { _glyphs = glyphs; }

    private Coverage(ushort[] starts, ushort[] ends, ushort[] startIndex)
    {
        _rangeStart = starts; _rangeEnd = ends; _rangeStartIndex = startIndex;
    }

    public static Coverage? Parse(OtReader r, int offset)
    {
        if (offset <= 0) return null;
        int format = r.U16(offset);
        int count = r.U16(offset + 2);
        if (format == 1)
        {
            var glyphs = new ushort[count];
            for (int i = 0; i < count; i++) glyphs[i] = r.U16(offset + 4 + 2 * i);
            return new Coverage(glyphs);
        }
        if (format == 2)
        {
            var starts = new ushort[count];
            var ends = new ushort[count];
            var idx = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                int p = offset + 4 + 6 * i;
                starts[i] = r.U16(p);
                ends[i] = r.U16(p + 2);
                idx[i] = r.U16(p + 4);
            }
            return new Coverage(starts, ends, idx);
        }
        return null;
    }

    /// <summary>Coverage index of the glyph, or -1 when not covered.</summary>
    public int IndexOf(ushort glyph)
    {
        if (_glyphs != null)
        {
            int lo = 0, hi = _glyphs.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ushort g = _glyphs[mid];
                if (g == glyph) return mid;
                if (g < glyph) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }
        if (_rangeStart != null)
        {
            int lo = 0, hi = _rangeStart.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (glyph < _rangeStart[mid]) hi = mid - 1;
                else if (glyph > _rangeEnd![mid]) lo = mid + 1;
                else return _rangeStartIndex![mid] + (glyph - _rangeStart[mid]);
            }
        }
        return -1;
    }

    public bool Contains(ushort glyph) => IndexOf(glyph) >= 0;

    public IEnumerable<ushort> Glyphs()
    {
        if (_glyphs != null)
        {
            foreach (var g in _glyphs) yield return g;
        }
        else if (_rangeStart != null)
        {
            for (int i = 0; i < _rangeStart.Length; i++)
                for (int g = _rangeStart[i]; g <= _rangeEnd![i]; g++)
                    yield return (ushort)g;
        }
    }
}

/// <summary>OpenType ClassDef table: glyph ID -> class value (0 for unlisted glyphs).</summary>
internal sealed class ClassDef
{
    private readonly ushort _startGlyph;
    private readonly ushort[]? _classes;                       // format 1
    private readonly ushort[]? _rangeStart, _rangeEnd, _rangeClass; // format 2

    private ClassDef(ushort start, ushort[] classes) { _startGlyph = start; _classes = classes; }
    private ClassDef(ushort[] starts, ushort[] ends, ushort[] cls)
    {
        _rangeStart = starts; _rangeEnd = ends; _rangeClass = cls;
    }

    public static ClassDef? Parse(OtReader r, int offset)
    {
        if (offset <= 0) return null;
        int format = r.U16(offset);
        if (format == 1)
        {
            ushort start = r.U16(offset + 2);
            int count = r.U16(offset + 4);
            var classes = new ushort[count];
            for (int i = 0; i < count; i++) classes[i] = r.U16(offset + 6 + 2 * i);
            return new ClassDef(start, classes);
        }
        if (format == 2)
        {
            int count = r.U16(offset + 2);
            var starts = new ushort[count];
            var ends = new ushort[count];
            var cls = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                int p = offset + 4 + 6 * i;
                starts[i] = r.U16(p);
                ends[i] = r.U16(p + 2);
                cls[i] = r.U16(p + 4);
            }
            return new ClassDef(starts, ends, cls);
        }
        return null;
    }

    public int GetClass(ushort glyph)
    {
        if (_classes != null)
        {
            int i = glyph - _startGlyph;
            return i >= 0 && i < _classes.Length ? _classes[i] : 0;
        }
        if (_rangeStart != null)
        {
            int lo = 0, hi = _rangeStart.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (glyph < _rangeStart[mid]) hi = mid - 1;
                else if (glyph > _rangeEnd![mid]) lo = mid + 1;
                else return _rangeClass![mid];
            }
        }
        return 0;
    }
}

/// <summary>
/// GDEF: glyph classes (1 base, 2 ligature, 3 mark, 4 component), mark attachment classes and
/// mark glyph sets -- what lookup flags such as IgnoreMarks / UseMarkFilteringSet consult.
/// </summary>
internal sealed class Gdef
{
    public const int ClassBase = 1, ClassLigature = 2, ClassMark = 3, ClassComponent = 4;

    public ClassDef? GlyphClasses;
    public ClassDef? MarkAttachClasses;
    public Coverage?[]? MarkGlyphSets;

    public static Gdef? Parse(byte[] data, int offset)
    {
        var r = new OtReader(data);
        if (offset <= 0 || r.U16(offset) != 1) return null;

        var gdef = new Gdef();
        int classDefOff = r.U16(offset + 4);
        int markAttachOff = r.U16(offset + 10);
        gdef.GlyphClasses = ClassDef.Parse(r, classDefOff > 0 ? offset + classDefOff : 0);
        gdef.MarkAttachClasses = ClassDef.Parse(r, markAttachOff > 0 ? offset + markAttachOff : 0);

        int minor = r.U16(offset + 2);
        if (minor >= 2)
        {
            int setsOff = r.U16(offset + 12);
            if (setsOff > 0)
            {
                int setsBase = offset + setsOff;
                int count = r.U16(setsBase + 2);
                var sets = new Coverage?[count];
                for (int i = 0; i < count; i++)
                    sets[i] = Coverage.Parse(r, setsBase + (int)r.U32(setsBase + 4 + 4 * i));
                gdef.MarkGlyphSets = sets;
            }
        }
        return gdef;
    }

    public int GlyphClass(ushort glyph) => GlyphClasses?.GetClass(glyph) ?? 0;
}
