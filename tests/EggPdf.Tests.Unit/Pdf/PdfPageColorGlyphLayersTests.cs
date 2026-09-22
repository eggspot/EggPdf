using System.Collections.Generic;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// PdfPage.AddColorGlyphLayers must paint each layer at the same position (a "0 0 Td"
/// between layers resets the text matrix without moving it) in its own fill color, and
/// substitute the caller's text color for a useTextColor layer.
/// </summary>
public class PdfPageColorGlyphLayersTests
{
    private static string RenderToText(PdfDocument doc)
        => Encoding.Latin1.GetString(doc.ToByteArray());

    [Fact]
    public void AddColorGlyphLayers_EmitsOneTjPerLayerWithResetBetween()
    {
        var doc = new PdfDocument();
        doc.AddEmbeddedFont("ColorFont", new byte[] { 0x00 },
            new Dictionary<int, ushort>(), new ushort[] { 0, 500 }, unitsPerEm: 1000, ascent: 800, descent: -200);
        var page = doc.AddPage(595.28f, 841.89f);

        var layers = new List<(ushort glyphId, float r, float g, float b, bool useTextColor)>
        {
            (5, 1f, 0f, 0f, false),
            (6, 0f, 0f, 1f, false),
        };
        page.AddColorGlyphLayers(layers, 72, 720, "ColorFont", 24, 0, 0, 0);

        var text = RenderToText(doc);
        text.Should().Contain("<0005> Tj");
        text.Should().Contain("<0006> Tj");
        text.Should().Contain("1.00 0.00 0.00 rg");
        text.Should().Contain("0.00 0.00 1.00 rg");
        text.Should().Contain("0 0 Td");
    }

    [Fact]
    public void AddColorGlyphLayers_UseTextColorLayer_PaintsInCallerTextColor()
    {
        var doc = new PdfDocument();
        doc.AddEmbeddedFont("ColorFont", new byte[] { 0x00 },
            new Dictionary<int, ushort>(), new ushort[] { 0, 500 }, unitsPerEm: 1000, ascent: 800, descent: -200);
        var page = doc.AddPage(595.28f, 841.89f);

        var layers = new List<(ushort glyphId, float r, float g, float b, bool useTextColor)>
        {
            (7, 0f, 0f, 0f, true),
        };
        // Caller's text color: green (0, 1, 0)
        page.AddColorGlyphLayers(layers, 72, 720, "ColorFont", 24, 0f, 1f, 0f);

        var text = RenderToText(doc);
        text.Should().Contain("0.00 1.00 0.00 rg", "a useTextColor layer must paint in the caller's text color, not (0,0,0)");
    }

    [Fact]
    public void AddColorGlyphLayers_EmptyLayers_EmitsNoTextContent()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);

        page.AddColorGlyphLayers(new List<(ushort, float, float, float, bool)>(), 72, 720, "ColorFont", 24, 0, 0, 0);

        var text = RenderToText(doc);
        text.Should().NotContain("Tj", "no layers means no glyphs painted");
        text.Should().NotContain("BT");
    }
}
