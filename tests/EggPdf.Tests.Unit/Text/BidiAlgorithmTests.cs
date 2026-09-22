using System.Linq;
using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// UAX #9 conformance cases, hand-derived from the rules. Hebrew letters stand in for strong RTL
/// text (alef = א, bet = ב, gimel = ג), Arabic for AL, Latin for L.
/// </summary>
public class BidiAlgorithmTests
{
    private const string A = "א", B = "ב", G = "ג";            // Hebrew alef, bet, gimel (R)
    private const string AR1 = "ا", AR2 = "ب";                      // Arabic alef, beh (AL)

    // Directional formatting characters, spelled numerically so they never appear literally in source
    private static readonly string LRE = ((char)0x202A).ToString(), RLE = ((char)0x202B).ToString(),
        PDF = ((char)0x202C).ToString(), LRO = ((char)0x202D).ToString(), RLO = ((char)0x202E).ToString(),
        LRI = ((char)0x2066).ToString(), RLI = ((char)0x2067).ToString(), FSI = ((char)0x2068).ToString(),
        PDI = ((char)0x2069).ToString();

    private static string Visual(string logical, bool rtl = false) => BidiAlgorithm.Reorder(logical, rtl).visual;
    private static int[] Levels(string logical, bool rtl = false) => BidiAlgorithm.ResolveLevels(logical, rtl);

    // ── Base cases ────────────────────────────────────────────────────────────

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
        // Latin-first text in an RTL paragraph still has paragraph level 1 (CSS direction wins)
        Levels("ab " + A, rtl: true)[0].Should().Be(2, "L text inside an RTL paragraph sits at level 2");
        Levels(A + " ab", rtl: false)[0].Should().Be(1);
    }

    // ── W rules: numbers ──────────────────────────────────────────────────────

    [Fact]
    public void EuropeanNumber_AfterHebrew_StaysLeftToRightInsideTheRtlRun()
    {
        // alef 12 bet -> RTL paragraph reading: bet, then 12, then alef (visual left-to-right)
        Visual(A + " 12 " + B, rtl: true).Should().Be(B + " 12 " + A);
    }

    [Fact]
    public void W2_EuropeanNumberAfterArabicLetter_BecomesArabicNumber()
    {
        // AL then EN -> AN; at level 1 an AN goes to level 2
        var levels = Levels(AR1 + "1");
        levels[1].Should().Be(2);
    }

    [Fact]
    public void W4_SingleSeparatorBetweenNumbers_JoinsThem()
    {
        // "1,000" and "12:30" stay one left-to-right number inside RTL text
        Visual(A + " 1,000 " + B, rtl: true).Should().Be(B + " 1,000 " + A);
        Visual(A + " 12:30 " + B, rtl: true).Should().Be(B + " 12:30 " + A);
    }

    [Fact]
    public void W5_TerminatorAdjacentToNumber_BecomesPartOfTheNumber()
    {
        // "50%" and "$5" keep their sign next to the digits
        Visual(A + " 50% " + B, rtl: true).Should().Be(B + " 50% " + A);
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

    // ── N rules: neutrals ─────────────────────────────────────────────────────

    [Fact]
    public void N1_NeutralsBetweenSameDirection_TakeThatDirection()
    {
        // alef SPACE bet in an LTR paragraph: the space is between two R chars -> level 1
        var levels = Levels(A + " " + B);
        levels.Should().Equal(1, 1, 1);
        Visual(A + " " + B).Should().Be(B + " " + A);
    }

    [Fact]
    public void N2_NeutralsBetweenDifferentDirections_TakeEmbeddingDirection()
    {
        // "a alef" in LTR paragraph: the space sits between L and R -> embedding direction L (level 0)
        Levels("a " + A).Should().Equal(0, 0, 1);
        // same text in an RTL paragraph: space between L and R -> level 1
        Levels("a " + A, rtl: true).Should().Equal(2, 1, 1);
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
        // The regression the old implementation had: "alef SP bet" must reverse as a whole
        Visual(A + " " + B + " " + G).Should().Be(G + " " + B + " " + A);
    }

    // ── N0: paired brackets ──────────────────────────────────────────────────

    [Fact]
    public void N0_BracketsAroundRtlText_InLtrParagraph_TakeRtlDirection()
    {
        // (alef bet) with context sos=L: brackets enclose R only, context L -> embedding L wins?
        // Inside is R (opposite of embedding L); preceding context is sos = L, so brackets stay L.
        var levels = Levels("(" + A + B + ")");
        levels.Should().Equal(0, 1, 1, 0);
    }

    [Fact]
    public void N0_BracketsAroundRtlText_AfterRtlContext_TakeRtlDirection()
    {
        // alef (bet) : the context before "(" is R, so the pair joins the R run
        var levels = Levels(A + " (" + B + ")");
        levels.Should().OnlyContain(l => l == 1);
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

    // ── Explicit formatting (X rules) ────────────────────────────────────────

    [Fact]
    public void RtlEmbedding_ForcesRtlLevelForTheEmbeddedText()
    {
        // LRE/RLE/PDF are removed from ordering but raise levels of the enclosed text
        var text = "a" + RLE + "bc" + PDF + "d";     // a RLE b c PDF d
        var levels = Levels(text);
        levels[2].Should().Be(2, "L text inside an RLE at paragraph level 0 lands on level 2");
        levels[3].Should().Be(2);
        levels[0].Should().Be(0);
        levels[5].Should().Be(0);
    }

    [Fact]
    public void RtlOverride_TreatsLatinLettersAsRightToLeft()
    {
        var text = "" + RLO + "abc" + PDF + "";       // RLO a b c PDF
        Visual(text).Where(c => c != (char)0x202E && c != (char)0x202C).Should().Equal('c', 'b', 'a');
    }

    [Fact]
    public void LtrOverride_InsideRtlParagraph_KeepsHebrewInLogicalOrder()
    {
        var text = "" + LRO + "" + A + B + "" + PDF + "";   // LRO alef bet PDF
        Visual(text, rtl: true).Where(c => c == A[0] || c == B[0]).Should().Equal(A[0], B[0]);
    }

    [Fact]
    public void RtlIsolate_KeepsItsContentOutOfTheSurroundingRuns()
    {
        // a RLI alef PDI b : the isolate is an R island; "a" and "b" both stay level 0
        var text = "a" + RLI + "" + A + "" + PDI + "b";
        var levels = Levels(text);
        levels[0].Should().Be(0);
        levels[2].Should().Be(1);
        levels[4].Should().Be(0);
    }

    [Fact]
    public void FirstStrongIsolate_ChoosesDirectionFromItsContent()
    {
        // FSI over Hebrew -> RTL isolate, over Latin -> LTR isolate
        var rtlIsolate = Levels(A + "" + FSI + "" + B + "" + PDI + "");
        rtlIsolate[2].Should().Be(1);
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

    // ── Structure guarantees ──────────────────────────────────────────────────

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
        // alef + emoji (surrogate pair) + bet in an RTL run
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
