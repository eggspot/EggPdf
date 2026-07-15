using System;

namespace EggPdf.Text.TrueType;

/// <summary>
/// Parses TrueType font files (.ttf). Reads essential tables:
/// head, hhea, maxp, hmtx, cmap, name.
/// Returns null on invalid/empty input (infallible).
/// </summary>
public static class TtfParser
{
    public static FontData? Parse(byte[] data)
    {
        if (data == null || data.Length < 12)
            return null;

        try
        {
            return ParseInternal(data);
        }
        catch
        {
            return null; // infallible
        }
    }

    private static FontData? ParseInternal(byte[] data)
    {
        int pos = 0;

        // Offset table
        uint sfVersion = ReadUInt32(data, ref pos);
        // Accept TrueType (0x00010000) or OpenType ('OTTO')
        if (sfVersion != 0x00010000 && sfVersion != 0x4F54544F)
            return null;

        ushort numTables = ReadUInt16(data, ref pos);
        pos += 6; // skip searchRange, entrySelector, rangeShift

        // Read table directory
        var tables = new System.Collections.Generic.Dictionary<string, (uint offset, uint length)>();
        for (int i = 0; i < numTables; i++)
        {
            string tag = ReadTag(data, ref pos);
            pos += 4; // skip checksum
            uint offset = ReadUInt32(data, ref pos);
            uint length = ReadUInt32(data, ref pos);
            tables[tag] = (offset, length);
        }

        var font = new FontData { RawData = data };

        // Parse head table
        if (tables.TryGetValue("head", out var head))
            ParseHead(data, (int)head.offset, font);

        // Parse hhea table
        if (tables.TryGetValue("hhea", out var hhea))
            ParseHhea(data, (int)hhea.offset, font);

        // Parse maxp table
        if (tables.TryGetValue("maxp", out var maxp))
            ParseMaxp(data, (int)maxp.offset, font);

        // Parse hmtx table (needs hhea.numberOfHMetrics and maxp.numGlyphs)
        if (tables.TryGetValue("hmtx", out var hmtx))
            ParseHmtx(data, (int)hmtx.offset, font);

        // Parse cmap table
        if (tables.TryGetValue("cmap", out var cmap))
            ParseCmap(data, (int)cmap.offset, font);

        // Parse name table
        if (tables.TryGetValue("name", out var name))
            ParseName(data, (int)name.offset, (int)name.length, font);

        // Parse kern table (TrueType kerning)
        if (tables.TryGetValue("kern", out var kern))
            ParseKern(data, (int)kern.offset, (int)kern.length, font);

        // Parse GPOS table for pair kerning (OpenType kerning, takes precedence)
        if (tables.TryGetValue("GPOS", out var gpos))
            ParseGposPairKerning(data, (int)gpos.offset, (int)gpos.length, font);

        return font;
    }

    private static void ParseHead(byte[] data, int offset, FontData font)
    {
        // head table: version(4), fontRevision(4), checksumAdjust(4), magicNumber(4),
        // flags(2), unitsPerEm(2), ...
        int pos = offset + 18; // skip to unitsPerEm
        font.UnitsPerEm = ReadUInt16(data, ref pos);
    }

    private static int _numberOfHMetrics;

    private static void ParseHhea(byte[] data, int offset, FontData font)
    {
        int pos = offset + 4; // skip version
        font.Ascent = ReadInt16(data, ref pos);
        font.Descent = ReadInt16(data, ref pos);
        font.LineGap = ReadInt16(data, ref pos);

        // Skip to numberOfHMetrics (at offset+34)
        pos = offset + 34;
        _numberOfHMetrics = ReadUInt16(data, ref pos);
    }

    private static void ParseMaxp(byte[] data, int offset, FontData font)
    {
        int pos = offset + 4; // skip version
        font.NumGlyphs = ReadUInt16(data, ref pos);
    }

