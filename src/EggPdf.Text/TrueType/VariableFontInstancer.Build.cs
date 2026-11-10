using System;
using System.Collections.Generic;
using static EggPdf.Text.TrueType.SfntWriter;

namespace EggPdf.Text.TrueType;

public static partial class VariableFontInstancer
{
    /// <summary>A glyph after instancing: serialized glyf data, its bounds and horizontal metrics.</summary>
    private sealed class InstancedGlyph
    {
        public byte[] Data = Array.Empty<byte>();
        public int XMin, YMin, XMax, YMax;
        public bool HasOutline;
        public int Advance, LeftSideBearing;
        /// <summary>The glyph's points after instancing (a composite's are its components' placed points), for anchor matching.</summary>
        public int[] PointsX = Array.Empty<int>(), PointsY = Array.Empty<int>();
    }

    private struct Component
    {
        public int Flags, GlyphIndex, Arg1, Arg2;

        /// <summary>The raw transform bytes: none, 2 (scale), 4 (x and y scale) or 8 (2x2 matrix), as F2Dot14 values.</summary>
        public byte[] Transform;
    }

    private const int HeadCheckSumAdjustment = 8, HeadIndexToLocFormat = 50;
    private const int MaxpNumGlyphs = 4, MaxpMaxPoints = 6, MaxpMaxContours = 8, MaxpMaxZones = 14;
    private const int HheaAdvanceWidthMax = 10, HheaMinLeftSideBearing = 12, HheaMinRightSideBearing = 14, HheaXMaxExtent = 16;
    private const int ScaledComponentOffset = 0x0800;

    private const int ArgsAreWords = 0x0001, ArgsAreXyValues = 0x0002, WeHaveScale = 0x0008, MoreComponents = 0x0020,
        WeHaveXyScale = 0x0040, WeHaveTwoByTwo = 0x0080, WeHaveInstructions = 0x0100;

    /// <summary>Everything needed to instance one font at one design-space location.</summary>
    private sealed class InstanceContext
    {
        private readonly Sfnt _s;
        private readonly byte[] _d;
        private readonly float[] _coords;
        private readonly int _numGlyphs, _numHMetrics, _locaFormat;
        private readonly int _glyf, _loca, _hmtx;
        private readonly GvarTable _gvar;
        private readonly HvarTable? _hvar;
        private readonly InstancedGlyph?[] _glyphs;
        private readonly bool[] _inProgress;

        public InstanceContext(Sfnt sfnt, float[] coords)
        {
            _s = sfnt;
            _d = sfnt.Data;
            _coords = coords;
            _numGlyphs = U16(_d, sfnt.Offset("maxp") + 4);
            _numHMetrics = U16(_d, sfnt.Offset("hhea") + 34);
            _locaFormat = I16(_d, sfnt.Offset("head") + 50);
            _glyf = sfnt.Offset("glyf");
            _loca = sfnt.Offset("loca");
            _hmtx = sfnt.Offset("hmtx");
            _gvar = new GvarTable(_d, sfnt.Offset("gvar"));
            if (sfnt.Has("HVAR")) _hvar = new HvarTable(_d, sfnt.Offset("HVAR"), coords);
            _glyphs = new InstancedGlyph?[_numGlyphs];
            _inProgress = new bool[_numGlyphs];
        }

        public byte[] Build()
        {
            for (int g = 0; g < _numGlyphs; g++) Instance(g);
            return Assemble();
        }

        private int GlyphStart(int gid) => _locaFormat == 0 ? U16(_d, _loca + gid * 2) * 2 : (int)U32(_d, _loca + gid * 4);

        private (int advance, int lsb) DefaultMetrics(int gid)
        {
            int advance = U16(_d, _hmtx + Math.Min(gid, _numHMetrics - 1) * 4);
            int lsb = gid < _numHMetrics ? I16(_d, _hmtx + gid * 4 + 2) : I16(_d, _hmtx + _numHMetrics * 4 + (gid - _numHMetrics) * 2);
            return (advance, lsb);
        }

        /// <summary>The instanced glyph, built once; a component cycle reads the inner reference as an empty glyph.</summary>
        private InstancedGlyph Instance(int gid)
        {
            var done = _glyphs[gid];
            if (done != null) return done;
            if (_inProgress[gid]) return new InstancedGlyph();
            _inProgress[gid] = true;

            var glyph = BuildGlyph(gid);
            _glyphs[gid] = glyph;
            _inProgress[gid] = false;
            return glyph;
        }

