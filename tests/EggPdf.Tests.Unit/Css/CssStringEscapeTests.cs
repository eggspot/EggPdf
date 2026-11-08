using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EggPdf.Css.Tokenizer;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Css;

public class CssStringEscapeTests
{
    private static string StringToken(string css)
    {
        var tokenizer = new CssTokenizer(css);
        CssToken token;
        while ((token = tokenizer.NextToken()).Type != CssTokenType.EOF)
        {
            if (token.Type == CssTokenType.String) return token.Value!;
        }
        return "";
    }

    [Theory]
    [InlineData("\"\\2022\"", 0x2022)]
    [InlineData("\"\\201C\"", 0x201C)]
    [InlineData("'\\00A9'", 0x00A9)]
    [InlineData("\"\\1F600\"", 0x1F600)]
    public void HexEscape_DecodesToCodepoint(string css, int codepoint)
    {
        StringToken(css).Should().Be(char.ConvertFromUtf32(codepoint));
    }

    [Theory]
    [InlineData("\"\\2022 Item\"", "Item")]
    [InlineData("\"\\2022  Item\"", " Item")]
    public void HexEscape_ConsumesOneFollowingSpace(string css, string expectedSuffix)
    {
        StringToken(css).Should().Be(((char)0x2022) + expectedSuffix);
    }

    [Theory]
    [InlineData("\"a\\\"b\"", "a\"b")]
    [InlineData("'it\\'s'", "it's")]
    [InlineData("\"back\\\\slash\"", "back\\slash")]
    [InlineData("\"line\\\ncontinued\"", "linecontinued")]
    [InlineData("\"\\0\"", "\uFFFD")]
    [InlineData("\"\\D800\"", "\uFFFD")]
    [InlineData("\"\\110000\"", "\uFFFD")]
    public void OtherEscapes_DecodeAsCssSyntax(string css, string expected)
    {
        StringToken(css).Should().Be(expected);
    }

    private static async Task<string> RenderAscii(string css, string body) =>
        Encoding.GetEncoding("ISO-8859-1").GetString(await HtmlToPdf.RenderAsync(
            "<html><head><style>" + css + "</style></head><body>" + body + "</body></html>"));

    [Fact]
    public async Task ContentWithApostrophe_IsNotCutShort()
    {
        var content = await RenderAscii(".x::after{content:\" it's done\"}", "<p class='x'>Task</p>");

        content.Should().Contain("it's").And.Contain("done");
    }

    [Fact]
    public async Task ContentWithHexEscape_RendersTheCharacterNotTheDigits()
    {
        var content = await RenderAscii(".x::before{content:\"\\A9 \"}", "<p class='x'>2026 Acme</p>");

        content.Should().NotContain("A9");
        content.Should().Contain(((char)0xA9).ToString());
    }
}
