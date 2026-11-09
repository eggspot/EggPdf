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

        /// <summary>
        /// COLR/CPAL color-glyph layers per codepoint, resolved to actual colors and
        /// remapped to new (subset) glyph IDs. A layer with <c>useTextColor</c> true
        /// should be painted in the caller's current text fill color instead of a
        /// fixed palette color (COLR's "foreground color" sentinel, palette index 0xFFFF).
        /// Null/absent when the font has no COLR table or the text uses no color glyphs.
        /// </summary>
        public Dictionary<int, List<(ushort newGlyphId, float r, float g, float b, bool useTextColor)>>? ColorLayersByCodepoint { get; set; }
    }

    /// <summary>
    /// Subset a TrueType font to include only glyphs for the given codepoints.
    /// Always includes glyph 0 (.notdef). When <paramref name="activeFeatures"/> is
    /// given, each codepoint resolves through its GSUB single-substitution glyph
    /// (see <see cref="FontData.GetGlyphId(int, IReadOnlyList{string}?)"/>) so the
    /// subset embeds and maps to the feature-substituted glyph, not the original.
    /// </summary>
    public static SubsetResult? Subset(FontData font, IEnumerable<int> codepoints, IReadOnlyList<string>? activeFeatures = null,
        IEnumerable<ushort>? extraGlyphs = null)
    {
        if (font.RawData == null || font.RawData.Length < 12)
            return null;

        try
        {
            return SubsetInternal(font, codepoints, activeFeatures, extraGlyphs);
        }
        catch
        {
            return null;
        }
    }

    private static SubsetResult SubsetInternal(FontData font, IEnumerable<int> codepoints, IReadOnlyList<string>? activeFeatures,
        IEnumerable<ushort>? extraGlyphs)
    {
        var data = font.RawData;

        var tables = ParseTableDirectory(data);

        var neededGlyphs = new SortedSet<ushort> { 0 };
        var codepointMap = new Dictionary<int, ushort>();

        foreach (var cp in codepoints)
        {
            var gid = font.GetGlyphId(cp, activeFeatures);
            if (gid > 0)
            {
                neededGlyphs.Add(gid);
                codepointMap[cp] = gid;
            }
        }

        if (extraGlyphs != null)
            foreach (var g in extraGlyphs)
                if (g < font.NumGlyphs) neededGlyphs.Add(g);

        if (font.ColrLayers != null)
        {
            foreach (var oldGid in codepointMap.Values)
            {
                if (font.ColrLayers.TryGetValue(oldGid, out var colrLayers))
                    foreach (var layer in colrLayers)
                        neededGlyphs.Add(layer.layerGlyphId);
            }
        }

        if (tables.TryGetValue("glyf", out var glyfTable) && tables.TryGetValue("loca", out var locaTable))
        {
            bool longLoca = GetLocaFormat(data, tables);
            ResolveCompositeGlyphs(data, glyfTable, locaTable, longLoca, font.NumGlyphs, neededGlyphs);
        }

        var oldToNew = new Dictionary<ushort, ushort>();
        ushort newGid = 0;
        foreach (var oldGid in neededGlyphs)
            oldToNew[oldGid] = newGid++;

        int numNewGlyphs = oldToNew.Count;

        var cpToNewGid = new Dictionary<int, ushort>();
        foreach (var kv in codepointMap)
        {
            if (oldToNew.TryGetValue(kv.Value, out var newId))
                cpToNewGid[kv.Key] = newId;
        }

        Dictionary<int, List<(ushort newGlyphId, float r, float g, float b, bool useTextColor)>>? colorLayersByCp = null;
        if (font.ColrLayers != null)
        {
            foreach (var kv in codepointMap)
            {
                if (!font.ColrLayers.TryGetValue(kv.Value, out var colrLayers)) continue;
                var resolved = new List<(ushort, float, float, float, bool)>();
                foreach (var layer in colrLayers)
                {
                    if (!oldToNew.TryGetValue(layer.layerGlyphId, out var newLayerGid)) continue;

                    if (layer.paletteIndex < 0)
                    {
                        resolved.Add((newLayerGid, 0f, 0f, 0f, true));
                    }
                    else if (font.CpalPalette != null && layer.paletteIndex < font.CpalPalette.Length)
                    {
                        var c = font.CpalPalette[layer.paletteIndex];
                        resolved.Add((newLayerGid, c.r / 255f, c.g / 255f, c.b / 255f, false));
                    }
                }
                if (resolved.Count > 0)
                {
                    colorLayersByCp ??= new Dictionary<int, List<(ushort, float, float, float, bool)>>();
                    colorLayersByCp[kv.Key] = resolved;
                }
            }
        }

        byte[] newGlyf;
        uint[] newLocaOffsets;
        BuildGlyfAndLoca(data, tables, neededGlyphs.ToList(), oldToNew, font.NumGlyphs, out newGlyf, out newLocaOffsets);

        var newWidths = new ushort[numNewGlyphs];
        var newLsb = new short[numNewGlyphs];
        foreach (var kv in oldToNew)
        {
            newWidths[kv.Value] = font.GetAdvanceWidth(kv.Key);
            newLsb[kv.Value] = ReadLeftSideBearing(data, tables, kv.Key);
        }

        byte[] newHmtx = BuildHmtx(newWidths, newLsb);
        byte[] newCmap = BuildCmap(cpToNewGid);
        byte[] newMaxp = BuildMaxp(data, tables, numNewGlyphs);
        byte[] newHhea = BuildHhea(data, tables, numNewGlyphs);
        byte[] newHead = BuildHead(data, tables, true);
        byte[] newLoca = BuildLoca(newLocaOffsets);

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
            ColorLayersByCodepoint = colorLayersByCp,
        };
    }

    private static Dictionary<string, (uint offset, uint length)> ParseTableDirectory(byte[] data)
    {
        int pos = 4;
        if (data.Length > 16 && data[0] == 't' && data[1] == 't' && data[2] == 'c' && data[3] == 'f')
            pos = (int)ReadU32(data, 12) + 4;
        ushort numTables = ReadU16(data, pos); pos += 2;
        pos += 6;

        var tables = new Dictionary<string, (uint offset, uint length)>();
        for (int i = 0; i < numTables; i++)
        {
            string tag = System.Text.Encoding.ASCII.GetString(data, pos, 4); pos += 4;
            pos += 4;
            uint offset = ReadU32(data, pos); pos += 4;
            uint length = ReadU32(data, pos); pos += 4;
            tables[tag] = (offset, length);
        }
        return tables;
    }

    private static bool GetLocaFormat(byte[] data, Dictionary<string, (uint offset, uint length)> tables)
    {
        if (!tables.TryGetValue("head", out var head)) return false;
        return ReadI16(data, (int)head.offset + HeadIndexToLocFormatOffset) == 1;
    }

    private static byte[] ExtractTable(byte[] data, (uint offset, uint length) table)
    {
        var result = new byte[table.length];
        Array.Copy(data, table.offset, result, 0, Math.Min(table.length, (uint)data.Length - table.offset));
        return result;
    }

    private static void ResolveCompositeGlyphs(byte[] data, (uint offset, uint length) glyfTable,
        (uint offset, uint length) locaTable, bool longLoca, int numGlyphs, SortedSet<ushort> needed)
    {
        var toCheck = new Queue<ushort>(needed);
        var visited = new HashSet<ushort>(needed);

        while (toCheck.Count > 0)
        {
            var gid = toCheck.Dequeue();
            var (glyphOffset, glyphLength) = GetGlyphBounds(data, locaTable, longLoca, numGlyphs, gid);
            if (glyphLength == 0) continue;

            int pos = (int)(glyfTable.offset + glyphOffset);
            if (pos + GlyphHeaderSize > data.Length) continue;

            short numberOfContours = ReadI16(data, pos);
            if (numberOfContours >= 0) continue;

            pos += GlyphHeaderSize;

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

                pos += ComponentArgumentsSize(flags);

                if ((flags & MoreComponents) == 0) break;
            }
        }
    }

    private const int GlyphHeaderSize = 10;
    private const int HeadIndexToLocFormatOffset = 50;
    private const int HeadCheckSumAdjustmentOffset = 8;
    private const int HheaNumberOfHMetricsOffset = 34;
    private const ushort ArgsAreWords = 0x0001;
    private const ushort HasScale = 0x0008;
    private const ushort MoreComponents = 0x0020;
    private const ushort HasXAndYScale = 0x0040;
    private const ushort HasTwoByTwo = 0x0080;

    private static int ComponentArgumentsSize(ushort flags)
    {
        int size = (flags & ArgsAreWords) != 0 ? 4 : 2;
        if ((flags & HasScale) != 0) size += 2;
        else if ((flags & HasXAndYScale) != 0) size += 4;
        else if ((flags & HasTwoByTwo) != 0) size += 8;
        return size;
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

                    RemapCompositeGlyph(glyphData, oldToNew);

                    ms.Write(glyphData, 0, glyphData.Length);

                    while (ms.Position % 4 != 0) ms.WriteByte(0);
                }
            }
        }
        newLocaOffsets[numNewGlyphs] = (uint)ms.Position;
        newGlyf = ms.ToArray();
    }

    private static void RemapCompositeGlyph(byte[] glyphData, Dictionary<ushort, ushort> oldToNew)
    {
        if (glyphData.Length < GlyphHeaderSize) return;
        short numberOfContours = ReadI16(glyphData, 0);
        if (numberOfContours >= 0) return;

        int pos = GlyphHeaderSize;
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

            pos += ComponentArgumentsSize(flags);

            if ((flags & MoreComponents) == 0) break;
        }
    }

    private static byte[] BuildHmtx(ushort[] widths, short[] leftSideBearings)
    {
        var buf = new byte[widths.Length * 4];
        for (int i = 0; i < widths.Length; i++)
        {
            WriteU16(buf, i * 4, widths[i]);
            WriteI16(buf, i * 4 + 2, leftSideBearings[i]);
        }
        return buf;
    }

    private static short ReadLeftSideBearing(byte[] data, Dictionary<string, (uint offset, uint length)> tables, ushort glyphId)
    {
        if (!tables.TryGetValue("hmtx", out var hmtx) || !tables.TryGetValue("hhea", out var hhea))
            return 0;
        int numHMetrics = ReadU16(data, (int)hhea.offset + HheaNumberOfHMetricsOffset);
        if (numHMetrics <= 0) return 0;

        long pos = glyphId < numHMetrics
            ? hmtx.offset + 4L * glyphId + 2
            : hmtx.offset + 4L * numHMetrics + 2L * (glyphId - numHMetrics);
        if (pos < 0 || pos + 2 > data.Length || pos + 2 > hmtx.offset + hmtx.length) return 0;
        return ReadI16(data, (int)pos);
    }

    private const ushort WindowsPlatform = 3;
    private const ushort UnicodeBmpEncoding = 1;
    private const ushort UnicodeFullEncoding = 10;
    private const uint FirstSubtableOffset = 12;

    private static byte[] BuildCmap(Dictionary<int, ushort> cpToGid)
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteU16BE(bw, 0);
        WriteU16BE(bw, 1);

        bool needsFormat12 = cpToGid.Keys.Any(k => k > 0xFFFF);

        if (needsFormat12)
        {
            WriteU16BE(bw, WindowsPlatform);
            WriteU16BE(bw, UnicodeFullEncoding);
            WriteU32BE(bw, FirstSubtableOffset);
            WriteFormat12(bw, cpToGid);
        }
        else
        {
            WriteU16BE(bw, WindowsPlatform);
            WriteU16BE(bw, UnicodeBmpEncoding);
            WriteU32BE(bw, FirstSubtableOffset);
            WriteFormat4(bw, cpToGid);
        }

        return ms.ToArray();
    }

    private static void WriteFormat4(BinaryWriter bw, Dictionary<int, ushort> cpToGid)
    {
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
        segments.Add((0xFFFF, 0xFFFF, 1));

        int segCount = segments.Count;
        int searchRange = 1;
        while (searchRange * 2 <= segCount) searchRange *= 2;
        searchRange *= 2;
        int entrySelector = (int)Math.Log(searchRange / 2, 2);
        int rangeShift = segCount * 2 - searchRange;

        int length = 14 + segCount * 8;

        WriteU16BE(bw, 4);
        WriteU16BE(bw, (ushort)length);
        WriteU16BE(bw, 0);
        WriteU16BE(bw, (ushort)(segCount * 2));
        WriteU16BE(bw, (ushort)searchRange);
        WriteU16BE(bw, (ushort)entrySelector);
        WriteU16BE(bw, (ushort)rangeShift);

        foreach (var s in segments) WriteU16BE(bw, s.endCode);
        WriteU16BE(bw, 0);

        foreach (var s in segments) WriteU16BE(bw, s.startCode);

        foreach (var s in segments) WriteI16BE(bw, s.idDelta);

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

        WriteU16BE(bw, 12);
        WriteU16BE(bw, 0);
        WriteU32BE(bw, (uint)length);
        WriteU32BE(bw, 0);
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
            WriteU32(maxp, 0, 0x00010000);
        }
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
            WriteU32(hhea, 0, 0x00010000);
        }
        if (hhea.Length >= HheaNumberOfHMetricsOffset + 2)
        {
            hhea[HheaNumberOfHMetricsOffset] = (byte)(numHMetrics >> 8);
            hhea[HheaNumberOfHMetricsOffset + 1] = (byte)(numHMetrics & 0xFF);
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
            WriteU32(head, 0, 0x00010000);
        }
        if (head.Length >= HeadIndexToLocFormatOffset + 2)
        {
            head[HeadIndexToLocFormatOffset] = 0;
            head[HeadIndexToLocFormatOffset + 1] = (byte)(longLoca ? 1 : 0);
        }
        if (head.Length >= HeadCheckSumAdjustmentOffset + 4)
        {
            head[8] = 0; head[9] = 0; head[10] = 0; head[11] = 0;
        }
        return head;
    }

    private static byte[] BuildLoca(uint[] offsets)
    {
        var buf = new byte[offsets.Length * 4];
        for (int i = 0; i < offsets.Length; i++)
            WriteU32(buf, i * 4, offsets[i]);
        return buf;
    }

    private static byte[] BuildMinimalPost()
    {
        var buf = new byte[32];
        WriteU32(buf, 0, 0x00030000);
        return buf;
    }

    private static byte[] AssembleTtf(Dictionary<string, byte[]> tables)
    {
        int numTables = tables.Count;

        int searchRange = 1;
        int entrySelector = 0;
        while (searchRange * 2 <= numTables) { searchRange *= 2; entrySelector++; }
        searchRange *= 16;
        int rangeShift = numTables * 16 - searchRange;

        int headerSize = 12 + numTables * 16;
        if (headerSize % 4 != 0) headerSize += 4 - headerSize % 4;

        using var ms = new MemoryStream();

        WriteU32(ms, 0x00010000);
        WriteU16(ms, (ushort)numTables);
        WriteU16(ms, (ushort)searchRange);
        WriteU16(ms, (ushort)entrySelector);
        WriteU16(ms, (ushort)rangeShift);

        int dataOffset = headerSize;
        var tableEntries = new List<(string tag, byte[] data, uint offset)>();
        var orderedTags = tables.Keys.OrderBy(t => t).ToList();
        foreach (var tag in orderedTags)
        {
            var tdata = tables[tag];
            tableEntries.Add((tag, tdata, (uint)dataOffset));
            dataOffset += tdata.Length;
            if (dataOffset % 4 != 0) dataOffset += 4 - dataOffset % 4;
        }

        foreach (var entry in tableEntries)
        {
            var tagBytes = System.Text.Encoding.ASCII.GetBytes(entry.tag.PadRight(4).Substring(0, 4));
            ms.Write(tagBytes, 0, 4);
            WriteU32(ms, CalculateChecksum(entry.data));
            WriteU32(ms, entry.offset);
            WriteU32(ms, (uint)entry.data.Length);
        }

        while (ms.Position < headerSize) ms.WriteByte(0);

        foreach (var entry in tableEntries)
        {
            ms.Write(entry.data, 0, entry.data.Length);
            while (ms.Position % 4 != 0) ms.WriteByte(0);
        }

        return ms.ToArray();
    }

    private static uint CalculateChecksum(byte[] data)
    {
        uint sum = 0;
        int len = (data.Length + 3) & ~3;
        for (int i = 0; i < len; i += 4)
        {
            uint val = 0;
            for (int j = 0; j < 4 && i + j < data.Length; j++)
                val = (val << 8) | data[i + j];
            sum += val;
        }
        return sum;
    }

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
