using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using EggPdf.Text;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

[Collection(EggPdf.Tests.Unit.EndToEnd.ResourceLoadingCollection.Name)]
public class Woff2DecoderTests
{
    private const uint TrueTypeFlavor = 0x00010000;
    private const int NullTransformOfGlyfAndLoca = 3;
    private const int ArbitraryTagIndex = 63;
    private static readonly string[] KnownTags = { "cmap", "head", "hhea", "hmtx", "maxp" };

    private sealed class Table
    {
        public Table(string tag, byte[] stored, int transformVersion = 0, uint? origLength = null)
        {
            Tag = tag;
            Stored = stored;
            TransformVersion = transformVersion;
            OrigLength = origLength ?? (uint)stored.Length;
        }

        public string Tag { get; }

        public byte[] Stored { get; }

        public int TransformVersion { get; }

        public uint OrigLength { get; }

        public bool Transformed => Tag == "glyf" || Tag == "loca" ? TransformVersion != NullTransformOfGlyfAndLoca : TransformVersion != 0;
    }

    private static byte[] Woff2(uint flavor, IEnumerable<Table> tables, Func<byte[], byte[]>? compress = null)
    {
        var list = tables.ToList();
        var directory = new List<byte>();
        foreach (var table in list)
        {
            int index = Array.IndexOf(KnownTags, table.Tag);
            if (table.Tag == "glyf") index = 10;
            if (table.Tag == "loca") index = 11;
            directory.Add((byte)((table.TransformVersion << 6) | (index >= 0 ? index : ArbitraryTagIndex)));
            if (index < 0) directory.AddRange(Encoding.ASCII.GetBytes(table.Tag));
            directory.AddRange(Base128(table.OrigLength));
            if (table.Transformed) directory.AddRange(Base128((uint)table.Stored.Length));
        }

        var raw = list.SelectMany(t => t.Stored).ToArray();
        var compressed = (compress ?? Brotli)(raw);

        var file = new List<byte>();
        file.AddRange(Be32(0x774F4632));
        file.AddRange(Be32(flavor));
        file.AddRange(Be32(0));
        file.AddRange(Be16(list.Count));
        file.AddRange(Be16(0));
        file.AddRange(Be32(0));
        file.AddRange(Be32((uint)compressed.Length));
        file.AddRange(new byte[24]);
        file.AddRange(directory);
        file.AddRange(compressed);
        var bytes = file.ToArray();
        Array.Copy(Be32((uint)bytes.Length), 0, bytes, 8, 4);
        return bytes;
    }

