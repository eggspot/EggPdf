using System.Linq;
using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class ThaiLineBreakerTests
{
    private const string Sentence = "ภาษาไทยเป็นภาษาที่ไม่มีช่องว่างระหว่างคำและประโยค";

    [Fact]
    public void Split_ProducesSegmentsThatRejoinToTheOriginal()
    {
        string.Concat(ThaiLineBreaker.Split(Sentence)).Should().Be(Sentence);
    }

    [Fact]
    public void Split_OffersBreakOpportunitiesInALongWord()
    {
        ThaiLineBreaker.Split(Sentence).Count.Should().BeGreaterThan(3);
    }

    [Fact]
    public void Split_NeverStartsASegmentWithADependentMark()
    {
        foreach (var seg in ThaiLineBreaker.Split(Sentence))
        {
            char first = seg[0];
            bool dependent = first == '\u0E31' || (first >= '\u0E34' && first <= '\u0E3A') || (first >= '\u0E47' && first <= '\u0E4E')
                             || first == '\u0E30' || first == '\u0E32' || first == '\u0E33';
            dependent.Should().BeFalse($"segment '{seg}' must not begin with a vowel/tone mark");
        }
    }

    [Fact]
    public void Split_NeverEndsASegmentWithALeadingVowel()
    {
        foreach (var seg in ThaiLineBreaker.Split(Sentence))
            (seg[seg.Length - 1] >= '\u0E40' && seg[seg.Length - 1] <= '\u0E44').Should().BeFalse();
    }

    [Fact]
    public void Split_KeepsConsonantWithItsFinalConsonant()
    {
        ThaiLineBreaker.Split("\u0E01\u0E34\u0E19").Should().HaveCount(1, "kin: the final consonant is a coda, not a new syllable");
    }

    [Fact]
    public void Split_LatinWord_IsNotSplit()
    {
        ThaiLineBreaker.Split("hello").Should().Equal("hello");
    }

    [Fact]
    public void ContainsThai_Detects()
    {
        ThaiLineBreaker.ContainsThai("abc").Should().BeFalse();
        ThaiLineBreaker.ContainsThai("a\u0E01").Should().BeTrue();
    }
}
