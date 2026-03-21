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

        // Find a Unicode subtable (platformID 0 or 3)
        int bestOffset = -1;
        for (int i = 0; i < numSubtables; i++)
        {
            ushort platformID = ReadUInt16(data, ref pos);
            ushort encodingID = ReadUInt16(data, ref pos);
            uint subtableOffset = ReadUInt32(data, ref pos);

            if (platformID == 0 || (platformID == 3 && (encodingID == 1 || encodingID == 10)))
            {
                bestOffset = offset + (int)subtableOffset;
            }
        }

        if (bestOffset < 0) return;

        // Read subtable format
        int subPos = bestOffset;
        ushort format = ReadUInt16(data, ref subPos);

        if (format == 4)
            ParseCmapFormat4(data, bestOffset, font.Cmap);
        // Format 12 support can be added later for supplementary planes
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
