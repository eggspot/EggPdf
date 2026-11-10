using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class TrueTypeParserTests
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
    public void ParseSystemFont_ReadsHeadTable()
    {
        var font = LoadTestFont();
        if (font == null) return;

        font.UnitsPerEm.Should().BeGreaterThan(0);
        font.Ascent.Should().BeGreaterThan(0);
        font.Descent.Should().BeLessThan(0);
    }

    [Fact]
    public void ParseSystemFont_HasGlyphCount()
    {
        var font = LoadTestFont();
        if (font == null) return;

        font.NumGlyphs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ParseSystemFont_MapsAsciiGlyphs()
    {
        var font = LoadTestFont();
        if (font == null) return;

        var aGlyph = font.GetGlyphId('A');
        var spaceGlyph = font.GetGlyphId(' ');
        aGlyph.Should().BeGreaterThan((ushort)0, "Arial, DejaVu Sans and Liberation all map 'A' to a real glyph, not .notdef");
        spaceGlyph.Should().BeGreaterThan((ushort)0);
        spaceGlyph.Should().NotBe(aGlyph);
    }

    [Fact]
    public void ParseSystemFont_GlyphWidths()
    {
        var font = LoadTestFont();
        if (font == null) return;

        var aGlyph = font.GetGlyphId('A');
        var aWidth = font.GetAdvanceWidth(aGlyph);
        aWidth.Should().BeGreaterThan(0);

        var iGlyph = font.GetGlyphId('i');
        var mGlyph = font.GetGlyphId('M');
        iGlyph.Should().BeGreaterThan((ushort)0);
        mGlyph.Should().BeGreaterThan((ushort)0);
        font.GetAdvanceWidth(iGlyph).Should().BeLessThan(font.GetAdvanceWidth(mGlyph), "the test fonts are proportional");
    }

    [Fact]
    public void ParseSystemFont_FontName()
    {
        var font = LoadTestFont();
        if (font == null) return;

        font.FamilyName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ParseSystemFont_MeasureText()
    {
        var font = LoadTestFont();
        if (font == null) return;

        float width = font.MeasureTextWidthPx("Hello", 16);
        width.Should().BeGreaterThan(0);

        float widerWidth = font.MeasureTextWidthPx("Hello World", 16);
        widerWidth.Should().BeGreaterThan(width);
    }

    [Fact]
    public void Parse_EmptyData_ReturnsNull()
    {
        TtfParser.Parse(new byte[0]).Should().BeNull();
    }

    [Fact]
    public void Parse_InvalidData_ReturnsNull()
    {
        TtfParser.Parse(new byte[] { 0, 1, 2, 3 }).Should().BeNull();
    }

    [Fact]
    public void Parse_NullData_ReturnsNull()
    {
        TtfParser.Parse(null!).Should().BeNull();
    }
}
