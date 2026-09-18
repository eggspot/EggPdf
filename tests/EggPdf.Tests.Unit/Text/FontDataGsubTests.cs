using System.Collections.Generic;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// FontData.GetGlyphId(codepoint, activeFeatures) applies GSUB single-substitution
/// (font-feature-settings) after the normal cmap lookup. These tests construct FontData
/// directly (internal access) rather than a real GSUB binary table, since the
/// substitution algorithm -- not the raw table parsing -- is the consumer-visible
/// behavior shared by measurement, painting, and subsetting.
/// </summary>
public class FontDataGsubTests
{
    private static FontData BuildFont()
    {
        var cmap = new CmapData();
        cmap.Add('0', 10); // digit '0' -> glyph 10 (proportional figure)
        cmap.Add('a', 20);
        var font = new FontData { Cmap = cmap };
        return font;
    }

    [Fact]
    public void GetGlyphId_NoActiveFeatures_ReturnsOriginalGlyph()
    {
        var font = BuildFont();
        font.GsubFeatures = new Dictionary<string, Dictionary<ushort, ushort>>
        {
            ["zero"] = new Dictionary<ushort, ushort> { [10] = 99 }
        };

        font.GetGlyphId('0', null).Should().Be(10);
        font.GetGlyphId('0', new List<string>()).Should().Be(10);
    }

    [Fact]
    public void GetGlyphId_ActiveFeatureWithMapping_ReturnsSubstitutedGlyph()
    {
        var font = BuildFont();
        font.GsubFeatures = new Dictionary<string, Dictionary<ushort, ushort>>
        {
            ["zero"] = new Dictionary<ushort, ushort> { [10] = 99 } // slashed-zero variant
        };

        font.GetGlyphId('0', new List<string> { "zero" }).Should().Be(99);
    }

    [Fact]
    public void GetGlyphId_ActiveFeatureWithoutMappingForThisGlyph_ReturnsOriginalGlyph()
    {
        var font = BuildFont();
        font.GsubFeatures = new Dictionary<string, Dictionary<ushort, ushort>>
        {
            ["zero"] = new Dictionary<ushort, ushort> { [10] = 99 }
        };

        // 'a' -> glyph 20, not covered by the "zero" feature's map
        font.GetGlyphId('a', new List<string> { "zero" }).Should().Be(20);
    }

    [Fact]
    public void GetGlyphId_UnknownFeatureTag_ReturnsOriginalGlyph()
    {
        var font = BuildFont();
        font.GsubFeatures = new Dictionary<string, Dictionary<ushort, ushort>>
        {
            ["zero"] = new Dictionary<ushort, ushort> { [10] = 99 }
        };

        font.GetGlyphId('0', new List<string> { "smcp" }).Should().Be(10);
    }

    [Fact]
    public void GetGlyphId_FontWithNoGsubTable_ReturnsOriginalGlyph()
    {
        var font = BuildFont(); // GsubFeatures left null
        font.GetGlyphId('0', new List<string> { "zero" }).Should().Be(10);
    }

    [Fact]
    public void GetGlyphId_MultipleActiveFeatures_ChainsSubstitutionsInOrder()
    {
        var font = BuildFont();
        font.GsubFeatures = new Dictionary<string, Dictionary<ushort, ushort>>
        {
            ["ss01"] = new Dictionary<ushort, ushort> { [10] = 50 },
            ["ss02"] = new Dictionary<ushort, ushort> { [50] = 60 },
        };

        // ss01 first: 10 -> 50, then ss02: 50 -> 60
        font.GetGlyphId('0', new List<string> { "ss01", "ss02" }).Should().Be(60);
    }

    [Fact]
    public void GetGlyphId_UnmappedCodepoint_ReturnsZeroWithoutFeatureLookup()
    {
        var font = BuildFont();
        font.GsubFeatures = new Dictionary<string, Dictionary<ushort, ushort>>
        {
            ["zero"] = new Dictionary<ushort, ushort> { [0] = 99 } // glyph 0 (.notdef) mapping, should never apply
        };

        font.GetGlyphId('Z', new List<string> { "zero" }).Should().Be(0, "unmapped codepoints stay glyph 0 regardless of GSUB features");
    }
}
