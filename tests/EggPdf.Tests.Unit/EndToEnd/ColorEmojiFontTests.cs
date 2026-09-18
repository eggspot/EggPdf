using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// COLR/CPAL color-glyph (emoji) rendering, end-to-end through HtmlToPdf.RenderAsync.
/// A real installed color-emoji font (Segoe UI Emoji on Windows, Noto Color Emoji on
/// Linux) is needed to exercise the actual multi-layer paint path; when none is present
/// or the installed one uses a different color format (e.g. CBDT/CBLC bitmap glyphs
/// instead of COLR vector layers), the environment-dependent assertions skip gracefully
/// -- the crash-safety test does not skip, since font-feature-agnostic rendering must
/// never throw regardless of what's installed.
/// </summary>
public class ColorEmojiFontTests
{
    private static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

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
            catch { }
        }
        return null;
    }

    [Fact]
    public async Task EmojiText_AlwaysRendersWithoutCrashing()
    {
        var html = "<p>Grinning face: \U0001F600</p>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RealColorEmojiFont_GrinningFace_PaintsMultipleColorLayers()
    {
        var path = FindColorEmojiFont();
        if (path == null) return; // skip: no color-emoji font installed in this environment

        var font = TtfParser.Parse(File.ReadAllBytes(path));
        if (font?.ColrLayers == null || font.ColrLayers.Count == 0) return; // skip: not a COLR-format font
        if (!font.ColrLayers.TryGetValue(font.GetGlyphId(0x1F600), out var layers) || layers.Count < 2)
            return; // skip: this font's grinning-face glyph has <2 layers to verify against

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
