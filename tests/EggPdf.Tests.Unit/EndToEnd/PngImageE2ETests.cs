using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class PngImageE2ETests
{
    private const string RedPixelRgbPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

    private static readonly string SemiTransparentBluePngBase64 = CreateOnePixelRgbaPngBase64(0, 0, 255, 128);

    [Fact]
    public async Task PngImage_RgbDataUri_RendersInPdf()
    {
        var html = $"<img src='data:image/png;base64,{RedPixelRgbPngBase64}' width='100' height='100'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("/Subtype /Image", "the PNG is embedded as an image XObject");
        text.Should().Contain(" Do", "the image XObject is drawn");
    }

    [Fact]
    public async Task PngImage_RgbaWithAlpha_RendersWithSMask()
    {
        var html = $"<img src='data:image/png;base64,{SemiTransparentBluePngBase64}' width='80' height='80'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("/SMask", "the PNG's alpha channel becomes a soft mask");
        text.Should().Contain(" Do", "the image is drawn");
    }

    [Fact]
    public async Task PngImage_AlongsideText_BothPresent()
    {
        var html = $@"
            <h1>Product Photo</h1>
            <img src='data:image/png;base64,{RedPixelRgbPngBase64}' width='200' height='150'>
            <p>Description of the product</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Product Photo");
        text.Should().Contain("Description of the product");
        text.Should().Contain(" Do", "the image between the texts is drawn");
    }

    [Fact]
    public async Task PngImage_MultipleImages_AllRendered()
    {
        var html = $@"
            <div>
                <img src='data:image/png;base64,{RedPixelRgbPngBase64}' width='50' height='50'>
                <img src='data:image/png;base64,{RedPixelRgbPngBase64}' width='50' height='50'>
                <img src='data:image/png;base64,{RedPixelRgbPngBase64}' width='50' height='50'>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        PdfAssert.Count(text, " Do").Should().Be(3, "each of the three <img> elements is drawn");
    }

    [Fact]
    public async Task PngImage_OneBitGrayscale_RendersInPdf()
    {
        var html = $"<img src='data:image/png;base64,{CreateOneBitGrayscalePngBase64()}' width='100' height='100'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("/Subtype /Image",
            "QR code generators commonly emit 1-bit grayscale PNGs (color type 0, bit depth 1), which must decode and embed");
        text.Should().Contain(" Do");
    }

    [Fact]
    public async Task PngImage_DisplayBlock_RendersInPdf()
    {
        var html = "<style>.fr { display:inline-block; padding:8px; } .fr img { display:block; width:100px; height:100px; }</style>" +
                   $"<div class='fr'><img src='data:image/png;base64,{RedPixelRgbPngBase64}' alt='qr'></div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("/Subtype /Image",
            "display:block on an <img> (common in CSS resets and QR frames) must not route it into the generic block " +
            "path, where its image source would be lost");
        text.Should().Contain(" Do");
    }

    [Fact]
    public async Task PngImage_InvalidBase64_IsSkippedAndSurroundingTextRenders()
    {
        var html = "<p>Before</p><img src='data:image/png;base64,NOT_VALID_PNG_DATA' width='50' height='50'><p>After</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "(Before) Tj", "(After) Tj");

        text.Should().NotContain("/Subtype /Image", "undecodable image data must not be embedded");
    }

    [Fact]
    public async Task PngImage_InTable_RendersCorrectly()
    {
        var html = $@"
            <table>
                <tr>
                    <td><img src='data:image/png;base64,{RedPixelRgbPngBase64}' width='40' height='40'></td>
                    <td>Product Name</td>
                    <td>$29.99</td>
                </tr>
            </table>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Product Name");
        text.Should().Contain("$29.99");
        text.Should().Contain(" Do", "the image in the first cell is drawn");
    }

    private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    private const byte PngFilterNone = 0;
    private const byte PngColorTypeGrayscale = 0;
    private const byte PngColorTypeRgba = 6;

    /// <summary>Create a 1x1 8-bit RGBA PNG as base64.</summary>
    private static string CreateOnePixelRgbaPngBase64(byte r, byte g, byte b, byte a)
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        bw.Write(PngSignature);
        WriteChunk(bw, "IHDR", ImageHeader(width: 1, height: 1, bitDepth: 8, PngColorTypeRgba));
        WriteChunk(bw, "IDAT", ZlibCompress(new byte[] { PngFilterNone, r, g, b, a }));
        WriteChunk(bw, "IEND", Array.Empty<byte>());

        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>Create an 8x8 checkerboard 1-bit grayscale PNG (color type 0, bit depth 1) as base64.</summary>
    private static string CreateOneBitGrayscalePngBase64()
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        bw.Write(PngSignature);
        WriteChunk(bw, "IHDR", ImageHeader(width: 8, height: 8, bitDepth: 1, PngColorTypeGrayscale));

        var scanlines = new byte[16];
        for (int y = 0; y < 8; y++)
        {
            scanlines[y * 2] = PngFilterNone;
            scanlines[y * 2 + 1] = (byte)(y % 2 == 0 ? 0xAA : 0x55);
        }

        WriteChunk(bw, "IDAT", ZlibCompress(scanlines));
        WriteChunk(bw, "IEND", Array.Empty<byte>());

        return Convert.ToBase64String(ms.ToArray());
    }

    private static byte[] ImageHeader(int width, int height, byte bitDepth, byte colorType)
    {
        var header = new byte[13];
        Array.Copy(ToBigEndian(width), 0, header, 0, 4);
        Array.Copy(ToBigEndian(height), 0, header, 4, 4);
        header[8] = bitDepth;
        header[9] = colorType;
        return header;
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var cms = new MemoryStream();
        cms.WriteByte(0x78);
        cms.WriteByte(0x01);
        using (var deflate = new DeflateStream(cms, CompressionLevel.Fastest, true))
        {
            deflate.Write(data, 0, data.Length);
        }
        cms.Write(ToBigEndian((int)Adler32(data)), 0, 4);
        return cms.ToArray();
    }

    private static void WriteChunk(BinaryWriter bw, string type, byte[] data)
    {
        bw.Write(ToBigEndian(data.Length));
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        bw.Write(typeBytes);
        bw.Write(data);
        byte[] crcInput = new byte[4 + data.Length];
        Array.Copy(typeBytes, 0, crcInput, 0, 4);
        Array.Copy(data, 0, crcInput, 4, data.Length);
        bw.Write(ToBigEndian((int)Crc32(crcInput)));
    }

    private static byte[] ToBigEndian(int value)
    {
        return new byte[] {
            (byte)(value >> 24), (byte)(value >> 16),
            (byte)(value >> 8), (byte)value
        };
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < data.Length; i++)
        {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
                crc = (crc >> 1) ^ (0xEDB88320 & ~((crc & 1) - 1));
        }
        return crc ^ 0xFFFFFFFF;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        for (int i = 0; i < data.Length; i++)
        {
            a = (a + data[i]) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }
}
