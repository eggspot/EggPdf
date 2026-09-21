using System;
using System.Collections.Generic;

namespace EggPdf.Text.TrueType;

public static partial class VariableFontInstancer
{
    /// <summary>
    /// The glyph variation data of a font's gvar table: per-glyph tuple variations whose
    /// deltas, scaled by how close the requested design-space location is to each tuple's peak,
    /// are summed onto the default outline (OpenType "gvar" table).
    /// </summary>
    private sealed class GvarTable
    {
        private readonly byte[] _d;
        private readonly int _table, _axisCount, _glyphCount, _dataArray;
        private readonly bool _longOffsets;
        private readonly int _offsetsAt;
        private readonly float[][] _sharedTuples;

        public GvarTable(byte[] data, int table)
        {
            _d = data;
            _table = table;
            _axisCount = U16(data, table + 4);
            int sharedCount = U16(data, table + 6);
            int sharedOffset = (int)U32(data, table + 8);
            _glyphCount = U16(data, table + 12);
            _longOffsets = (U16(data, table + 14) & 1) != 0;
            _dataArray = table + (int)U32(data, table + 16);
            _offsetsAt = table + 20;

            _sharedTuples = new float[sharedCount][];
            for (int i = 0; i < sharedCount; i++)
            {
                var t = new float[_axisCount];
                for (int a = 0; a < _axisCount; a++) t[a] = F2Dot14(data, table + sharedOffset + (i * _axisCount + a) * 2);
                _sharedTuples[i] = t;
            }
        }

        private int GlyphDataOffset(int gid)
            => _longOffsets ? (int)U32(_d, _offsetsAt + gid * 4) : U16(_d, _offsetsAt + gid * 2) * 2;

        /// <summary>
        /// Add this glyph's scaled tuple deltas into <paramref name="dx"/>/<paramref name="dy"/> (one entry per outline
        /// point plus the four phantom points). <paramref name="contourEnds"/> enables IUP interpolation for simple glyphs.
        /// </summary>
        public void Accumulate(int gid, float[] coords, int pointCount, int[]? contourEnds,
            float[] origX, float[] origY, float[] dx, float[] dy)
        {
            if (gid < 0 || gid >= _glyphCount) return;
            int start = GlyphDataOffset(gid), end = GlyphDataOffset(gid + 1);
            if (end <= start) return; // no variation data for this glyph

            int g = _dataArray + start;
            int tupleCountField = U16(_d, g);
            int tupleCount = tupleCountField & 0x0FFF;
            bool sharedPoints = (tupleCountField & 0x8000) != 0;
            int serialized = g + U16(_d, g + 2);

            // Tuple variation headers
            int header = g + 4;
            var headers = new List<TupleHeader>(tupleCount);
            for (int i = 0; i < tupleCount; i++)
            {
                int size = U16(_d, header), index = U16(_d, header + 2);
                header += 4;

                float[] peak;
                if ((index & 0x8000) != 0)
                {
                    peak = new float[_axisCount];
                    for (int a = 0; a < _axisCount; a++) peak[a] = F2Dot14(_d, header + a * 2);
                    header += _axisCount * 2;
                }
                else
                {
                    peak = _sharedTuples[index & 0x0FFF];
                }

                float[]? intermediateStart = null, intermediateEnd = null;
                if ((index & 0x4000) != 0)
                {
                    intermediateStart = new float[_axisCount]; intermediateEnd = new float[_axisCount];
                    for (int a = 0; a < _axisCount; a++) intermediateStart[a] = F2Dot14(_d, header + a * 2);
                    header += _axisCount * 2;
                    for (int a = 0; a < _axisCount; a++) intermediateEnd[a] = F2Dot14(_d, header + a * 2);
                    header += _axisCount * 2;
                }
                headers.Add(new TupleHeader(size, (index & 0x2000) != 0, peak, intermediateStart, intermediateEnd));
            }

            // Serialized data: optional shared point numbers, then each tuple's (optional private points +) deltas
            int pos = serialized;
            int[]? shared = null;
            if (sharedPoints) shared = ReadPackedPoints(ref pos, pointCount);

            foreach (var h in headers)
            {
                int tupleEnd = pos + h.DataSize;
                int[]? points = shared;
                if (h.PrivatePoints) points = ReadPackedPoints(ref pos, pointCount);

                float scalar = Scalar(coords, h);
                if (scalar == 0f) { pos = tupleEnd; continue; }

                int deltaCount = points == null ? pointCount : points.Length;
                var deltaX = ReadPackedDeltas(ref pos, deltaCount);
                var deltaY = ReadPackedDeltas(ref pos, deltaCount);
                pos = tupleEnd;

                if (points == null)
                {
                    for (int i = 0; i < pointCount; i++) { dx[i] += scalar * deltaX[i]; dy[i] += scalar * deltaY[i]; }
                }
                else
                {
                    var tx = new float[pointCount]; var ty = new float[pointCount];
                    var touched = new bool[pointCount];
                    for (int i = 0; i < points.Length; i++)
                    {
                        int p = points[i];
                        if (p < 0 || p >= pointCount) continue;
                        tx[p] = deltaX[i]; ty[p] = deltaY[i]; touched[p] = true;
                    }
                    if (contourEnds != null) InterpolateUntouched(contourEnds, touched, origX, origY, tx, ty);
                    for (int i = 0; i < pointCount; i++) { dx[i] += scalar * tx[i]; dy[i] += scalar * ty[i]; }
                }
            }
        }

