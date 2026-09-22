using System;

namespace EggPdf.Text.TrueType;

/// <summary>
/// An OpenType ItemVariationStore (HVAR, CFF2, MVAR...): variation regions plus delta sets, evaluated at a
/// normalized design-space location. Each delta set is a list of per-region deltas; a region's scalar says how
/// strongly the current location falls inside it.
/// </summary>
internal sealed class ItemVariationStore
{
    private readonly byte[] _d;
    private readonly int _store;
    private readonly float[] _coords;

    /// <param name="data">The font (or table) bytes.</param>
    /// <param name="storeOffset">Absolute offset of the store's format field.</param>
    /// <param name="coords">Normalized axis coordinates.</param>
    public ItemVariationStore(byte[] data, int storeOffset, float[] coords)
    {
        _d = data; _store = storeOffset; _coords = coords;
    }

    public int DataCount => U16(_store + 6);

    private int U16(int o) => (_d[o] << 8) | _d[o + 1];
    private int I16(int o) => (short)((_d[o] << 8) | _d[o + 1]);
    private uint U32(int o) => (uint)((_d[o] << 24) | (_d[o + 1] << 16) | (_d[o + 2] << 8) | _d[o + 3]);
    private float F2Dot14(int o) => I16(o) / 16384f;

    /// <summary>The region scalars of one ItemVariationData subtable, in the order its deltas are stored.</summary>
    public float[] Scalars(int dataIndex)
    {
        if (dataIndex < 0 || dataIndex >= DataCount) return Array.Empty<float>();

        int regionListAt = _store + (int)U32(_store + 2);
        int axisCount = U16(regionListAt), regionCount = U16(regionListAt + 2);
        int ivd = _store + (int)U32(_store + 8 + dataIndex * 4);
        int regionIndexCount = U16(ivd + 4);

        var scalars = new float[regionIndexCount];
        for (int r = 0; r < regionIndexCount; r++)
        {
            int region = U16(ivd + 6 + r * 2);
            scalars[r] = region < regionCount ? RegionScalar(regionListAt + 4 + region * axisCount * 6, axisCount) : 0f;
        }
        return scalars;
    }

    /// <summary>The scaled sum of one delta set's values (outer = subtable index, inner = row).</summary>
    public float Delta(int outer, int inner)
    {
        if (outer < 0 || outer >= DataCount) return 0f;

        int ivd = _store + (int)U32(_store + 8 + outer * 4);
        int itemCount = U16(ivd);
        int wordDeltaField = U16(ivd + 2);
        bool longWords = (wordDeltaField & 0x8000) != 0;
        int wordCount = wordDeltaField & 0x7FFF;
        int regionIndexCount = U16(ivd + 4);
        if (inner < 0 || inner >= itemCount) return 0f;

        var scalars = Scalars(outer);
        int rowSize = longWords ? wordCount * 4 + (regionIndexCount - wordCount) * 2 : wordCount * 2 + (regionIndexCount - wordCount);
        int at = ivd + 6 + regionIndexCount * 2 + inner * rowSize;

        float total = 0f;
        for (int r = 0; r < regionIndexCount; r++)
        {
            int delta;
            if (r < wordCount) { delta = longWords ? (int)U32(at) : I16(at); at += longWords ? 4 : 2; }
            else { delta = longWords ? I16(at) : (sbyte)_d[at]; at += longWords ? 2 : 1; }
            total += delta * scalars[r];
        }
        return total;
    }

    private float RegionScalar(int regionAt, int axisCount)
    {
        float scalar = 1f;
        for (int a = 0; a < axisCount && a < _coords.Length; a++)
        {
            float start = F2Dot14(regionAt + a * 6), peak = F2Dot14(regionAt + a * 6 + 2), end = F2Dot14(regionAt + a * 6 + 4);
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
