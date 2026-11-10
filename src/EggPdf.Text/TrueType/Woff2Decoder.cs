using System;
using System.Collections.Generic;
#if NET6_0_OR_GREATER
using System.IO;
using System.IO.Compression;
#endif

namespace EggPdf.Text.TrueType;

/// <summary>
/// Decodes WOFF 2.0 to raw TrueType/OpenType data: the Brotli-compressed table data is decompressed and the glyf/loca
/// and hmtx transforms are reversed. Brotli comes from the base class library, so fonts decode on .NET 6 and later;
/// on .NET Standard <see cref="Decode"/> returns null and an @font-face falls back to its next source. Font
/// collections (flavor <c>ttcf</c>) are not supported.
/// </summary>
public static partial class Woff2Decoder
{
    private const uint Woff2Signature = 0x774F4632;
    private const uint CollectionFlavor = 0x74746366;
    private const int HeaderSize = 48;
    private const int ArbitraryTagIndex = 63;
    private const int NullTransformOfGlyfAndLoca = 3;
    private const long MaxDecompressedSize = 256L * 1024 * 1024;
    private const int SfntHeaderSize = 12;
    private const int SfntTableRecordSize = 16;
    private const int MaxpNumGlyphsOffset = 4;
    private const int HheaNumberOfHMetricsOffset = 34;

    private static readonly uint GlyfTag = Tag("glyf"), LocaTag = Tag("loca"), HmtxTag = Tag("hmtx");
    private static readonly uint MaxpTag = Tag("maxp"), HheaTag = Tag("hhea");

    /// <summary>The table tags WOFF 2.0 encodes as an index into this list (index 63 means the tag follows).</summary>
    private static readonly uint[] KnownTags = Tags(
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ",
        "VORG", "EBDT", "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS",
        "GSUB", "EBSC", "JSTF", "MATH", "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc",
        "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar", "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop",
        "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill");

    /// <summary>Whether <paramref name="data"/> starts with the WOFF 2.0 signature.</summary>
    public static bool IsWoff2(byte[] data) => data != null && data.Length >= 4 && ReadU32(data, 0) == Woff2Signature;

