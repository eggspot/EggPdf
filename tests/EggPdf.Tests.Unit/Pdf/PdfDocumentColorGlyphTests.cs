using System.Collections.Generic;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// PdfDocument's color-glyph query methods (ContainsColorGlyphs/TryGetColorLayers/
/// TryGetGlyphAdvancePt) are what BoxPainter's per-codepoint color-glyph stepping loop
/// relies on -- these are pure data-flow tests against a synthetically registered
/// embedded font, independent of any real font file.
/// </summary>
public class PdfDocumentColorGlyphTests
{
    private static PdfDocument BuildDocWithColorFont()
    {
        var doc = new PdfDocument();
        var colorLayers = new Dictionary<int, List<(ushort newGlyphId, float r, float g, float b, bool useTextColor)>>
        {
            [0x1F600] = new List<(ushort, float, float, float, bool)>
            {
                (5, 1f, 0f, 0f, false), // red layer
                (6, 0f, 0f, 0f, true),  // text-color layer
            }
        };

        doc.AddEmbeddedFont("EmojiFont", new byte[] { 0x00 },
            new Dictionary<int, ushort> { [0x1F600] = 3 }, // base glyph 3 for the emoji codepoint
            new ushort[] { 0, 0, 0, 500, 0, 300, 200 }, // widths indexed by new glyph id; gid 3 -> 500
            unitsPerEm: 1000, ascent: 800, descent: -200,
            colorLayers: colorLayers);

        return doc;
    }

    [Fact]
    public void ContainsColorGlyphs_TextWithColorCodepoint_ReturnsTrue()
    {
        var doc = BuildDocWithColorFont();
        string emoji = char.ConvertFromUtf32(0x1F600);

        doc.ContainsColorGlyphs("EmojiFont", "Hi " + emoji).Should().BeTrue();
    }

    [Fact]
    public void ContainsColorGlyphs_TextWithoutColorCodepoint_ReturnsFalse()
    {
        var doc = BuildDocWithColorFont();
        doc.ContainsColorGlyphs("EmojiFont", "Hello World").Should().BeFalse();
    }

    [Fact]
    public void ContainsColorGlyphs_UnknownFont_ReturnsFalse()
    {
        var doc = BuildDocWithColorFont();
        doc.ContainsColorGlyphs("NoSuchFont", "anything").Should().BeFalse();
    }

    [Fact]
    public void TryGetColorLayers_KnownColorCodepoint_ReturnsLayersInOrder()
    {
        var doc = BuildDocWithColorFont();
        var found = doc.TryGetColorLayers("EmojiFont", 0x1F600, out var layers);

        found.Should().BeTrue();
        layers.Should().HaveCount(2);
        layers![0].newGlyphId.Should().Be(5);
        layers[0].useTextColor.Should().BeFalse();
        layers[1].newGlyphId.Should().Be(6);
        layers[1].useTextColor.Should().BeTrue();
    }

    [Fact]
    public void TryGetColorLayers_NonColorCodepoint_ReturnsFalse()
    {
        var doc = BuildDocWithColorFont();
        doc.TryGetColorLayers("EmojiFont", 'H', out var layers).Should().BeFalse();
        layers.Should().BeNull();
    }

    [Fact]
    public void TryGetGlyphAdvancePt_KnownGlyph_ScalesByUnitsPerEmAndFontSize()
    {
        var doc = BuildDocWithColorFont();
        // gid 3 -> width 500, unitsPerEm 1000, fontSize 24pt -> 500/1000*24 = 12pt
        doc.TryGetGlyphAdvancePt("EmojiFont", 3, 24f, out var advance).Should().BeTrue();
        advance.Should().BeApproximately(12f, 0.01f);
    }

    [Fact]
    public void TryGetGlyphAdvancePt_GlyphIdBeyondWidthsArray_FallsBackToLastWidth()
    {
        var doc = BuildDocWithColorFont();
        doc.TryGetGlyphAdvancePt("EmojiFont", 999, 24f, out var advance).Should().BeTrue();
        // last width in the array (index 6) = 200 -> 200/1000*24 = 4.8pt
        advance.Should().BeApproximately(4.8f, 0.01f);
    }

    [Fact]
    public void TryGetGlyphAdvancePt_UnknownFont_ReturnsFalse()
    {
        var doc = BuildDocWithColorFont();
        doc.TryGetGlyphAdvancePt("NoSuchFont", 3, 24f, out var advance).Should().BeFalse();
        advance.Should().Be(0);
    }
}