    private static void ParseHmtx(byte[] data, int offset, FontData font)
    {
        // Each longHorMetric: advanceWidth(2) + lsb(2)
        int numMetrics = _numberOfHMetrics;
        font.AdvanceWidths = new ushort[font.NumGlyphs];

        int pos = offset;
        ushort lastWidth = 0;

        for (int i = 0; i < numMetrics && i < font.NumGlyphs; i++)
        {
            if (pos + 4 > data.Length) break;
            lastWidth = ReadUInt16(data, ref pos);
            font.AdvanceWidths[i] = lastWidth;
            pos += 2; // skip lsb
        }

        // Remaining glyphs use the last advance width
        for (int i = numMetrics; i < font.NumGlyphs; i++)
            font.AdvanceWidths[i] = lastWidth;
    }

    private static void ParseCmap(byte[] data, int offset, FontData font)
    {
        font.Cmap = new CmapData();

        int pos = offset;
        pos += 2; // skip version

        ushort numSubtables = ReadUInt16(data, ref pos);

        // Find Unicode subtables (platformID 0 or 3). A font may expose several;
        // prefer format 12 (full Unicode) over format 4 (BMP only).
        int format4Offset = -1;
        int format12Offset = -1;
        for (int i = 0; i < numSubtables; i++)
        {
            ushort platformID = ReadUInt16(data, ref pos);
            ushort encodingID = ReadUInt16(data, ref pos);
            uint subtableOffset = ReadUInt32(data, ref pos);

            if (platformID != 0 && !(platformID == 3 && (encodingID == 1 || encodingID == 10)))
                continue;

            int subStart = offset + (int)subtableOffset;
            if (subStart + 2 > data.Length) continue;

            int subPos = subStart;
            ushort format = ReadUInt16(data, ref subPos);
            if (format == 4 && format4Offset < 0)
                format4Offset = subStart;
            else if (format == 12 && format12Offset < 0)
                format12Offset = subStart;
        }

        if (format12Offset >= 0)
            ParseCmapFormat12(data, format12Offset, font.Cmap);
        else if (format4Offset >= 0)
            ParseCmapFormat4(data, format4Offset, font.Cmap);
    }

    private static void ParseCmapFormat12(byte[] data, int offset, CmapData cmap)
    {
        int pos = offset + 4; // skip format (2) + reserved (2)
        pos += 4; // length
        pos += 4; // language
        uint numGroups = ReadUInt32(data, ref pos);

        const int MaxMappings = 500000; // safety cap against corrupt group counts
        int added = 0;
        for (uint g = 0; g < numGroups; g++)
        {
            if (pos + 12 > data.Length) return;
            uint startChar = ReadUInt32(data, ref pos);
            uint endChar = ReadUInt32(data, ref pos);
            uint startGlyph = ReadUInt32(data, ref pos);

            if (endChar < startChar || endChar > 0x10FFFF) continue;

            for (uint cp = startChar; cp <= endChar; cp++)
            {
                ushort glyphId = (ushort)(startGlyph + (cp - startChar));
                if (glyphId != 0)
                    cmap.Add((int)cp, glyphId);
                if (++added >= MaxMappings) return;
            }
        }
    }

    private static void ParseCmapFormat4(byte[] data, int offset, CmapData cmap)
    {
        int pos = offset + 2; // skip format
        ushort length = ReadUInt16(data, ref pos);
        pos += 2; // skip language
        ushort segCount2 = ReadUInt16(data, ref pos);
        int segCount = segCount2 / 2;
        pos += 6; // skip searchRange, entrySelector, rangeShift

        var endCodes = new ushort[segCount];
        for (int i = 0; i < segCount; i++)
            endCodes[i] = ReadUInt16(data, ref pos);

        pos += 2; // reservedPad

        var startCodes = new ushort[segCount];
        for (int i = 0; i < segCount; i++)
            startCodes[i] = ReadUInt16(data, ref pos);

        var idDeltas = new short[segCount];
        for (int i = 0; i < segCount; i++)
            idDeltas[i] = ReadInt16(data, ref pos);

        int idRangeOffsetsStart = pos;
        var idRangeOffsets = new ushort[segCount];
        for (int i = 0; i < segCount; i++)
            idRangeOffsets[i] = ReadUInt16(data, ref pos);

        // Map codepoints to glyph IDs
        for (int seg = 0; seg < segCount; seg++)
        {
            if (startCodes[seg] == 0xFFFF) break;

            for (int cp = startCodes[seg]; cp <= endCodes[seg]; cp++)
            {
                ushort glyphId;
                if (idRangeOffsets[seg] == 0)
                {
                    glyphId = (ushort)((cp + idDeltas[seg]) & 0xFFFF);
                }
                else
                {
                    int glyphIdOffset = idRangeOffsetsStart + seg * 2 + idRangeOffsets[seg]
                        + (cp - startCodes[seg]) * 2;
                    if (glyphIdOffset + 2 > data.Length) continue;
                    int rawPos = glyphIdOffset;
                    glyphId = ReadUInt16(data, ref rawPos);
                    if (glyphId != 0)
                        glyphId = (ushort)((glyphId + idDeltas[seg]) & 0xFFFF);
                }

                if (glyphId != 0)
                    cmap.Add(cp, glyphId);
            }
        }
    }

