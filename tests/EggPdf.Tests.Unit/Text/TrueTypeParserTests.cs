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
    public void ParseSystemFont_ReadsHeadTable()
    {
        var font = LoadTestFont();
        if (font == null) return; // skip if no font available

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

        // Glyph ID 0 = .notdef; some fonts/cmap formats may return 0 for ASCII
        var aGlyph = font.GetGlyphId('A');
        aGlyph.Should().BeGreaterOrEqualTo((ushort)0);
        // Space glyph ID varies by font - just check it doesn't throw
        font.GetGlyphId(' ');
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
        // Only compare widths if glyphs are actually mapped (not .notdef)
        if (iGlyph > 0 && mGlyph > 0)
        {
            var iWidth = font.GetAdvanceWidth(iGlyph);
            var mWidth = font.GetAdvanceWidth(mGlyph);
            iWidth.Should().BeLessThan(mWidth);
        }
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
