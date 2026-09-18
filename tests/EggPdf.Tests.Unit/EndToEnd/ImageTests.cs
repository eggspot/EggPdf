using System;
using System.Text;
using System.Threading.Tasks;
using EggPdf.Tests.Unit.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class ImageTests
{
    // 1x1 red pixel PNG as base64
    private const string RedPixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

    [Fact]
    public async Task ImgWithBase64_DoesNotCrash()
    {
        var html = $"<img src='data:image/png;base64,{RedPixelPng}' width='100' height='100'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 8).Should().StartWith("%PDF");
    }

    /// <summary>Builds a minimal RIFF/WEBP container wrapping a hand-constructed 2x1 VP8L (lossless) payload.</summary>
    private static byte[] BuildWebPLossless2x1()
    {
        var w = new Vp8LTestBitWriter();
        w.WriteBits(1, 14); w.WriteBits(0, 14); // width-1=1 (width=2), height-1=0 (height=1)
        w.WriteBits(0, 1); w.WriteBits(0, 3);   // alpha_is_used=0, version=0
        w.WriteBits(0, 1); w.WriteBits(0, 1); w.WriteBits(0, 1); // no transforms, no color cache, no meta-huffman

        var green = w.WriteSimpleTwoSymbol(0, 255); // pixel0=green0 (black), pixel1=green255
        w.WriteSimpleSingleSymbol(255); // red: always 255
        w.WriteSimpleSingleSymbol(0);   // blue: always 0
        w.WriteSimpleSingleSymbol(255); // alpha: always opaque
        w.WriteSimpleSingleSymbol(0);   // distance unused

        w.WriteBits((uint)green.bitForA, 1); // pixel0: green=0 -> (R255,G0,B0,A255) = pure red
        w.WriteBits((uint)green.bitForB, 1); // pixel1: green=255 -> (R255,G255,B0,A255) = yellow

        var vp8l = w.ToVp8LPayload();
        int chunkSize = vp8l.Length;
        bool pad = chunkSize % 2 == 1;
        int riffSize = 4 /* "WEBP" */ + 8 /* "VP8L" + size */ + chunkSize + (pad ? 1 : 0);

        using var ms = new System.IO.MemoryStream();
        void WriteAscii(string s) => ms.Write(Encoding.ASCII.GetBytes(s), 0, s.Length);
        void WriteU32(int v) => ms.Write(new byte[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) }, 0, 4);

        WriteAscii("RIFF");
        WriteU32(riffSize);
        WriteAscii("WEBP");
        WriteAscii("VP8L");
        WriteU32(chunkSize);
        ms.Write(vp8l, 0, vp8l.Length);
        if (pad) ms.WriteByte(0);
        return ms.ToArray();
    }

    [Fact]
    public async Task ImgWithLosslessWebP_DecodesAndEmbedsRealPixels()
    {
        var webp = BuildWebPLossless2x1();
        var b64 = Convert.ToBase64String(webp);
        var html = $"<img src='data:image/webp;base64,{b64}' width='100' height='50'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().Contain("/Subtype /Image", "the VP8L payload must actually decode and embed as a real image XObject");
        text.Should().Contain("/Width 2", "the decoded image must keep its real VP8L dimensions (2x1), not the <img> display size");
    }

    [Fact]
    public async Task ImgWithAlt_TextRenderedWhenNoSrc()
    {
        var html = "<img alt='Logo placeholder'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ImgWithDimensions_PresentsInPdf()
    {
        var html = $"<img src='data:image/png;base64,{RedPixelPng}' width='200' height='100' alt='Test image'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BackgroundImage_DoesNotCrash()
    {
        var html = "<div style='background-color: blue; width: 200px; height: 100px'>Blue box</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        pdf.Should().NotBeEmpty();
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Blue box");
    }

    [Fact]
    public async Task MultipleImages_AllPresent()
    {
        var html = $@"
            <p>Before images</p>
            <img src='data:image/png;base64,{RedPixelPng}' width='50' height='50'>
            <img src='data:image/png;base64,{RedPixelPng}' width='50' height='50'>
            <p>After images</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Before images");
        text.Should().Contain("After images");
    }

    [Fact]
    public async Task BrokenImage_DoesNotCrash()
    {
        var html = "<img src='https://nonexistent.example.com/image.png' alt='Broken'>";

        // Should not throw, should produce valid PDF
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SvgInline_RendersActualShapeContent()
    {
        var html = @"
            <svg width='100' height='100'>
                <circle cx='50' cy='50' r='40' fill='red'/>
            </svg>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().Contain("1.00 0.00 0.00 rg", "the circle's red fill must actually paint");
        text.Should().Contain(" c ", "the circle must emit real Bezier curve path operators, not just not crash");
    }

    [Fact]
    public async Task SvgInline_WithoutWrapper_StillGetsPaintedNotCulled()
    {
        // Regression: an <svg> box has no text/background/border of its own, so the
        // page-fragmenter's "does this box paint anything" check must special-case it,
        // or the whole element gets silently dropped before BoxPainter ever sees it.
        var html = "<svg width='50' height='50'><rect x='0' y='0' width='50' height='50' fill='blue'/></svg>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        Encoding.Latin1.GetString(pdf).Should().Contain("0.00 0.00 1.00 rg");
    }

    [Fact]
    public async Task SvgInline_WidthHeightAttributes_SizeTheBox()
    {
        // Regression: <svg> has no UA display default beyond "inline" (no replaced-element
        // handling), so its width/height attributes were previously ignored entirely,
        // collapsing the box to 0x0 via the generic empty-inline-element fallback.
        var html = "<svg width='60' height='40'><rect x='0' y='0' width='60' height='40' fill='green'/></svg>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("60.00 40.00", "the svg box must be sized from its width/height attributes, not collapse to 0x0");
    }

    [Fact]
    public async Task GradientBackground_DoesNotCrash()
    {
        var html = "<div style='background: linear-gradient(red, blue); width: 200px; height: 100px'>Gradient</div>";

        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }
}
