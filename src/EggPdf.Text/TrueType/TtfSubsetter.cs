using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EggPdf.Text.TrueType;

/// <summary>
/// Subsets a TrueType font file to include only the glyphs needed for a specific text.
/// Produces a valid TrueType file with:
/// - Minimal glyph table (only used glyphs + composite glyph components)
/// - Updated cmap (format 4 for BMP, format 12 for supplementary)
/// - Updated hmtx, loca, maxp, hhea, head tables
/// - Glyph ID remapping (old GIDs -> contiguous new GIDs)
/// </summary>
public class TtfSubsetter
{
    /// <summary>
    /// Result of a subsetting operation: the subset font bytes and the glyph mapping.
    /// </summary>
    public class SubsetResult
    {
        /// <summary>Subset TrueType font bytes (valid .ttf).</summary>
        public byte[] FontData { get; set; } = Array.Empty<byte>();

        /// <summary>Map from Unicode codepoint to new glyph ID in the subset font.</summary>
        public Dictionary<int, ushort> CodepointToNewGlyphId { get; set; } = new();

        /// <summary>Map from old glyph ID to new glyph ID.</summary>
        public Dictionary<ushort, ushort> OldToNewGlyphId { get; set; } = new();

        /// <summary>Advance widths indexed by new glyph ID.</summary>
        public ushort[] AdvanceWidths { get; set; } = Array.Empty<ushort>();
    }

    /// <summary>
    /// Subset a TrueType font to include only glyphs for the given codepoints.
    /// Always includes glyph 0 (.notdef).
    /// </summary>
    public static SubsetResult? Subset(FontData font, IEnumerable<int> codepoints)
    {
        if (font.RawData == null || font.RawData.Length < 12)
            return null;

        try
        {
            return SubsetInternal(font, codepoints);
        }
        catch
        {
            return null; // infallible
        }
    }

    private static SubsetResult SubsetInternal(FontData font, IEnumerable<int> codepoints)
    {
        var data = font.RawData;

        // Parse table directory
        var tables = ParseTableDirectory(data);

        // Collect needed glyph IDs (always include glyph 0 = .notdef)
        var neededGlyphs = new SortedSet<ushort> { 0 };
        var codepointMap = new Dictionary<int, ushort>(); // codepoint -> old GID

        foreach (var cp in codepoints)
        {
            var gid = font.GetGlyphId(cp);
            if (gid > 0)
            {
                neededGlyphs.Add(gid);
                codepointMap[cp] = gid;
            }
        }

        // Resolve composite glyphs (glyphs that reference other glyphs)
        if (tables.TryGetValue("glyf", out var glyfTable) && tables.TryGetValue("loca", out var locaTable))
        {
            bool longLoca = GetLocaFormat(data, tables);
            ResolveCompositeGlyphs(data, glyfTable, locaTable, longLoca, font.NumGlyphs, neededGlyphs);
        }

        // Create old-to-new GID mapping (contiguous, preserving order)
        var oldToNew = new Dictionary<ushort, ushort>();
        ushort newGid = 0;
        foreach (var oldGid in neededGlyphs)
            oldToNew[oldGid] = newGid++;

        int numNewGlyphs = oldToNew.Count;

        // Build new cmap (codepoint -> new GID)
        var cpToNewGid = new Dictionary<int, ushort>();
        foreach (var kv in codepointMap)
        {
            if (oldToNew.TryGetValue(kv.Value, out var newId))
                cpToNewGid[kv.Key] = newId;
        }

        // Extract glyph data and build new glyf + loca
        byte[] newGlyf;
        uint[] newLocaOffsets;
        BuildGlyfAndLoca(data, tables, neededGlyphs.ToList(), oldToNew, font.NumGlyphs, out newGlyf, out newLocaOffsets);

        // Build new hmtx
        var newWidths = new ushort[numNewGlyphs];
        foreach (var kv in oldToNew)
            newWidths[kv.Value] = font.GetAdvanceWidth(kv.Key);

        byte[] newHmtx = BuildHmtx(newWidths);

        // Build new cmap table
        byte[] newCmap = BuildCmap(cpToNewGid);

        // Build new maxp
        byte[] newMaxp = BuildMaxp(data, tables, numNewGlyphs);

        // Build new hhea
        byte[] newHhea = BuildHhea(data, tables, numNewGlyphs);

        // Build new head (update loca format to long)
        byte[] newHead = BuildHead(data, tables, true);

        // Build new loca (long format)
        byte[] newLoca = BuildLoca(newLocaOffsets);

        // Assemble the subset font
        var subsetTables = new Dictionary<string, byte[]>
        {
            ["head"] = newHead,
            ["hhea"] = newHhea,
            ["maxp"] = newMaxp,
            ["hmtx"] = newHmtx,
            ["cmap"] = newCmap,
            ["glyf"] = newGlyf,
            ["loca"] = newLoca,
        };

        // Copy OS/2 and name tables verbatim if present
        if (tables.TryGetValue("OS/2", out var os2))
            subsetTables["OS/2"] = ExtractTable(data, os2);
        if (tables.TryGetValue("name", out var nameT))
            subsetTables["name"] = ExtractTable(data, nameT);
        if (tables.TryGetValue("post", out var post))
            subsetTables["post"] = BuildMinimalPost();

        byte[] fontBytes = AssembleTtf(subsetTables);

        return new SubsetResult
        {
            FontData = fontBytes,
            CodepointToNewGlyphId = cpToNewGid,
            OldToNewGlyphId = oldToNew,
            AdvanceWidths = newWidths,
        };
    }

