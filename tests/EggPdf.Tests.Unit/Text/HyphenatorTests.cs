using System;
using EggPdf.Text.Hyphenation;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class HyphenatorTests
{
    [Fact]
    public void Hyphenate_ExceptionWord_UsesItsListedBreaks()
    {
        Hyphenator.CreateEnglish().Hyphenate("associate").Should().Equal(2, 4, 6);
    }

    [Fact]
    public void Hyphenate_Pattern_MarksOddLevelsAsBreaks()
    {
        var hyphenator = new Hyphenator();
        hyphenator.AddPattern("1na");

        hyphenator.Hyphenate("banana").Should().Equal(2);
    }

    [Fact]
    public void Hyphenate_LongWord_LooksUpOnlySubstringsAsLongAsAPattern()
    {
        var hyphenator = Hyphenator.CreateEnglish();
        var word = string.Concat(System.Linq.Enumerable.Repeat("internationalization", 25));
        hyphenator.Hyphenate("warmup");

        long before = GC.GetAllocatedBytesForCurrentThread();
        var breaks = hyphenator.Hyphenate(word);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        breaks.Should().NotBeEmpty();
        breaks.Should().OnlyContain(b => b >= hyphenator.LeftMin && b <= word.Length - hyphenator.RightMin);
        allocated.Should().BeLessThan(1024 * 1024, "a 500-letter word must not allocate all of its ~125,000 substrings");
    }
}
