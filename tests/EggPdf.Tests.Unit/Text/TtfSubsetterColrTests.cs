using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// TtfSubsetter.Subset must pull COLR layer glyphs into the embedded subset (they are
/// separate glyph IDs, not composite components of the base glyph, so nothing else
/// would keep them) and resolve them to actual colors via CPAL, remapped to new glyph
/// IDs. Uses a real system font for valid outlines; the COLR/CPAL data itself is
/// injected directly so the test doesn't depend on any installed font actually having
/// color-glyph data.
/// </summary>
public class TtfSubsetterColrTests
{
    private static string? FindSystemFont(string name)
    {
        string[] searchPaths;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            searchPaths = new[] { @"C:\Windows\Fonts" };
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            searchPaths = new[] { "/System/Library/Fonts", "/Library/Fonts" };
        else
            searchPaths = new[] { "/usr/share/fonts", "/usr/local/share/fonts" };

        foreach (var dir in searchPaths)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var files = Directory.GetFiles(dir, "*.ttf", SearchOption.AllDirectories);
                var match = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                    .IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;
            }
            catch { }
        }
        return null;
    }

    private static FontData? LoadTestFont()
    {
        var path = FindSystemFont("arial") ?? FindSystemFont("DejaVuSans") ?? FindSystemFont("Liberation");
        if (path == null) return null;
        return TtfParser.Parse(File.ReadAllBytes(path));
    }

    [Fact]
    public void Subset_BaseGlyphWithColorLayers_EmbedsLayerGlyphsAndResolvesColors()
    {
        var font = LoadTestFont();
        if (font == null) return;

        ushort baseGid = font.GetGlyphId('A');
        ushort layerGidRed = font.GetGlyphId('B');
        ushort layerGidText = font.GetGlyphId('C');
        if (baseGid == 0 || layerGidRed == 0 || layerGidText == 0) return;
        if (baseGid == layerGidRed || baseGid == layerGidText || layerGidRed == layerGidText) return;

        font.CpalPalette = new (byte r, byte g, byte b, byte a)[] { (255, 0, 0, 255) }; // palette[0] = red
        font.ColrLayers = new Dictionary<ushort, List<(ushort layerGlyphId, int paletteIndex)>>
        {
            [baseGid] = new List<(ushort, int)>
            {
                (layerGidRed, 0),   // red palette layer
                (layerGidText, -1), // "use text color" layer
            }
        };

        var result = TtfSubsetter.Subset(font, new[] { (int)'A' });
        result.Should().NotBeNull();
        result!.ColorLayersByCodepoint.Should().NotBeNull();
        result.ColorLayersByCodepoint.Should().ContainKey('A');

        var layers = result.ColorLayersByCodepoint!['A'];
        layers.Should().HaveCount(2);

        layers[0].useTextColor.Should().BeFalse();
        layers[0].r.Should().BeApproximately(1f, 0.01f);
        layers[0].g.Should().BeApproximately(0f, 0.01f);
        layers[0].b.Should().BeApproximately(0f, 0.01f);
        result.OldToNewGlyphId.Should().ContainKey(layerGidRed, "the red layer's outline must be embedded");
        layers[0].newGlyphId.Should().Be(result.OldToNewGlyphId[layerGidRed]);

        layers[1].useTextColor.Should().BeTrue();
        result.OldToNewGlyphId.Should().ContainKey(layerGidText, "the text-color layer's outline must be embedded too");
        layers[1].newGlyphId.Should().Be(result.OldToNewGlyphId[layerGidText]);
    }

    [Fact]
    public void Subset_BaseGlyphWithoutColorLayers_NoColorLayersEntry()
    {
        var font = LoadTestFont();
        if (font == null) return;

        ushort baseGid = font.GetGlyphId('A');
        if (baseGid == 0) return;

        font.ColrLayers = new Dictionary<ushort, List<(ushort layerGlyphId, int paletteIndex)>>
        {
            [(ushort)(baseGid + 1)] = new List<(ushort, int)> { (baseGid, 0) } // unrelated glyph, not 'A'
        };

        var result = TtfSubsetter.Subset(font, new[] { (int)'A' });
        result.Should().NotBeNull();
        if (result!.ColorLayersByCodepoint != null)
            result.ColorLayersByCodepoint.Should().NotContainKey('A');
    }

    [Fact]
    public void Subset_FontWithNoColrTable_ColorLayersByCodepointIsNull()
    {
        var font = LoadTestFont();
        if (font == null) return;

        var result = TtfSubsetter.Subset(font, new[] { (int)'A' });
        result.Should().NotBeNull();
        result!.ColorLayersByCodepoint.Should().BeNull();
    }
}