    /// <summary>
    /// Decode WOFF 2.0 to raw TrueType/OpenType bytes; null when the data is not WOFF 2.0, is malformed, is a font
    /// collection, or the platform has no Brotli decoder. Never throws.
    /// </summary>
    public static byte[]? Decode(byte[] data)
    {
        if (!IsWoff2(data) || data.Length < HeaderSize) return null;
        try
        {
            return DecodeInternal(data);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private readonly struct TableEntry
    {
        public TableEntry(uint tag, uint origLength, bool transformed, uint storedLength, long streamOffset)
        {
            Tag = tag;
            OrigLength = origLength;
            Transformed = transformed;
            StoredLength = storedLength;
            StreamOffset = streamOffset;
        }

        public uint Tag { get; }

        public uint OrigLength { get; }

        public bool Transformed { get; }

        /// <summary>The table's length in the decompressed stream: its transformed length, or its original one.</summary>
        public uint StoredLength { get; }

        public long StreamOffset { get; }
    }

    private static byte[]? DecodeInternal(byte[] data)
    {
        var header = new Woff2Reader(data, 0, data.Length);
        header.Skip(4);
        uint flavor = header.U32();
        if (flavor == CollectionFlavor) return null;
        header.Skip(4);
        int numTables = header.U16();
        header.Skip(6);
        uint totalCompressedSize = header.U32();
        header.Seek(HeaderSize);
        if (numTables == 0) return null;

        var entries = new TableEntry[numTables];
        long decompressedSize = 0;
        for (int i = 0; i < numTables; i++)
        {
            int flags = header.U8();
            int tagIndex = flags & 0x3F;
            int transformVersion = flags >> 6;
            uint tag = tagIndex == ArbitraryTagIndex ? header.U32() : KnownTags[tagIndex];
            uint origLength = header.UIntBase128();
            bool glyfOrLoca = tag == GlyfTag || tag == LocaTag;
            bool transformed = glyfOrLoca ? transformVersion != NullTransformOfGlyfAndLoca : transformVersion != 0;
            uint storedLength = transformed ? header.UIntBase128() : origLength;
            entries[i] = new TableEntry(tag, origLength, transformed, storedLength, decompressedSize);
            decompressedSize += storedLength;
            if (decompressedSize > MaxDecompressedSize) return null;
        }

        long compressedStart = header.Position;
        if (compressedStart + totalCompressedSize > data.Length) return null;

        var stream = Decompress(data, (int)compressedStart, (int)totalCompressedSize, (int)decompressedSize);
        return stream == null ? null : AssembleSfnt(flavor, entries, stream);
    }

    /// <summary>
    /// Rebuilds every table (reversing the glyf/loca and hmtx transforms) and writes the sfnt: offset table, table
    /// records sorted by tag, and each table padded to 4 bytes.
    /// </summary>
    private static byte[]? AssembleSfnt(uint flavor, TableEntry[] entries, byte[] stream)
    {
        var tables = new SortedDictionary<uint, byte[]>();
        int glyf = -1, loca = -1, hmtx = -1;
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (tables.ContainsKey(entry.Tag)) return null;
            if (entry.Tag == GlyfTag) glyf = i;
            else if (entry.Tag == LocaTag) loca = i;
            else if (entry.Tag == HmtxTag) hmtx = i;
            if (entry.Transformed && entry.Tag != GlyfTag && entry.Tag != LocaTag && entry.Tag != HmtxTag) return null;
            if (!entry.Transformed) tables[entry.Tag] = Slice(stream, entry.StreamOffset, entry.StoredLength);
        }

        bool glyfTransformed = glyf >= 0 && entries[glyf].Transformed;
        bool locaTransformed = loca >= 0 && entries[loca].Transformed;
        if (glyfTransformed != locaTransformed) return null;
        if (glyfTransformed)
        {
            if (entries[loca].StoredLength != 0) return null;
            if (!ReconstructGlyf(stream, (int)entries[glyf].StreamOffset, (int)entries[glyf].StoredLength,
                    out var glyfTable, out var locaTable))
                return null;
            if (locaTable.Length != entries[loca].OrigLength) return null;
            tables[GlyfTag] = glyfTable;
            tables[LocaTag] = locaTable;
        }

        if (hmtx >= 0 && entries[hmtx].Transformed)
        {
            var hmtxTable = ReconstructHmtx(stream, (int)entries[hmtx].StreamOffset, (int)entries[hmtx].StoredLength, tables);
            if (hmtxTable == null) return null;
            tables[HmtxTag] = hmtxTable;
        }

        return WriteSfnt(flavor, tables);
    }

    private static byte[] WriteSfnt(uint flavor, SortedDictionary<uint, byte[]> tables)
    {
        int numTables = tables.Count;
        long length = SfntHeaderSize + (long)numTables * SfntTableRecordSize;
        foreach (var table in tables.Values) length += Pad4(table.Length);
        if (length > int.MaxValue) throw new FormatException("The decoded font is too large.");

        var output = new byte[length];
        int entrySelector = 0;
        while ((2 << entrySelector) <= numTables) entrySelector++;
        int searchRange = SfntTableRecordSize << entrySelector;
        WriteU32(output, 0, flavor);
        WriteU16(output, 4, numTables);
        WriteU16(output, 6, searchRange);
        WriteU16(output, 8, entrySelector);
        WriteU16(output, 10, numTables * SfntTableRecordSize - searchRange);

        int record = SfntHeaderSize;
        int offset = SfntHeaderSize + numTables * SfntTableRecordSize;
        foreach (var table in tables)
        {
            WriteU32(output, record, table.Key);
            WriteU32(output, record + 4, Checksum(table.Value));
            WriteU32(output, record + 8, (uint)offset);
            WriteU32(output, record + 12, (uint)table.Value.Length);
            Buffer.BlockCopy(table.Value, 0, output, offset, table.Value.Length);
            record += SfntTableRecordSize;
            offset += (int)Pad4(table.Value.Length);
        }
        return output;
    }

    private static byte[]? Decompress(byte[] data, int offset, int count, int expectedLength)
    {
#if NET6_0_OR_GREATER
        var output = new byte[expectedLength];
        using var input = new MemoryStream(data, offset, count, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        int total = 0;
        while (total < expectedLength)
        {
            int read = brotli.Read(output, total, expectedLength - total);
            if (read == 0) return null;
            total += read;
        }
        return brotli.ReadByte() == -1 ? output : null;
#else
        _ = data; _ = offset; _ = count; _ = expectedLength;
        return null;
#endif
    }

    private static byte[] Slice(byte[] stream, long offset, uint length)
    {
        if (offset + length > stream.Length) throw new FormatException("A WOFF2 table runs past the decompressed data.");
        var table = new byte[length];
        Buffer.BlockCopy(stream, (int)offset, table, 0, (int)length);
        return table;
    }

    private static uint Checksum(byte[] table)
    {
        uint sum = 0;
        for (int i = 0; i < table.Length; i += 4)
        {
            uint word = 0;
            for (int k = 0; k < 4; k++) word = (word << 8) | (i + k < table.Length ? table[i + k] : 0u);
            sum += word;
        }
        return sum;
    }

    private static long Pad4(long length) => (length + 3L) & ~3L;

    private static uint Tag(string tag) => (uint)((tag[0] << 24) | (tag[1] << 16) | (tag[2] << 8) | tag[3]);

    private static uint[] Tags(params string[] tags)
    {
        var result = new uint[tags.Length];
        for (int i = 0; i < tags.Length; i++) result[i] = Tag(tags[i]);
        return result;
    }

    private static uint ReadU32(byte[] data, int pos)
        => (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);

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