        private InstancedGlyph BuildGlyph(int gid)
        {
            var (advance0, lsb0) = DefaultMetrics(gid);
            int start = GlyphStart(gid), end = GlyphStart(gid + 1);
            int g = _glyf + start;

            var result = new InstancedGlyph { Advance = advance0, LeftSideBearing = lsb0 };
            int contours = end > start ? I16(_d, g) : 0;
            int xMin0 = end > start ? I16(_d, g + 2) : 0;

            if (end <= start)
            {
                ApplyMetricsOnly(gid, result);
                return result;
            }

            if (contours >= 0) BuildSimpleGlyph(gid, g, contours, xMin0, lsb0, result);
            else BuildCompositeGlyph(gid, g, xMin0, lsb0, result);
            return result;
        }

        private void ApplyMetricsOnly(int gid, InstancedGlyph result)
        {
            var dx = new float[4]; var dy = new float[4];
            _gvar.Accumulate(gid, _coords, 4, null, new float[4], new float[4], dx, dy);
            SetAdvance(gid, result, dx);
        }

        /// <summary>Advance from HVAR when the font has it, else from the phantom points' gvar deltas.</summary>
        private void SetAdvance(int gid, InstancedGlyph result, float[] phantomDx)
        {
            float delta = _hvar != null ? _hvar.AdvanceDelta(gid) : phantomDx[phantomDx.Length - 3] - phantomDx[phantomDx.Length - 4];
            result.Advance = Math.Max(0, (int)Math.Round(result.Advance + delta));
        }

        /// <summary>
        /// Apply the gvar deltas to a simple glyph's points and its two horizontal phantom points (the vertical ones
        /// are unused). The left phantom point moves too, so the new side bearing is xMin minus its new position.
        /// </summary>
        private void BuildSimpleGlyph(int gid, int g, int contours, int xMin0, int lsb0, InstancedGlyph result)
        {
            var ends = new int[contours];
            for (int i = 0; i < contours; i++) ends[i] = U16(_d, g + 10 + i * 2);
            int pointCount = contours == 0 ? 0 : ends[contours - 1] + 1;

            int p = g + 10 + contours * 2;
            int instructionLength = U16(_d, p);
            p += 2 + instructionLength;

            var flags = new byte[pointCount];
            for (int i = 0; i < pointCount;)
            {
                byte f = _d[p++];
                flags[i++] = f;
                if ((f & 0x08) != 0)
                {
                    int repeat = _d[p++];
                    while (repeat-- > 0 && i < pointCount) flags[i++] = f;
                }
            }

            var xs = new float[pointCount + 4]; var ys = new float[pointCount + 4];
            int v = 0;
            for (int i = 0; i < pointCount; i++)
            {
                byte f = flags[i];
                if ((f & 0x02) != 0) { int d = _d[p++]; v += (f & 0x10) != 0 ? d : -d; }
                else if ((f & 0x10) == 0) { v += I16(_d, p); p += 2; }
                xs[i] = v;
            }
            v = 0;
            for (int i = 0; i < pointCount; i++)
            {
                byte f = flags[i];
                if ((f & 0x04) != 0) { int d = _d[p++]; v += (f & 0x20) != 0 ? d : -d; }
                else if ((f & 0x20) == 0) { v += I16(_d, p); p += 2; }
                ys[i] = v;
            }

            xs[pointCount] = xMin0 - lsb0;
            xs[pointCount + 1] = xMin0 - lsb0 + result.Advance;

            var dx = new float[pointCount + 4]; var dy = new float[pointCount + 4];
            _gvar.Accumulate(gid, _coords, pointCount + 4, ends, xs, ys, dx, dy);
            SetAdvance(gid, result, dx);

            var nx = new int[pointCount]; var ny = new int[pointCount];
            int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
            for (int i = 0; i < pointCount; i++)
            {
                nx[i] = (int)Math.Round(xs[i] + dx[i]);
                ny[i] = (int)Math.Round(ys[i] + dy[i]);
                xMin = Math.Min(xMin, nx[i]); xMax = Math.Max(xMax, nx[i]);
                yMin = Math.Min(yMin, ny[i]); yMax = Math.Max(yMax, ny[i]);
            }
            if (pointCount == 0) { xMin = yMin = xMax = yMax = 0; }

            result.HasOutline = pointCount > 0;
            result.XMin = xMin; result.YMin = yMin; result.XMax = xMax; result.YMax = yMax;
            result.PointsX = nx; result.PointsY = ny;
            float pp1 = xs[pointCount] + dx[pointCount];
            result.LeftSideBearing = (int)Math.Round(xMin - pp1);
            result.Data = WriteSimpleGlyph(ends, nx, ny, flags, xMin, yMin, xMax, yMax);
        }