    private static void ParseName(byte[] data, int offset, int tableLength, FontData font)
    {
        if (offset + 6 > data.Length) return;

        int pos = offset;
        pos += 2; // format
        ushort count = ReadUInt16(data, ref pos);
        ushort stringOffset = ReadUInt16(data, ref pos);
        int storageStart = offset + stringOffset;

        for (int i = 0; i < count; i++)
        {
            if (pos + 12 > data.Length) break;

            ushort platformID = ReadUInt16(data, ref pos);
            ushort encodingID = ReadUInt16(data, ref pos);
            ushort languageID = ReadUInt16(data, ref pos);
            ushort nameID = ReadUInt16(data, ref pos);
            ushort nameLength = ReadUInt16(data, ref pos);
            ushort nameOffset = ReadUInt16(data, ref pos);

            // nameID 1 = Font Family
            if (nameID == 1 && string.IsNullOrEmpty(font.FamilyName))
            {
                int nameStart = storageStart + nameOffset;
                if (nameStart + nameLength <= data.Length)
                {
                    if (platformID == 3 || platformID == 0)
                    {
                        // UTF-16 BE
                        font.FamilyName = System.Text.Encoding.BigEndianUnicode.GetString(data, nameStart, nameLength);
                    }
                    else
                    {
                        // ASCII/Latin
                        font.FamilyName = System.Text.Encoding.ASCII.GetString(data, nameStart, nameLength);
                    }
                }
            }
        }
    }

    /// <summary>Parse the TrueType 'kern' table for pair kerning.</summary>
    private static void ParseKern(byte[] data, int offset, int length, FontData font)
    {
        if (offset + 4 > data.Length) return;

        int pos = offset;
        ushort version = ReadUInt16(data, ref pos);
        ushort nTables = ReadUInt16(data, ref pos);

        if (font.Kern == null)
            font.Kern = new KernData();

        for (int t = 0; t < nTables; t++)
        {
            if (pos + 6 > data.Length) break;

            ushort subVersion = ReadUInt16(data, ref pos);
            ushort subLength = ReadUInt16(data, ref pos);
            ushort coverage = ReadUInt16(data, ref pos);

            // Only format 0 (ordered pairs), horizontal kerning
            int format = coverage >> 8;
            bool horizontal = (coverage & 0x01) != 0;
            bool crossStream = (coverage & 0x04) != 0;

            if (format == 0 && horizontal && !crossStream)
            {
                if (pos + 8 > data.Length) break;
                ushort nPairs = ReadUInt16(data, ref pos);
                pos += 6; // skip searchRange, entrySelector, rangeShift

                for (int p = 0; p < nPairs; p++)
                {
                    if (pos + 6 > data.Length) break;
                    ushort left = ReadUInt16(data, ref pos);
                    ushort right = ReadUInt16(data, ref pos);
                    short value = ReadInt16(data, ref pos);
                    font.Kern.Add(left, right, value);
                }
            }
            else
            {
                // Skip unsupported subtable
                pos = offset + subLength;
            }
        }
    }

