using System.Collections.Generic;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfDocumentColorGlyphTests
{
    private const int GrinningFace = 0x1F600;
    private const ushort EmojiBaseGlyph = 3, RedLayerGlyph = 5, TextColorLayerGlyph = 6;
    private const ushort EmojiBaseGlyphWidth = 500, LastGlyphWidth = 200;
    private const int UnitsPerEm = 1000;
    private const float FontSizePt = 24f;

    private static PdfDocument BuildDocWithColorFont()
    {
        var doc = new PdfDocument();
        var colorLayers = new Dictionary<int, List<(ushort newGlyphId, float r, float g, float b, bool useTextColor)>>
        {
            [GrinningFace] = new List<(ushort, float, float, float, bool)>
            {
                (RedLayerGlyph, 1f, 0f, 0f, false),
                (TextColorLayerGlyph, 0f, 0f, 0f, true),
            }
        };
        var widthsByNewGlyphId = new ushort[] { 0, 0, 0, EmojiBaseGlyphWidth, 0, 300, LastGlyphWidth };

        doc.AddEmbeddedFont("EmojiFont", new byte[] { 0x00 },
            new Dictionary<int, ushort> { [GrinningFace] = EmojiBaseGlyph },
            widthsByNewGlyphId,
            unitsPerEm: UnitsPerEm, ascent: 800, descent: -200,
            colorLayers: colorLayers);

        return doc;
    }

    [Fact]
    public void ContainsColorGlyphs_TextWithColorCodepoint_ReturnsTrue()
    {
        var doc = BuildDocWithColorFont();
        string emoji = char.ConvertFromUtf32(GrinningFace);

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
        var found = doc.TryGetColorLayers("EmojiFont", GrinningFace, out var layers);

        found.Should().BeTrue();
        layers.Should().HaveCount(2);
        layers![0].newGlyphId.Should().Be(RedLayerGlyph);
        layers[0].useTextColor.Should().BeFalse();
        layers[1].newGlyphId.Should().Be(TextColorLayerGlyph);
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

        doc.TryGetGlyphAdvancePt("EmojiFont", EmojiBaseGlyph, FontSizePt, out var advance).Should().BeTrue();
        advance.Should().BeApproximately(12f, 0.01f, "500 units of a 1000-unit em at 24pt is 12pt");
    }

    [Fact]
    public void TryGetGlyphAdvancePt_GlyphIdBeyondWidthsArray_FallsBackToLastWidth()
    {
        var doc = BuildDocWithColorFont();

        doc.TryGetGlyphAdvancePt("EmojiFont", 999, FontSizePt, out var advance).Should().BeTrue();
        advance.Should().BeApproximately(4.8f, 0.01f, "the last width, 200 units of a 1000-unit em at 24pt, is 4.8pt");
    }

    [Fact]
    public void TryGetGlyphAdvancePt_UnknownFont_ReturnsFalse()
    {
        var doc = BuildDocWithColorFont();
        doc.TryGetGlyphAdvancePt("NoSuchFont", EmojiBaseGlyph, FontSizePt, out var advance).Should().BeFalse();
        advance.Should().Be(0);
    }
}
