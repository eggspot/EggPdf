using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EggPdf.Text.OpenType;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class FontSubsetAndKerningTests
{
    private const uint ChecksumMagic = 0xB1B0AFBA;
    private const short KernAV = -80;

    private static byte[] U16(int v) => new[] { (byte)(v >> 8), (byte)v };
    private static byte[] U32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
    private static byte[] Tag(string tag) => System.Text.Encoding.ASCII.GetBytes(tag);
    private static byte[] Bytes(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] WithTables(byte[] font, params (string Tag, byte[] Data)[] extra)
    {
        var tables = new Dictionary<string, byte[]>();
        foreach (var kv in SfntDirectory.Read(font))
            tables[kv.Key] = font.Skip((int)kv.Value.offset).Take((int)kv.Value.length).ToArray();
        foreach (var (tag, data) in extra) tables[tag] = data;
        return SfntWriter.WriteFont(tables);
    }

    private static byte[] Table(byte[] font, string tag)
    {
        var entry = SfntDirectory.Read(font)[tag];
        return font.Skip((int)entry.offset).Take((int)entry.length).ToArray();
    }

    private static byte[] ClassPairPosSubtable() => Bytes(
        U16(2), U16(24), U16(0x0004), U16(0), U16(30), U16(38), U16(2), U16(2),
        U16(0), U16(0), U16(0), U16(unchecked((ushort)KernAV)),
        U16(1), U16(1), U16(1),
        U16(1), U16(1), U16(1), U16(1),
        U16(1), U16(2), U16(1), U16(1));

    private static byte[] GposWithClassKerning(bool extension)
    {
        var subtable = extension
            ? Bytes(U16(1), U16(2), U32(8), ClassPairPosSubtable())
            : ClassPairPosSubtable();
        var lookup = Bytes(U16(extension ? 9 : 2), U16(0), U16(1), U16(8), subtable);
        var lookupList = Bytes(U16(1), U16(4), lookup);
        var featureList = Bytes(U16(1), Tag("kern"), U16(8), U16(0), U16(1), U16(0));
        var scriptList = U16(0);
        return Bytes(U32(0x00010000), U16(10), U16(12), U16(12 + featureList.Length), scriptList, featureList, lookupList);
    }

    private static FontData FontWith(params (string Tag, byte[] Data)[] tables) =>
        TtfParser.Parse(WithTables(SyntheticFonts.Build(new[] { (int)'A', (int)'V' }), tables))!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GposClassBasedKerning_IsRead_PlainAndInsideAnExtensionLookup(bool extension)
    {
        var font = FontWith(("GPOS", GposWithClassKerning(extension)));
        ushort a = font.GetGlyphId('A'), v = font.GetGlyphId('V');

        font.GetKerning(a, v).Should().Be(KernAV);
        font.GetKerning(v, a).Should().Be(0);
    }

    [Fact]
    public void KernTable_SkipsAnUnsupportedSubtableFromItsOwnStart()
    {
        var unsupported = Bytes(U16(0), U16(14), U16((2 << 8) | 1), new byte[8]);
        var pairs = Bytes(U16(0), U16(20), U16(1), U16(1), U16(6), U16(0), U16(0), U16(1), U16(2), U16(unchecked((ushort)(short)-50)));
        var font = FontWith(("kern", Bytes(U16(0), U16(2), unsupported, pairs)));

        font.GetKerning(font.GetGlyphId('A'), font.GetGlyphId('V')).Should().Be(-50);
    }

    [Fact]
    public void CoverageRangeEndingAtTheLastGlyphId_DoesNotHangTheParser()
    {
        var subtable = Bytes(U16(1), U16(6), U16(1), U16(2), U16(1), U16(0xFFF0), U16(0xFFFF), U16(0));
        var lookup = Bytes(U16(1), U16(0), U16(1), U16(8), subtable);
        var lookupList = Bytes(U16(1), U16(4), lookup);
        var featureList = Bytes(U16(1), Tag("zero"), U16(8), U16(0), U16(1), U16(0));
        var gsub = Bytes(U32(0x00010000), U16(10), U16(12), U16(12 + featureList.Length), U16(0), featureList, lookupList);
        var bytes = WithTables(SyntheticFonts.Build(new[] { (int)'A' }), ("GSUB", gsub));

        var parse = Task.Run(() => TtfParser.Parse(bytes));

        parse.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("a coverage range ending at 0xFFFF must not wrap around");
        parse.Result.Should().NotBeNull();
    }

    private static TtfSubsetter.SubsetResult SubsetOfFontWithOs2NameAndPost()
    {
        var post = Bytes(U32(0x00020000), U32(unchecked((uint)(-12 << 16))), U16(unchecked((ushort)(short)-100)), U16(50), U32(1), new byte[16]);
        var name = Bytes(U16(0), U16(0), U16(6));
        var font = FontWith(("OS/2", new byte[96]), ("name", name), ("post", post));
        return TtfSubsetter.Subset(font, new[] { (int)'A' })!;
    }

    [Fact]
    public void Subset_TableDirectory_IsInOrdinalTagOrder()
    {
        var subset = SubsetOfFontWithOs2NameAndPost().FontData;

        int numTables = (subset[4] << 8) | subset[5];
        var tags = Enumerable.Range(0, numTables)
            .Select(i => System.Text.Encoding.ASCII.GetString(subset, 12 + 16 * i, 4)).ToList();
        tags.Should().BeInAscendingOrder(StringComparer.Ordinal);
        tags[0].Should().Be("OS/2", "uppercase sorts before lowercase in binary tag order");
    }

    [Fact]
    public void Subset_WholeFontChecksum_EqualsTheMagicNumber()
    {
        var subset = SubsetOfFontWithOs2NameAndPost().FontData;

        uint sum = 0;
        for (int i = 0; i < subset.Length; i += 4)
            sum += (uint)((subset[i] << 24) | (subset[i + 1] << 16) | (subset[i + 2] << 8) | subset[i + 3]);
        sum.Should().Be(ChecksumMagic);
    }

    [Fact]
    public void Subset_PostTable_KeepsItalicAngleUnderlineAndFixedPitch()
    {
        var post = Table(SubsetOfFontWithOs2NameAndPost().FontData, "post");
        var r = new OtReader(post);

        r.U32(0).Should().Be(0x00030000u, "glyph names are dropped");
        ((int)r.U32(4) >> 16).Should().Be(-12);
        r.I16(8).Should().Be(-100);
        r.U32(12).Should().Be(1u, "isFixedPitch survives");
    }

    [Fact]
    public void Subset_FewCodepoints_WritesFormat4WithItsTrueLength()
    {
        var subset = TtfSubsetter.Subset(TtfParser.Parse(SyntheticFonts.Build(new[] { (int)'A', (int)'C' }))!, new[] { (int)'A', (int)'C' })!;
        var cmap = Table(subset.FontData, "cmap");
        var r = new OtReader(cmap);

        r.U16(12).Should().Be(4);
        ((int)r.U16(14)).Should().Be(cmap.Length - 12, "the length counts the reservedPad word");
    }

    [Fact]
    public void Subset_ThousandsOfSeparatedCodepoints_StillMapsEveryCodepoint()
    {
        var codepoints = Enumerable.Range(0, 9000).Select(i => 0x4E00 + 2 * i).ToArray();
        var font = TtfParser.Parse(SyntheticFonts.Build(codepoints))!;

        var subset = TtfSubsetter.Subset(font, codepoints)!;
        var reparsed = TtfParser.Parse(subset.FontData)!;

        foreach (int cp in new[] { codepoints[0], codepoints[4500], codepoints[^1] })
            reparsed.GetGlyphId(cp).Should().Be(subset.CodepointToNewGlyphId[cp]);
    }
}
