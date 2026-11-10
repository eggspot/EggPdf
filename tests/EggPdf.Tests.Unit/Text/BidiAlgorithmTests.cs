using System.Linq;
using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class BidiAlgorithmTests
{
    /// <summary>Hebrew alef, bet and gimel: strong right-to-left (R) letters.</summary>
    private const string A = "א", B = "ב", G = "ג";

    /// <summary>Arabic alef and beh: Arabic letters (AL).</summary>
    private const string AR1 = "ا", AR2 = "ب";

    /// <summary>Directional formatting characters, spelled numerically so they never appear literally in source.</summary>
    private static readonly string LRE = ((char)0x202A).ToString(), RLE = ((char)0x202B).ToString(),
        PDF = ((char)0x202C).ToString(), LRO = ((char)0x202D).ToString(), RLO = ((char)0x202E).ToString(),
        LRI = ((char)0x2066).ToString(), RLI = ((char)0x2067).ToString(), FSI = ((char)0x2068).ToString(),
        PDI = ((char)0x2069).ToString();

    private static string Visual(string logical, bool rtl = false) => BidiAlgorithm.Reorder(logical, rtl).visual;
    private static int[] Levels(string logical, bool rtl = false) => BidiAlgorithm.ResolveLevels(logical, rtl);

    [Fact]
    public void PureLtr_IsUnchanged()
    {
        Visual("hello world").Should().Be("hello world");
        Levels("hello").Should().OnlyContain(l => l == 0);
    }

    [Fact]
    public void PureRtl_IsReversed()
    {
        Visual(A + B + G).Should().Be(G + B + A);
        Levels(A + B + G).Should().OnlyContain(l => l == 1);
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        BidiAlgorithm.Reorder("").visual.Should().BeEmpty();
    }

    [Fact]
    public void ParagraphLevelFollowsBaseDirection_NotFirstStrongCharacter()
    {
        Levels("ab " + A, rtl: true)[0].Should().Be(2,
            "CSS direction sets paragraph level 1 even when the text starts with Latin, so that L text sits at level 2");
        Levels(A + " ab", rtl: false)[0].Should().Be(1);
    }

    [Fact]
    public void EuropeanNumber_AfterHebrew_StaysLeftToRightInsideTheRtlRun()
    {
        Visual(A + " 12 " + B, rtl: true).Should().Be(B + " 12 " + A,
            "read left to right, the RTL paragraph shows bet, then 12, then alef");
    }

    [Fact]
    public void W2_EuropeanNumberAfterArabicLetter_BecomesArabicNumber()
    {
        var levels = Levels(AR1 + "1");
        levels[1].Should().Be(2, "EN after AL becomes AN, and an AN at level 1 goes to level 2");
    }

    [Fact]
    public void W4_SingleSeparatorBetweenNumbers_JoinsThem()
    {
        Visual(A + " 1,000 " + B, rtl: true).Should().Be(B + " 1,000 " + A, "it stays one left-to-right number inside RTL text");
        Visual(A + " 12:30 " + B, rtl: true).Should().Be(B + " 12:30 " + A);
    }

    [Fact]
    public void W5_TerminatorAdjacentToNumber_BecomesPartOfTheNumber()
    {
        Visual(A + " 50% " + B, rtl: true).Should().Be(B + " 50% " + A, "the sign stays next to the digits");
        Visual(A + " $5 " + B, rtl: true).Should().Be(B + " $5 " + A);
    }

    [Fact]
    public void W7_EuropeanNumberAfterLatin_IsLeftToRight()
    {
        var levels = Levels("a1");
        levels.Should().OnlyContain(l => l == 0);
    }

    [Fact]
    public void ArabicIndicDigits_AreArabicNumbers_OrderedLeftToRightWithinRtlText()
    {
        Visual(AR1 + " ١٢٣ " + AR2, rtl: true).Should().Be(AR2 + " ١٢٣ " + AR1);
    }

    [Fact]
    public void N1_NeutralsBetweenSameDirection_TakeThatDirection()
    {
        var levels = Levels(A + " " + B);
        levels.Should().Equal(new[] { 1, 1, 1 }, "in an LTR paragraph the space between two R letters takes level 1");
        Visual(A + " " + B).Should().Be(B + " " + A);
    }

    [Fact]
    public void N2_NeutralsBetweenDifferentDirections_TakeEmbeddingDirection()
    {
        Levels("a " + A).Should().Equal(new[] { 0, 0, 1 },
            "in an LTR paragraph the space between L and R takes the embedding direction, L (level 0)");
        Levels("a " + A, rtl: true).Should().Equal(new[] { 2, 1, 1 },
            "in an RTL paragraph the same space takes the embedding direction, R (level 1)");
    }

    [Fact]
    public void TrailingWhitespace_ResetsToParagraphLevel_L1()
    {
        var levels = Levels(A + B + "  ");
        levels[2].Should().Be(0);
        levels[3].Should().Be(0);
        Visual(A + B + "  ").Should().Be(B + A + "  ");
    }

    [Fact]
    public void InteriorRtlWords_AreOneRun_SpaceBetweenThemIsRtl()
    {
        Visual(A + " " + B + " " + G).Should().Be(G + " " + B + " " + A, "the RTL words and the spaces between them reverse as one run");
    }

    [Fact]
    public void N0_BracketsAroundRtlText_InLtrParagraph_KeepTheEmbeddingDirection()
    {
        var levels = Levels("(" + A + B + ")");
        levels.Should().Equal(new[] { 0, 1, 1, 0 },
            "the brackets enclose only R text, opposite to the L embedding, but the context before them is sos = L, so they stay L");
    }

    [Fact]
    public void N0_BracketsAroundRtlText_AfterRtlContext_TakeRtlDirection()
    {
        var levels = Levels(A + " (" + B + ")");
        levels.Should().OnlyContain(l => l == 1, "the context before the '(' is R, so the bracket pair joins the R run");
        Visual(A + " (" + B + ")").Should().Be("(" + B + ") " + A, "brackets are mirrored at odd levels and reversed with the run");
    }

    [Fact]
    public void N0_BracketsWithMatchingDirectionInside_TakeEmbeddingDirection()
    {
        var levels = Levels("(ab)", rtl: false);
        levels.Should().OnlyContain(l => l == 0);
    }

    [Fact]
    public void Mirroring_AppliesToBracketsAtOddLevels()
    {
        Visual("(" + A + ")", rtl: true).Should().Be("(" + A + ")",
            "reversing the run swaps the brackets' positions and mirroring swaps their glyphs back");
        Visual("<" + A, rtl: true).Should().Be(A + ">");
    }

    [Fact]
    public void RtlEmbedding_ForcesRtlLevelForTheEmbeddedText()
    {
        var text = "a" + RLE + "bc" + PDF + "d";
        var levels = Levels(text);
        levels[2].Should().Be(2,
            "RLE and PDF take no part in ordering but raise the enclosed text's level: L text inside an RLE at " +
            "paragraph level 0 lands on level 2");
        levels[3].Should().Be(2);
        levels[0].Should().Be(0);
        levels[5].Should().Be(0);
    }

    [Fact]
    public void RtlOverride_TreatsLatinLettersAsRightToLeft()
    {
        var text = "" + RLO + "abc" + PDF + "";
        Visual(text).Where(c => c != (char)0x202E && c != (char)0x202C).Should().Equal('c', 'b', 'a');
    }

    [Fact]
    public void LtrOverride_InsideRtlParagraph_KeepsHebrewInLogicalOrder()
    {
        var text = "" + LRO + "" + A + B + "" + PDF + "";
        Visual(text, rtl: true).Where(c => c == A[0] || c == B[0]).Should().Equal(A[0], B[0]);
    }

    [Fact]
    public void RtlIsolate_KeepsItsContentOutOfTheSurroundingRuns()
    {
        var text = "a" + RLI + "" + A + "" + PDI + "b";
        var levels = Levels(text);
        levels[0].Should().Be(0, "the isolate is an R island and the 'a' before it stays at level 0");
        levels[2].Should().Be(1);
        levels[4].Should().Be(0, "the 'b' after the isolate stays at level 0");
    }

    [Fact]
    public void FirstStrongIsolate_ChoosesDirectionFromItsContent()
    {
        var rtlIsolate = Levels(A + "" + FSI + "" + B + "" + PDI + "");
        rtlIsolate[2].Should().Be(1, "an FSI over Hebrew is an RTL isolate");
        var ltrIsolate = Levels(A + "" + FSI + "ab" + PDI + "", rtl: true);
        ltrIsolate[2].Should().Be(2, "Latin inside an LTR isolate nested in an RTL paragraph");
    }

    [Fact]
    public void NestedEmbeddingsBeyondMaxDepth_DoNotCrash()
    {
        var text = new string((char)0x202B, 200) + "a" + new string((char)0x202C, 200);
        var act = () => BidiAlgorithm.Reorder(text);
        act.Should().NotThrow();
        var (visual, logicalOrder) = act();
        visual.Should().Contain("a", "the embedded letter survives the deep embedding");
        logicalOrder.Length.Should().Be(text.Length);
        Levels(text).Max().Should().BeLessThanOrEqualTo(126);
    }

    [Theory]
    [InlineData("abc אבג def 123 א")]
    [InlineData("العربية (English) 2024")]
    [InlineData("  א  ")]
    [InlineData("1+2=3 אב")]
    public void LogicalOrder_IsAPermutationOfAllPositions(string text)
    {
        foreach (var rtl in new[] { false, true })
        {
            var (visual, order) = BidiAlgorithm.Reorder(text, rtl);
            order.Should().HaveCount(text.Length);
            order.OrderBy(i => i).Should().Equal(Enumerable.Range(0, text.Length));
            visual.Length.Should().Be(text.Length);
        }
    }

    [Fact]
    public void SurrogatePairs_AreNeverSplitByReversal()
    {
        var text = A + "\U0001F600" + B;
        var (visual, _) = BidiAlgorithm.Reorder(text, true);
        visual.Should().Contain("\U0001F600", "the pair must stay high-surrogate first");
    }

    [Fact]
    public void ContainsRtl_DetectsHebrewAndArabicOnly()
    {
        BidiAlgorithm.ContainsRTL("abc").Should().BeFalse();
        BidiAlgorithm.ContainsRTL("abc" + A).Should().BeTrue();
        BidiAlgorithm.ContainsRTL(AR1).Should().BeTrue();
        BidiAlgorithm.ContainsRTL("").Should().BeFalse();
    }
}