        /// <summary>
        /// Apply the gvar deltas to a composite glyph: its "points" are the component offsets followed by the phantom
        /// points. It is re-serialized with word-sized arguments (a moved offset may no longer fit a byte) and no
        /// instructions. The composite's own points, for anchor matching and bounds, are its components' transformed
        /// points in order; an anchor-point component (no ARGS_ARE_XY_VALUES) aligns a child point with one already
        /// placed.
        /// </summary>
        private void BuildCompositeGlyph(int gid, int g, int xMin0, int lsb0, InstancedGlyph result)
        {
            var components = new List<Component>();
            int p = g + 10;
            int flags;
            do
            {
                var c = new Component { Flags = U16(_d, p), GlyphIndex = U16(_d, p + 2) };
                flags = c.Flags;
                p += 4;
                bool words = (flags & ArgsAreWords) != 0, xy = (flags & ArgsAreXyValues) != 0;
                if (words)
                {
                    c.Arg1 = xy ? I16(_d, p) : U16(_d, p);
                    c.Arg2 = xy ? I16(_d, p + 2) : U16(_d, p + 2);
                    p += 4;
                }
                else
                {
                    c.Arg1 = xy ? (sbyte)_d[p] : _d[p];
                    c.Arg2 = xy ? (sbyte)_d[p + 1] : _d[p + 1];
                    p += 2;
                }

                int transformBytes = (flags & WeHaveScale) != 0 ? 2 : (flags & WeHaveXyScale) != 0 ? 4 : (flags & WeHaveTwoByTwo) != 0 ? 8 : 0;
                c.Transform = new byte[transformBytes];
                Array.Copy(_d, p, c.Transform, 0, transformBytes);
                p += transformBytes;
                components.Add(c);
            } while ((flags & MoreComponents) != 0);

            int n = components.Count;
            var ox = new float[n + 4]; var oy = new float[n + 4];
            for (int i = 0; i < n; i++)
            {
                if ((components[i].Flags & ArgsAreXyValues) == 0) continue;
                ox[i] = components[i].Arg1; oy[i] = components[i].Arg2;
            }
            ox[n] = xMin0 - lsb0;
            ox[n + 1] = xMin0 - lsb0 + result.Advance;

            var dx = new float[n + 4]; var dy = new float[n + 4];
            _gvar.Accumulate(gid, _coords, n + 4, null, ox, oy, dx, dy);
            SetAdvance(gid, result, dx);

            var w = new List<byte>(10 + n * 12);
            void U16w(int v) { w.Add((byte)(v >> 8)); w.Add((byte)v); }

            var allX = new List<int>(); var allY = new List<int>();
            U16w(-1); U16w(0); U16w(0); U16w(0); U16w(0);
            for (int i = 0; i < n; i++)
            {
                var c = components[i];
                int argFlags = c.Flags | ArgsAreWords;
                argFlags &= ~WeHaveInstructions;
                if (i == n - 1) argFlags &= ~MoreComponents; else argFlags |= MoreComponents;

                int a1 = c.Arg1, a2 = c.Arg2;
                bool xy = (c.Flags & ArgsAreXyValues) != 0;
                if (xy)
                {
                    a1 = (int)Math.Round(c.Arg1 + dx[i]);
                    a2 = (int)Math.Round(c.Arg2 + dy[i]);
                }
                U16w(argFlags); U16w(c.GlyphIndex); U16w(a1); U16w(a2);
                foreach (byte b in c.Transform) w.Add(b);

                if (c.GlyphIndex >= _numGlyphs) continue;
                var child = Instance(c.GlyphIndex);
                var (tx, ty) = TransformPoints(child, c.Transform);

                int offX = a1, offY = a2;
                if (!xy)
                {
                    offX = offY = 0;
                    if (a1 < allX.Count && a2 < tx.Length) { offX = allX[a1] - tx[a2]; offY = allY[a1] - ty[a2]; }
                }
                else if ((c.Flags & ScaledComponentOffset) != 0)
                {
                    var (a, b, cc, d) = TransformMatrix(c.Transform);
                    offX = (int)Math.Round(a * a1 + cc * a2);
                    offY = (int)Math.Round(b * a1 + d * a2);
                }
                for (int k = 0; k < tx.Length; k++) { allX.Add(tx[k] + offX); allY.Add(ty[k] + offY); }
            }

            bool any = allX.Count > 0;
            int bxMin = 0, byMin = 0, bxMax = 0, byMax = 0;
            if (any)
            {
                bxMin = int.MaxValue; byMin = int.MaxValue; bxMax = int.MinValue; byMax = int.MinValue;
                for (int k = 0; k < allX.Count; k++)
                {
                    bxMin = Math.Min(bxMin, allX[k]); bxMax = Math.Max(bxMax, allX[k]);
                    byMin = Math.Min(byMin, allY[k]); byMax = Math.Max(byMax, allY[k]);
                }
            }
            var data = w.ToArray();
            PutI16(data, 2, bxMin); PutI16(data, 4, byMin); PutI16(data, 6, bxMax); PutI16(data, 8, byMax);

            result.HasOutline = any;
            result.XMin = bxMin; result.YMin = byMin; result.XMax = bxMax; result.YMax = byMax;
            result.PointsX = allX.ToArray(); result.PointsY = allY.ToArray();
            float pp1 = ox[n] + dx[n];
            result.LeftSideBearing = (int)Math.Round(bxMin - pp1);
            result.Data = data;
        }


