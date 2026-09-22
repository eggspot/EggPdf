using System.Linq;
using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

/// <summary>Syllable-boundary break opportunities for Lao, Khmer and Myanmar (no spaces between words).</summary>
public class SpacelessLineBreakerTests
{
    private const string Khmer = "ខ្ញុំស្រឡាញ់ប្រទេសកម្ពុជាហើយខ្ញុំចូលចិត្តភាសាខ្មែរ";
    private const string Myanmar = "မြန်မာဘာသာစကားကိုလေ့လာပါသည်";
    private const string Lao = "ຂ້ອຍຮັກປະເທດລາວແລະພາສາລາວ";

    [Theory]
    [InlineData("ก")]
    [InlineData("abc")]
    [InlineData("")]
    public void Contains_ThaiAndLatin(string text)
    {
        SpacelessLineBreaker.Contains(text).Should().Be(text == "ก");
    }

    [Theory]
    [InlineData(Khmer)]
    [InlineData(Myanmar)]
    [InlineData(Lao)]
    public void Contains_DetectsLaoKhmerMyanmar(string text)
    {
        SpacelessLineBreaker.Contains(text).Should().BeTrue();
    }

    [Theory]
    [InlineData(Khmer)]
    [InlineData(Myanmar)]
    [InlineData(Lao)]
    public void Split_ProducesSeveralSegmentsThatRejoinToTheOriginal(string word)
    {
        var parts = SpacelessLineBreaker.Split(word);

        parts.Count.Should().BeGreaterThan(2, "a sentence-length run must offer break opportunities");
        string.Concat(parts).Should().Be(word, "segments are joined without any space");
    }

    [Theory]
    [InlineData(Khmer)]
    [InlineData(Myanmar)]
    [InlineData(Lao)]
    public void Split_NeverStartsASegmentWithACombiningMark(string word)
    {
        foreach (var part in SpacelessLineBreaker.Split(word))
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(part[0]);
            cat.Should().NotBe(System.Globalization.UnicodeCategory.NonSpacingMark,
                "a break before '" + part + "' would strand a combining mark on the new line");
            cat.Should().NotBe(System.Globalization.UnicodeCategory.SpacingCombiningMark);
        }
    }

    [Fact]
    public void Khmer_NeverSplitsACoengCluster()
    {
        // ្ (coeng) glues the next consonant under the previous one: neither side may be split off
        foreach (var part in SpacelessLineBreaker.Split(Khmer))
        {
            part.Should().NotStartWith("្");
            part.Should().NotEndWith("្", "a trailing coeng would separate it from its subscript consonant");
        }
    }

    [Fact]
    public void Khmer_KeepsCodaConsonantWithItsSyllable()
    {
        // "ក្នុង" ends in the coda consonant ង -- it must not be split off as if it started a new syllable
        SpacelessLineBreaker.Split("ក្នុង").Should().HaveCount(1);
    }

    [Fact]
    public void Myanmar_KeepsAsatFinalConsonantWithItsSyllable()
    {
        // "ကျွန်" = consonant + medials + န + asat: one syllable
        SpacelessLineBreaker.Split("ကျွန်").Should().HaveCount(1);
        SpacelessLineBreaker.Split("ကျွန်တော်").Count.Should().BeGreaterThan(1, "the second syllable begins at တ");
    }

    [Fact]
    public void Lao_SplitsAtLeadingVowels_LikeThai()
    {
        var parts = SpacelessLineBreaker.Split("ພາສາລາວແລະເປັນ");
        parts.Should().Contain(p => p[0] == 'ແ' || p[0] == 'ເ', "a leading vowel begins a new syllable");
        string.Concat(parts).Should().Be("ພາສາລາວແລະເປັນ");
    }

    [Fact]
    public void ThaiBehaviourIsUnchanged()
    {
        var word = "ภาษาไทยเป็นภาษาที่สวยงาม";
        SpacelessLineBreaker.Split(word).Should().Equal(ThaiLineBreaker.Split(word));
    }

    [Fact]
    public void WordWithoutOpportunities_IsReturnedWhole()
    {
        SpacelessLineBreaker.Split("ក").Should().Equal("ក");
        SpacelessLineBreaker.Split("hello").Should().Equal("hello");
    }

    [Fact]
    public void LatinDigitsAdjacentToScriptText_AreBreakOpportunities()
    {
        var parts = SpacelessLineBreaker.Split("ကိုABC");
        parts.Should().Equal("ကို", "ABC");
        parts.Any(p => p.Length == 0).Should().BeFalse();
    }
}
