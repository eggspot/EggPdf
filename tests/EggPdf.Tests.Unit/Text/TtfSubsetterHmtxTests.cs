using System;
using System.IO;
using EggPdf.Text.OpenType;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// A subset's hmtx must carry each glyph's real left side bearing. Rasterizers place a TrueType
/// outline at (xMin - lsb); writing 0 shifts any glyph whose outline doesn't start at x=0 --
/// worst for zero-width combining marks, whose outlines sit at large negative x.
/// </summary>
public class TtfSubsetterHmtxTests
{
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
        // sara i (U+0E34) is a zero-width mark whose outline lies left of its origin.
        var subset = TtfSubsetter.Subset(font, new[] { 0x0E01, 0x0E34 })!;

        foreach (int cp in new[] { 0x0E01, 0x0E34 })
        {
            ushort oldGid = font.GetGlyphId(cp);
            ushort newGid = subset.OldToNewGlyphId[oldGid];
            LeftSideBearing(subset.FontData, newGid).Should().Be(LeftSideBearing(font.RawData, oldGid),
                $"U+{cp:X4} must keep its original left side bearing in the subset");
        }
        LeftSideBearing(font.RawData, font.GetGlyphId(0x0E34)).Should().NotBe(0,
            "the test needs a glyph with a non-zero lsb to be meaningful");
    }
}