        /// <summary>
        /// A child glyph's points under a component's 2x2 / scale transform, x' = a*x + c*y and y' = b*x + d*y (offset
        /// applied by the caller).
        /// </summary>
        private static (int[] x, int[] y) TransformPoints(InstancedGlyph child, byte[] t)
        {
            var (a, b, cc, d) = TransformMatrix(t);
            var xs = new int[child.PointsX.Length]; var ys = new int[xs.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                xs[i] = (int)Math.Round(a * child.PointsX[i] + cc * child.PointsY[i]);
                ys[i] = (int)Math.Round(b * child.PointsX[i] + d * child.PointsY[i]);
            }
            return (xs, ys);
        }

        /// <summary>A component's transform as the matrix (a, b, c, d) of x' = a*x + c*y, y' = b*x + d*y.</summary>
        private static (float a, float b, float c, float d) TransformMatrix(byte[] t)
        {
            if (t.Length == 2) { float s = F2Dot14(t, 0); return (s, 0, 0, s); }
            if (t.Length == 4) return (F2Dot14(t, 0), 0, 0, F2Dot14(t, 2));
            if (t.Length == 8) return (F2Dot14(t, 0), F2Dot14(t, 2), F2Dot14(t, 4), F2Dot14(t, 6));
            return (1, 0, 0, 1);
        }

        private byte[] Assemble() => AssembleFont(_s, _glyphs!, _numHMetrics);
    }

    private static readonly HashSet<string> DroppedTables = new HashSet<string>
    {
        "fvar", "gvar", "avar", "HVAR", "VVAR", "MVAR", "STAT", "cvar", "DSIG", "hdmx", "LTSH", "VDMX", "CFF ", "CFF2", "VORG",
    };

