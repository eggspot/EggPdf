using System;
using System.Collections.Generic;

namespace EggPdf.Text.TrueType;

/// <summary>Serialization helpers shared by the code that builds TrueType fonts (variable-font instancing, CFF conversion).</summary>
internal static class SfntWriter
{
    public static void PutI16(byte[] a, int at, int v) { a[at] = (byte)(v >> 8); a[at + 1] = (byte)v; }

    public static void PutU32(byte[] a, int at, uint v)
    {
        a[at] = (byte)(v >> 24); a[at + 1] = (byte)(v >> 16); a[at + 2] = (byte)(v >> 8); a[at + 3] = (byte)v;
    }

    private static uint Checksum(byte[] data)
    {
        uint sum = 0;
        for (int i = 0; i < data.Length; i += 4)
        {
            uint word = 0;
            for (int k = 0; k < 4; k++) word = (word << 8) | (i + k < data.Length ? data[i + k] : (byte)0);
            sum += word;
        }
        return sum;
    }

    private const uint ChecksumMagic = 0xB1B0AFBA;
    private const int HeadCheckSumAdjustmentOffset = 8;

    /// <summary>
    /// Assemble a TrueType (0x00010000) font file from its tables, in ordinal tag order as the format requires,
    /// with the head table's checkSumAdjustment set so the whole file sums to 0xB1B0AFBA.
    /// </summary>
    public static byte[] WriteFont(IDictionary<string, byte[]> tables)
    {
        var tags = new List<string>(tables.Keys);
        tags.Sort(StringComparer.Ordinal);

        int count = tags.Count;
        int dirSize = 12 + count * 16;
        int total = dirSize;
        foreach (var t in tables.Values) total += (t.Length + 3) & ~3;

        var output = new byte[total];
        PutU32(output, 0, 0x00010000);
        PutI16(output, 4, count);
        int entrySelector = 0;
        while ((1 << (entrySelector + 1)) <= count) entrySelector++;
        int searchRange = (1 << entrySelector) * 16;
        PutI16(output, 6, searchRange);
        PutI16(output, 8, entrySelector);
        PutI16(output, 10, count * 16 - searchRange);

        int record = 12, offset = dirSize, headOffset = -1;
        foreach (var tag in tags)
        {
            var table = tables[tag];
            if (tag == "head" && table.Length >= HeadCheckSumAdjustmentOffset + 4)
            {
                table = (byte[])table.Clone();
                PutU32(table, HeadCheckSumAdjustmentOffset, 0);
                headOffset = offset;
            }
            for (int i = 0; i < 4; i++) output[record + i] = (byte)tag[i];
            PutU32(output, record + 4, Checksum(table));
            PutU32(output, record + 8, (uint)offset);
            PutU32(output, record + 12, (uint)table.Length);
            Array.Copy(table, 0, output, offset, table.Length);
            offset += (table.Length + 3) & ~3;
            record += 16;
        }
        if (headOffset >= 0)
            PutU32(output, headOffset + HeadCheckSumAdjustmentOffset, unchecked(ChecksumMagic - Checksum(output)));
        return output;
    }

    /// <summary>
    /// A simple glyph record: contour end points, on/off-curve flags (bit 0 of each entry) and absolute coordinates,
    /// written with 16-bit deltas and no hinting instructions.
    /// </summary>
    public static byte[] WriteSimpleGlyph(int[] ends, int[] xs, int[] ys, byte[] flags, int xMin, int yMin, int xMax, int yMax)
    {
        int n = xs.Length;
        var w = new List<byte>(10 + ends.Length * 2 + n * 5);
        void U16w(int v) { w.Add((byte)(v >> 8)); w.Add((byte)v); }

        U16w(ends.Length);
        U16w(xMin); U16w(yMin); U16w(xMax); U16w(yMax);
        foreach (int e in ends) U16w(e);
        U16w(0);

        for (int i = 0; i < n; i++) w.Add((byte)(flags[i] & 0x01));
        int prev = 0;
        for (int i = 0; i < n; i++) { U16w(xs[i] - prev); prev = xs[i]; }
        prev = 0;
        for (int i = 0; i < n; i++) { U16w(ys[i] - prev); prev = ys[i]; }
        return w.ToArray();
    }
}
