using System;
using System.IO;
using System.Linq;
using EggPdf.Text;
using EggPdf.Text.OpenType;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class ComplexTextShaperTests
{
    private const char ZeroWidthJoiner = (char)0x200D;

    /// <summary>
    /// A system font by file name, or null when it isn't installed, so each shaping test returns early on
    /// minimal CI images and otherwise checks structural properties of the shaped output, not pixels.
    /// </summary>
    private static FontData? Load(string fileName)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), fileName);
        if (!File.Exists(path)) return null;
        return TtfParser.Parse(File.ReadAllBytes(path));
    }

    [Fact]
    public void Thai_ToneMarkAboveConsonant_MarkIsZeroWidthAndPositioned()
    {
        var font = Load("LeelawUI.ttf");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "กิ", baseRtl: false);
        glyphs.Should().HaveCount(2, "ko kai and sara i");
        glyphs[1].XAdvance.Should().Be(0, "a Thai above-vowel mark is zero-width");
    }

    [Fact]
    public void Thai_SaraAm_DecomposesWithNikhahitBeforeToneMark()
    {
        var font = Load("LeelawUI.ttf");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "น้ำ", baseRtl: false);
        glyphs.Should().HaveCount(4, "no nu + mai tho + sara am becomes no nu + nikhahit + mai tho + sara aa");
        glyphs[3].GlyphId.Should().Be(font.GetGlyphId(0x0E32));
        glyphs[1].GlyphId.Should().Be(font.GetGlyphId(0x0E4D), "nikhahit is moved before the tone mark");
    }

    [Fact]
    public void Devanagari_PreBaseMatra_MovesBeforeConsonant()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "कि", baseRtl: false);
        glyphs.Should().HaveCountGreaterOrEqualTo(2, "ka and the i-matra");
        glyphs[0].GlyphId.Should().NotBe(font.GetGlyphId(0x0915), "the i-matra is drawn before the consonant");
        glyphs.Last().GlyphId.Should().Be(font.GetGlyphId(0x0915));
    }

    [Fact]
    public void Devanagari_Conjunct_FormsFewerGlyphsThanCodepoints()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "क्ष", baseRtl: false);
        glyphs.Length.Should().BeLessThan(3, "ka+virama+ssa must substitute into a conjunct/half-form, not stay 3 glyphs");
    }

    [Fact]
    public void Devanagari_Reph_MovesAfterBaseConsonant()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "र्क", baseRtl: false);
        glyphs.Should().HaveCount(2, "ra + virama + ka");
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x0915), "the base consonant comes first");
        glyphs[1].GlyphId.Should().NotBe(font.GetGlyphId(0x0930), "RA+virama became a reph glyph");
    }

    [Fact]
    public void Arabic_Fatha_AttachesAsZeroWidthMarkBeforeBaseInVisualOrder()
    {
        var font = Load("arial.ttf");
        if (font == null) return;

        var text = ArabicShaper.Shape("بَ");
        var glyphs = ComplexTextShaper.Shape(font, text, baseRtl: true);
        glyphs.Should().HaveCount(2, "an isolated beh and its fatha");
        glyphs[0].XAdvance.Should().Be(0, "the fatha is a zero-width mark, drawn before (left of) the base in an RTL run");
        glyphs[1].XAdvance.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Kannada_Reph_FollowsTheBaseConsonant()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "ರ್ಕ", baseRtl: false);
        glyphs.Should().HaveCount(2, "ra + virama + ka");
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x0C95), "the base consonant comes first");
        glyphs[1].GlyphId.Should().NotBe(font.GetGlyphId(0x0CB0), "RA + virama became a reph glyph after the base");
    }

    [Fact]
    public void Malayalam_ChilluRa_StaysInLogicalPosition()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        var kaChilluRaMa = string.Concat((char)0x0D15, (char)0x0D30, (char)0x0D4D, ZeroWidthJoiner, (char)0x0D2E);
        var glyphs = ComplexTextShaper.Shape(font, kaChilluRaMa, baseRtl: false);
        glyphs.Length.Should().BeGreaterThan(2);
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x0D15));
        glyphs.Last().GlyphId.Should().Be(font.GetGlyphId(0x0D2E),
            "ra + virama + zero width joiner is a chillu, not a reph, so it doesn't move and the following consonant stays last");
    }

    [Fact]
    public void Sinhala_Kombuva_MovesBeforeConsonant()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "කො", baseRtl: false);
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x0DD9),
            "the two-part vowel sign o (kombuva + aela) has its kombuva written before the consonant");
    }

    [Fact]
    public void Khmer_PreBaseVowel_MovesBeforeConsonant()
    {
        var font = Load("LeelawUI.ttf");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "កេ", baseRtl: false);
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x17C1), "sign E is written before its consonant ka");
    }

    [Fact]
    public void Myanmar_EVowel_MovesBeforeConsonant()
    {
        var font = Load("mmrtext.ttf");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "ကေ", baseRtl: false);
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x1031), "the e-vowel is written before its consonant");
    }

    [Fact]
    public void Myanmar_Kinzi_IsPlacedAfterBaseAsAMark()
    {
        var font = Load("mmrtext.ttf");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "င်္က", baseRtl: false);
        glyphs.Should().HaveCount(2, "the kinzi and ka");
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x1000), "the base comes first so GPOS can attach the kinzi to it");
        glyphs[1].XAdvance.Should().Be(0, "the kinzi is a zero-width mark");
    }

    [Fact]
    public void Tibetan_Stack_FormsFewerGlyphsThanCodepoints()
    {
        var font = Load("himalaya.ttf");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "ཀྱི", baseRtl: false);
        glyphs.Length.Should().BeLessThan(4, "ka + subjoined ya + vowel sign i stack into fewer glyphs");
        glyphs.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Arabic_FontWithoutPresentationForms_UsesGsubPositionalForms()
    {
        var font = Load("DUBAI-REGULAR.TTF");
        if (font == null) return;

        var text = ArabicShaper.Shape("الببب");
        text.Any(c => font.GetGlyphId(c) == 0).Should().BeTrue(
            "the precondition: Dubai lacks some Arabic Presentation Forms glyphs, such as the isolated alef");

        var glyphs = ComplexTextShaper.Shape(font, text, baseRtl: true);
        glyphs.Should().HaveCount(5);
        glyphs.Should().OnlyContain(g => g.GlyphId != 0,
            "shaping falls back to the base letters plus the font's own positional features, never to .notdef");
    }

    [Fact]
    public void LatinTextThroughShaper_KeepsGlyphCountAndAdvances()
    {
        var font = Load("arial.ttf");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "abc", baseRtl: false);
        glyphs.Should().HaveCount(3);
        glyphs[0].XAdvance.Should().Be(font.GetAdvanceWidth(font.GetGlyphId('a')));
    }

    [Theory]
    [InlineData("hello", false)]
    [InlineData("با", true)]
    [InlineData("ก", true)]
    [InlineData("क", true)]
    [InlineData("بَ", true)]
    public void NeedsShaping_DetectsComplexScripts(string text, bool expected)
    {
        ComplexTextShaper.NeedsShaping(text).Should().Be(expected);
    }
}