    // =====================================================
    // Table parsing helpers
    // =====================================================

    private static Dictionary<string, (uint offset, uint length)> ParseTableDirectory(byte[] data)
    {
        int pos = 4; // skip sfVersion
        ushort numTables = ReadU16(data, pos); pos += 2;
        pos += 6; // skip searchRange, entrySelector, rangeShift

        var tables = new Dictionary<string, (uint offset, uint length)>();
        for (int i = 0; i < numTables; i++)
        {
            string tag = System.Text.Encoding.ASCII.GetString(data, pos, 4); pos += 4;
            pos += 4; // skip checksum
            uint offset = ReadU32(data, pos); pos += 4;
            uint length = ReadU32(data, pos); pos += 4;
            tables[tag] = (offset, length);
        }
        return tables;
    }

    private static bool GetLocaFormat(byte[] data, Dictionary<string, (uint offset, uint length)> tables)
    {
        if (!tables.TryGetValue("head", out var head)) return false;
        // indexToLocFormat is at offset 50 in head table
        return ReadI16(data, (int)head.offset + 50) == 1; // 0 = short, 1 = long
    }

    private static byte[] ExtractTable(byte[] data, (uint offset, uint length) table)
    {
        var result = new byte[table.length];
        Array.Copy(data, table.offset, result, 0, Math.Min(table.length, (uint)data.Length - table.offset));
        return result;
    }

    // =====================================================
    // Composite glyph resolution
    // =====================================================

    private static void ResolveCompositeGlyphs(byte[] data, (uint offset, uint length) glyfTable,
        (uint offset, uint length) locaTable, bool longLoca, int numGlyphs, SortedSet<ushort> needed)
    {
        // Iteratively add component glyphs from composite glyphs
        var toCheck = new Queue<ushort>(needed);
        var visited = new HashSet<ushort>(needed);

        while (toCheck.Count > 0)
        {
            var gid = toCheck.Dequeue();
            var (glyphOffset, glyphLength) = GetGlyphBounds(data, locaTable, longLoca, numGlyphs, gid);
            if (glyphLength == 0) continue;

            int pos = (int)(glyfTable.offset + glyphOffset);
            if (pos + 10 > data.Length) continue;

            short numberOfContours = ReadI16(data, pos);
            if (numberOfContours >= 0) continue; // Simple glyph, no components

            // Composite glyph: read component glyph IDs
            pos += 10; // skip header (numberOfContours, xMin, yMin, xMax, yMax)

            while (pos + 4 <= data.Length)
            {
                ushort flags = ReadU16(data, pos); pos += 2;
                ushort componentGid = ReadU16(data, pos); pos += 2;

                if (!visited.Contains(componentGid))
                {
                    visited.Add(componentGid);
                    needed.Add(componentGid);
                    toCheck.Enqueue(componentGid);
                }

                // Skip component arguments based on flags
                if ((flags & 0x0001) != 0) pos += 4; // ARG_1_AND_2_ARE_WORDS
                else pos += 2;

                if ((flags & 0x0008) != 0) pos += 2; // WE_HAVE_A_SCALE
                else if ((flags & 0x0040) != 0) pos += 4; // WE_HAVE_AN_X_AND_Y_SCALE
                else if ((flags & 0x0080) != 0) pos += 8; // WE_HAVE_A_TWO_BY_TWO

                if ((flags & 0x0020) == 0) break; // MORE_COMPONENTS flag
            }
        }
    }