    private static byte[] Brotli(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true)) brotli.Write(raw, 0, raw.Length);
        return output.ToArray();
    }

    private static IEnumerable<byte> Base128(uint value)
    {
        var groups = new List<byte>();
        do { groups.Insert(0, (byte)(value & 0x7F)); value >>= 7; } while (value != 0);
        for (int i = 0; i < groups.Count - 1; i++) groups[i] |= 0x80;
        return groups;
    }

    private static byte[] Be16(int v) => new[] { (byte)(v >> 8), (byte)v };

    private static byte[] Be32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    private static Dictionary<string, byte[]> SfntTables(byte[] font)
    {
        int count = (font[4] << 8) | font[5];
        var tables = new Dictionary<string, byte[]>();
        for (int i = 0; i < count; i++)
        {
            int record = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(font, record, 4);
            int offset = (font[record + 8] << 24) | (font[record + 9] << 16) | (font[record + 10] << 8) | font[record + 11];
            int length = (font[record + 12] << 24) | (font[record + 13] << 16) | (font[record + 14] << 8) | font[record + 15];
            tables[tag] = font.Skip(offset).Take(length).ToArray();
        }
        return tables;
    }

    private static byte[] SyntheticFont() => SyntheticFonts.Build(Enumerable.Range(32, 95));

    private static IEnumerable<Table> NullTransformed(byte[] font)
        => SfntTables(font).Select(kv => new Table(kv.Key, kv.Value,
            kv.Key == "glyf" || kv.Key == "loca" ? NullTransformOfGlyfAndLoca : 0));

    [Fact]
    public void NullTransformedTables_DecodeToTheOriginalTables()
    {
        var font = SyntheticFont();

        var decoded = Woff2Decoder.Decode(Woff2(TrueTypeFlavor, NullTransformed(font)));

        decoded.Should().NotBeNull();
        var original = SfntTables(font);
        var result = SfntTables(decoded!);
        result.Keys.Should().BeEquivalentTo(original.Keys);
        foreach (var tag in original.Keys) result[tag].Should().Equal(original[tag], $"table {tag}");
        TtfParser.Parse(decoded!)!.GetGlyphId('A').Should().Be(TtfParser.Parse(font)!.GetGlyphId('A'));
    }

    /// <summary>
    /// Three glyphs: 0 empty; 1 simple, two contours of three points whose triplets cover every flag range, two
    /// instruction bytes and the overlap bit; 2 a composite of glyph 1 offset by (100, -20) with a stored bbox.
    /// </summary>
    private static byte[] TransformedGlyf()
    {
        var stream = new List<byte>();
        stream.AddRange(Be16(0));
        stream.AddRange(Be16(1));
        stream.AddRange(Be16(3));
        stream.AddRange(Be16(0));
        byte[] contours = Be16(0).Concat(Be16(2)).Concat(Be16(-1)).ToArray();
        byte[] points = { 3, 3 };
        byte[] flags = { 0x01, 0x8D, 20, 87, 121, 124 };
        byte[] glyphs = { 50, 4, 0x23, 9, 19, 0x10, 0x23, 0x04, 0x01, 0x00, 0x00, 0x05, 2 };
        byte[] composites = { 0x00, 0x03, 0x00, 0x01, 0x00, 0x64, 0xFF, 0xEC };
        byte[] boxes = new byte[] { 0x20, 0, 0, 0 }.Concat(Be16(100)).Concat(Be16(-731)).Concat(Be16(625)).Concat(Be16(46)).ToArray();
        byte[] instructions = { 0xB0, 0x01 };
        byte[] overlap = { 0x40, 0, 0, 0 };
        foreach (var part in new[] { contours, points, flags, glyphs, composites, boxes, instructions })
            stream.AddRange(Be32((uint)part.Length));
        foreach (var part in new[] { contours, points, flags, glyphs, composites, boxes, instructions, overlap })
            stream.AddRange(part);
        return stream.ToArray();
    }

    private static readonly byte[] ExpectedSimpleGlyph = new[]
    {
        Be16(2), Be16(0), Be16(-711), Be16(525), Be16(66),
        Be16(2), Be16(5),
        Be16(2), new byte[] { 0xB0, 0x01 },
        new byte[] { 0x41, 0x00, 0x01, 0x01, 0x01, 0x01 },
        Be16(0), Be16(260), Be16(-3), Be16(10), Be16(258), Be16(-256),
        Be16(50), Be16(0), Be16(-4), Be16(20), Be16(-772), Be16(-5),
    }.SelectMany(b => b).ToArray();

    private static readonly byte[] ExpectedCompositeGlyph = new[]
    {
        Be16(-1), Be16(100), Be16(-731), Be16(625), Be16(46),
        new byte[] { 0x00, 0x03, 0x00, 0x01, 0x00, 0x64, 0xFF, 0xEC },
        new byte[] { 0, 0 },
    }.SelectMany(b => b).ToArray();

    private static byte[] TransformedFont(out Dictionary<string, byte[]> plainTables)
    {
        plainTables = SfntTables(SyntheticFonts.Build(new[] { (int)'A', 'B' }));
        plainTables["hhea"][35] = 2;
        byte[] hmtx = new byte[] { 0x03 }.Concat(Be16(500)).Concat(Be16(600)).ToArray();
        var tables = new List<Table>
        {
            new("cmap", plainTables["cmap"]), new("head", plainTables["head"]), new("hhea", plainTables["hhea"]),
            new("maxp", plainTables["maxp"]),
            new("glyf", TransformedGlyf(), transformVersion: 0, origLength: 68),
            new("loca", Array.Empty<byte>(), transformVersion: 0, origLength: 8),
            new("hmtx", hmtx, transformVersion: 1, origLength: 10),
        };
        return Woff2(TrueTypeFlavor, tables);
    }

    [Fact]
    public void TransformedGlyf_RebuildsSimpleAndCompositeGlyphsAndLoca()
    {
        var decoded = Woff2Decoder.Decode(TransformedFont(out _));

        decoded.Should().NotBeNull();
        var tables = SfntTables(decoded!);
        tables["glyf"].Should().Equal(ExpectedSimpleGlyph.Concat(ExpectedCompositeGlyph));
        tables["loca"].Should().Equal(Be16(0).Concat(Be16(0)).Concat(Be16(24)).Concat(Be16(34)));
    }

    [Fact]
    public void TransformedHmtx_TakesOmittedSideBearingsFromGlyphXMin()
    {
        var tables = SfntTables(Woff2Decoder.Decode(TransformedFont(out _))!);

        tables["hmtx"].Should().Equal(Be16(500).Concat(Be16(0)).Concat(Be16(600)).Concat(Be16(0)).Concat(Be16(100)));
    }

    [Fact]
    public void TransformedFont_ParsesAndDrawsTheSimpleGlyph()
    {
        var font = TtfParser.Parse(Woff2Decoder.Decode(TransformedFont(out _))!);

        font.Should().NotBeNull();
        var contours = GlyphOutlines.Get(font!, (ushort)1);
        contours.Should().HaveCount(2);
    }

    [Fact]
    public void FontCollection_IsNotDecoded()
    {
        Woff2Decoder.Decode(Woff2(0x74746366, NullTransformed(SyntheticFont()))).Should().BeNull();
    }

    [Fact]
    public void TruncatedFile_ReturnsNull()
    {
        var woff2 = Woff2(TrueTypeFlavor, NullTransformed(SyntheticFont()));

        for (int length = 4; length < woff2.Length; length += 7)
            Woff2Decoder.Decode(woff2.Take(length).ToArray()).Should().BeNull($"{length} of {woff2.Length} bytes");
    }

    [Fact]
    public void TableLengthsClaimingGigabytes_ReturnNullWithoutAllocatingThem()
    {
        var tables = new[] { new Table("head", new byte[54], origLength: 0x7FFF0000) };

        long before = GC.GetAllocatedBytesForCurrentThread();
        Woff2Decoder.Decode(Woff2(TrueTypeFlavor, tables)).Should().BeNull();
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(1024 * 1024);
    }

    [Fact]
    public void DecompressedDataLongerThanTheDirectory_ReturnsNull()
    {
        var tables = NullTransformed(SyntheticFont()).ToList();

        Woff2Decoder.Decode(Woff2(TrueTypeFlavor, tables, raw => Brotli(raw.Concat(new byte[16]).ToArray()))).Should().BeNull();
    }

    [Fact]
    public void GlyfStreamsLongerThanTheTable_ReturnNull()
    {
        var glyf = TransformedGlyf();
        glyf[12] = 0xFF;
        var plain = SfntTables(SyntheticFonts.Build(new[] { (int)'A', 'B' }));
        var tables = new List<Table>
        {
            new("head", plain["head"]), new("maxp", plain["maxp"]),
            new("glyf", glyf, transformVersion: 0, origLength: 68),
            new("loca", Array.Empty<byte>(), transformVersion: 0, origLength: 8),
        };

        Woff2Decoder.Decode(Woff2(TrueTypeFlavor, tables)).Should().BeNull();
    }

    [Fact]
    public void DecodeIfWoff_DecodesWoff2()
    {
        var font = SyntheticFont();

        WoffDecoder.DecodeIfWoff(Woff2(TrueTypeFlavor, NullTransformed(font))).Should().Equal(Woff2Decoder.Decode(Woff2(TrueTypeFlavor, NullTransformed(font))));
        WoffDecoder.DecodeIfWoff(font).Should().BeSameAs(font);
    }

    [Fact]
    public void FontFace_Woff2Source_IsDecodedAndEmbedded()
    {
        const string url = "https://woff2-source.example/w.woff2";
        var woff2 = Woff2(TrueTypeFlavor, NullTransformed(SyntheticFont()));
        FontUrlFetcher.FetchOverride = u => u == url ? woff2 : null;
        try
        {
            var pdf = Encoding.GetEncoding("ISO-8859-1").GetString(HtmlToPdf.Render(
                "<html><head><style>@font-face { font-family: W; src: url(\"" + url + "\") format(\"woff2\"); } p { font-family: W; }</style></head>" +
                "<body><p>Webfont</p></body></html>"));

            pdf.Should().Contain("/FontFile2");
        }
        finally
        {
            FontUrlFetcher.FetchOverride = null;
        }
    }
}
