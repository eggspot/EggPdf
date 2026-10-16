using System;
using System.Collections.Generic;
using System.IO;

namespace EggPdf.Text.Fallback;

/// <summary>
/// The set of Unicode code points a font face maps to a glyph, stored as sorted merged ranges.
/// Built from the face's cmap table alone (no outline or metrics parsing), so indexing an
/// installed font costs a few kilobytes of I/O instead of loading the whole file.
/// </summary>
internal sealed class FontCoverage
{
    private readonly int[] _starts;
    private readonly int[] _ends;

    /// <summary>Number of distinct code points covered.</summary>
    public int Count { get; }

    /// <summary>
    /// True when the face carries drawable outlines (glyf / CFF / CFF2, or a COLR layer font).
    /// Bitmap-only color fonts (sbix, CBDT) map code points but this engine cannot paint them.
    /// </summary>
    public bool HasOutlines { get; }

    private FontCoverage(int[] starts, int[] ends, int count, bool hasOutlines)
    {
        _starts = starts;
        _ends = ends;
        Count = count;
        HasOutlines = hasOutlines;
    }

    /// <summary>True when the face maps <paramref name="codepoint"/> to a glyph.</summary>
    public bool Contains(int codepoint)
    {
        int lo = 0, hi = _starts.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (codepoint < _starts[mid]) hi = mid - 1;
            else if (codepoint > _ends[mid]) lo = mid + 1;
            else return true;
        }
        return false;
    }

    internal static FontCoverage FromRanges(List<(int start, int end)> ranges, bool hasOutlines)
    {
        ranges.Sort((a, b) => a.start != b.start ? a.start.CompareTo(b.start) : a.end.CompareTo(b.end));
        var starts = new List<int>(ranges.Count);
        var ends = new List<int>(ranges.Count);
        long count = 0;
        foreach (var (s, e) in ranges)
        {
            if (starts.Count > 0 && s <= ends[ends.Count - 1] + 1)
            {
                if (e > ends[ends.Count - 1]) ends[ends.Count - 1] = e;
            }
            else
            {
                starts.Add(s);
                ends.Add(e);
            }
        }
        for (int i = 0; i < starts.Count; i++) count += ends[i] - starts[i] + 1;
        return new FontCoverage(starts.ToArray(), ends.ToArray(), (int)Math.Min(count, int.MaxValue), hasOutlines);
    }
}

/// <summary>
/// Reads <see cref="FontCoverage"/> for every face of a font file by streaming only the sfnt table
/// directory and the cmap table. Handles single fonts (TrueType/OpenType) and TrueType Collections.
/// Never throws: unreadable or malformed files yield no faces.
/// </summary>
internal static class FontCoverageReader
{
    private const int MaxFaces = 64;
    private const int MaxCmapBytes = 8 * 1024 * 1024;