    private static (uint offset, uint length) GetGlyphBounds(byte[] data,
        (uint offset, uint length) locaTable, bool longLoca, int numGlyphs, ushort glyphId)
    {
        if (glyphId >= numGlyphs) return (0, 0);

        uint locaOff = locaTable.offset;
        uint start, end;

        if (longLoca)
        {
            start = ReadU32(data, (int)(locaOff + glyphId * 4));
            end = ReadU32(data, (int)(locaOff + (glyphId + 1) * 4));
        }
        else
        {
            start = (uint)(ReadU16(data, (int)(locaOff + glyphId * 2)) * 2);
            end = (uint)(ReadU16(data, (int)(locaOff + (glyphId + 1) * 2)) * 2);
        }

        return (start, end > start ? end - start : 0);
    }

    // =====================================================
    // Build new tables
    // =====================================================

    private static void BuildGlyfAndLoca(byte[] data, Dictionary<string, (uint offset, uint length)> tables,
        List<ushort> glyphIds, Dictionary<ushort, ushort> oldToNew, int numOldGlyphs,
        out byte[] newGlyf, out uint[] newLocaOffsets)
    {
        int numNewGlyphs = glyphIds.Count;
        newLocaOffsets = new uint[numNewGlyphs + 1];

        if (!tables.TryGetValue("glyf", out var glyfT) || !tables.TryGetValue("loca", out var locaT))
        {
            newGlyf = Array.Empty<byte>();
            return;
        }

        bool longLoca = GetLocaFormat(data, tables);
        using var ms = new MemoryStream();

        for (int i = 0; i < numNewGlyphs; i++)
        {
            newLocaOffsets[i] = (uint)ms.Position;
            var oldGid = glyphIds[i];
            var (glyphOffset, glyphLength) = GetGlyphBounds(data, locaT, longLoca, numOldGlyphs, oldGid);

            if (glyphLength > 0)
            {
                int srcPos = (int)(glyfT.offset + glyphOffset);
                if (srcPos + glyphLength <= data.Length)
                {
                    var glyphData = new byte[glyphLength];
                    Array.Copy(data, srcPos, glyphData, 0, glyphLength);

                    // Remap composite glyph component IDs
                    RemapCompositeGlyph(glyphData, oldToNew);

                    ms.Write(glyphData, 0, glyphData.Length);

                    // Pad to 4-byte boundary
                    while (ms.Position % 4 != 0) ms.WriteByte(0);
                }
            }
        }
        newLocaOffsets[numNewGlyphs] = (uint)ms.Position;
        newGlyf = ms.ToArray();
    }

    private static void RemapCompositeGlyph(byte[] glyphData, Dictionary<ushort, ushort> oldToNew)
    {
        if (glyphData.Length < 10) return;
        short numberOfContours = ReadI16(glyphData, 0);
        if (numberOfContours >= 0) return; // Simple glyph

        int pos = 10;
        while (pos + 4 <= glyphData.Length)
        {
            ushort flags = ReadU16(glyphData, pos); pos += 2;
            ushort oldGid = ReadU16(glyphData, pos);

            if (oldToNew.TryGetValue(oldGid, out var newGid))
            {
                glyphData[pos] = (byte)(newGid >> 8);
                glyphData[pos + 1] = (byte)(newGid & 0xFF);
            }
            pos += 2;

            if ((flags & 0x0001) != 0) pos += 4;
            else pos += 2;

            if ((flags & 0x0008) != 0) pos += 2;
            else if ((flags & 0x0040) != 0) pos += 4;
            else if ((flags & 0x0080) != 0) pos += 8;

            if ((flags & 0x0020) == 0) break;
        }
    }

