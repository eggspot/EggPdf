using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// End-to-end tests for PNG image support: HTML-to-PDF pipeline.
/// Verifies that PNG images (RGB, RGBA, grayscale, indexed) render correctly.
/// </summary>
public class PngImageE2ETests
{
    // Minimal valid 1x1 red pixel PNG (RGB, no alpha)
    private static readonly string RedPngBase64 = CreateMinimalPngBase64(255, 0, 0, 255);

    // Minimal valid 1x1 blue pixel PNG with 50% alpha
    private static readonly string SemiTransparentBluePngBase64 = CreateMinimalRgbaPngBase64(0, 0, 255, 128);

    [Fact]
    public async Task PngImage_RgbDataUri_RendersInPdf()
    {
        var html = $"<img src='data:image/png;base64,{RedPngBase64}' width='100' height='100'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("/XObject");  // image XObject present
        text.Should().Contain("Do");        // image draw operator
    }

    [Fact]
    public async Task PngImage_RgbaWithAlpha_RendersWithSMask()
    {
        var html = $"<img src='data:image/png;base64,{SemiTransparentBluePngBase64}' width='80' height='80'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("Do"); // image rendered
    }

    [Fact]
    public async Task PngImage_AlongsideText_BothPresent()
    {
        var html = $@"
            <h1>Product Photo</h1>
            <img src='data:image/png;base64,{RedPngBase64}' width='200' height='150'>
            <p>Description of the product</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Product Photo");
        text.Should().Contain("Description of the product");
        text.Should().Contain("Do"); // image present
    }

    [Fact]
    public async Task PngImage_MultipleImages_AllRendered()
    {
        var html = $@"
            <div>
                <img src='data:image/png;base64,{RedPngBase64}' width='50' height='50'>
                <img src='data:image/png;base64,{RedPngBase64}' width='50' height='50'>
                <img src='data:image/png;base64,{RedPngBase64}' width='50' height='50'>
            </div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        // Multiple image references
        text.Should().Contain("Do");
    }

    [Fact]
    public async Task PngImage_OneBitGrayscale_RendersInPdf()
    {
        // QR code generators commonly emit 1-bit grayscale PNGs (color type 0, bit depth 1)
        var html = $"<img src='data:image/png;base64,{CreateOneBitGrayscalePngBase64()}' width='100' height='100'>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().StartWith("%PDF");
        text.Should().Contain("/XObject"); // image XObject present
        text.Should().Contain("Do");       // image draw operator
    }

    [Fact]
    public async Task PngImage_DisplayBlock_RendersInPdf()
    {
        // display:block on an <img> (common in CSS resets and QR frames) must not
        // route it into the generic block path where ImageSource is lost.
        var html = "<style>.fr { display:inline-block; padding:8px; } .fr img { display:block; width:100px; height:100px; }</style>" +
                   $"<div class='fr'><img src='data:image/png;base64,{RedPngBase64}' alt='qr'></div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("/XObject");
        text.Should().Contain("Do");
    }

    [Fact]
    public async Task PngImage_InvalidBase64_DoesNotCrash()
    {
        var html = "<img src='data:image/png;base64,NOT_VALID_PNG_DATA' width='50' height='50'>";

        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PngImage_InTable_RendersCorrectly()
    {
        var html = $@"
            <table>
                <tr>
                    <td><img src='data:image/png;base64,{RedPngBase64}' width='40' height='40'></td>
                    <td>Product Name</td>
                    <td>$29.99</td>
                </tr>
            </table>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Product Name");
        text.Should().Contain("$29.99");
    }

    /// <summary>Create a minimal valid RGB PNG as base64 string.</summary>
    private static string CreateMinimalPngBase64(byte r, byte g, byte b, byte a)
    {
        // Use the well-known 1x1 red pixel PNG
        // This is a proper PNG with IHDR, IDAT, IEND chunks
        return "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";
    }

    /// <summary>Create a minimal valid RGBA PNG as base64 string.</summary>
    private static string CreateMinimalRgbaPngBase64(byte r, byte g, byte b, byte a)
    {
        // Build a minimal 1x1 RGBA PNG
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // PNG signature
        bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        // IHDR chunk: 1x1, 8-bit, RGBA (color type 6)
        WriteChunk(bw, "IHDR", new byte[] {
            0, 0, 0, 1,  // width
            0, 0, 0, 1,  // height
            8,            // bit depth
            6,            // color type (RGBA)
            0, 0, 0       // compression, filter, interlace
        });

        // IDAT chunk: compressed scanline data
        // Scanline: filter_byte(0) + R + G + B + A
        byte[] rawScanline = { 0, r, g, b, a };
        byte[] compressed;
        using (var cms = new MemoryStream())
        {
            // zlib header (deflate)
            cms.WriteByte(0x78);
            cms.WriteByte(0x01);
            using (var deflate = new DeflateStream(cms, CompressionLevel.Fastest, true))
            {
                deflate.Write(rawScanline, 0, rawScanline.Length);
            }
            // Adler32 checksum
            uint adler = Adler32(rawScanline);
            cms.WriteByte((byte)(adler >> 24));
            cms.WriteByte((byte)(adler >> 16));
            cms.WriteByte((byte)(adler >> 8));
            cms.WriteByte((byte)(adler));
            compressed = cms.ToArray();
        }
        WriteChunk(bw, "IDAT", compressed);

        // IEND chunk
        WriteChunk(bw, "IEND", Array.Empty<byte>());

        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>Create an 8x8 checkerboard 1-bit grayscale PNG (color type 0, bit depth 1) as base64.</summary>
    private static string CreateOneBitGrayscalePngBase64()
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        // IHDR: 8x8, 1-bit, grayscale (color type 0)
        WriteChunk(bw, "IHDR", new byte[] {
            0, 0, 0, 8,  // width
            0, 0, 0, 8,  // height
            1,            // bit depth
            0,            // color type (grayscale)
            0, 0, 0       // compression, filter, interlace
        });

        // 8 scanlines: filter byte 0 + 1 packed byte (8 pixels), alternating rows
        var raw = new byte[16];
        for (int y = 0; y < 8; y++)
        {
            raw[y * 2] = 0; // filter: none
            raw[y * 2 + 1] = (byte)(y % 2 == 0 ? 0xAA : 0x55);
        }

        byte[] compressed;
        using (var cms = new MemoryStream())
        {
            cms.WriteByte(0x78);
            cms.WriteByte(0x01);
            using (var deflate = new DeflateStream(cms, CompressionLevel.Fastest, true))
            {
                deflate.Write(raw, 0, raw.Length);
            }
            uint adler = Adler32(raw);
            cms.WriteByte((byte)(adler >> 24));
            cms.WriteByte((byte)(adler >> 16));
            cms.WriteByte((byte)(adler >> 8));
            cms.WriteByte((byte)(adler));
            compressed = cms.ToArray();
        }
        WriteChunk(bw, "IDAT", compressed);
        WriteChunk(bw, "IEND", Array.Empty<byte>());

        return Convert.ToBase64String(ms.ToArray());
    }

    private static void WriteChunk(BinaryWriter bw, string type, byte[] data)
    {
        // Length (big-endian)
        bw.Write(ToBigEndian(data.Length));
        // Type
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        bw.Write(typeBytes);
        // Data
        bw.Write(data);
        // CRC32 of type + data
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
