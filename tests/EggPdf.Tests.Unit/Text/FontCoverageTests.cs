using System;
using System.IO;
using System.Linq;
using EggPdf.Text.Fallback;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class FontCoverageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eggpdf-fontcov-" + Guid.NewGuid().ToString("N"));

    public FontCoverageTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void ReadFaces_Format12Font_CoversExactlyTheMappedCodepoints()
    {
        var path = SyntheticFonts.Write(_dir, "a.ttf", SyntheticFonts.Build(new[] { 0xAC00, 0xAC01, 0xAC02, 0x4F60, 0x1F600 }));

        var faces = FontCoverageReader.ReadFaces(path);

        faces.Should().HaveCount(1);
        var c = faces[0]!;
        c.Contains(0xAC01).Should().BeTrue();
        c.Contains(0x4F60).Should().BeTrue();
        c.Contains(0x1F600).Should().BeTrue("format 12 reaches the supplementary planes");
        c.Contains(0xAC03).Should().BeFalse();
        c.Contains(0x41).Should().BeFalse();
        c.Count.Should().Be(5);
        c.HasOutlines.Should().BeTrue();
    }

    [Fact]
    public void ReadFaces_Format4Font_CoversBmpRuns()
    {
        var path = SyntheticFonts.Write(_dir, "b.ttf", SyntheticFonts.Build(new[] { 0x05D0, 0x05D1, 0x05D2, 0x0627 }, format4: true));

        var c = FontCoverageReader.ReadFaces(path).Single()!;

        c.Contains(0x05D1).Should().BeTrue();
        c.Contains(0x0627).Should().BeTrue();
        c.Contains(0x05D3).Should().BeFalse();
        c.Contains(0x0628).Should().BeFalse();
        c.Count.Should().Be(4);
    }

    [Fact]
    public void ReadFaces_FontWithoutGlyphOutlines_IsNotDrawable()
    {
        var path = SyntheticFonts.Write(_dir, "nooutlines.ttf", SyntheticFonts.Build(new[] { 0x41 }, outlines: false));

        FontCoverageReader.ReadFaces(path).Single()!.HasOutlines.Should().BeFalse();
    }

    [Fact]
    public void ReadFaces_BitmapOnlyColorFont_IsNotDrawable()
    {
        // sbix/CBDT emoji fonts map code points but carry no vector outlines this engine can paint
        var path = SyntheticFonts.Write(_dir, "bitmap.ttf", SyntheticFonts.Build(new[] { 0x1F600 }, bitmapColorOnly: true));

        FontCoverageReader.ReadFaces(path).Single()!.HasOutlines.Should().BeFalse();
    }

    [Fact]
    public void ReadFaces_Collection_ReturnsCoveragePerFace()
    {
        var ttc = SyntheticFonts.BuildCollection(
            SyntheticFonts.Build(new[] { 0x41, 0x42 }),
            SyntheticFonts.Build(new[] { 0xAC00, 0xAC01 }));
        var path = SyntheticFonts.Write(_dir, "set.ttc", ttc);

        var faces = FontCoverageReader.ReadFaces(path);

        faces.Should().HaveCount(2);
        faces[0]!.Contains(0x41).Should().BeTrue();
        faces[0]!.Contains(0xAC00).Should().BeFalse();
        faces[1]!.Contains(0xAC00).Should().BeTrue();
        faces[1]!.Contains(0x41).Should().BeFalse();
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { 0, 1, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0 })]   // sfnt header promising tables that are not there
    public void ReadFaces_GarbageFile_ReturnsNoCoverageWithoutThrowing(byte[] bytes)
    {
        var path = SyntheticFonts.Write(_dir, "junk.ttf", bytes);

        var faces = FontCoverageReader.ReadFaces(path);

        faces.Where(f => f != null).Should().BeEmpty();
    }

    [Fact]
    public void ReadFaces_MissingFile_ReturnsEmpty()
    {
        FontCoverageReader.ReadFaces(Path.Combine(_dir, "nope.ttf")).Should().BeEmpty();
    }

    [Fact]
    public void ReadFaces_AgreesWithTheFullParser()
    {
        // The cmap-only reader and TtfParser must agree on what a font covers, or the resolver
        // would pick a font the embedder then finds empty.
        var cps = new[] { 0x20, 0x41, 0x5A, 0x0416, 0x0E01, 0x0E02, 0x0E50, 0xAC00 };
        var bytes = SyntheticFonts.Build(cps);
        var path = SyntheticFonts.Write(_dir, "same.ttf", bytes);

        var coverage = FontCoverageReader.ReadFaces(path).Single()!;
        var font = TtfParser.Parse(bytes)!;

        var disagreements = Enumerable.Range(0, 0xB000)
            .Where(cp => coverage.Contains(cp) != (font.GetGlyphId(cp) != 0))
            .ToList();
        disagreements.Should().BeEmpty();
    }
}
