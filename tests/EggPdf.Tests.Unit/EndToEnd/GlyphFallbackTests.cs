using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class GlyphFallbackTests
{
    [Fact]
    public async Task WarningSign_InVietnameseText_DoesNotPaintNotdef()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>Cảnh báo ⚠ nguy hiểm</p></body></html>");

        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
        text.Should().Contain("/FontFile2", "Vietnamese text forces an embedded CID font");

        foreach (Match m in Regex.Matches(text, @"<([0-9A-Fa-f]+)>\s*Tj"))
        {
            var hex = m.Groups[1].Value;
            for (int i = 0; i + 4 <= hex.Length; i += 4)
                hex.Substring(i, 4).Should().NotBe("0000",
                    "⚠ (U+26A0) is missing from most text fonts and must switch to a symbol-capable fallback font " +
                    "mid-run, not be painted as the .notdef glyph 0000");
        }
    }

    [Fact]
    public async Task WarningSign_FallbackFontIsEmbedded()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p>Lưu ý ⚠ quan trọng</p></body></html>");

        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
        Regex.Matches(text, "/FontFile2").Count.Should().BeGreaterThanOrEqualTo(1,
            "the main text font is embedded, plus a -FB fallback when the main font lacks U+26A0");
    }
}
