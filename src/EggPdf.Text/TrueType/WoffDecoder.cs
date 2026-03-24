using System;
using System.IO;
using System.IO.Compression;

namespace EggPdf.Text.TrueType;

/// <summary>
/// Decodes WOFF (Web Open Font Format) 1.0 to raw TrueType/OpenType data.
/// WOFF wraps TrueType/OTF tables with zlib compression and metadata.
/// Required for @font-face with .woff files (most common web font format).
/// </summary>
public static class WoffDecoder
{
    private const uint WoffSignature = 0x774F4646; // 'wOFF'

    /// <summary>Check if data starts with WOFF signature.</summary>
    public static bool IsWoff(byte[] data)
    {
        if (data == null || data.Length < 4) return false;
        uint sig = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
        return sig == WoffSignature;
    }

    /// <summary>
    /// Decode WOFF to raw TrueType/OpenType bytes.
    /// Returns null if data is invalid or too short.
    /// </summary>
    public static byte[]? Decode(byte[] data)
    {
        if (data == null || data.Length < 44)
            return null;

        try
        {
            return DecodeInternal(data);
        }
        catch
        {
            return null; // infallible
        }
    }

    private static byte[]? DecodeInternal(byte[] data)
    {
        int pos = 0;

        // WOFF Header
        uint signature = ReadU32(data, ref pos);
        if (signature != WoffSignature) return null;

        uint flavor = ReadU32(data, ref pos);     // The "sfVersion" of the original font
        uint woffLength = ReadU32(data, ref pos);  // Total WOFF file size
        ushort numTables = ReadU16(data, ref pos);
        ushort reserved = ReadU16(data, ref pos);  // Must be 0
        uint totalSfntSize = ReadU32(data, ref pos); // Total decompressed size
        // Skip: majorVersion(2), minorVersion(2), metaOffset(4), metaLength(4),
        //       metaOrigLength(4), privOffset(4), privLength(4)
        pos += 20;

        // Read table directory entries
        var entries = new WoffTableEntry[numTables];
        for (int i = 0; i < numTables; i++)
        {
            entries[i] = new WoffTableEntry
            {
                Tag = ReadU32(data, ref pos),
                Offset = ReadU32(data, ref pos),
                CompLength = ReadU32(data, ref pos),
                OrigLength = ReadU32(data, ref pos),
                OrigChecksum = ReadU32(data, ref pos),
            };
        }

        // Build output TrueType file
        using var ms = new MemoryStream((int)totalSfntSize);

        // Write TrueType offset table
        WriteU32(ms, flavor); // sfVersion
        WriteU16(ms, numTables);

        // Compute searchRange, entrySelector, rangeShift
        int searchRange = 1, entrySelector = 0;
        while (searchRange * 2 <= numTables) { searchRange *= 2; entrySelector++; }
        searchRange *= 16;
        int rangeShift = numTables * 16 - searchRange;

        WriteU16(ms, (ushort)searchRange);
        WriteU16(ms, (ushort)entrySelector);
        WriteU16(ms, (ushort)rangeShift);

        // Calculate output table offsets
        int headerSize = 12 + numTables * 16;
        uint outputOffset = (uint)headerSize;
        var outputOffsets = new uint[numTables];

        for (int i = 0; i < numTables; i++)
        {
            outputOffsets[i] = outputOffset;
            outputOffset += entries[i].OrigLength;
            // Pad to 4-byte boundary
            if (outputOffset % 4 != 0)
                outputOffset += 4 - (outputOffset % 4);
        }

        // Write table directory entries
        for (int i = 0; i < numTables; i++)
        {
            WriteU32(ms, entries[i].Tag);
            WriteU32(ms, entries[i].OrigChecksum);
            WriteU32(ms, outputOffsets[i]);
            WriteU32(ms, entries[i].OrigLength);
        }

        // Write table data (decompress if needed)
        for (int i = 0; i < numTables; i++)
        {
            // Pad to current output offset
            while (ms.Position < outputOffsets[i])
                ms.WriteByte(0);

            var entry = entries[i];
            if (entry.CompLength == entry.OrigLength)
            {
                // Not compressed — copy directly
                ms.Write(data, (int)entry.Offset, (int)entry.OrigLength);
            }
            else
            {
                // zlib compressed — decompress (skip 2-byte zlib header)
                using var compressed = new MemoryStream(data, (int)entry.Offset + 2, (int)entry.CompLength - 2);
                using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
                var decompressed = new byte[entry.OrigLength];
                int totalRead = 0;
                while (totalRead < decompressed.Length)
                {
                    int read = deflate.Read(decompressed, totalRead, decompressed.Length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
                ms.Write(decompressed, 0, totalRead);
            }

            // Pad to 4-byte boundary
            while (ms.Position % 4 != 0)
                ms.WriteByte(0);
        }

        return ms.ToArray();
    }

    private struct WoffTableEntry
    {
        public uint Tag;
        public uint Offset;
        public uint CompLength;
        public uint OrigLength;
        public uint OrigChecksum;
    }

    private static uint ReadU32(byte[] data, ref int pos)
    {
        uint val = (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
        pos += 4;
        return val;
    }

    private static ushort ReadU16(byte[] data, ref int pos)
    {
        ushort val = (ushort)((data[pos] << 8) | data[pos + 1]);
        pos += 2;
        return val;
    }

    private static void WriteU32(Stream s, uint val)
    {
        s.WriteByte((byte)(val >> 24));
        s.WriteByte((byte)((val >> 16) & 0xFF));
        s.WriteByte((byte)((val >> 8) & 0xFF));
        s.WriteByte((byte)(val & 0xFF));
    }

    private static void WriteU16(Stream s, ushort val)
    {
        s.WriteByte((byte)(val >> 8));
        s.WriteByte((byte)(val & 0xFF));
    }
}