    /// <summary>Coverage of each face in the file, in face-index order (null for an unreadable face).</summary>
    public static FontCoverage?[] ReadFaces(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = ReadAt(stream, 0, 12);
            if (header == null) return Array.Empty<FontCoverage?>();

            if (U32(header, 0) == 0x74746366) // 'ttcf'
            {
                int numFaces = (int)Math.Min(U32(header, 8), MaxFaces);
                var faces = new FontCoverage?[numFaces];
                var offsets = ReadAt(stream, 12, numFaces * 4);
                if (offsets == null) return Array.Empty<FontCoverage?>();
                for (int i = 0; i < numFaces; i++)
                    faces[i] = ReadFace(stream, U32(offsets, i * 4));
                return faces;
            }

            return new[] { ReadFace(stream, 0) };
        }
        catch
        {
            return Array.Empty<FontCoverage?>();
        }
    }

    private static FontCoverage? ReadFace(Stream stream, long faceOffset)
    {
        var head = ReadAt(stream, faceOffset, 12);
        if (head == null) return null;
        uint version = U32(head, 0);
        if (version != 0x00010000 && version != 0x4F54544F && version != 0x74727565) // 1.0, 'OTTO', 'true'
            return null;

        int numTables = (head[4] << 8) | head[5];
        if (numTables == 0 || numTables > 256) return null;
        var dir = ReadAt(stream, faceOffset + 12, numTables * 16);
        if (dir == null) return null;

        long cmapOffset = -1, cmapLength = 0;
        bool glyf = false, cff = false, colr = false, bitmapColor = false;
        for (int i = 0; i < numTables; i++)
        {
            int rec = i * 16;
            string tag = new string(new[] { (char)dir[rec], (char)dir[rec + 1], (char)dir[rec + 2], (char)dir[rec + 3] });
            switch (tag)
            {
                case "cmap": cmapOffset = U32(dir, rec + 8); cmapLength = U32(dir, rec + 12); break;
                case "glyf": glyf = true; break;
                case "CFF ": case "CFF2": cff = true; break;
                case "COLR": colr = true; break;
                case "sbix": case "CBDT": bitmapColor = true; break;
            }
        }
        if (cmapOffset < 0 || cmapLength < 4 || cmapLength > MaxCmapBytes) return null;

        var cmap = ReadAt(stream, cmapOffset, (int)cmapLength);
        if (cmap == null) return null;

        var ranges = new List<(int start, int end)>();
        ParseCmap(cmap, ranges);
        if (ranges.Count == 0) return null;

        bool outlines = (glyf || cff) && (!bitmapColor || colr);
        return FontCoverage.FromRanges(ranges, outlines);
    }

    /// <summary>Union of every Unicode cmap subtable (format 4 and 12) in the table.</summary>
    private static void ParseCmap(byte[] cmap, List<(int start, int end)> ranges)
    {
        int numSubtables = U16(cmap, 2);
        for (int i = 0; i < numSubtables; i++)
        {
            int rec = 4 + i * 8;
            if (rec + 8 > cmap.Length) return;
            int platform = U16(cmap, rec);
            int encoding = U16(cmap, rec + 2);
            if (platform != 0 && !(platform == 3 && (encoding == 1 || encoding == 10))) continue;

            long sub = U32(cmap, rec + 4);
            if (sub + 4 > cmap.Length) continue;
            int format = U16(cmap, (int)sub);
            if (format == 4) ParseFormat4(cmap, (int)sub, ranges);
            else if (format == 12) ParseFormat12(cmap, (int)sub, ranges);
        }
    }

    private static void ParseFormat12(byte[] d, int at, List<(int start, int end)> ranges)
    {
        if (at + 16 > d.Length) return;
        long groups = U32(d, at + 12);
        for (long g = 0; g < groups; g++)
        {
            int p = at + 16 + (int)(g * 12);
            if (p + 12 > d.Length) return;
            long start = U32(d, p), end = U32(d, p + 4);
            if (end < start || end > 0x10FFFF) continue;
            ranges.Add(((int)start, (int)end));
        }
    }

    private static void ParseFormat4(byte[] d, int at, List<(int start, int end)> ranges)
    {
        if (at + 14 > d.Length) return;
        int segCount = U16(d, at + 6) / 2;
        int endCodes = at + 14;
        int startCodes = endCodes + segCount * 2 + 2;
        int idDeltas = startCodes + segCount * 2;
        int idRangeOffsets = idDeltas + segCount * 2;
        if (idRangeOffsets + segCount * 2 > d.Length) return;

        for (int seg = 0; seg < segCount; seg++)
        {
            int start = U16(d, startCodes + seg * 2);
            int end = U16(d, endCodes + seg * 2);
            if (start == 0xFFFF || end < start) continue;

            int rangeOffset = U16(d, idRangeOffsets + seg * 2);
            if (rangeOffset == 0)
            {
                ranges.Add((start, end));
                continue;
            }

            // Glyph-index-array segment: individual code points can map to glyph 0 (unmapped)
            int runStart = -1;
            for (int cp = start; cp <= end; cp++)
            {
                int glyphAt = idRangeOffsets + seg * 2 + rangeOffset + (cp - start) * 2;
                bool mapped = glyphAt + 2 <= d.Length && U16(d, glyphAt) != 0;
                if (mapped && runStart < 0) runStart = cp;
                else if (!mapped && runStart >= 0) { ranges.Add((runStart, cp - 1)); runStart = -1; }
            }
            if (runStart >= 0) ranges.Add((runStart, end));
        }
    }

    private static byte[]? ReadAt(Stream stream, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > stream.Length) return null;
        stream.Position = offset;
        var buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0) return null;
            read += n;
        }
        return buffer;
    }

    private static int U16(byte[] d, int at) => (d[at] << 8) | d[at + 1];

    private static uint U32(byte[] d, int at)
        => ((uint)d[at] << 24) | ((uint)d[at + 1] << 16) | ((uint)d[at + 2] << 8) | d[at + 3];
}
