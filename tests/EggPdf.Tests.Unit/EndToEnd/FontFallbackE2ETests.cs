using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EggPdf.Core;
using EggPdf.Layout;
using EggPdf.Text.Fallback;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class FontFallbackE2ETests
{
    public static IEnumerable<object[]> Scripts() => new[]
    {
        new object[] { "Korean (Hangul)", "개요 안녕하세요" },
        new object[] { "Japanese (kana + kanji)", "こんにちは 漢字" },
        new object[] { "Chinese (Han)", "你好，世界" },
        new object[] { "Arabic", "مرحبا بالعالم" },
        new object[] { "Hebrew", "שלום עולם" },
        new object[] { "Thai", "สวัสดี" },
        new object[] { "Devanagari", "नमस्ते" },
        new object[] { "Cyrillic", "Привет мир" },
        new object[] { "Greek", "Γειά σου κόσμε" },
        new object[] { "Vietnamese", "Cảnh báo Việt Nam" },
        new object[] { "Armenian", "Բարեւ աշխարհ" },
        new object[] { "Georgian", "გამარჯობა" },
        new object[] { "Symbols", "→ ✓ ⚠" },
    };

    private static IEnumerable<int> Codepoints(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            int cp = char.ConvertToUtf32(text, i);
            if (char.IsHighSurrogate(text[i])) i++;
            yield return cp;
        }
    }

    /// <summary>True when the document's font (Arial, or a stand-in) or some fallback font draws every visible character.</summary>
    private static bool MachineCanDraw(string text)
    {
        var result = FontFallbackResolver.Default.Resolve(Codepoints(text).Where(cp => cp > 0x7F));
        return result.Uncovered.Count == 0;
    }

    /// <summary>Glyph IDs shown by Tj/TJ operators in the page content (4-hex-digit CIDs).</summary>
    private static List<string> ShownGlyphs(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var glyphs = new List<string>();
        foreach (Match show in Regex.Matches(text, @"\[[^\]]*\]\s*TJ|<[0-9A-Fa-f]+>\s*Tj"))
        {
            foreach (Match hex in Regex.Matches(show.Value, "<([0-9A-Fa-f]+)>"))
            {
                var digits = hex.Groups[1].Value;
                for (int i = 0; i + 4 <= digits.Length; i += 4) glyphs.Add(digits.Substring(i, 4));
            }
        }
        return glyphs;
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public async Task Text_InAnyScript_IsNeverPaintedAsMissingGlyphBoxes(string script, string sample)
    {
        if (!MachineCanDraw(sample)) return;

        var html = "<html><body style='font-family: Arial, sans-serif'><h1>Invoice #2024-001 " + sample + "</h1></body></html>";
        var pdf = await HtmlToPdf.RenderAsync(html);

        var glyphs = ShownGlyphs(pdf);
        glyphs.Should().NotBeEmpty($"{script} text must be shown with embedded fonts");
        glyphs.Should().NotContain("0000", $"{script} characters must be drawn from a covering font, not as .notdef");
    }

    [Fact]
    public async Task IssueRepro_KoreanHeadingInArial_UsesAFallbackFontForHangul()
    {
        const string hangul = "개요";
        if (!MachineCanDraw(hangul)) return;

        var html = @"<html><head><style>
            body { font-family: Arial, sans-serif; margin: 40px; }
            h1 { color: #2d3436; border-bottom: 2px solid #6c5ce7; padding-bottom: 8px; }
            th { background: #6c5ce7; color: white; }
          </style></head><body>
          <h1>Invoice #2024-001 개요</h1>
          <p>Date: 2024-01-15 | Customer: Acme</p>
          <table><thead><tr><th>Item</th><th>Qty</th></tr></thead><tbody><tr><td>Web Development</td><td>40h</td></tr></tbody></table>
          <p><a href='https://example.com/pay'>Pay Now</a></p></body></html>";
        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        ShownGlyphs(pdf).Should().NotContain("0000");
        text.Should().Contain("-FB", "Arial has no Hangul, so a fallback font is embedded next to it");
    }

    [Fact]
    public async Task MixedScripts_InOneParagraph_EachRunUsesACoveringFont()
    {
        const string sample = "Hello 안녕 こんにちは 你好 Привет";
        if (!MachineCanDraw(sample)) return;

        var pdf = await HtmlToPdf.RenderAsync("<html><body style='font-family: Arial'><p>" + sample + "</p></body></html>");

        ShownGlyphs(pdf).Should().NotContain("0000");
    }

    [Fact]
    public async Task BoldText_InFallbackScript_IsNeverPaintedAsMissingGlyphBoxes()
    {
        const string hangul = "개요";
        if (!MachineCanDraw(hangul)) return;

        var pdf = await HtmlToPdf.RenderAsync("<html><body style='font-family: Arial'><p><b>Total 개요</b> and <i>개요</i></p></body></html>");

        ShownGlyphs(pdf).Should().NotContain("0000");
    }

    [Fact]
    public async Task FallbackFonts_AreSubsetted_NotEmbeddedWhole()
    {
        const string hangul = "개요";
        if (!MachineCanDraw(hangul)) return;

        var pdf = await HtmlToPdf.RenderAsync("<html><body style='font-family: Arial'><p>" + hangul + "</p></body></html>");

        pdf.Length.Should().BeLessThan(1_000_000, "a full CJK font is many megabytes, and two subsetted glyphs must stay small");
    }

    [Fact]
    public async Task PdfA_WithFallbackScript_StillEmbedsEveryFont()
    {
        const string hangul = "개요";
        if (!MachineCanDraw(hangul)) return;

        var pdf = await HtmlToPdf.RenderAsync("<html><body style='font-family: Arial'><p>Invoice " + hangul + "</p></body></html>",
            EggPdf.Pdf.PdfAConformance.PdfA2b);

        var text = Encoding.Latin1.GetString(pdf);
        ShownGlyphs(pdf).Should().NotContain("0000");
        Regex.Matches(text, "/FontFile2").Count.Should().BeGreaterThanOrEqualTo(2, "PDF/A requires the fallback font to be embedded too");
    }

    [Fact]
    public async Task CjkText_WrapsAccordingToItsRealGlyphWidth()
    {
        const string han = "一二三四五六七八九十";
        if (!MachineCanDraw(han)) return;

        var html = "<html><head><style>@page{size:400px 400px;margin:0}body{margin:0}</style></head>" +
                   "<body><div style='font-family:Arial;font-size:20px;width:120px;word-break:break-all'>" + han + "</div></body></html>";
        var pdf = await HtmlToPdf.RenderAsync(html);

        var lines = Regex.Matches(Encoding.Latin1.GetString(pdf), @"<[0-9A-Fa-f]{4,}>\s*Tj").Count;
        lines.Should().Be(2,
            "break-all lets every character break, and 6 fit in 120px at their real 20px advance, so the 10 take two " +
            "lines; a flat half-em estimate (10px each) would have put them on one");
    }

    [Fact]
    public async Task UncoveredCharacter_ProducesAMissingGlyphWarning_InsteadOfSilentTofu()
    {
        var warnings = new WarningCollector();
        var options = new PdfRenderOptions { Warnings = warnings };

        var pdf = await HtmlToPdf.RenderAsync("<html><body style='font-family: Arial'><p>Ref \U0010FFFD end</p></body></html>", options);

        pdf.Length.Should().BeGreaterThan(0, "rendering still succeeds");
        var warning = warnings.Warnings.Should().ContainSingle(w => w.Code == WarningCodes.GlyphMissing).Subject;
        warning.Message.Should().Contain("U+10FFFD", "this plane-16 private use code point is mapped by no installed font")
            .And.Contain("Arial");
        warning.Level.Should().Be(RenderWarningLevel.Warning);
    }

    [Fact]
    public async Task MissingGlyphWarning_ListsEachCodepointOnce_AcrossFontsAndRepeats()
    {
        var warnings = new WarningCollector();
        var options = new PdfRenderOptions { Warnings = warnings };

        await HtmlToPdf.RenderAsync(
            "<html><body style='font-family: Arial'><p>\U0010FFFD\U0010FFFD</p><p><b>\U0010FFFD</b></p></body></html>", options);

        warnings.Warnings.Count(w => w.Code == WarningCodes.GlyphMissing).Should().Be(1);
    }

    [Fact]
    public async Task MissingGlyphWarning_NamesAFewCodepointsThenCountsTheRest_AndSaysWhatToInstall()
    {
        var warnings = new WarningCollector();
        var options = new PdfRenderOptions { Warnings = warnings };
        var text = string.Concat(Enumerable.Range(0, 9).Select(i => char.ConvertFromUtf32(0x10FFF0 + i)));

        await HtmlToPdf.RenderAsync("<html><body style='font-family: Arial'><p>" + text + "</p></body></html>", options);

        var message = warnings.Warnings.Should().ContainSingle(w => w.Code == WarningCodes.GlyphMissing).Subject.Message;
        message.Should().Contain("9 characters").And.Contain("U+10FFF0").And.Contain("U+10FFF5")
            .And.NotContain("U+10FFF6").And.Contain("and 3 more").And.Contain("Install a font");
    }

    [Fact]
    public async Task OrdinaryDocument_ProducesNoWarnings()
    {
        var warnings = new WarningCollector();
        var options = new PdfRenderOptions { Warnings = warnings };

        await HtmlToPdf.RenderAsync("<html><body><p>Plain ASCII text, café — “quotes” … €5</p></body></html>", options);

        warnings.HasWarnings.Should().BeFalse();
    }

    [Fact]
    public async Task Rendering_WithoutACollector_StillWorksForUncoveredCharacters()
    {
        var pdf = await HtmlToPdf.RenderAsync("<html><body><p>\U0010FFFD</p></body></html>");

        Encoding.Latin1.GetString(pdf).Should().StartWith("%PDF");
    }

    [Fact]
    public void GlyphAdvanceProvider_DrivesMeasurementOfNonLatinText()
    {
        try
        {
            float flat = TextMeasurer.MeasureWidth("개요", 20, "Arial");
            TextMeasurer.GlyphAdvanceProvider = (family, weight, style, cp) => 1.0f;

            float measured = TextMeasurer.MeasureWidth("개요", 20, "Arial");

            flat.Should().BeApproximately(20f, 0.01f, "two characters at the flat 0.5em estimate");
            measured.Should().BeApproximately(40f, 0.01f, "two characters at the provider's 1em advance");
        }
        finally
        {
            TextMeasurer.GlyphAdvanceProvider = null;
        }
    }

    [Fact]
    public void GlyphAdvanceProvider_NegativeAnswer_KeepsTheDefaultEstimate()
    {
        try
        {
            TextMeasurer.GlyphAdvanceProvider = (family, weight, style, cp) => -1f;

            TextMeasurer.MeasureWidth("개요", 20, "Arial").Should().BeApproximately(20f, 0.01f);
        }
        finally
        {
            TextMeasurer.GlyphAdvanceProvider = null;
        }
    }

    [Fact]
    public void GlyphAdvanceProvider_IsNotConsultedForLatin1Text()
    {
        try
        {
            int calls = 0;
            TextMeasurer.GlyphAdvanceProvider = (family, weight, style, cp) => { calls++; return 2f; };

            float width = TextMeasurer.MeasureWidth("Hello café", 16, "Arial");

            calls.Should().Be(0, "ASCII and Latin-1 keep the table-based metrics, so Latin documents measure exactly as before");
            width.Should().BeGreaterThan(0);
        }
        finally
        {
            TextMeasurer.GlyphAdvanceProvider = null;
        }
    }

    [Fact]
    public void GlyphAdvanceProvider_SurrogatePair_IsMeasuredAsOneCharacter()
    {
        try
        {
            TextMeasurer.GlyphAdvanceProvider = (family, weight, style, cp) => cp == 0x1F600 ? 2.0f : -1f;

            TextMeasurer.MeasureWidth("\U0001F600", 20, "Arial").Should().BeApproximately(40f, 0.01f);
        }
        finally
        {
            TextMeasurer.GlyphAdvanceProvider = null;
        }
    }
}
