using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// Arabic contextual shaping: joining-form selection (isolated/initial/medial/final) mapped
/// onto Unicode Arabic Presentation Forms, lam-alef ligatures, and transparent-mark handling.
/// Expected strings are exact -- the shaper is a pure string transform with no font dependency.
/// </summary>
public class ArabicShaperTests
{
    [Fact]
    public void ContainsArabic_DetectsArabicBlock()
    {
        ArabicShaper.ContainsArabic("hello").Should().BeFalse();
        ArabicShaper.ContainsArabic("").Should().BeFalse();
        ArabicShaper.ContainsArabic("abc ب").Should().BeTrue();
    }

    [Fact]
    public void Shape_NonArabicText_ReturnedUnchanged()
    {
        ArabicShaper.Shape("Hello 123").Should().Be("Hello 123");
    }

    [Fact]
    public void Shape_SingleDualJoiningLetter_IsIsolated()
    {
        ArabicShaper.Shape("ب").Should().Be("ﺏ");
    }

    [Fact]
    public void Shape_TwoDualJoiningLetters_InitialThenFinal()
    {
        ArabicShaper.Shape("بب").Should().Be("ﺑﺐ");
    }

    [Fact]
    public void Shape_ThreeDualJoiningLetters_InitialMedialFinal()
    {
        ArabicShaper.Shape("ببب").Should().Be("ﺑﺒﺐ");
    }

    [Fact]
    public void Shape_RightJoiningLettersDoNotConnectForward()
    {
        // ا (R) د (R) ب (D): neither alef nor dal joins the following letter.
        ArabicShaper.Shape("ادب").Should().Be("ﺍﺩﺏ");
    }

    [Fact]
    public void Shape_Kitab_MixesInitialMedialFinalAndIsolated()
    {
        // ك ت ا ب : initial, medial, final (alef ends the join), isolated (after alef).
        ArabicShaper.Shape("كتاب").Should().Be("ﻛﺘﺎﺏ");
    }

    [Fact]
    public void Shape_LamAlef_IsolatedLigature()
    {
        ArabicShaper.Shape("لا").Should().Be("ﻻ");
    }

    [Fact]
    public void Shape_LamAlef_FinalLigatureWhenJoinedFromPreviousLetter()
    {
        ArabicShaper.Shape("بلا").Should().Be("ﺑﻼ");
    }

    [Fact]
    public void Shape_LamAlefWithHamzaAbove_UsesItsOwnLigature()
    {
        ArabicShaper.Shape("لأ").Should().Be("ﻷ");
    }

    [Fact]
    public void Shape_HarakatAreTransparentToJoining()
    {
        // ب + fatha + ب : the fatha must not break the join between the two beh letters.
        ArabicShaper.Shape("بَب").Should().Be("ﺑَﺐ");
    }

    [Fact]
    public void Shape_SpaceBreaksJoining()
    {
        ArabicShaper.Shape("ب ب").Should().Be("ﺏ ﺏ");
    }

    [Fact]
    public void Shape_ArabicInsideLatinText_OnlyArabicChanges()
    {
        ArabicShaper.Shape("abc ب def").Should().Be("abc ﺏ def");
    }

    [Fact]
    public void Shape_HamzaIsNonJoining()
    {
        ArabicShaper.Shape("بء").Should().Be("ﺏﺀ");
    }

    [Fact]
    public void Shape_TatweelActsAsJoiner()
    {
        ArabicShaper.Shape("بـب").Should().Be("ﺑـﺐ");
    }

    [Fact]
    public void Shape_PersianPeh_UsesPresentationFormsA()
    {
        ArabicShaper.Shape("پ").Should().Be("ﭖ");
        ArabicShaper.Shape("پپ").Should().Be("ﭘﭗ");
    }

    [Fact]
    public void Shape_IsIdempotent()
    {
        var once = ArabicShaper.Shape("كتاب");
        ArabicShaper.Shape(once).Should().Be(once);
    }

    [Fact]
    public void TryGetBaseForm_MapsPresentationFormBackToBaseLetter()
    {
        ArabicShaper.TryGetBaseForm(0xFE91, out var text).Should().BeTrue();
        text.Should().Be("ب");
    }

    [Fact]
    public void TryGetBaseForm_LamAlefLigature_ExpandsToTwoLetters()
    {
        ArabicShaper.TryGetBaseForm(0xFEFB, out var text).Should().BeTrue();
        text.Should().Be("لا");
    }

    [Fact]
    public void TryGetBaseForm_NonPresentationForm_ReturnsFalse()
    {
        ArabicShaper.TryGetBaseForm('a', out _).Should().BeFalse();
        ArabicShaper.TryGetBaseForm(0x0628, out _).Should().BeFalse();
    }
}
