using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EggPdf.Text.Fallback;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// Font fallback is coverage-driven for every script, so these tests use synthetic fonts mapping
/// chosen code points in a temp directory -- no dependency on what the host has installed.
/// </summary>
public class FontFallbackResolverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eggpdf-fallback-" + Guid.NewGuid().ToString("N"));

    // Sample characters per script (first character of a greeting in that script)
    private const int Hangul = 0xAC1C, Kana = 0x3053, Han = 0x4F60, Arabic = 0x0627, Hebrew = 0x05E9, Thai = 0x0E2A,
        Devanagari = 0x0928, Cyrillic = 0x041F, Greek = 0x0393, Armenian = 0x0531, Cherokee = 0x13A0, Emoji = 0x1F600, Warning = 0x26A0;

    public FontFallbackResolverTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Font(string fileName, params int[] codepoints) => SyntheticFonts.Write(_dir, fileName, SyntheticFonts.Build(codepoints));

    private FontFallbackResolver ResolverOverDirectory(FontFallbackCatalog? catalog = null)
        => new FontFallbackResolver(() => Directory.GetFiles(_dir), catalog ?? new FontFallbackCatalog());

    [Fact]
    public void Resolve_EachScriptIsSuppliedByItsOwnRegisteredFont()
    {
        // The mechanism is script-keyed, not language-specific: any script can be given candidates
        Font("KoreanFace.ttf", Hangul);
        Font("ArabicFace.ttf", Arabic);
        Font("HebrewFace.ttf", Hebrew);
        Font("DevaFace.ttf", Devanagari);
        var catalog = new FontFallbackCatalog();
        catalog.Register(UnicodeScript.Hangul, "KoreanFace");
        catalog.Register(UnicodeScript.Arabic, "ArabicFace");
        catalog.Register(UnicodeScript.Hebrew, "HebrewFace");
        catalog.Register(UnicodeScript.Devanagari, "DevaFace");

        var result = ResolverOverDirectory(catalog).Resolve(new[] { Hangul, Arabic, Hebrew, Devanagari });

        result.Uncovered.Should().BeEmpty();
        result.Fonts.Should().HaveCount(4);
        string FontFor(int cp) => Path.GetFileName(result.Fonts.Single(f => f.Codepoints.Contains(cp)).Path);
        FontFor(Hangul).Should().Be("KoreanFace.ttf");
        FontFor(Arabic).Should().Be("ArabicFace.ttf");
        FontFor(Hebrew).Should().Be("HebrewFace.ttf");
        FontFor(Devanagari).Should().Be("DevaFace.ttf");
    }

    [Fact]
    public void Resolve_CodepointsSharingAFont_AreGroupedIntoOneEmbeddedFont()
    {
        Font("Pan.ttf", Hangul, Kana, Han, Cyrillic);
        var catalog = new FontFallbackCatalog();
        catalog.RegisterUniversal("Pan");

        var result = ResolverOverDirectory(catalog).Resolve(new[] { Han, Hangul, Kana, Cyrillic });

        result.Fonts.Should().ContainSingle();
        result.Fonts[0].Codepoints.Should().BeEquivalentTo(new[] { Han, Hangul, Kana, Cyrillic });
        result.Fonts[0].Font.GetGlyphId(Hangul).Should().NotBe(0, "the returned FontData is the parsed face, ready to subset");
    }

    [Fact]
    public void Resolve_PicksTheFirstCandidateThatCoversEachCodepoint_NotJustTheFirstInstalled()
    {
        Font("Narrow.ttf", Greek);            // first candidate, lacks Hangul
        Font("Wide.ttf", Greek, Hangul);
        var catalog = new FontFallbackCatalog();
        catalog.Register(UnicodeScript.Hangul, "Narrow", "Wide");

        var result = ResolverOverDirectory(catalog).Resolve(new[] { Hangul });

        Path.GetFileName(result.Fonts.Single().Path).Should().Be("Wide.ttf");
    }

    [Fact]
    public void Resolve_CodepointInNoCatalogList_IsFoundByScanningInstalledFonts()
    {
        // Nothing names this font anywhere: the safety net must still find it by coverage alone
        Font("ObscureHeritageFont.ttf", Cherokee);

        var result = ResolverOverDirectory().Resolve(new[] { Cherokee });

        result.Uncovered.Should().BeEmpty();
        Path.GetFileName(result.Fonts.Single().Path).Should().Be("ObscureHeritageFont.ttf");
    }

    [Fact]
    public void Resolve_FullScan_PrefersTheBroadestFontSoFewerFontsAreEmbedded()
    {
        Font("Tiny.ttf", Armenian);
        Font("Broad.ttf", Armenian, Cherokee, Thai, Hebrew);

        var result = ResolverOverDirectory().Resolve(new[] { Armenian, Cherokee });

        result.Fonts.Should().ContainSingle();
        Path.GetFileName(result.Fonts[0].Path).Should().Be("Broad.ttf");
    }

    [Fact]
    public void Resolve_CodepointNoInstalledFontCovers_IsReportedUncovered()
    {
        Font("Only.ttf", Greek);

        var result = ResolverOverDirectory().Resolve(new[] { Greek, Hangul, 0x10FFFD });

        result.Uncovered.Should().Equal(Hangul, 0x10FFFD);
        result.Fonts.Should().ContainSingle().Which.Codepoints.Should().Equal(Greek);
    }

    [Fact]
    public void Resolve_InvisibleCharacters_NeverCountAsMissing()
    {
        var result = ResolverOverDirectory().Resolve(new[] { 0x20, 0x0A, 0x00A0, 0x200B, 0x200D, 0xFE0F, 0xFEFF });

        result.Uncovered.Should().BeEmpty();
        result.Fonts.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NoInstalledFontsAtAll_ReportsEverythingUncoveredWithoutThrowing()
    {
        var result = ResolverOverDirectory().Resolve(new[] { Hangul, Han });

        result.Fonts.Should().BeEmpty();
        result.Uncovered.Should().Equal(Han, Hangul); // ascending code point order
    }

    [Fact]
    public void Resolve_FontWithoutDrawableOutlines_IsNeverChosen()
    {
        // A bitmap-only emoji font maps U+1F600 but would paint nothing: better to report it
        SyntheticFonts.Write(_dir, "BitmapEmoji.ttf", SyntheticFonts.Build(new[] { Emoji }, bitmapColorOnly: true));
        var catalog = new FontFallbackCatalog();
        catalog.Register(UnicodeScript.Emoji, "BitmapEmoji");

        var result = ResolverOverDirectory(catalog).Resolve(new[] { Emoji });

        result.Fonts.Should().BeEmpty();
        result.Uncovered.Should().Equal(Emoji);
    }

    [Fact]
    public void Resolve_VectorEmojiFont_IsChosenOverTheBitmapOne()
    {
        SyntheticFonts.Write(_dir, "BitmapEmoji.ttf", SyntheticFonts.Build(new[] { Emoji }, bitmapColorOnly: true));
        Font("VectorEmoji.ttf", Emoji, Warning);
        var catalog = new FontFallbackCatalog();
        catalog.Register(UnicodeScript.Emoji, "BitmapEmoji", "VectorEmoji");

        var result = ResolverOverDirectory(catalog).Resolve(new[] { Emoji });

        Path.GetFileName(result.Fonts.Single().Path).Should().Be("VectorEmoji.ttf");
    }

    [Fact]
    public void Resolve_FaceInsideACollection_ReturnsThatFaceAndItsIndex()
    {
        var ttc = SyntheticFonts.BuildCollection(
            SyntheticFonts.Build(new[] { Greek }),
            SyntheticFonts.Build(new[] { Hangul, Kana }, advance: 900));
        SyntheticFonts.Write(_dir, "Suite.ttc", ttc);
        var catalog = new FontFallbackCatalog();
        catalog.Register(UnicodeScript.Hangul, "Suite");

        var result = ResolverOverDirectory(catalog).Resolve(new[] { Hangul });

        var fallback = result.Fonts.Single();
        fallback.FaceIndex.Should().Be(1);
        fallback.Font.GetGlyphId(Hangul).Should().NotBe(0, "the standalone copy of face 1 maps Hangul");
        fallback.Font.GetGlyphId(Greek).Should().Be(0, "face 0's characters belong to a different face");
    }

    [Fact]
    public void Resolve_Bold_PrefersTheBoldFileOfACandidate()
    {
        Font("Fallback.ttf", Hangul);
        Font("Fallback Bold.ttf", Hangul);
        var catalog = new FontFallbackCatalog();
        catalog.Register(UnicodeScript.Hangul, "Fallback");
        var resolver = ResolverOverDirectory(catalog);

        Path.GetFileName(resolver.Resolve(new[] { Hangul }, bold: false).Fonts.Single().Path).Should().Be("Fallback.ttf");
        Path.GetFileName(resolver.Resolve(new[] { Hangul }, bold: true).Fonts.Single().Path).Should().Be("Fallback Bold.ttf");
    }

    [Fact]
    public void Resolve_Bold_FallsBackToRegularWhenNoBoldFileExists()
    {
        Font("OnlyRegular.ttf", Hangul);
        var catalog = new FontFallbackCatalog();
        catalog.Register(UnicodeScript.Hangul, "OnlyRegular");

        var result = ResolverOverDirectory(catalog).Resolve(new[] { Hangul }, bold: true);

        Path.GetFileName(result.Fonts.Single().Path).Should().Be("OnlyRegular.ttf");
    }

    [Fact]
    public void Register_AfterAnEarlierResolve_TakesEffect()
    {
        Font("First.ttf", Hangul);
        Font("Second.ttf", Hangul);
        var catalog = new FontFallbackCatalog();
        catalog.Register(UnicodeScript.Hangul, "First");
        var resolver = ResolverOverDirectory(catalog);
        Path.GetFileName(resolver.Resolve(new[] { Hangul }).Fonts.Single().Path).Should().Be("First.ttf");

        catalog.Register(UnicodeScript.Hangul, "Second");

        Path.GetFileName(resolver.Resolve(new[] { Hangul }).Fonts.Single().Path).Should().Be("Second.ttf",
            "later registrations take priority and must not be hidden by cached decisions");
    }

    [Fact]
    public void TryGetAdvanceEm_ReportsTheFallbackGlyphsAdvanceInEm()
    {
        SyntheticFonts.Write(_dir, "Wide.ttf", SyntheticFonts.Build(new[] { Hangul }, advance: 1000));
        SyntheticFonts.Write(_dir, "Half.ttf", SyntheticFonts.Build(new[] { Hebrew }, advance: 500));
        var catalog = new FontFallbackCatalog();
        catalog.Register(UnicodeScript.Hangul, "Wide");
        catalog.Register(UnicodeScript.Hebrew, "Half");
        var resolver = ResolverOverDirectory(catalog);

        resolver.TryGetAdvanceEm(Hangul, false, false, HanVariant.Auto, out var wide).Should().BeTrue();
        resolver.TryGetAdvanceEm(Hebrew, false, false, HanVariant.Auto, out var half).Should().BeTrue();
        resolver.TryGetAdvanceEm(Thai, false, false, HanVariant.Auto, out _).Should().BeFalse("no font covers Thai");

        wide.Should().BeApproximately(1.0f, 0.001f);
        half.Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void FindFont_ReturnsTheSameFaceResolveWouldEmbed()
    {
        // Layout measurement (FindFont/TryGetAdvanceEm) and embedding (Resolve) must pick the same font
        Font("One.ttf", Greek, Hangul);
        Font("Two.ttf", Hangul, Kana);

        var resolver = ResolverOverDirectory();
        var viaFind = resolver.FindFont(Hangul);
        var viaResolve = resolver.Resolve(new[] { Hangul, Kana, Greek });

        viaFind.Should().BeSameAs(viaResolve.Fonts.Single(f => f.Codepoints.Contains(Hangul)).Font);
    }

    [Fact]
    public void Resolve_IsStableAcrossRepeatedCalls()
    {
        Font("Pan.ttf", Hangul, Han, Kana);

        var resolver = ResolverOverDirectory();
        var first = resolver.Resolve(new[] { Hangul, Han });
        var second = resolver.Resolve(new[] { Han, Hangul });

        second.Fonts.Single().Font.Should().BeSameAs(first.Fonts.Single().Font, "parsed fonts are cached, not reloaded per render");
    }

    [Fact]
    public void Catalog_HanCandidates_FollowTheRegionalVariant()
    {
        var catalog = new FontFallbackCatalog();

        int IndexOf(HanVariant variant, string family) =>
            catalog.GetCandidates(UnicodeScript.Han, variant).ToList().IndexOf(family);

        IndexOf(HanVariant.Japanese, "NotoSansCJKjp").Should().BeLessThan(IndexOf(HanVariant.Japanese, "NotoSansCJKkr"));
        IndexOf(HanVariant.Korean, "NotoSansCJKkr").Should().BeLessThan(IndexOf(HanVariant.Korean, "NotoSansCJKjp"));
        IndexOf(HanVariant.SimplifiedChinese, "NotoSansCJKsc").Should().BeLessThan(IndexOf(HanVariant.SimplifiedChinese, "NotoSansCJKtc"));
        IndexOf(HanVariant.TraditionalChinese, "NotoSansCJKtc").Should().BeLessThan(IndexOf(HanVariant.TraditionalChinese, "NotoSansCJKsc"));
    }

    [Fact]
    public void Catalog_EveryNonLatinScriptHasCandidates()
    {
        // A new script must not silently fall off the end: each has Noto-based names at minimum
        var catalog = new FontFallbackCatalog();
        var withoutCandidates = Enum.GetValues(typeof(UnicodeScript)).Cast<UnicodeScript>()
            .Where(s => s != UnicodeScript.Other)
            .Where(s => catalog.GetCandidates(s).Count == 0)
            .ToList();

        withoutCandidates.Should().BeEmpty();
        catalog.GetUniversalCandidates().Should().NotBeEmpty();
    }

    [Fact]
    public void Catalog_Register_PutsApplicationFontsAheadOfBuiltIns()
    {
        var catalog = new FontFallbackCatalog();
        catalog.Register(UnicodeScript.Thai, "Corporate Thai");

        catalog.GetCandidates(UnicodeScript.Thai)[0].Should().Be("Corporate Thai");
        catalog.GetCandidates(UnicodeScript.Thai).Should().Contain("NotoSansThai", "built-ins remain as fallbacks");
    }

    [Theory]
    [InlineData(UnicodeScript.Han, "Noto Sans CJK")]
    [InlineData(UnicodeScript.Hangul, "Noto Sans CJK")]
    [InlineData(UnicodeScript.Arabic, "Noto Sans Arabic")]
    [InlineData(UnicodeScript.CanadianAboriginal, "Noto Sans Canadian Aboriginal")]
    public void InstallHint_NamesAFontWorthInstalling(UnicodeScript script, string expected)
    {
        FontFallbackCatalog.InstallHint(script).Should().Be(expected);
    }

    [Fact]
    public void DefaultResolver_ResolvesWithoutThrowing_WhateverIsInstalled()
    {
        var result = FontFallbackResolver.Default.Resolve(new[] { Hangul, Han, Kana, Arabic, Hebrew, Thai, Devanagari, 0x10FFFD });

        // Every requested visible codepoint is either supplied by exactly one font or reported
        var supplied = result.Fonts.SelectMany(f => f.Codepoints).ToList();
        supplied.Concat(result.Uncovered).Should().BeEquivalentTo(new[] { Hangul, Han, Kana, Arabic, Hebrew, Thai, Devanagari, 0x10FFFD });
        supplied.Should().OnlyHaveUniqueItems();
        result.Uncovered.Should().Contain(0x10FFFD);
    }
}
