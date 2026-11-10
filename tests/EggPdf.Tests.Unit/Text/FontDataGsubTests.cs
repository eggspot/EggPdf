using System.Collections.Generic;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class FontDataGsubTests
{
    private const ushort ProportionalZeroGlyph = 10;
    private const ushort LetterAGlyph = 20;
    private const ushort SlashedZeroGlyph = 99;
    private const ushort NotdefGlyph = 0;

    private static FontData BuildFont()
    {
        var cmap = new CmapData();
        cmap.Add('0', ProportionalZeroGlyph);
        cmap.Add('a', LetterAGlyph);
        return new FontData { Cmap = cmap };
    }

    private static Dictionary<string, Dictionary<ushort, ushort>> SlashedZeroFeature() =>
        new Dictionary<string, Dictionary<ushort, ushort>>
        {
            ["zero"] = new Dictionary<ushort, ushort> { [ProportionalZeroGlyph] = SlashedZeroGlyph }
        };

    [Fact]
    public void GetGlyphId_NoActiveFeatures_ReturnsOriginalGlyph()
    {
        var font = BuildFont();
        font.GsubFeatures = SlashedZeroFeature();

        font.GetGlyphId('0', null).Should().Be(ProportionalZeroGlyph);
        font.GetGlyphId('0', new List<string>()).Should().Be(ProportionalZeroGlyph);
    }

    [Fact]
    public void GetGlyphId_ActiveFeatureWithMapping_ReturnsSubstitutedGlyph()
    {
        var font = BuildFont();
        font.GsubFeatures = SlashedZeroFeature();

        font.GetGlyphId('0', new List<string> { "zero" }).Should().Be(SlashedZeroGlyph);
    }

    [Fact]
    public void GetGlyphId_ActiveFeatureWithoutMappingForThisGlyph_ReturnsOriginalGlyph()
    {
        var font = BuildFont();
        font.GsubFeatures = SlashedZeroFeature();

        font.GetGlyphId('a', new List<string> { "zero" }).Should().Be(LetterAGlyph,
            "the \"zero\" feature's map doesn't cover the letter a's glyph");
    }

    [Fact]
    public void GetGlyphId_UnknownFeatureTag_ReturnsOriginalGlyph()
    {
        var font = BuildFont();
        font.GsubFeatures = SlashedZeroFeature();

        font.GetGlyphId('0', new List<string> { "smcp" }).Should().Be(ProportionalZeroGlyph);
    }

    [Fact]
    public void GetGlyphId_FontWithNoGsubTable_ReturnsOriginalGlyph()
    {
        var font = BuildFont();

        font.GsubFeatures.Should().BeNull();
        font.GetGlyphId('0', new List<string> { "zero" }).Should().Be(ProportionalZeroGlyph);
    }

    [Fact]
    public void GetGlyphId_MultipleActiveFeatures_ChainsSubstitutionsInOrder()
    {
        const ushort stylisticSet1Glyph = 50, stylisticSet2Glyph = 60;
        var font = BuildFont();
        font.GsubFeatures = new Dictionary<string, Dictionary<ushort, ushort>>
        {
            ["ss01"] = new Dictionary<ushort, ushort> { [ProportionalZeroGlyph] = stylisticSet1Glyph },
            ["ss02"] = new Dictionary<ushort, ushort> { [stylisticSet1Glyph] = stylisticSet2Glyph },
        };

        font.GetGlyphId('0', new List<string> { "ss01", "ss02" }).Should().Be(stylisticSet2Glyph,
            "ss01 maps the zero to its glyph first, then ss02 maps that result");
    }

    [Fact]
    public void GetGlyphId_UnmappedCodepoint_ReturnsZeroWithoutFeatureLookup()
    {
        var font = BuildFont();
        font.GsubFeatures = new Dictionary<string, Dictionary<ushort, ushort>>
        {
            ["zero"] = new Dictionary<ushort, ushort> { [NotdefGlyph] = SlashedZeroGlyph }
        };

        font.GetGlyphId('Z', new List<string> { "zero" }).Should().Be(NotdefGlyph,
            "unmapped code points stay .notdef regardless of GSUB features, even one that maps .notdef");
    }
}