    /// <summary>
    /// A static TrueType font from the source's non-outline tables plus freshly built glyphs (glyf, long loca, hmtx,
    /// head/hhea bounds, a version 1.0 maxp). Variation and PostScript-outline tables are dropped. hmtx keeps its
    /// layout, so glyphs past numberOfHMetrics still share the last advance. head's checkSumAdjustment is zeroed
    /// rather than recomputed; a CFF source's version 0.5 maxp is replaced by a 1.0 table, which glyf outlines need,
    /// with maxContours bounded by maxPoints and two zones. glyf and loca are added even when the source had none
    /// (PostScript outlines).
    /// </summary>
    private static byte[] AssembleFont(Sfnt source, InstancedGlyph?[] glyphs, int numHMetrics)
    {
        int numGlyphs = glyphs.Length;
        var glyf = new List<byte>();
        var loca = new byte[(numGlyphs + 1) * 4];
        for (int g = 0; g < numGlyphs; g++)
        {
            PutU32(loca, g * 4, (uint)glyf.Count);
            glyf.AddRange(glyphs[g]!.Data);
            while (glyf.Count % 4 != 0) glyf.Add(0);
        }
        PutU32(loca, numGlyphs * 4, (uint)glyf.Count);

        var hmtx = new byte[numHMetrics * 4 + (numGlyphs - numHMetrics) * 2];
        int advanceMax = 0, maxPoints = 0;
        for (int g = 0; g < numGlyphs; g++)
        {
            var glyph = glyphs[g]!;
            maxPoints = Math.Max(maxPoints, glyph.PointsX.Length);
            if (g < numHMetrics)
            {
                PutI16(hmtx, g * 4, glyph.Advance);
                PutI16(hmtx, g * 4 + 2, glyph.LeftSideBearing);
                advanceMax = Math.Max(advanceMax, glyph.Advance);
            }
            else
            {
                PutI16(hmtx, numHMetrics * 4 + (g - numHMetrics) * 2, glyph.LeftSideBearing);
            }
        }

        var replaced = new Dictionary<string, byte[]> { ["glyf"] = glyf.ToArray(), ["loca"] = loca, ["hmtx"] = hmtx };

        var head = Slice(source, "head");
        PutI16(head, HeadIndexToLocFormat, 1);
        PutU32(head, HeadCheckSumAdjustment, 0);
        int gxMin = int.MaxValue, gyMin = int.MaxValue, gxMax = int.MinValue, gyMax = int.MinValue;
        foreach (var glyph in glyphs)
        {
            if (glyph == null || !glyph.HasOutline) continue;
            gxMin = Math.Min(gxMin, glyph.XMin); gyMin = Math.Min(gyMin, glyph.YMin);
            gxMax = Math.Max(gxMax, glyph.XMax); gyMax = Math.Max(gyMax, glyph.YMax);
        }
        if (gxMin != int.MaxValue) { PutI16(head, 36, gxMin); PutI16(head, 38, gyMin); PutI16(head, 40, gxMax); PutI16(head, 42, gyMax); }
        replaced["head"] = head;

        var hhea = Slice(source, "hhea");
        PutI16(hhea, HheaAdvanceWidthMax, advanceMax);
        WriteHorizontalExtents(hhea, glyphs, numHMetrics);
        replaced["hhea"] = hhea;

        var maxp = Slice(source, "maxp");
        if (U32(maxp, 0) == 0x00005000)
        {
            maxp = new byte[32];
            PutU32(maxp, 0, 0x00010000);
            PutI16(maxp, MaxpNumGlyphs, numGlyphs);
            PutI16(maxp, MaxpMaxPoints, maxPoints);
            PutI16(maxp, MaxpMaxContours, maxPoints);
            PutI16(maxp, MaxpMaxZones, 2);
        }
        replaced["maxp"] = maxp;

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var tag in source.Tags)
        {
            if (DroppedTables.Contains(tag)) continue;
            tables[tag] = replaced.TryGetValue(tag, out var body) ? body : Slice(source, tag);
        }
        foreach (var kv in replaced) tables[kv.Key] = kv.Value;
        return WriteFont(tables);
    }

    /// <summary>
    /// hhea's minLeftSideBearing, minRightSideBearing and xMaxExtent for the instanced glyphs (those with an outline),
    /// so they describe the instance rather than the source's default location.
    /// </summary>
    private static void WriteHorizontalExtents(byte[] hhea, InstancedGlyph?[] glyphs, int numHMetrics)
    {
        int minLsb = int.MaxValue, minRsb = int.MaxValue, maxExtent = int.MinValue;
        int sharedAdvance = numHMetrics > 0 && numHMetrics <= glyphs.Length ? glyphs[numHMetrics - 1]!.Advance : 0;
        for (int g = 0; g < glyphs.Length; g++)
        {
            var glyph = glyphs[g]!;
            if (!glyph.HasOutline) continue;
            int advance = g < numHMetrics ? glyph.Advance : sharedAdvance;
            int extent = glyph.LeftSideBearing + (glyph.XMax - glyph.XMin);
            minLsb = Math.Min(minLsb, glyph.LeftSideBearing);
            minRsb = Math.Min(minRsb, advance - extent);
            maxExtent = Math.Max(maxExtent, extent);
        }
        if (maxExtent == int.MinValue) return;
        PutI16(hhea, HheaMinLeftSideBearing, minLsb);
        PutI16(hhea, HheaMinRightSideBearing, minRsb);
        PutI16(hhea, HheaXMaxExtent, maxExtent);
    }

    private static byte[] Slice(Sfnt sfnt, string tag)
    {
        var b = new byte[sfnt.Length(tag)];
        Array.Copy(sfnt.Data, sfnt.Offset(tag), b, 0, b.Length);
        return b;
    }
}