    /// <summary>
    /// Parse basic GPOS pair kerning (Lookup Type 2, format 1 = specific pairs).
    /// This handles the most common GPOS kerning format. Full GPOS is far more complex
    /// but this covers the majority of fonts that use GPOS for kerning.
    /// </summary>
    private static void ParseGposPairKerning(byte[] data, int offset, int length, FontData font)
    {
        if (offset + 10 > data.Length) return;

        int pos = offset;
        uint gposVersion = ReadUInt32(data, ref pos);
        ushort scriptListOffset = ReadUInt16(data, ref pos);
        ushort featureListOffset = ReadUInt16(data, ref pos);
        ushort lookupListOffset = ReadUInt16(data, ref pos);

        int featureListPos = offset + featureListOffset;
        int lookupListPos = offset + lookupListOffset;

        if (featureListPos + 2 > data.Length || lookupListPos + 2 > data.Length) return;

        // Find 'kern' feature in feature list
        int fpos = featureListPos;
        ushort featureCount = ReadUInt16(data, ref fpos);
        var kernLookupIndices = new System.Collections.Generic.List<int>();

        for (int f = 0; f < featureCount; f++)
        {
            if (fpos + 6 > data.Length) break;
            string featureTag = ReadTag(data, ref fpos);
            ushort featureOffset = ReadUInt16(data, ref fpos);

            if (featureTag == "kern")
            {
                // Read feature table to get lookup indices
                int ftPos = featureListPos + featureOffset;
                if (ftPos + 4 > data.Length) continue;
                int ftSave = ftPos;
                ftPos += 2; // skip featureParams
                ushort lookupCount = ReadUInt16(data, ref ftPos);
                for (int li = 0; li < lookupCount; li++)
                {
                    if (ftPos + 2 > data.Length) break;
                    kernLookupIndices.Add(ReadUInt16(data, ref ftPos));
                }
            }
        }

        if (kernLookupIndices.Count == 0) return;

        if (font.Kern == null)
            font.Kern = new KernData();

        // Read lookup list
        int llPos = lookupListPos;
        ushort lookupCount2 = ReadUInt16(data, ref llPos);

        foreach (int lookupIdx in kernLookupIndices)
        {
            if (lookupIdx >= lookupCount2) continue;

            int lookupOffsetPos = lookupListPos + 2 + lookupIdx * 2;
            if (lookupOffsetPos + 2 > data.Length) continue;
            int tmpPos = lookupOffsetPos;
            ushort lookupOffset = ReadUInt16(data, ref tmpPos);

            int lookupPos = lookupListPos + lookupOffset;
            if (lookupPos + 6 > data.Length) continue;

            int lPos = lookupPos;
            ushort lookupType = ReadUInt16(data, ref lPos);
            ushort lookupFlag = ReadUInt16(data, ref lPos);
            ushort subtableCount = ReadUInt16(data, ref lPos);

            if (lookupType != 2) continue; // Only pair adjustment (type 2)

            for (int st = 0; st < subtableCount; st++)
            {
                if (lPos + 2 > data.Length) break;
                ushort subtableOffset = ReadUInt16(data, ref lPos);

                int stPos = lookupPos + subtableOffset;
                if (stPos + 2 > data.Length) continue;

                int stSave = stPos;
                ushort posFormat = ReadUInt16(data, ref stPos);

                if (posFormat == 1)
                {
                    // Format 1: specific pairs
                    ParseGposPairFormat1(data, stSave, font);
                }
                // Format 2 (class-based) is more complex, skip for now
            }
        }
    }