    private static byte[] BuildHmtx(ushort[] widths)
    {
        // Each entry: advanceWidth (u16) + leftSideBearing (i16 = 0)
        var buf = new byte[widths.Length * 4];
        for (int i = 0; i < widths.Length; i++)
        {
            WriteU16(buf, i * 4, widths[i]);
            WriteI16(buf, i * 4 + 2, 0);
        }
        return buf;
    }

    private static byte[] BuildCmap(Dictionary<int, ushort> cpToGid)
    {
        // Build a format 4 cmap subtable for BMP codepoints
        // Simpler approach: use format 12 (segmented coverage) which handles all codepoints
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // cmap header: version=0, numTables=1
        WriteU16BE(bw, 0); // version
        WriteU16BE(bw, 1); // numTables

        // Encoding record: platformID=3 (Windows), encodingID=1 (Unicode BMP)
        // For format 12, use platformID=3, encodingID=10 (Unicode full repertoire)
        bool needsFormat12 = cpToGid.Keys.Any(k => k > 0xFFFF);

        if (needsFormat12)
        {
            WriteU16BE(bw, 3); // platformID
            WriteU16BE(bw, 10); // encodingID
            WriteU32BE(bw, 12); // offset to subtable (after header + 1 encoding record = 4 + 8 = 12)
            WriteFormat12(bw, cpToGid);
        }
        else
        {
            WriteU16BE(bw, 3); // platformID
            WriteU16BE(bw, 1); // encodingID (BMP)
            WriteU32BE(bw, 12); // offset to subtable
            WriteFormat4(bw, cpToGid);
        }

        return ms.ToArray();
    }

    private static void WriteFormat4(BinaryWriter bw, Dictionary<int, ushort> cpToGid)
    {
        // Sort codepoints into segments. Because only idDelta mapping is written
        // (no glyphIdArray), a segment must be contiguous in BOTH codepoints and
        // glyph IDs, so break whenever either sequence jumps.
        int[] sorted = GetSortedKeys(cpToGid, bmp: true);

        var segments = new List<(ushort startCode, ushort endCode, short idDelta)>();
        if (sorted.Length > 0)
        {
            int segStart = 0;
            for (int i = 1; i <= sorted.Length; i++)
            {
                if (i == sorted.Length ||
                    sorted[i] != sorted[i - 1] + 1 ||
                    cpToGid[sorted[i]] != cpToGid[sorted[i - 1]] + 1)
                {
                    ushort sc = (ushort)sorted[segStart];
                    ushort ec = (ushort)sorted[i - 1];
                    short delta = (short)(cpToGid[sorted[segStart]] - sc);
                    segments.Add((sc, ec, delta));
                    segStart = i;
                }
            }
        }
        // Add sentinel segment
        segments.Add((0xFFFF, 0xFFFF, 1));

        int segCount = segments.Count;
        int searchRange = 1;
        while (searchRange * 2 <= segCount) searchRange *= 2;
        searchRange *= 2;
        int entrySelector = (int)Math.Log(searchRange / 2, 2);
        int rangeShift = segCount * 2 - searchRange;

        int length = 14 + segCount * 8; // header + 4 arrays

        WriteU16BE(bw, 4); // format
        WriteU16BE(bw, (ushort)length);
        WriteU16BE(bw, 0); // language
        WriteU16BE(bw, (ushort)(segCount * 2)); // segCountX2
        WriteU16BE(bw, (ushort)searchRange);
        WriteU16BE(bw, (ushort)entrySelector);
        WriteU16BE(bw, (ushort)rangeShift);

        // endCode
        foreach (var s in segments) WriteU16BE(bw, s.endCode);
        WriteU16BE(bw, 0); // reservedPad

        // startCode
        foreach (var s in segments) WriteU16BE(bw, s.startCode);

        // idDelta
        foreach (var s in segments) WriteI16BE(bw, s.idDelta);

        // idRangeOffset (all zeros - using delta mapping)
        foreach (var _ in segments) WriteU16BE(bw, 0);
    }

