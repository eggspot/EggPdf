using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class InterlacedPngTests
{
    private static readonly int[] XStart = { 0, 4, 0, 2, 0, 1, 0 };
    private static readonly int[] YStart = { 0, 0, 4, 0, 2, 0, 1 };
    private static readonly int[] XStep = { 8, 8, 4, 4, 2, 2, 1 };
    private static readonly int[] YStep = { 8, 8, 8, 4, 4, 2, 2 };

    private static byte[] Rgb(int x, int y) => new[] { (byte)(x * 20), (byte)(y * 20), (byte)(x * 7 + y * 11) };

    private static byte[] EncodeAdam7(int width, int height, int bitDepth, int colorType, Func<int, int, byte[]> rgbPixel, Func<int, int, int> grayBit)
    {
        using var raw = new MemoryStream();
        for (int pass = 0; pass < 7; pass++)
        {
            for (int y = YStart[pass]; y < height; y += YStep[pass])
            {
                var row = new MemoryStream();
                int bitBuffer = 0, bitCount = 0, pixelsInRow = 0;
                for (int x = XStart[pass]; x < width; x += XStep[pass])
                {
                    pixelsInRow++;
                    if (bitDepth == 8)
                    {
                        row.Write(rgbPixel(x, y));
                    }
                    else
                    {
                        bitBuffer = (bitBuffer << 1) | grayBit(x, y);
                        if (++bitCount == 8) { row.WriteByte((byte)bitBuffer); bitBuffer = 0; bitCount = 0; }
                    }
                }
                if (pixelsInRow == 0) break;
                if (bitCount > 0) row.WriteByte((byte)(bitBuffer << (8 - bitCount)));
                raw.WriteByte(0);
                row.WriteTo(raw);
            }
        }

        var header = new byte[13];
        WriteInt32BE(header, 0, width);
        WriteInt32BE(header, 4, height);
        header[8] = (byte)bitDepth;
        header[9] = (byte)colorType;
        header[12] = 1;

        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", Zlib(raw.ToArray()));
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    [Fact]
    public void Decode_Adam7RgbImage_PlacesEveryPassPixelCorrectly()
    {
        const int width = 9, height = 10;
        var png = EncodeAdam7(width, height, 8, 2, Rgb, (_, _) => 0);

        var image = PdfImage.FromPng("Img", png);

        image.Should().NotBeNull();
        var decoded = PngDecoder.Decode(png)!;
        decoded.Width.Should().Be(width);
        decoded.Height.Should().Be(height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 3;
            new[] { decoded.PixelData[i], decoded.PixelData[i + 1], decoded.PixelData[i + 2] }
                .Should().Equal(Rgb(x, y), $"pixel ({x},{y})");
        }
    }

    [Fact]
    public void Decode_Adam7OneBitGrayscale_PlacesSubBytePixelsCorrectly()
    {
        const int width = 11, height = 9;
        Func<int, int, int> checker = (x, y) => (x + y) % 2;
        var png = EncodeAdam7(width, height, 1, 0, (_, _) => Array.Empty<byte>(), checker);

        var decoded = PngDecoder.Decode(png)!;

        decoded.Should().NotBeNull();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte expected = checker(x, y) == 1 ? (byte)255 : (byte)0;
            decoded.PixelData[(y * width + x) * 3].Should().Be(expected, $"pixel ({x},{y})");
        }
    }

    [Fact]
    public void Decode_Adam7TruncatedData_ReturnsNull()
    {
        var png = EncodeAdam7(9, 10, 8, 2, Rgb, (_, _) => 0);
        var header = new byte[13];
        Array.Copy(png, 16, header, 0, 13);
        using var truncated = new MemoryStream();
        truncated.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteChunk(truncated, "IHDR", header);
        WriteChunk(truncated, "IDAT", Zlib(new byte[] { 0, 1, 2, 3 }));
        WriteChunk(truncated, "IEND", Array.Empty<byte>());

        PngDecoder.Decode(truncated.ToArray()).Should().BeNull();
    }

    [Fact]
    public void Decode_Adam7HugeClaimedSizeWithTinyData_ReturnsNullWithoutAllocatingTheImage()
    {
        var header = new byte[13];
        WriteInt32BE(header, 0, 20000);
        WriteInt32BE(header, 4, 20000);
        header[8] = 8;
        header[9] = 2;
        header[12] = 1;
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", Zlib(new byte[64]));
        WriteChunk(png, "IEND", Array.Empty<byte>());
        var bytes = png.ToArray();

        long before = GC.GetAllocatedBytesForCurrentThread();
        var decoded = PngDecoder.Decode(bytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        decoded.Should().BeNull();
        allocated.Should().BeLessThan(10 * 1024 * 1024, "a 1.2 GB image buffer must not be allocated before the data is checked");
    }

    [Fact]
    public void Render_InterlacedPngImg_IsEmbeddedAndDrawn()
    {
        var png = EncodeAdam7(9, 10, 8, 2, Rgb, (_, _) => 0);

        var pdf = HtmlToPdf.Render($"<img src='data:image/png;base64,{Convert.ToBase64String(png)}' width='18' height='20'>");

        var text = PdfAssert.ValidPdf(pdf);
        text.Should().Contain("/Subtype /Image");
        PdfAssert.EveryDrawnXObjectIsDefined(text).Should().Be(1);
    }

    private static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static void WriteInt32BE(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteInt32BE(length, 0, data.Length);
        stream.Write(length);
        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type).CopyTo(typeAndData, 0);
        data.CopyTo(typeAndData, 4);
        stream.Write(typeAndData);
        var crc = new byte[4];
        WriteInt32BE(crc, 0, (int)Crc32(typeAndData));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