    private static void ParseGposPairFormat1(byte[] data, int offset, FontData font)
    {
        int pos = offset;
        ushort posFormat = ReadUInt16(data, ref pos); // 1
        ushort coverageOffset = ReadUInt16(data, ref pos);
        ushort valueFormat1 = ReadUInt16(data, ref pos);
        ushort valueFormat2 = ReadUInt16(data, ref pos);
        ushort pairSetCount = ReadUInt16(data, ref pos);

        // Calculate value record sizes (number of fields * 2 bytes each)
        int vr1Size = CountBits(valueFormat1) * 2;
        int vr2Size = CountBits(valueFormat2) * 2;

        // Parse coverage table to get first glyph IDs
        var coveredGlyphs = ParseCoverage(data, offset + coverageOffset);
        if (coveredGlyphs == null) return;

        for (int ps = 0; ps < pairSetCount && ps < coveredGlyphs.Count; ps++)
        {
            if (pos + 2 > data.Length) break;
            ushort pairSetOffset = ReadUInt16(data, ref pos);

            int psPos = offset + pairSetOffset;
            if (psPos + 2 > data.Length) continue;

            ushort leftGlyph = coveredGlyphs[ps];
            ushort pvCount = ReadUInt16(data, ref psPos);

            for (int pv = 0; pv < pvCount; pv++)
            {
                if (psPos + 2 + vr1Size + vr2Size > data.Length) break;

                ushort secondGlyph = ReadUInt16(data, ref psPos);

                // Read x-advance from value record 1 (if present)
                short xAdvance = 0;
                if ((valueFormat1 & 0x0004) != 0) // XAdvance
                {
                    // XPlacement(2) if bit 0, YPlacement(2) if bit 1, then XAdvance(2) if bit 2
                    int skipBefore = 0;
                    if ((valueFormat1 & 0x0001) != 0) skipBefore += 2; // XPlacement
                    if ((valueFormat1 & 0x0002) != 0) skipBefore += 2; // YPlacement

                    int xaPos = psPos + skipBefore;
                    if (xaPos + 2 <= data.Length)
                    {
                        int tmpXa = xaPos;
                        xAdvance = ReadInt16(data, ref tmpXa);
                    }
                }

                psPos += vr1Size + vr2Size;

                if (xAdvance != 0)
                    font.Kern!.Add(leftGlyph, secondGlyph, xAdvance);
            }
        }
    }

    private static System.Collections.Generic.List<ushort>? ParseCoverage(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) return null;

        int pos = offset;
        ushort format = ReadUInt16(data, ref pos);
        var glyphs = new System.Collections.Generic.List<ushort>();

        if (format == 1)
        {
            ushort glyphCount = ReadUInt16(data, ref pos);
            for (int i = 0; i < glyphCount; i++)
            {
                if (pos + 2 > data.Length) break;
                glyphs.Add(ReadUInt16(data, ref pos));
            }
        }
        else if (format == 2)
        {
            ushort rangeCount = ReadUInt16(data, ref pos);
            for (int i = 0; i < rangeCount; i++)
            {
                if (pos + 6 > data.Length) break;
                ushort startGlyph = ReadUInt16(data, ref pos);
                ushort endGlyph = ReadUInt16(data, ref pos);
                ushort startCoverageIndex = ReadUInt16(data, ref pos);
                for (ushort g = startGlyph; g <= endGlyph; g++)
                    glyphs.Add(g);
            }
        }

        return glyphs;
    }

    private static int CountBits(int value)
    {
        int count = 0;
        while (value != 0) { count += value & 1; value >>= 1; }
        return count;
    }

    // Binary readers (big-endian)
    private static ushort ReadUInt16(byte[] data, ref int pos)
    {
        if (pos + 2 > data.Length) { pos += 2; return 0; }
        ushort val = (ushort)((data[pos] << 8) | data[pos + 1]);
        pos += 2;
        return val;
    }

    private static short ReadInt16(byte[] data, ref int pos)
    {
        return (short)ReadUInt16(data, ref pos);
    }

    private static uint ReadUInt32(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length) { pos += 4; return 0; }
        uint val = ((uint)data[pos] << 24) | ((uint)data[pos + 1] << 16) |
                   ((uint)data[pos + 2] << 8) | data[pos + 3];
        pos += 4;
        return val;
    }

    private static string ReadTag(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length) { pos += 4; return ""; }
        var tag = System.Text.Encoding.ASCII.GetString(data, pos, 4);
        pos += 4;
        return tag;
    }
}
