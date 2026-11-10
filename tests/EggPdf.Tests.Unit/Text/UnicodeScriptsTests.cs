using EggPdf.Text.Fallback;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class UnicodeScriptsTests
{
    [Theory]
    [InlineData(0x41, UnicodeScript.Latin, "A")]
    [InlineData(0xE9, UnicodeScript.Latin, "é")]
    [InlineData(0x1EA0, UnicodeScript.Latin, "Vietnamese Ạ")]
    [InlineData(0x0416, UnicodeScript.Cyrillic, "Ж")]
    [InlineData(0x03A9, UnicodeScript.Greek, "Ω")]
    [InlineData(0x0531, UnicodeScript.Armenian, "Armenian capital ayb")]
    [InlineData(0x05D0, UnicodeScript.Hebrew, "Hebrew alef")]
    [InlineData(0x0627, UnicodeScript.Arabic, "Arabic alef")]
    [InlineData(0xFE8D, UnicodeScript.Arabic, "Arabic alef isolated presentation form")]
    [InlineData(0x0915, UnicodeScript.Devanagari, "Devanagari ka")]
    [InlineData(0x0985, UnicodeScript.Bengali, "Bengali a")]
    [InlineData(0x0B95, UnicodeScript.Tamil, "Tamil ka")]
    [InlineData(0x0E01, UnicodeScript.Thai, "Thai ko kai")]
    [InlineData(0x10D0, UnicodeScript.Georgian, "Georgian an")]
    [InlineData(0xAC1C, UnicodeScript.Hangul, "개")]
    [InlineData(0x3053, UnicodeScript.Kana, "hiragana こ")]
    [InlineData(0x30AB, UnicodeScript.Kana, "katakana カ")]
    [InlineData(0x4F60, UnicodeScript.Han, "你")]
    [InlineData(0x20000, UnicodeScript.Han, "the first CJK Extension B ideograph")]
    [InlineData(0x3001, UnicodeScript.Han, "the ideographic comma 、")]
    [InlineData(0xFF0C, UnicodeScript.Han, "the fullwidth comma ，")]
    [InlineData(0x2192, UnicodeScript.Symbols, "→")]
    [InlineData(0x26A0, UnicodeScript.Symbols, "⚠")]
    [InlineData(0x2211, UnicodeScript.Math, "∑")]
    [InlineData(0x1F600, UnicodeScript.Emoji, "the grinning face emoji")]
    [InlineData(0x2800, UnicodeScript.Braille, "the blank Braille pattern")]
    [InlineData(0x20, UnicodeScript.Common, "space")]
    [InlineData(0x35, UnicodeScript.Common, "the digit 5")]
    [InlineData(0x2014, UnicodeScript.Common, "the em dash")]
    public void Of_ClassifiesRepresentativeCodepoints(int codepoint, UnicodeScript expected, string character)
    {
        UnicodeScripts.Of(codepoint).Should().Be(expected, character);
    }

    [Theory]
    [InlineData(0x10FFFD, "a plane-16 private use code point")]
    [InlineData(0xE000, "a BMP private use code point")]
    [InlineData(0x0870 - 1, "the gap between Thaana/NKo and Arabic Extended-B")]
    public void Of_CodepointOutsideKnownBlocks_IsOther(int codepoint, string where)
    {
        UnicodeScripts.Of(codepoint).Should().Be(UnicodeScript.Other, where);
    }

    [Theory]
    [InlineData(0x20, "space")]
    [InlineData(0x0A, "line feed")]
    [InlineData(0x00A0, "no-break space")]
    [InlineData(0x200B, "zero width space")]
    [InlineData(0x200D, "zero width joiner")]
    [InlineData(0xFE0F, "variation selector-16")]
    [InlineData(0xFEFF, "byte order mark")]
    public void IsInvisible_WhitespaceControlAndFormatCharacters(int codepoint, string character)
    {
        UnicodeScripts.IsInvisible(codepoint).Should().BeTrue(character);
    }

    [Theory]
    [InlineData(0x41)]
    [InlineData(0xAC1C)]
    [InlineData(0x2014)]
    public void IsInvisible_VisibleCharacters_False(int codepoint)
    {
        UnicodeScripts.IsInvisible(codepoint).Should().BeFalse();
    }

    [Fact]
    public void IsDefaultIgnorable_ExcludesSpaces()
    {
        UnicodeScripts.IsDefaultIgnorable(' ').Should().BeFalse("a space takes room in a line");
        UnicodeScripts.IsDefaultIgnorable(0x00A0).Should().BeFalse("a no-break space takes room in a line");
        UnicodeScripts.IsDefaultIgnorable(0x200D).Should().BeTrue("only zero-width format characters may be dropped by the painter");
        UnicodeScripts.IsDefaultIgnorable(0xFEFF).Should().BeTrue("only zero-width format characters may be dropped by the painter");
    }

    [Theory]
    [InlineData(UnicodeScript.CanadianAboriginal, "Canadian Aboriginal")]
    [InlineData(UnicodeScript.Hangul, "Hangul")]
    [InlineData(UnicodeScript.NKo, "NKo")]
    public void DisplayName_SplitsWordsForDiagnostics(UnicodeScript script, string expected)
    {
        UnicodeScripts.DisplayName(script).Should().Be(expected);
    }

    [Fact]
    public void Of_IsTotalOverAllUnicode()
    {
        for (int cp = 0; cp <= 0x10FFFF; cp += 7)
            UnicodeScripts.Of(cp);
        UnicodeScripts.Of(0).Should().Be(UnicodeScript.Common, "the lookup never throws, at either end of the range");
        UnicodeScripts.Of(0x10FFFF).Should().Be(UnicodeScript.Other, "the lookup never throws, at either end of the range");
    }
}
