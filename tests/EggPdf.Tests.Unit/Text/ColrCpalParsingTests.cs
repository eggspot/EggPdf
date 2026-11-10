using System;
using System.IO;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class ColrCpalParsingTests
{
    private const ushort UseTextColorPaletteIndex = 0xFFFF;
    private static readonly byte[] RedBgra = { 0x00, 0x00, 0xFF, 0xFF };
    private static readonly byte[] BlueBgra = { 0xFF, 0x00, 0x00, 0xFF };

    private static void WriteU16(MemoryStream ms, int v) => ms.Write(new byte[] { (byte)(v >> 8), (byte)v }, 0, 2);
    private static void WriteU32(MemoryStream ms, uint v) => ms.Write(new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }, 0, 4);

    /// <summary>A CPAL table with one palette of two BGRA colors: entry 0 red, entry 1 blue.</summary>
    private static byte[] BuildCpalTable()
    {
        const int version = 0, numPaletteEntries = 2, numPalettes = 1, numColorRecords = 2;
        const int headerSize = 12, paletteIndexArraySize = 2 * numPalettes;
        const int firstColorRecordOfPalette0 = 0;

        using var ms = new MemoryStream();
        WriteU16(ms, version);
        WriteU16(ms, numPaletteEntries);
        WriteU16(ms, numPalettes);
        WriteU16(ms, numColorRecords);
        WriteU32(ms, headerSize + paletteIndexArraySize);
        WriteU16(ms, firstColorRecordOfPalette0);
        ms.Write(RedBgra, 0, 4);
        ms.Write(BlueBgra, 0, 4);
        return ms.ToArray();
    }

    /// <summary>A version 0 COLR table: base glyph 5 drawn as glyph 10 in palette color 0, then glyph 11 in the text color.</summary>
    private static byte[] BuildColrTable()
    {
        const int version = 0, numBaseGlyphRecords = 1, numLayerRecords = 2;
        const int headerSize = 14, baseGlyphRecordSize = 6;
        const int baseGlyph = 5, firstLayerIndex = 0;

        using var ms = new MemoryStream();
        WriteU16(ms, version);
        WriteU16(ms, numBaseGlyphRecords);
        WriteU32(ms, headerSize);
        WriteU32(ms, headerSize + numBaseGlyphRecords * baseGlyphRecordSize);
        WriteU16(ms, numLayerRecords);

        WriteU16(ms, baseGlyph);
        WriteU16(ms, firstLayerIndex);
        WriteU16(ms, numLayerRecords);

        WriteU16(ms, 10); WriteU16(ms, 0);
        WriteU16(ms, 11); WriteU16(ms, UseTextColorPaletteIndex);
        return ms.ToArray();
    }

    /// <summary>
    /// An sfnt with only a table directory and the given tables: TtfParser reads every other table only when it
    /// exists, so head/hmtx/cmap/glyf aren't needed to exercise the byte-level CPAL/COLR parsing.
    /// </summary>
    private static byte[] BuildMinimalSfntWithTables(params (string tag, byte[] data)[] tables)
    {
        const uint trueTypeOutlines = 0x00010000;
        const int searchRange = 0, entrySelector = 0, rangeShift = 0, unusedChecksum = 0;

        using var ms = new MemoryStream();
        WriteU32(ms, trueTypeOutlines);
        WriteU16(ms, tables.Length);
        WriteU16(ms, searchRange); WriteU16(ms, entrySelector); WriteU16(ms, rangeShift);

        int headerSize = 12 + tables.Length * 16;
        int cursor = headerSize;
        var offsets = new int[tables.Length];
        for (int i = 0; i < tables.Length; i++)
        {
            offsets[i] = cursor;
            cursor += tables[i].data.Length;
        }

        for (int i = 0; i < tables.Length; i++)
        {
            ms.Write(System.Text.Encoding.ASCII.GetBytes(tables[i].tag), 0, 4);
            WriteU32(ms, unusedChecksum);
            WriteU32(ms, (uint)offsets[i]);
            WriteU32(ms, (uint)tables[i].data.Length);
        }

        for (int i = 0; i < tables.Length; i++)
            ms.Write(tables[i].data, 0, tables[i].data.Length);

        return ms.ToArray();
    }

    [Fact]
    public void Parse_CpalTable_ResolvesPalette0Colors()
    {
        var sfnt = BuildMinimalSfntWithTables(("CPAL", BuildCpalTable()));
        var font = TtfParser.Parse(sfnt);

        font.Should().NotBeNull();
        font!.CpalPalette.Should().NotBeNull();
        font.CpalPalette!.Length.Should().Be(2);
        font.CpalPalette[0].Should().Be(((byte)255, (byte)0, (byte)0, (byte)255), "entry 0 is red (BGRA 00 00 FF FF)");
        font.CpalPalette[1].Should().Be(((byte)0, (byte)0, (byte)255, (byte)255), "entry 1 is blue (BGRA FF 00 00 FF)");
    }

    [Fact]
    public void Parse_ColrTable_MapsBaseGlyphToOrderedLayers()
    {
        var sfnt = BuildMinimalSfntWithTables(("COLR", BuildColrTable()));
        var font = TtfParser.Parse(sfnt);

        font.Should().NotBeNull();
        font!.ColrLayers.Should().NotBeNull();
        font.ColrLayers.Should().ContainKey((ushort)5);

        var layers = font.ColrLayers![5];
        layers.Should().HaveCount(2);
        layers[0].Should().Be(((ushort)10, 0), "first layer uses palette index 0");
        layers[1].Should().Be(((ushort)11, -1), "second layer's 0xFFFF sentinel means 'use text color'");
    }

    [Fact]
    public void Parse_ColrAndCpalTogether_BothPopulated()
    {
        var sfnt = BuildMinimalSfntWithTables(("CPAL", BuildCpalTable()), ("COLR", BuildColrTable()));
        var font = TtfParser.Parse(sfnt);

        font.Should().NotBeNull();
        font!.CpalPalette.Should().NotBeNull();
        font.ColrLayers.Should().NotBeNull();
    }

    [Fact]
    public void Parse_NoColrOrCpalTable_LeavesBothNull()
    {
        var font = TtfParser.Parse(BuildMinimalSfntWithTables());
        font.Should().NotBeNull();
        font!.ColrLayers.Should().BeNull();
        font.CpalPalette.Should().BeNull();
    }

    [Fact]
    public void Parse_ColrVersion1_IsIgnored()
    {
        const int colrVersion1 = 1;
        using var ms = new MemoryStream();
        WriteU16(ms, colrVersion1);
        WriteU16(ms, 0); WriteU32(ms, 0); WriteU32(ms, 0); WriteU16(ms, 0);
        var sfnt = BuildMinimalSfntWithTables(("COLR", ms.ToArray()));

        var font = TtfParser.Parse(sfnt);
        font.Should().NotBeNull();
        font!.ColrLayers.Should().BeNull("COLRv1 (gradients/paint graphs) is out of scope; a v1 table must be quietly skipped, not misparsed");
    }
}
