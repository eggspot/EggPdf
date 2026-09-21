using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Thai, Devanagari and Arabic-with-harakat text through the whole pipeline. Each test returns
/// early when no installed font covers the script (minimal CI images).
/// </summary>
public class ComplexScriptE2ETests
{
    private static bool HasFont(string file)
        => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file));

    [Fact]
    public async Task Thai_RendersShapedGlyphRunWithMarkPositioning()
    {
        if (!HasFont("LeelawUI.ttf") && !HasFont("tahoma.ttf")) return;

        var html = "<html><body><p style='font-size:20px'>น้ำ กิน ข้าว</p></body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("-CXT", "Thai text must be embedded under its own shaped-script font key");
        text.Should().Contain("] TJ", "shaped glyphs are emitted with positioned TJ runs");
        text.Should().Contain("/FontFile2");
    }

    [Fact]
    public async Task Devanagari_RendersConjunctsAndMatras()
    {
        if (!HasFont("Nirmala.ttc")) return;

        var html = "<html><body><p style='font-size:20px'>किताब क्षत्रिय र्क</p></body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("-CX0900");
        text.Should().Contain("] TJ");
    }

    [Fact]
    public async Task ArabicWithHarakat_RendersMarksThroughShaper()
    {
        if (!HasFont("arial.ttf")) return;

        var html = "<html><body dir='rtl'><p style='font-size:20px'>كَتَبَ</p></body></html>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("-CXA");
    }

    [Fact]
    public async Task Myanmar_SpacelessParagraph_WrapsInsideANarrowBox()
    {
        if (!HasFont("mmrtext.ttf")) return;

        var html = "<html><head><style>@page{size:400px 800px;margin:0}body{margin:0}</style></head><body>" +
                   "<div style='font-family:\"Myanmar Text\";font-size:20px;width:90px'>" +
                   "မြန်မာဘာသာစကားကိုလေ့လာပါသည်မြန်မာဘာသာစကားကိုလေ့လာပါသည်</div></body></html>";
        var text = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));

        var runs = System.Text.RegularExpressions.Regex.Matches(text, @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Td\s*\[?[^\r\n]*?\] TJ");
        runs.Count.Should().BeGreaterThan(2, "a 60-character space-free run must break at syllable boundaries instead of overflowing one line");
    }

    [Fact]
    public async Task Myanmar_LineHeightNormal_FollowsFontMetricsInsteadOfFixed1_2em()
    {
        const string fontFile = "mmrtext.ttf";
        if (!HasFont(fontFile)) return;

        var font = EggPdf.Text.TrueType.TtfParser.Parse(
            File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), fontFile)))!;
        float expectedEm = (font.Ascent + Math.Abs(font.Descent) + Math.Max(0, font.LineGap)) / (float)font.UnitsPerEm;
        expectedEm.Should().BeGreaterThan(1.3f, "Myanmar Text is a tall font -- the premise of the fix");

        // Narrow box forces wrapping onto several lines; the paragraph's height is lines * line-height
        var html = "<html><head><style>@page{size:400px 800px;margin:0}body{margin:0}</style></head><body>" +
                   "<div style='font-family:\"Myanmar Text\";font-size:20px;width:120px;line-height:normal'>" +
                   "မြန်မာဘာသာစကား မြန်မာဘာသာစကား မြန်မာဘာသာစကား မြန်မာဘာသာစကား</div></body></html>";
        var text = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));

        // Baseline y (points) of each shaped line, taken from its text-positioning operator
        var ys = new System.Collections.Generic.List<float>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     text, @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Td\s*\[?[^\r\n]*?\] TJ"))
            ys.Add(float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));

        ys.Count.Should().BeGreaterThan(1, "the narrow box wraps the text onto several lines");
        float spacingPx = (ys[0] - ys[1]) / 0.75f;
        spacingPx.Should().BeApproximately(20f * expectedEm, 0.6f,
            "each line box is ascent + descent + lineGap of the shaping font, not 1.2em (24px)");
    }

    [Fact]
    public async Task LatinOnlyDocument_NeverUsesComplexPath()
    {
        byte[] pdf = await HtmlToPdf.RenderAsync("<html><body><p>Plain Latin text</p></body></html>");
        Encoding.Latin1.GetString(pdf).Should().NotContain("-CX");
    }

    [Fact]
    public async Task MixedLatinAndThai_RendersValidPdf()
    {
        var act = async () => await HtmlToPdf.RenderAsync(
            "<html><body><p>Invoice ใบแจ้งหนี้ #42</p></body></html>");
        await act.Should().NotThrowAsync();
    }
}
