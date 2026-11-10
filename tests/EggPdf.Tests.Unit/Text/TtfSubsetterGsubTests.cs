using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

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
            catch { }
        }
        return null;
    }

    /// <summary>
    /// A real system font, for valid glyph outlines and composites; the tests inject the GSUB mapping
    /// themselves, so they don't depend on which features the installed font has.
    /// </summary>
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
        if (font == null) return;

        ushort gidA = font.GetGlyphId('A');
        ushort gidB = font.GetGlyphId('B');
        if (gidA == 0 || gidB == 0 || gidA == gidB) return;

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
            "codepoint 'A' must resolve to glyph B's new ID once the 'test' feature is active; otherwise the " +
            "font-feature-settings font's cache key (the \"-Feat-...\" suffix) names a subset whose outlines never changed");
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