        private readonly struct TupleHeader
        {
            public readonly int DataSize;
            public readonly bool PrivatePoints;
            public readonly float[] Peak;
            public readonly float[]? Start, End;
            public TupleHeader(int size, bool privatePoints, float[] peak, float[]? start, float[]? end)
            { DataSize = size; PrivatePoints = privatePoints; Peak = peak; Start = start; End = end; }
        }

        /// <summary>How strongly a tuple applies at the requested location: the product of its per-axis tent functions.</summary>
        private static float Scalar(float[] coords, TupleHeader h)
        {
            float scalar = 1f;
            for (int a = 0; a < h.Peak.Length && a < coords.Length; a++)
            {
                float peak = h.Peak[a];
                if (peak == 0f) continue;

                float lower = h.Start != null ? h.Start[a] : Math.Min(0f, peak);
                float upper = h.End != null ? h.End[a] : Math.Max(0f, peak);
                float c = coords[a];
                if (c == peak) continue;
                if (c <= lower || c >= upper) return 0f;
                scalar *= c < peak ? (c - lower) / (peak - lower) : (upper - c) / (upper - peak);
            }
            return scalar;
        }

        /// <summary>Packed point numbers: null means "all points".</summary>
        private int[]? ReadPackedPoints(ref int pos, int pointCount)
        {
            int count = _d[pos++];
            if ((count & 0x80) != 0) count = ((count & 0x7F) << 8) | _d[pos++];
            if (count == 0) return null;

            var points = new int[count];
            int read = 0, last = 0;
            while (read < count)
            {
                int control = _d[pos++];
                bool words = (control & 0x80) != 0;
                int run = (control & 0x7F) + 1;
                for (int i = 0; i < run && read < count; i++)
                {
                    int delta = words ? U16(_d, pos) : _d[pos];
                    pos += words ? 2 : 1;
                    last += delta;
                    points[read++] = last;
                }
            }
            return points;
        }

        private float[] ReadPackedDeltas(ref int pos, int count)
        {
            var result = new float[count];
            int read = 0;
            while (read < count)
            {
                int control = _d[pos++];
                int run = (control & 0x3F) + 1;
                if ((control & 0x80) != 0)
                {
                    read += run; // a run of zero deltas
                }
                else if ((control & 0x40) != 0)
                {
                    for (int i = 0; i < run && read < count; i++) { result[read++] = I16(_d, pos); pos += 2; }
                }
                else
                {
                    for (int i = 0; i < run && read < count; i++) result[read++] = (sbyte)_d[pos++];
                }
            }
            return result;
        }
    }

    /// <summary>
    /// IUP (interpolate untouched points): points a tuple doesn't list get deltas interpolated, per axis and per
    /// contour, from the nearest touched points on either side in outline order.
    /// </summary>
    private static void InterpolateUntouched(int[] contourEnds, bool[] touched, float[] origX, float[] origY, float[] dx, float[] dy)
    {
        int start = 0;
        foreach (int end in contourEnds)
        {
            if (end >= start && end < touched.Length)
            {
                var touchedIdx = new List<int>();
                for (int i = start; i <= end; i++) if (touched[i]) touchedIdx.Add(i);

                if (touchedIdx.Count == 1)
                {
                    for (int i = start; i <= end; i++) { dx[i] = dx[touchedIdx[0]]; dy[i] = dy[touchedIdx[0]]; }
                }
                else if (touchedIdx.Count > 1)
                {
                    for (int k = 0; k < touchedIdx.Count; k++)
                    {
                        int a = touchedIdx[k], b = touchedIdx[(k + 1) % touchedIdx.Count];
                        InterpolateBetween(start, end, a, b, origX, dx);
                        InterpolateBetween(start, end, a, b, origY, dy);
                    }
                }
            }
            start = end + 1;
        }
    }

