using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class ColorEmojiFontTests
{
    private const int GrinningFace = 0x1F600;

    private static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    /// <summary>
    /// True when the font draws the grinning face with at least two COLR vector layers; bitmap color
    /// formats (CBDT/CBLC) and single-layer glyphs leave nothing to verify the multi-layer paint path against.
    /// </summary>
    private static bool DrawsGrinningFaceInColrLayers(string fontPath)
    {
        var font = TtfParser.Parse(File.ReadAllBytes(fontPath));
        return font?.ColrLayers != null
            && font.ColrLayers.TryGetValue(font.GetGlyphId(GrinningFace), out var layers)
            && layers.Count >= 2;
    }

    private static string? FindColorEmojiFont()
    {
        string[] names = { "seguiemj", "NotoColorEmoji", "AppleColorEmoji" };
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
                var files = Directory.GetFiles(dir, "*.tt*", SearchOption.AllDirectories);
                foreach (var name in names)
                {
                    var match = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                        .IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) return match;
                }
            }
            catch (System.UnauthorizedAccessException)
            {
            }
        }
        return null;
    }

    [Fact]
    public async Task EmojiText_AlwaysRendersATextRun()
    {
        var html = "<p>Grinning face: \U0001F600</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf);

        System.Text.RegularExpressions.Regex.IsMatch(text, @"BT [^\n]*(\) Tj|> Tj|\] TJ)[^\n]* ET").Should().BeTrue(
            "whichever emoji font is installed, if any, the paragraph is painted as text and rendering never throws");
        PdfAssert.PageCount(text).Should().Be(1);
    }

    [Fact]
    public async Task RealColorEmojiFont_GrinningFace_PaintsMultipleColorLayers()
    {
        var path = FindColorEmojiFont();
        if (path == null || !DrawsGrinningFaceInColrLayers(path)) return;

        var fontFamilyName = System.IO.Path.GetFileNameWithoutExtension(path);
        var html = $"<p style=\"font-family: '{fontFamilyName}', sans-serif\">\U0001F600</p>";
        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = Latin1(pdf);

        int tjCount = 0, idx = 0;
        while ((idx = text.IndexOf("Tj", idx, System.StringComparison.Ordinal)) >= 0) { tjCount++; idx += 2; }

        tjCount.Should().BeGreaterOrEqualTo(2,
            "a multi-layer color glyph must paint each layer as its own glyph-showing operation");
    }
}
