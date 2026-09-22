using System;
using System.IO;
using System.Linq;
using EggPdf.Text;
using EggPdf.Text.OpenType;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// Glyph-level shaping checked against real system fonts (Leelawadee UI for Thai, Nirmala for
/// Devanagari, Arial for Arabic). Each test returns early when its font isn't installed, so the
/// suite stays green on minimal CI images; where present they verify structural properties of the
/// shaped output (glyph order/count, zero-width marks) rather than pixel appearance.
/// </summary>
public class ComplexTextShaperTests
{
    private readonly ITestOutputHelper _out;
    public ComplexTextShaperTests(ITestOutputHelper output) { _out = output; }

    private static FontData? Load(string fileName)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), fileName);
        if (!File.Exists(path)) return null;
        return TtfParser.Parse(File.ReadAllBytes(path));
    }

    private void Dump(string label, FontData font, PositionedGlyph[] glyphs)
    {
        _out.WriteLine($"PROBE {label}: " + string.Join(" ", glyphs.Select(g =>
            $"[{g.GlyphId} adv={g.XAdvance} x={g.XOffset} y={g.YOffset}]")));
    }

    [Fact]
    public void Thai_ToneMarkAboveConsonant_MarkIsZeroWidthAndPositioned()
    {
        var font = Load("LeelawUI.ttf");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "กิ", baseRtl: false); // ko kai + sara i
        Dump("thai ki", font, glyphs);
        glyphs.Should().HaveCount(2);
        glyphs[1].XAdvance.Should().Be(0, "a Thai above-vowel mark is zero-width");
    }

    [Fact]
    public void Thai_SaraAm_DecomposesWithNikhahitBeforeToneMark()
    {
        var font = Load("LeelawUI.ttf");
        if (font == null) return;

        // น้ำ = no nu + mai tho + sara am  ->  น + nikhahit + mai tho + sara aa
        var glyphs = ComplexTextShaper.Shape(font, "น้ำ", baseRtl: false);
        Dump("thai nam", font, glyphs);
        glyphs.Should().HaveCount(4);
        glyphs[3].GlyphId.Should().Be(font.GetGlyphId(0x0E32));
        glyphs[1].GlyphId.Should().Be(font.GetGlyphId(0x0E4D), "nikhahit is moved before the tone mark");
    }

    [Fact]
    public void Devanagari_PreBaseMatra_MovesBeforeConsonant()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "कि", baseRtl: false); // ka + i-matra
        Dump("dev ki", font, glyphs);
        glyphs.Should().HaveCountGreaterOrEqualTo(2);
        glyphs[0].GlyphId.Should().NotBe(font.GetGlyphId(0x0915), "the i-matra is drawn before the consonant");
        glyphs.Last().GlyphId.Should().Be(font.GetGlyphId(0x0915));
    }

    [Fact]
    public void Devanagari_Conjunct_FormsFewerGlyphsThanCodepoints()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "क्ष", baseRtl: false); // k + virama + ssa
        Dump("dev kssa", font, glyphs);
        glyphs.Length.Should().BeLessThan(3, "ka+virama+ssa must substitute into a conjunct/half-form, not stay 3 glyphs");
    }

    [Fact]
    public void Devanagari_Reph_MovesAfterBaseConsonant()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "र्क", baseRtl: false); // ra + virama + ka
        Dump("dev rka", font, glyphs);
        glyphs.Should().HaveCount(2);
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x0915), "the base consonant comes first");
        glyphs[1].GlyphId.Should().NotBe(font.GetGlyphId(0x0930), "RA+virama became a reph glyph");
    }

    [Fact]
    public void Arabic_Fatha_AttachesAsZeroWidthMarkBeforeBaseInVisualOrder()
    {
        var font = Load("arial.ttf");
        if (font == null) return;

        // Isolated beh + fatha; in an RTL run the mark is drawn before (left of) the base.
        var text = ArabicShaper.Shape("بَ");
        var glyphs = ComplexTextShaper.Shape(font, text, baseRtl: true);
        Dump("arabic ba+fatha", font, glyphs);
        glyphs.Should().HaveCount(2);
        glyphs[0].XAdvance.Should().Be(0, "the fatha is a zero-width mark");
        glyphs[1].XAdvance.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Kannada_Reph_FollowsTheBaseConsonant()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "ರ್ಕ", baseRtl: false); // ra + virama + ka
        glyphs.Should().HaveCount(2);
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x0C95), "the base consonant comes first");
        glyphs[1].GlyphId.Should().NotBe(font.GetGlyphId(0x0CB0), "RA + virama became a reph glyph after the base");
    }

    [Fact]
    public void Malayalam_ChilluRa_StaysInLogicalPosition()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        // ka + (ra virama zwj = chillu-r) + ma: the chillu is NOT a reph and must not move to the end.
        var glyphs = ComplexTextShaper.Shape(font, "കര്‍മ", baseRtl: false);
        glyphs.Length.Should().BeGreaterThan(2);
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x0D15));
        glyphs.Last().GlyphId.Should().Be(font.GetGlyphId(0x0D2E), "the following consonant stays last");
    }

    [Fact]
    public void Sinhala_Kombuva_MovesBeforeConsonant()
    {
        var font = Load("Nirmala.ttc");
        if (font == null) return;

        // ka + two-part vowel sign o (kombuva + aela): the kombuva part is written before the consonant.
        var glyphs = ComplexTextShaper.Shape(font, "කො", baseRtl: false);
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x0DD9));
    }

    [Fact]
    public void Khmer_PreBaseVowel_MovesBeforeConsonant()
    {
        var font = Load("LeelawUI.ttf");
        if (font == null) return;

        var glyphs = ComplexTextShaper.Shape(font, "កេ", baseRtl: false); // ka + sign E
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x17C1), "sign E is written before its consonant");
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

        var glyphs = ComplexTextShaper.Shape(font, "င်္က", baseRtl: false); // kinzi + ka
        glyphs.Should().HaveCount(2);
        glyphs[0].GlyphId.Should().Be(font.GetGlyphId(0x1000), "the base comes first so GPOS can attach the kinzi to it");
        glyphs[1].XAdvance.Should().Be(0, "the kinzi is a zero-width mark");
    }

    [Fact]
    public void Tibetan_Stack_FormsFewerGlyphsThanCodepoints()
    {
        var font = Load("himalaya.ttf");
        if (font == null) return;

        // ka + subjoined ya + vowel sign i
        var glyphs = ComplexTextShaper.Shape(font, "ཀྱི", baseRtl: false);
        glyphs.Length.Should().BeLessThan(4);
        glyphs.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Arabic_FontWithoutPresentationForms_UsesGsubPositionalForms()
    {
        var font = Load("DUBAI-REGULAR.TTF");
        if (font == null) return;

        // Dubai lacks some Arabic Presentation Forms glyphs (e.g. isolated alef): shaping must fall
        // back to the base letters plus the font's own positional features, never emitting .notdef.
        var text = ArabicShaper.Shape("الببب");
        text.Any(c => font.GetGlyphId(c) == 0).Should().BeTrue("the precondition: some shaped form has no glyph in this font");

        var glyphs = ComplexTextShaper.Shape(font, text, baseRtl: true);
        glyphs.Should().HaveCount(5);
        glyphs.Should().OnlyContain(g => g.GlyphId != 0, "no glyph may fall back to .notdef");
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
