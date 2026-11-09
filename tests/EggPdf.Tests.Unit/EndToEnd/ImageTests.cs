using System;
using System.Text;
using System.Threading.Tasks;
using EggPdf.Tests.Unit.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class ImageTests
{
    private const string RedPixelPng ="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

    [Fact]
    public async Task ImgWithBase64_EmbedsImageDrawnAtRequestedSize()
    {
        var html = $"<img src='data:image/png;base64,{RedPixelPng}' width='100' height='100'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("/Subtype /Image");
        text.Should().Contain("75.00 0 0 75.00 6.00 760.89 cm", "100x100px image drawn as a 75pt square inside the body's 8px margin");
    }

    private static byte[] BuildWebPLossless2x1RedThenYellow()
    {
        const uint widthMinusOne = 1, heightMinusOne = 0;
        const uint alphaIsUsed = 0, version = 0;
        const uint hasTransform = 0, hasColorCache = 0, hasMetaHuffmanCodes = 0;
        const int greenOfRedPixel = 0, greenOfYellowPixel = 255;
        const int red = 255, blue = 0, opaque = 255, unusedDistance = 0;
        const int webpTagLength = 4, chunkHeaderLength = 8;

        var w = new Vp8LTestBitWriter();
        w.WriteBits(widthMinusOne, 14);
        w.WriteBits(heightMinusOne, 14);
        w.WriteBits(alphaIsUsed, 1);
        w.WriteBits(version, 3);
        w.WriteBits(hasTransform, 1);
        w.WriteBits(hasColorCache, 1);
        w.WriteBits(hasMetaHuffmanCodes, 1);

        var green = w.WriteSimpleTwoSymbol(greenOfRedPixel, greenOfYellowPixel);
        w.WriteSimpleSingleSymbol(red);
        w.WriteSimpleSingleSymbol(blue);
        w.WriteSimpleSingleSymbol(opaque);
        w.WriteSimpleSingleSymbol(unusedDistance);

        w.WriteBits((uint)green.bitForA, 1);
        w.WriteBits((uint)green.bitForB, 1);

        var vp8l = w.ToVp8LPayload();
        int chunkSize = vp8l.Length;
        bool pad = chunkSize % 2 == 1;
        int riffSize = webpTagLength + chunkHeaderLength + chunkSize + (pad ? 1 : 0);

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
        var webp = BuildWebPLossless2x1RedThenYellow();
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
    public async Task BrokenImage_UnreachableUrl_IsSkippedAndSurroundingTextRenders()
    {
        var html = "<p>Before</p><img src='https://nonexistent.example.com/image.png' alt='Broken'><p>After</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "(Before) Tj", "(After) Tj");
        text.Should().NotContain("/Subtype /Image", "an unreachable image is left out without failing the render");
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
        var html = "<svg width='50' height='50'><rect x='0' y='0' width='50' height='50' fill='blue'/></svg>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        Encoding.Latin1.GetString(pdf).Should().Contain("0.00 0.00 1.00 rg",
            "an svg box has no text, background or border of its own, yet the fragmenter must still keep it");
    }

    [Fact]
    public async Task SvgInline_WidthHeightAttributes_SizeTheBox()
    {
        var html = "<svg width='60' height='40'><rect x='0' y='0' width='60' height='40' fill='green'/></svg>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("60.00 40.00", "the svg box must be sized from its width/height attributes, not collapse to 0x0");
    }

    [Fact]
    public async Task GradientBackground_PaintsRedToBlueBandsClippedToBox()
    {
        var html = "<div style='background: linear-gradient(red, blue); width: 200px; height: 100px'>Gradient</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Gradient");

        text.Should().Contain("6.00 760.89 150.00 75.00 re W n", "the gradient is clipped to the 200x100px box");
        text.Should().Contain("1.00 0.00 0.00 rg", "red start");
        text.Should().Contain("0.00 0.00 1.00 rg", "blue end");
    }
}
