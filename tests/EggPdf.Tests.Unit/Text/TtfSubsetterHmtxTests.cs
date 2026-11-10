using System;
using System.IO;
using EggPdf.Text.OpenType;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class TtfSubsetterHmtxTests
{
    private const int ThaiKoKai = 0x0E01;
    private const int ThaiSaraI = 0x0E34;

    private static short LeftSideBearing(byte[] data, ushort gid)
    {
        var tables = SfntDirectory.Read(data);
        var r = new OtReader(data);
        int numHMetrics = r.U16((int)tables["hhea"].offset + 34);
        int hmtx = (int)tables["hmtx"].offset;
        return gid < numHMetrics ? r.I16(hmtx + 4 * gid + 2) : r.I16(hmtx + 4 * numHMetrics + 2 * (gid - numHMetrics));
    }

    [Fact]
    public void Subset_PreservesLeftSideBearingOfGlyphs()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "LeelawUI.ttf");
        if (!File.Exists(path)) return;

        var font = TtfParser.Parse(File.ReadAllBytes(path))!;
        var subset = TtfSubsetter.Subset(font, new[] { ThaiKoKai, ThaiSaraI })!;

        foreach (int cp in new[] { ThaiKoKai, ThaiSaraI })
        {
            ushort oldGid = font.GetGlyphId(cp);
            ushort newGid = subset.OldToNewGlyphId[oldGid];
            LeftSideBearing(subset.FontData, newGid).Should().Be(LeftSideBearing(font.RawData, oldGid),
                $"U+{cp:X4} must keep its original left side bearing: rasterizers place an outline at (xMin - lsb), " +
                "so writing 0 shifts every glyph whose outline doesn't start at x=0");
        }
        LeftSideBearing(font.RawData, font.GetGlyphId(ThaiSaraI)).Should().NotBe(0,
            "sara i is a zero-width mark whose outline lies left of its origin, the glyph a lost lsb shifts worst");
    }
}
