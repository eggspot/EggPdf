using System;
using System.IO;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// Hand-constructs a minimal sfnt containing only a table directory plus CPAL/COLR
/// table bytes (every other table in TtfParser.ParseInternal is read behind its own
/// "if the table exists" check, so head/hmtx/cmap/glyf aren't needed just to exercise
/// CPAL/COLR parsing). This verifies the actual byte-level table parsing, not just the
/// higher-level substitution logic.
/// </summary>
public class ColrCpalParsingTests
{
    private static void WriteU16(MemoryStream ms, int v) => ms.Write(new byte[] { (byte)(v >> 8), (byte)v }, 0, 2);
    private static void WriteU32(MemoryStream ms, uint v) => ms.Write(new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }, 0, 4);

    private static byte[] BuildCpalTable()
    {
        using var ms = new MemoryStream();
        WriteU16(ms, 0);  // version
        WriteU16(ms, 2);  // numPaletteEntries
        WriteU16(ms, 1);  // numPalettes
        WriteU16(ms, 2);  // numColorRecords
        WriteU32(ms, 14); // colorRecordsArrayOffset (right after this 12-byte header + 2-byte index array)
        WriteU16(ms, 0);  // colorRecordIndices[0]
        // Color records, BGRA order: entry 0 = red, entry 1 = blue
        ms.Write(new byte[] { 0x00, 0x00, 0xFF, 0xFF }, 0, 4); // B=0 G=0 R=255 A=255 -> red
        ms.Write(new byte[] { 0xFF, 0x00, 0x00, 0xFF }, 0, 4); // B=255 G=0 R=0 A=255 -> blue
        return ms.ToArray();
    }

    private static byte[] BuildColrTable()
    {
        using var ms = new MemoryStream();
        WriteU16(ms, 0);  // version
        WriteU16(ms, 1);  // numBaseGlyphRecords
        WriteU32(ms, 14); // baseGlyphRecordsOffset
        WriteU32(ms, 20); // layerRecordsOffset (14 + 1*6)
        WriteU16(ms, 2);  // numLayerRecords
        // BaseGlyphRecord: glyphID=5, firstLayerIndex=0, numLayers=2
        WriteU16(ms, 5);
        WriteU16(ms, 0);
        WriteU16(ms, 2);
        // LayerRecords
        WriteU16(ms, 10); WriteU16(ms, 0);      // layer glyph 10, palette index 0 (red)
        WriteU16(ms, 11); WriteU16(ms, 0xFFFF); // layer glyph 11, "use text color" sentinel
        return ms.ToArray();
    }

    private static byte[] BuildMinimalSfntWithTables(params (string tag, byte[] data)[] tables)
    {
        using var ms = new MemoryStream();
        WriteU32(ms, 0x00010000); // sfVersion
        WriteU16(ms, tables.Length);
        WriteU16(ms, 0); WriteU16(ms, 0); WriteU16(ms, 0); // searchRange, entrySelector, rangeShift

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
            WriteU32(ms, 0); // checksum, unused
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
        var sfnt = BuildMinimalSfntWithTables(("CPAL", Array.Empty<byte>())); // malformed/empty, but present
        var font = TtfParser.Parse(BuildMinimalSfntWithTables()); // no tables at all
        font.Should().NotBeNull();
        font!.ColrLayers.Should().BeNull();
        font.CpalPalette.Should().BeNull();
    }

    [Fact]
    public void Parse_ColrVersion1_IsIgnored()
    {
        using var ms = new MemoryStream();
        WriteU16(ms, 1); // version 1 (COLRv1, unsupported) -- rest of the table is irrelevant
        WriteU16(ms, 0); WriteU32(ms, 0); WriteU32(ms, 0); WriteU16(ms, 0);
        var sfnt = BuildMinimalSfntWithTables(("COLR", ms.ToArray()));

        var font = TtfParser.Parse(sfnt);
        font.Should().NotBeNull();
        font!.ColrLayers.Should().BeNull("COLRv1 (gradients/paint graphs) is out of scope; a v1 table must be quietly skipped, not misparsed");
    }
}
