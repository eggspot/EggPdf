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
    [InlineData("با", false)]
    [InlineData("ก", true)]
    [InlineData("क", true)]
    [InlineData("بَ", true)]
    public void NeedsShaping_DetectsComplexScripts(string text, bool expected)
    {
        ComplexTextShaper.NeedsShaping(text).Should().Be(expected);
    }
}
