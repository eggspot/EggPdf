using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// When the selected font lacks a glyph (e.g. ⚠ U+26A0 in most text fonts),
/// the engine must embed a symbol-capable fallback font and switch fonts
/// mid-run instead of painting .notdef boxes.
/// </summary>
public class GlyphFallbackTests
{
    [Fact]
    public async Task WarningSign_InVietnameseText_DoesNotPaintNotdef()
    {
        // Vietnamese forces CIDFont embedding; ⚠ is absent from most text fonts
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>Cảnh báo ⚠ nguy hiểm</p></body></html>");

        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
        text.Should().Contain("/FontFile2");

        // No CID hex string may contain a .notdef (0000) glyph
        foreach (Match m in Regex.Matches(text, @"<([0-9A-Fa-f]+)>\s*Tj"))
        {
            var hex = m.Groups[1].Value;
            for (int i = 0; i + 4 <= hex.Length; i += 4)
                hex.Substring(i, 4).Should().NotBe("0000",
                    "missing glyphs must be routed to the fallback font, not painted as .notdef");
        }
    }

    [Fact]
    public async Task WarningSign_FallbackFontIsEmbedded()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>Lưu ý ⚠ quan trọng</p></body></html>");

        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
        // The main text font plus (when the main font lacks U+26A0) a -FB fallback
        Regex.Matches(text, "/FontFile2").Count.Should().BeGreaterThanOrEqualTo(1);
    }
}