    /// <summary>Interpolate one axis' deltas for the untouched points strictly between touched points a and b (cyclic).</summary>
    private static void InterpolateBetween(int contourStart, int contourEnd, int a, int b, float[] coords, float[] deltas)
    {
        int length = contourEnd - contourStart + 1;
        float x1 = coords[a], x2 = coords[b], d1 = deltas[a], d2 = deltas[b];
        if (x1 > x2) { (x1, x2) = (x2, x1); (d1, d2) = (d2, d1); }

        for (int i = (a - contourStart + 1) % length; contourStart + i != b; i = (i + 1) % length)
        {
            int p = contourStart + i;
            float x = coords[p];
            float d;
            if (x1 == x2) d = d1 == d2 ? d1 : 0f;
            else if (x <= x1) d = d1;
            else if (x >= x2) d = d2;
            else d = d1 + (x - x1) * (d2 - d1) / (x2 - x1);
            deltas[p] = d;
        }
    }

    // ── HVAR: advance width variations ───────────────────────────────────────

    /// <summary>Horizontal metrics variations: an item variation store plus an optional glyph -> delta-set map.</summary>
    private sealed class HvarTable
    {
        private readonly byte[] _d;
        private readonly int _store;
        private readonly int _advanceMap;   // absolute offset of the DeltaSetIndexMap, or 0
        private readonly float[] _coords;

        public HvarTable(byte[] data, int table, float[] coords)
        {
            _d = data;
            _coords = coords;
            _store = table + (int)U32(data, table + 4);
            int mapOffset = (int)U32(data, table + 8);
            _advanceMap = mapOffset == 0 ? 0 : table + mapOffset;
        }

        /// <summary>The advance-width delta for a glyph at the requested location.</summary>
        public float AdvanceDelta(int gid)
        {
            int outer = 0, inner = gid;
            if (_advanceMap != 0 && !ReadMap(gid, out outer, out inner)) return 0f;
            return ItemDelta(outer, inner);
        }

        private bool ReadMap(int gid, out int outer, out int inner)
        {
            int format = _d[_advanceMap];
            int entryFormat = _d[_advanceMap + 1];
            int mapCount = format == 0 ? U16(_d, _advanceMap + 2) : (int)U32(_d, _advanceMap + 2);
            int header = format == 0 ? 4 : 6;
            int innerBits = (entryFormat & 0x0F) + 1;
            int entrySize = ((entryFormat & 0x30) >> 4) + 1;

            outer = inner = 0;
            if (mapCount == 0) return false;
            int index = Math.Min(gid, mapCount - 1);
            int at = _advanceMap + header + index * entrySize;
            uint entry = 0;
            for (int i = 0; i < entrySize; i++) entry = (entry << 8) | _d[at + i];
            outer = (int)(entry >> innerBits);
            inner = (int)(entry & ((1u << innerBits) - 1));
            return true;
        }

        private float ItemDelta(int outer, int inner)
        {
            int dataCount = U16(_d, _store + 6);
            if (outer >= dataCount) return 0f;

            int regionListAt = _store + (int)U32(_d, _store + 2);
            int axisCount = U16(_d, regionListAt), regionCount = U16(_d, regionListAt + 2);

            int ivd = _store + (int)U32(_d, _store + 8 + outer * 4);
            int itemCount = U16(_d, ivd);
            int wordDeltaField = U16(_d, ivd + 2);
            bool longWords = (wordDeltaField & 0x8000) != 0;
            int wordCount = wordDeltaField & 0x7FFF;
            int regionIndexCount = U16(_d, ivd + 4);
            if (inner >= itemCount) return 0f;

            int regionIndexes = ivd + 6;
            int rowSize = longWords ? wordCount * 4 + (regionIndexCount - wordCount) * 2 : wordCount * 2 + (regionIndexCount - wordCount);
            int row = regionIndexes + regionIndexCount * 2 + inner * rowSize;

            float total = 0f;
            int at = row;
            for (int r = 0; r < regionIndexCount; r++)
            {
                int delta;
                if (r < wordCount) { delta = longWords ? (int)U32(_d, at) : I16(_d, at); at += longWords ? 4 : 2; }
                else { delta = longWords ? I16(_d, at) : (sbyte)_d[at]; at += longWords ? 2 : 1; }
                if (delta == 0) continue;

                int region = U16(_d, regionIndexes + r * 2);
                if (region >= regionCount) continue;
                total += delta * RegionScalar(regionListAt + 4 + region * axisCount * 6, axisCount);
            }
            return total;
        }

        private float RegionScalar(int regionAt, int axisCount)
        {
            float scalar = 1f;
            for (int a = 0; a < axisCount && a < _coords.Length; a++)
            {
                float start = F2Dot14(_d, regionAt + a * 6), peak = F2Dot14(_d, regionAt + a * 6 + 2), end = F2Dot14(_d, regionAt + a * 6 + 4);
                float c = _coords[a];
                if (peak == 0f || start > peak || peak > end) continue;
                if (start < 0f && end > 0f) continue;
                if (c == peak) continue;
                if (c <= start || c >= end) return 0f;
                scalar *= c < peak ? (c - start) / (peak - start) : (end - c) / (end - peak);
            }
            return scalar;
        }
    }
}
