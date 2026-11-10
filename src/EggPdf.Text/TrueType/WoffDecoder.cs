using System.IO;
using System.IO.Compression;

namespace EggPdf.Text.TrueType;

/// <summary>
/// Decodes WOFF (Web Open Font Format) 1.0 to raw TrueType/OpenType data.
/// WOFF wraps TrueType/OTF tables with zlib compression and metadata.
/// Required for @font-face with .woff files.
/// </summary>
public static class WoffDecoder
{
    private const uint WoffSignature = 0x774F4646;
    private const int HeaderSize = 44;
    private const int FlavorField = 4;
    private const int NumTablesField = 12;
    private const int TableEntrySize = 20;
    private const int SfntHeaderSize = 12;
    private const int SfntTableRecordSize = 16;
    private const int ZlibHeaderSize = 2;
    private const long MaxDeflateRatio = 1032;

    /// <summary>Check if data starts with WOFF signature.</summary>
    public static bool IsWoff(byte[] data)
    {
        if (data == null || data.Length < 4) return false;
        return ReadU32(data, 0) == WoffSignature;
    }

    /// <summary>
    /// The decoded font when <paramref name="data"/> is WOFF or WOFF 2.0 (see <see cref="Woff2Decoder"/>),
    /// <paramref name="data"/> itself when it is neither, or null when it is one that cannot be decoded.
    /// </summary>
    public static byte[]? DecodeIfWoff(byte[] data)
    {
        if (IsWoff(data)) return Decode(data);
        if (Woff2Decoder.IsWoff2(data)) return Woff2Decoder.Decode(data);
        return data;
    }

    /// <summary>
    /// Decode WOFF to raw TrueType/OpenType bytes.
    /// Returns null if data is invalid or too short.
    /// </summary>
    public static byte[]? Decode(byte[] data)
    {
        if (data == null || data.Length < HeaderSize)
            return null;

        try
        {
            return DecodeInternal(data);
        }
        catch
        {
            return null;
        }
    }

    private readonly struct TableEntry
    {
        public readonly uint Tag, Offset, CompLength, OrigLength, OrigChecksum;

        public TableEntry(uint tag, uint offset, uint compLength, uint origLength, uint origChecksum)
        {
            Tag = tag;
            Offset = offset;
            CompLength = compLength;
            OrigLength = origLength;
            OrigChecksum = origChecksum;
        }

        public bool IsCompressed => CompLength < OrigLength;

        /// <summary>
        /// The spec forbids a stored length larger than the original; a compressed table must hold a zlib header and
        /// cannot inflate past deflate's maximum ratio, which bounds the output by the input.
        /// </summary>
        public bool IsValid(int fileLength)
        {
            if (CompLength > OrigLength || (long)Offset + CompLength > fileLength) return false;
            return !IsCompressed || (CompLength >= ZlibHeaderSize && OrigLength <= CompLength * MaxDeflateRatio);
        }
    }

    /// <summary>
    /// Rebuilds the sfnt: offset table, table directory, then every table padded to 4 bytes. The output size comes
    /// from the table directory, never from the header's totalSfntSize claim.
    /// </summary>
    private static byte[]? DecodeInternal(byte[] data)
    {
        if (ReadU32(data, 0) != WoffSignature) return null;

        uint flavor = ReadU32(data, FlavorField);
        int numTables = ReadU16(data, NumTablesField);
        if (HeaderSize + (long)numTables * TableEntrySize > data.Length) return null;

        var entries = new TableEntry[numTables];
        long outputLength = SfntHeaderSize + (long)numTables * SfntTableRecordSize;
        for (int i = 0; i < numTables; i++)
        {
            int at = HeaderSize + i * TableEntrySize;
            var entry = new TableEntry(ReadU32(data, at), ReadU32(data, at + 4), ReadU32(data, at + 8),
                ReadU32(data, at + 12), ReadU32(data, at + 16));
            if (!entry.IsValid(data.Length)) return null;
            entries[i] = entry;
            outputLength += Pad4(entry.OrigLength);
        }
        if (outputLength > int.MaxValue) return null;

        var output = new byte[outputLength];
        int entrySelector = 0;
        while ((2 << entrySelector) <= numTables) entrySelector++;
        int searchRange = SfntTableRecordSize << entrySelector;
        WriteU32(output, 0, flavor);
        WriteU16(output, 4, numTables);
        WriteU16(output, 6, searchRange);
        WriteU16(output, 8, entrySelector);
        WriteU16(output, 10, numTables * SfntTableRecordSize - searchRange);

        long tableOffset = SfntHeaderSize + (long)numTables * SfntTableRecordSize;
        for (int i = 0; i < numTables; i++)
        {
            var entry = entries[i];
            int record = SfntHeaderSize + i * SfntTableRecordSize;
            WriteU32(output, record, entry.Tag);
            WriteU32(output, record + 4, entry.OrigChecksum);
            WriteU32(output, record + 8, (uint)tableOffset);
            WriteU32(output, record + 12, entry.OrigLength);

            if (!CopyTable(data, entry, output, (int)tableOffset)) return null;
            tableOffset += Pad4(entry.OrigLength);
        }

        return output;
    }

    private static bool CopyTable(byte[] data, TableEntry entry, byte[] output, int outputOffset)
    {
        if (!entry.IsCompressed)
        {
            System.Buffer.BlockCopy(data, (int)entry.Offset, output, outputOffset, (int)entry.OrigLength);
            return true;
        }

        using var compressed = new MemoryStream(data, (int)entry.Offset + ZlibHeaderSize, (int)entry.CompLength - ZlibHeaderSize);
        using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
        int total = 0;
        while (total < entry.OrigLength)
        {
            int read = inflater.Read(output, outputOffset + total, (int)entry.OrigLength - total);
            if (read == 0) return false;
            total += read;
        }
        return true;
    }

    private static long Pad4(uint length) => (length + 3L) & ~3L;

    private static uint ReadU32(byte[] data, int pos)
        => (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);

    private static ushort ReadU16(byte[] data, int pos) => (ushort)((data[pos] << 8) | data[pos + 1]);

    private static void WriteU32(byte[] output, int at, uint value)
    {
        output[at] = (byte)(value >> 24);
        output[at + 1] = (byte)(value >> 16);
        output[at + 2] = (byte)(value >> 8);
        output[at + 3] = (byte)value;
    }

    private static void WriteU16(byte[] output, int at, int value)
    {
        output[at] = (byte)(value >> 8);
        output[at + 1] = (byte)value;
    }
}
