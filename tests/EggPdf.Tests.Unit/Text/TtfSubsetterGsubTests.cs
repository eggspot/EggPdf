using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// TtfSubsetter.Subset(font, codepoints, activeFeatures) must embed and map to the
/// GSUB-substituted glyph, not the original -- otherwise the composite embedded-font
/// cache key (see StandardFontMetrics.ResolvePdfFontName's "-Feat-..." suffix) would
/// point at a font-feature-settings entry whose glyph outlines never actually changed.
/// A real system font supplies valid glyph outlines/composites to subset; the GSUB
/// mapping itself is injected directly (internal access) so the test doesn't depend on
/// which, if any, GSUB features happen to be present on whatever font is installed.
/// </summary>
public class TtfSubsetterGsubTests
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
            catch { /* permission denied etc */ }
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
    public void Subset_WithActiveFeature_MapsCodepointToSubstitutedGlyph()
    {
        var font = LoadTestFont();
        if (font == null) return; // skip if no font available in this environment

        ushort gidA = font.GetGlyphId('A');
        ushort gidB = font.GetGlyphId('B');
        if (gidA == 0 || gidB == 0 || gidA == gidB) return; // guard: need two distinct real glyphs

        font.GsubFeatures = new Dictionary<string, Dictionary<ushort, ushort>>
        {
            ["test"] = new Dictionary<ushort, ushort> { [gidA] = gidB }
        };

        var result = TtfSubsetter.Subset(font, new[] { (int)'A' }, new List<string> { "test" });
        result.Should().NotBeNull();
        result!.CodepointToNewGlyphId.Should().ContainKey('A');

        ushort newGidForA = result.CodepointToNewGlyphId['A'];
        result.OldToNewGlyphId.Should().ContainKey(gidB, "the substituted glyph B's outline must be embedded");
        result.OldToNewGlyphId[gidB].Should().Be(newGidForA,
            "codepoint 'A' must resolve to glyph B's new ID, not glyph A's, once the 'test' feature is active");
    }

    [Fact]
    public void Subset_WithoutActiveFeature_MapsCodepointToOriginalGlyph()
    {
        var font = LoadTestFont();
        if (font == null) return;

        ushort gidA = font.GetGlyphId('A');
        ushort gidB = font.GetGlyphId('B');
        if (gidA == 0 || gidB == 0 || gidA == gidB) return;

        font.GsubFeatures = new Dictionary<string, Dictionary<ushort, ushort>>
        {
            ["test"] = new Dictionary<ushort, ushort> { [gidA] = gidB }
        };

        var result = TtfSubsetter.Subset(font, new[] { (int)'A' }, activeFeatures: null);
        result.Should().NotBeNull();

        ushort newGidForA = result!.CodepointToNewGlyphId['A'];
        result.OldToNewGlyphId[gidA].Should().Be(newGidForA,
            "with no active feature, subsetting must use the original glyph, not the GSUB substitute");
    }
}
