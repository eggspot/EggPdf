using EggPdf.Text.Fallback;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class UnicodeScriptsTests
{
    [Theory]
    [InlineData(0x41, UnicodeScript.Latin)]
    [InlineData(0xE9, UnicodeScript.Latin)]
    [InlineData(0x1EA0, UnicodeScript.Latin)]     // Vietnamese Ạ
    [InlineData(0x0416, UnicodeScript.Cyrillic)]  // Ж
    [InlineData(0x03A9, UnicodeScript.Greek)]     // Ω
    [InlineData(0x0531, UnicodeScript.Armenian)]
    [InlineData(0x05D0, UnicodeScript.Hebrew)]
    [InlineData(0x0627, UnicodeScript.Arabic)]
    [InlineData(0xFE8D, UnicodeScript.Arabic)]    // presentation form
    [InlineData(0x0915, UnicodeScript.Devanagari)]
    [InlineData(0x0985, UnicodeScript.Bengali)]
    [InlineData(0x0B95, UnicodeScript.Tamil)]
    [InlineData(0x0E01, UnicodeScript.Thai)]
    [InlineData(0x10D0, UnicodeScript.Georgian)]
    [InlineData(0xAC1C, UnicodeScript.Hangul)]    // 개
    [InlineData(0x3053, UnicodeScript.Kana)]      // こ
    [InlineData(0x30AB, UnicodeScript.Kana)]      // カ
    [InlineData(0x4F60, UnicodeScript.Han)]       // 你
    [InlineData(0x20000, UnicodeScript.Han)]      // CJK Extension B
    [InlineData(0x3001, UnicodeScript.Han)]       // 、
    [InlineData(0xFF0C, UnicodeScript.Han)]       // ，fullwidth comma
    [InlineData(0x2192, UnicodeScript.Symbols)]   // →
    [InlineData(0x26A0, UnicodeScript.Symbols)]   // ⚠
    [InlineData(0x2211, UnicodeScript.Math)]      // ∑
    [InlineData(0x1F600, UnicodeScript.Emoji)]    // 😀
    [InlineData(0x2800, UnicodeScript.Braille)]
    [InlineData(0x20, UnicodeScript.Common)]
    [InlineData(0x35, UnicodeScript.Common)]
    [InlineData(0x2014, UnicodeScript.Common)]    // em dash
    public void Of_ClassifiesRepresentativeCodepoints(int codepoint, UnicodeScript expected)
    {
        UnicodeScripts.Of(codepoint).Should().Be(expected);
    }

    [Theory]
    [InlineData(0x10FFFD)]   // Private Use Plane 16
    [InlineData(0xE000)]     // BMP Private Use
    [InlineData(0x0870 - 1)] // gap between Thaana/NKo and Arabic Extended-B
    public void Of_CodepointOutsideKnownBlocks_IsOther(int codepoint)
    {
        UnicodeScripts.Of(codepoint).Should().Be(UnicodeScript.Other);
    }

    [Theory]
    [InlineData(0x20)]
    [InlineData(0x0A)]
    [InlineData(0x00A0)]   // no-break space
    [InlineData(0x200B)]   // zero width space
    [InlineData(0x200D)]   // zero width joiner
    [InlineData(0xFE0F)]   // variation selector-16
    [InlineData(0xFEFF)]   // BOM
    public void IsInvisible_WhitespaceControlAndFormatCharacters(int codepoint)
    {
        UnicodeScripts.IsInvisible(codepoint).Should().BeTrue();
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
        // Spaces take room in a line; only zero-width format characters may be dropped by the painter
        UnicodeScripts.IsDefaultIgnorable(' ').Should().BeFalse();
        UnicodeScripts.IsDefaultIgnorable(0x00A0).Should().BeFalse();
        UnicodeScripts.IsDefaultIgnorable(0x200D).Should().BeTrue();
        UnicodeScripts.IsDefaultIgnorable(0xFEFF).Should().BeTrue();
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
        // The table lookup must never throw for any scalar value, including both ends of the range
        for (int cp = 0; cp <= 0x10FFFF; cp += 7)
            UnicodeScripts.Of(cp);
        UnicodeScripts.Of(0).Should().Be(UnicodeScript.Common);
        UnicodeScripts.Of(0x10FFFF).Should().Be(UnicodeScript.Other);
    }
}
