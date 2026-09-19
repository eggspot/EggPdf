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
