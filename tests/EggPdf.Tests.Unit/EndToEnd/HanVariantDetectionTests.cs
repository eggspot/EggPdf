using EggPdf.Html;
using EggPdf.Text.Fallback;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Han ideographs look different per region, and unmarked text gives no hint, so the preferred regional
/// font follows the document's <c>lang</c> and otherwise the other scripts present in it.
/// </summary>
public class HanVariantDetectionTests
{
    private static HanVariant Detect(string html) => HtmlToPdf.DetectHanVariant(HtmlParser.Parse(html));

    [Theory]
    [InlineData("ja", HanVariant.Japanese)]
    [InlineData("ja-JP", HanVariant.Japanese)]
    [InlineData("ko", HanVariant.Korean)]
    [InlineData("ko-KR", HanVariant.Korean)]
    [InlineData("zh", HanVariant.SimplifiedChinese)]
    [InlineData("zh-CN", HanVariant.SimplifiedChinese)]
    [InlineData("zh-Hans", HanVariant.SimplifiedChinese)]
    [InlineData("zh-TW", HanVariant.TraditionalChinese)]
    [InlineData("zh-HK", HanVariant.TraditionalChinese)]
    [InlineData("zh-Hant-TW", HanVariant.TraditionalChinese)]
    [InlineData("zh_tw", HanVariant.TraditionalChinese)]
    [InlineData("JA", HanVariant.Japanese)]
    [InlineData("en", HanVariant.Auto)]
    [InlineData("", HanVariant.Auto)]
    [InlineData(null, HanVariant.Auto)]
    public void HanVariantFromLang_MapsLanguageTags(string? lang, HanVariant expected)
    {
        HtmlToPdf.HanVariantFromLang(lang).Should().Be(expected);
    }

    [Theory]
    [InlineData("<html lang='ja'><body>漢字</body></html>", HanVariant.Japanese)]
    [InlineData("<html lang='zh-TW'><body>漢字</body></html>", HanVariant.TraditionalChinese)]
    [InlineData("<html lang='ko'><body>漢字</body></html>", HanVariant.Korean)]
    public void RootLang_DecidesTheVariant(string html, HanVariant expected)
    {
        Detect(html).Should().Be(expected);
    }

    [Fact]
    public void RootLang_WinsOverTheScriptsPresentInTheText()
    {
        Detect("<html lang='zh-TW'><body>こんにちは 漢字</body></html>").Should().Be(HanVariant.TraditionalChinese);
    }

    [Fact]
    public void NoLang_KanaInTheText_ImpliesJapanese()
    {
        Detect("<html><body><p>Invoice</p><p>日本語のテキスト 漢字</p></body></html>").Should().Be(HanVariant.Japanese);
    }

    [Fact]
    public void NoLang_HangulInTheText_ImpliesKorean()
    {
        Detect("<html><body><p>안녕하세요 漢字</p></body></html>").Should().Be(HanVariant.Korean);
    }

    [Fact]
    public void NoLang_KanaBeatsHangul_BecauseKoreanTextSeldomContainsKana()
    {
        Detect("<html><body>안녕 こんにちは</body></html>").Should().Be(HanVariant.Japanese);
    }

    [Fact]
    public void NoLangAndOnlyIdeographsOrLatin_StaysAuto()
    {
        Detect("<html><body>漢字 and Latin text</body></html>").Should().Be(HanVariant.Auto);
        Detect("<html><body>Plain Latin</body></html>").Should().Be(HanVariant.Auto);
    }

    [Fact]
    public void KanaDeepInTheTree_IsFound()
    {
        Detect("<html><body><div><section><ul><li><span><b>ひらがな</b></span></li></ul></section></div></body></html>")
            .Should().Be(HanVariant.Japanese);
    }
}