    private static void WriteFormat12(BinaryWriter bw, Dictionary<int, ushort> cpToGid)
    {
        int[] sorted = GetSortedKeys(cpToGid, bmp: false);
        var groups = new List<(uint startCharCode, uint endCharCode, uint startGlyphID)>();

        int i = 0;
        while (i < sorted.Length)
        {
            int start = sorted[i];
            ushort startGid = cpToGid[start];
            int end = start;

            while (i + 1 < sorted.Length && sorted[i + 1] == end + 1 &&
                   cpToGid[sorted[i + 1]] == startGid + (sorted[i + 1] - start))
            {
                end = sorted[++i];
            }
            groups.Add(((uint)start, (uint)end, startGid));
            i++;
        }

        int length = 16 + groups.Count * 12;

        WriteU16BE(bw, 12); // format
        WriteU16BE(bw, 0);  // reserved
        WriteU32BE(bw, (uint)length);
        WriteU32BE(bw, 0);  // language
        WriteU32BE(bw, (uint)groups.Count);

        foreach (var g in groups)
        {
            WriteU32BE(bw, g.startCharCode);
            WriteU32BE(bw, g.endCharCode);
            WriteU32BE(bw, g.startGlyphID);
        }
    }

    private static int[] GetSortedKeys(Dictionary<int, ushort> cpToGid, bool bmp)
    {
        int count = 0;
        foreach (var k in cpToGid.Keys)
            if (!bmp || k <= 0xFFFF) count++;
        var arr = new int[count];
        int idx = 0;
        foreach (var k in cpToGid.Keys)
            if (!bmp || k <= 0xFFFF) arr[idx++] = k;
        Array.Sort(arr);
        return arr;
    }

    private static byte[] BuildMaxp(byte[] data, Dictionary<string, (uint offset, uint length)> tables, int numGlyphs)
    {
        byte[] maxp;
        if (tables.TryGetValue("maxp", out var maxpT))
        {
            maxp = ExtractTable(data, maxpT);
        }
        else
        {
            maxp = new byte[6];
            WriteU32(maxp, 0, 0x00010000); // version 1.0
        }
        // Update numGlyphs at offset 4
        if (maxp.Length >= 6)
        {
            maxp[4] = (byte)(numGlyphs >> 8);
            maxp[5] = (byte)(numGlyphs & 0xFF);
        }
        return maxp;
    }

    private static byte[] BuildHhea(byte[] data, Dictionary<string, (uint offset, uint length)> tables, int numHMetrics)
    {
        byte[] hhea;
        if (tables.TryGetValue("hhea", out var hheaT))
        {
            hhea = ExtractTable(data, hheaT);
        }
        else
        {
            hhea = new byte[36];
            WriteU32(hhea, 0, 0x00010000); // version
        }
        // Update numberOfHMetrics at offset 34
        if (hhea.Length >= 36)
        {
            hhea[34] = (byte)(numHMetrics >> 8);
            hhea[35] = (byte)(numHMetrics & 0xFF);
        }
        return hhea;
    }

    private static byte[] BuildHead(byte[] data, Dictionary<string, (uint offset, uint length)> tables, bool longLoca)
    {
        byte[] head;
        if (tables.TryGetValue("head", out var headT))
        {
            head = ExtractTable(data, headT);
        }
        else
        {
            head = new byte[54];
            WriteU32(head, 0, 0x00010000); // version
        }
        // Set indexToLocFormat at offset 50
        if (head.Length >= 52)
        {
            head[50] = 0;
            head[51] = (byte)(longLoca ? 1 : 0);
        }
        // Zero the checksumAdjustment at offset 8
        if (head.Length >= 12)
        {
            head[8] = 0; head[9] = 0; head[10] = 0; head[11] = 0;
        }
        return head;
    }

    private static byte[] BuildLoca(uint[] offsets)
    {
        // Long format: 4 bytes per entry
        var buf = new byte[offsets.Length * 4];
        for (int i = 0; i < offsets.Length; i++)
            WriteU32(buf, i * 4, offsets[i]);
        return buf;
    }

    private static byte[] BuildMinimalPost()
    {
        // Version 3.0 post table (no glyph names)
        var buf = new byte[32];
        WriteU32(buf, 0, 0x00030000); // version 3.0
        // italicAngle, underlinePosition, underlineThickness, isFixedPitch (all 0)
        return buf;
    }

    // =====================================================
    // Assemble TrueType file
    // =====================================================

