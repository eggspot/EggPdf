using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class FontCollectionTests
{
    private static byte[] TwoFaceCollection() => SyntheticFonts.BuildCollection(
        SyntheticFonts.Build(new[] { 0x41, 0x42 }),
        SyntheticFonts.Build(new[] { 0xAC00, 0xAC01, 0xAC02 }, advance: 800));

    [Fact]
    public void ExtractFace_SecondFace_BecomesAStandaloneFontWithThatFacesGlyphs()
    {
        var face = FontCollection.ExtractFace(TwoFaceCollection(), 1);

        face.Should().NotBeNull();
        var font = TtfParser.Parse(face!);
        font.Should().NotBeNull();
        font!.GetGlyphId(0xAC01).Should().NotBe(0);
        font.GetGlyphId(0x41).Should().Be(0, "the first face's characters are not in the second face");
        font.GetAdvanceWidth(font.GetGlyphId(0xAC00)).Should().Be(800);
    }

    [Fact]
    public void ExtractFace_FirstFace_MatchesTheParsersOwnFirstFaceHandling()
    {
        var collection = TwoFaceCollection();

        var extracted = TtfParser.Parse(FontCollection.ExtractFace(collection, 0)!)!;
        var direct = TtfParser.Parse(collection)!;

        extracted.GetGlyphId(0x42).Should().Be(direct.GetGlyphId(0x42)).And.NotBe(0);
        extracted.GetGlyphId(0xAC00).Should().Be(0);
    }

    [Fact]
    public void ExtractFace_ExtractedFace_CanBeSubsetForEmbedding()
    {
        var font = TtfParser.Parse(FontCollection.ExtractFace(TwoFaceCollection(), 1)!)!;

        var subset = TtfSubsetter.Subset(font, new[] { 0xAC01 });

        subset.Should().NotBeNull();
        subset!.CodepointToNewGlyphId.Should().ContainKey(0xAC01);
    }

    [Fact]
    public void ExtractFace_PreservesTheFacesSfntVersion()
    {
        var face = FontCollection.ExtractFace(TwoFaceCollection(), 1)!;

        face[0].Should().Be(0); face[1].Should().Be(1); face[2].Should().Be(0); face[3].Should().Be(0);
    }

    [Fact]
    public void ExtractFace_NotACollection_ReturnsNull()
    {
        FontCollection.ExtractFace(SyntheticFonts.Build(new[] { 0x41 }), 0).Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public void ExtractFace_IndexOutOfRange_ReturnsNull(int index)
    {
        FontCollection.ExtractFace(TwoFaceCollection(), index).Should().BeNull();
    }

    [Fact]
    public void ExtractFace_TruncatedCollection_ReturnsNullInsteadOfThrowing()
    {
        var collection = TwoFaceCollection();
        var truncated = new byte[collection.Length / 2];
        System.Array.Copy(collection, truncated, truncated.Length);

        FontCollection.ExtractFace(truncated, 1).Should().BeNull();
    }
}