    private static byte[] AssembleTtf(Dictionary<string, byte[]> tables)
    {
        int numTables = tables.Count;

        // Calculate searchRange, entrySelector, rangeShift
        int searchRange = 1;
        int entrySelector = 0;
        while (searchRange * 2 <= numTables) { searchRange *= 2; entrySelector++; }
        searchRange *= 16;
        int rangeShift = numTables * 16 - searchRange;

        int headerSize = 12 + numTables * 16;
        // Pad header to 4-byte boundary
        if (headerSize % 4 != 0) headerSize += 4 - headerSize % 4;

        using var ms = new MemoryStream();

        // Offset table
        WriteU32(ms, 0x00010000); // sfVersion (TrueType)
        WriteU16(ms, (ushort)numTables);
        WriteU16(ms, (ushort)searchRange);
        WriteU16(ms, (ushort)entrySelector);
        WriteU16(ms, (ushort)rangeShift);

        // Calculate table offsets
        int dataOffset = headerSize;
        var tableEntries = new List<(string tag, byte[] data, uint offset)>();
        // Sort tables in recommended order
        var orderedTags = tables.Keys.OrderBy(t => t).ToList();
        foreach (var tag in orderedTags)
        {
            var tdata = tables[tag];
            tableEntries.Add((tag, tdata, (uint)dataOffset));
            dataOffset += tdata.Length;
            // Pad to 4-byte boundary
            if (dataOffset % 4 != 0) dataOffset += 4 - dataOffset % 4;
        }

        // Write table directory
        foreach (var entry in tableEntries)
        {
            var tagBytes = System.Text.Encoding.ASCII.GetBytes(entry.tag.PadRight(4).Substring(0, 4));
            ms.Write(tagBytes, 0, 4);
            WriteU32(ms, CalculateChecksum(entry.data)); // checksum
            WriteU32(ms, entry.offset); // offset
            WriteU32(ms, (uint)entry.data.Length); // length
        }

        // Pad to header boundary
        while (ms.Position < headerSize) ms.WriteByte(0);

        // Write table data
        foreach (var entry in tableEntries)
        {
            ms.Write(entry.data, 0, entry.data.Length);
            // Pad to 4-byte boundary
            while (ms.Position % 4 != 0) ms.WriteByte(0);
        }

        return ms.ToArray();
    }

    private static uint CalculateChecksum(byte[] data)
    {
        uint sum = 0;
        int len = (data.Length + 3) & ~3; // Round up to 4 bytes
        for (int i = 0; i < len; i += 4)
        {
            uint val = 0;
            for (int j = 0; j < 4 && i + j < data.Length; j++)
                val = (val << 8) | data[i + j];
            sum += val;
        }
        return sum;
    }

    // =====================================================
    // Binary read/write helpers
    // =====================================================

    private static ushort ReadU16(byte[] data, int offset)
        => (ushort)((data[offset] << 8) | data[offset + 1]);

    private static short ReadI16(byte[] data, int offset)
        => (short)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadU32(byte[] data, int offset)
        => (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

    private static void WriteU16(byte[] buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value >> 8);
        buf[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteI16(byte[] buf, int offset, short value)
    {
        buf[offset] = (byte)((ushort)value >> 8);
        buf[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteU32(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)((value >> 16) & 0xFF);
        buf[offset + 2] = (byte)((value >> 8) & 0xFF);
        buf[offset + 3] = (byte)(value & 0xFF);
    }

    private static void WriteU16(MemoryStream ms, ushort value)
    {
        ms.WriteByte((byte)(value >> 8));
        ms.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteU32(MemoryStream ms, uint value)
    {
        ms.WriteByte((byte)(value >> 24));
        ms.WriteByte((byte)((value >> 16) & 0xFF));
        ms.WriteByte((byte)((value >> 8) & 0xFF));
        ms.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteU16BE(BinaryWriter bw, ushort value)
    {
        bw.Write((byte)(value >> 8));
        bw.Write((byte)(value & 0xFF));
    }

    private static void WriteI16BE(BinaryWriter bw, short value)
    {
        bw.Write((byte)((ushort)value >> 8));
        bw.Write((byte)(value & 0xFF));
    }

    private static void WriteU32BE(BinaryWriter bw, uint value)
    {
        bw.Write((byte)(value >> 24));
        bw.Write((byte)((value >> 16) & 0xFF));
        bw.Write((byte)((value >> 8) & 0xFF));
        bw.Write((byte)(value & 0xFF));
    }
}
