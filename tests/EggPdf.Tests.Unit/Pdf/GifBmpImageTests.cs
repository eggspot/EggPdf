using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class GifBmpImageTests
{
    private const byte GifImageSeparator = 0x2C;
    private const byte GifTrailer = 0x3B;
    private const byte GifExtensionIntroducer = 0x21;
    private const byte GifGraphicControlLabel = 0xF9;
    private const int BmpFileHeaderSize = 14;
    private const int BmpInfoHeaderSize = 40;
    private const int BmpBitfields = 3;

    [Fact]
    public void Gif_ValidSignature_Decoded()
    {
        byte[] gifData = CreateGif2x2RedGreenBlueYellow();

        var image = PdfImage.FromGif("test", gifData);

        image.Should().NotBeNull();
        image!.Width.Should().Be(2);
        image.Height.Should().Be(2);
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.Data.Should().Equal(
            255, 0, 0,
            0, 255, 0,
            0, 0, 255,
            255, 255, 0);
    }

    [Fact]
    public void Gif_InvalidData_ReturnsNull()
    {
        var image = PdfImage.FromGif("test", new byte[] { 0x00, 0x01, 0x02, 0x03 });
        image.Should().BeNull();
    }

    [Fact]
    public void Gif_NullData_ReturnsNull()
    {
        var image = PdfImage.FromGif("test", null!);
        image.Should().BeNull();
    }

    [Fact]
    public void Gif_SignatureOnly_ReturnsNull()
    {
        var image = PdfImage.FromGif("test", Encoding.ASCII.GetBytes("GIF89a"));
        image.Should().BeNull();
    }

    [Fact]
    public void Gif_Transparency_HasAlpha()
    {
        byte[] gifData = CreateGif2x1RedThenTransparent();

        var image = PdfImage.FromGif("test", gifData);

        image.Should().NotBeNull();
        image!.Width.Should().Be(2);
        image.Height.Should().Be(1);
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.SMaskData.Should().NotBeNull();
        image.SMaskData!.Length.Should().Be(2);
        image.Data[0].Should().Be(255);
        image.Data[1].Should().Be(0);
        image.Data[2].Should().Be(0);
        image.SMaskData[0].Should().Be(255);
        image.SMaskData[1].Should().Be(0);
    }

    private const string GdiPlusGrid64x64Gif =
        "R0lGODlhQABAAPcAAAAAAAAAMwAAZgAAmQAAzAAA/wArAAArMwArZgArmQArzAAr/wBVAABVMwBVZgBVmQBVzABV/wCAAACAMwCAZgCAmQCAzACA/wCqAACq" +
        "MwCqZgCqmQCqzACq/wDVAADVMwDVZgDVmQDVzADV/wD/AAD/MwD/ZgD/mQD/zAD//zMAADMAMzMAZjMAmTMAzDMA/zMrADMrMzMrZjMrmTMrzDMr/zNVADNV" +
        "MzNVZjNVmTNVzDNV/zOAADOAMzOAZjOAmTOAzDOA/zOqADOqMzOqZjOqmTOqzDOq/zPVADPVMzPVZjPVmTPVzDPV/zP/ADP/MzP/ZjP/mTP/zDP//2YAAGYA" +
        "M2YAZmYAmWYAzGYA/2YrAGYrM2YrZmYrmWYrzGYr/2ZVAGZVM2ZVZmZVmWZVzGZV/2aAAGaAM2aAZmaAmWaAzGaA/2aqAGaqM2aqZmaqmWaqzGaq/2bVAGbV" +
        "M2bVZmbVmWbVzGbV/2b/AGb/M2b/Zmb/mWb/zGb//5kAAJkAM5kAZpkAmZkAzJkA/5krAJkrM5krZpkrmZkrzJkr/5lVAJlVM5lVZplVmZlVzJlV/5mAAJmA" +
        "M5mAZpmAmZmAzJmA/5mqAJmqM5mqZpmqmZmqzJmq/5nVAJnVM5nVZpnVmZnVzJnV/5n/AJn/M5n/Zpn/mZn/zJn//8wAAMwAM8wAZswAmcwAzMwA/8wrAMwr" +
        "M8wrZswrmcwrzMwr/8xVAMxVM8xVZsxVmcxVzMxV/8yAAMyAM8yAZsyAmcyAzMyA/8yqAMyqM8yqZsyqmcyqzMyq/8zVAMzVM8zVZszVmczVzMzV/8z/AMz/" +
        "M8z/Zsz/mcz/zMz///8AAP8AM/8AZv8Amf8AzP8A//8rAP8rM/8rZv8rmf8rzP8r//9VAP9VM/9VZv9Vmf9VzP9V//+AAP+AM/+AZv+Amf+AzP+A//+qAP+q" +
        "M/+qZv+qmf+qzP+q///VAP/VM//VZv/Vmf/VzP/V////AP//M///Zv//mf//zP///wAAAAAAAAAAAAAAACH5BAEAAPwALAAAAABAAEAAAAj/AAE8oZMJVzeB" +
        "BA0iLHhwIMOFCh1GTNiQIsSKDx3eA/GDTCJUGzs+AsmRzMiQJkmKVJkS5cmSL1e6ZImsHYQdVEYJa2ehRs6dPX/y9KlzqNCgRZECJbr0KNOhGS1KxDgxqtWq" +
        "WKlqBeCRZJGuG79+DAsWhFivZc+SHWs2rduxRJGdgxB3bl26OeXiHaX3rt+8dgHv7ZtXoJtMvhoeTmwYsWLHjRk/WfxYMuXIlTGuRctWbdvObzmL3ky6ZM3B" +
        "NuOmzruab+vTqlHLjv20Z9RJE3FX1I2Q90DfdIALz52V61gNRdQcT75cOUjkzu9Bb079OXPr0T+DpM16dnfurr2H/wdPmO/F81PRS11/datDyQRRsu04vyx9" +
        "lvXz498vn2XTpLUF6NSAABL4H2xOtaeeguk1yB5x45wXWmmeVTihdqRNN9pffAXW4WAelidiiCG+FuJClkE2mYqXrZjii5mh12J8JYFm42gY5mihfqV1hOB4" +
        "3wUJ5JA/FmnUkEvdRtxuS/bW5G9PBldcWtFpKN11V1aJpZVW6pidWhwaKZ6Y5M2F04dhLlicgwxKNaNV8GUyU3901sifnXV6JhOeJf3n55F/KgXokT8e2FqS" +
        "az7onqJOLopbhOlduCOOk4b1JVhdgilYmCSC6CmnY57oIkYwYmbqqAvGGdGbPUrqan0x2f93oY+v1TqmrQISSeihUEHY6K9QMukoLpBKKayE1WWZLJdYYpgp" +
        "lTfeQx6uQhZZoqdnjjiYmou2qaKb38KpGZ/31VkuubJGC9OdHQWqmqDwTotUobvuheiw3foaLLDrPYospa9ip26zaj0L16aCZdtpmTchjGa1ohJUKqoUg6sq" +
        "qe6xymelOZ6rZ0voqkvrrbfOy6vJqKEcoJLH7uuyser5+zJwD0ErsFeXdpbzzRTuLK3D1mIL9LV3KfxpYWyuymK4VV18KntxzrnxrCCf67GssdbnbrUnywue" +
        "ofb2ii+/ShY73LDF1jywlj4bjHO0bv/88NxBM2x0mCZuW7G3Tu9d3XTG4baaNbtUD56nrPTGy7XKBBrJOIDe6gvzy2XzS7NCAStLadsE20waqHYPHWrDdEe8" +
        "dN9v/i0j0xgvdDiPHVedLo57Wk0Gmaw9Dva7X9e7cpRnx0ys5YnKbFFAADs=";

    [Fact]
    public void Gif_EncodedByGdiPlus_DecodesEveryPixel()
    {
        const int Size = 64;
        const uint GdiPlusGreenChannelFnv1a = 3482593537;
        const int GdiPlusGreenChannelSum = 517798;

        var result = GifDecoder.Decode(Convert.FromBase64String(GdiPlusGrid64x64Gif));

        result.Should().NotBeNull();
        result!.Width.Should().Be(Size);
        result.Height.Should().Be(Size);
        int stride = result.HasAlpha ? 4 : 3;
        uint greenHash = 2166136261;
        int greenSum = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int at = (y * Size + x) * stride;
                result.PixelData[at].Should().Be((byte)((x * 37 + y * 11) % 6 * 51), $"red at ({x},{y})");
                result.PixelData[at + 2].Should().Be((byte)((x * 13 + y * 7) % 6 * 51), $"blue at ({x},{y})");
                if (result.HasAlpha) result.PixelData[at + 3].Should().Be(255);
                byte green = result.PixelData[at + 1];
                greenSum += green;
                greenHash = unchecked((greenHash ^ green) * 16777619);
            }
        }
        greenSum.Should().Be(GdiPlusGreenChannelSum);
        greenHash.Should().Be(GdiPlusGreenChannelFnv1a, "the green channel is dithered by the encoder; GDI+ decodes it to exactly these bytes");
    }

    [Fact]
    public void Gif_FrameSmallerThanScreen_ComposedAtItsOffsetOnTransparentCanvas()
    {
        using var ms = new MemoryStream();
        WriteGifHeader(ms, screenWidth: 4, screenHeight: 3, globalColorTable: new byte[] { 255, 0, 0, 0, 255, 0 });
        WriteGifImage(ms, left: 1, top: 1, width: 2, height: 1, indices: new byte[] { 0, 1 });
        ms.WriteByte(GifTrailer);

        var result = GifDecoder.Decode(ms.ToArray());

        result.Should().NotBeNull();
        result!.Width.Should().Be(4);
        result.Height.Should().Be(3);
        result.HasAlpha.Should().BeTrue();
        var expected = new byte[4 * 3 * 4];
        new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }.CopyTo(expected, (1 * 4 + 1) * 4);
        result.PixelData.Should().Equal(expected);
    }

    [Fact]
    public void Gif_FrameLargerThanScreen_CanvasGrowsToHoldTheFrame()
    {
        using var ms = new MemoryStream();
        WriteGifHeader(ms, screenWidth: 1, screenHeight: 1, globalColorTable: new byte[] { 255, 0, 0, 0, 255, 0 });
        WriteGifImage(ms, left: 0, top: 0, width: 2, height: 2, indices: new byte[] { 0, 1, 1, 0 });
        ms.WriteByte(GifTrailer);

        var result = GifDecoder.Decode(ms.ToArray());

        result.Should().NotBeNull();
        result!.Width.Should().Be(2);
        result.Height.Should().Be(2);
        result.HasAlpha.Should().BeFalse();
        result.PixelData.Should().Equal(255, 0, 0, 0, 255, 0, 0, 255, 0, 255, 0, 0);
    }

    [Fact]
    public void Gif_HeaderClaimingHugeFrame_ReturnsNullWithoutAllocatingIt()
    {
        using var ms = new MemoryStream();
        WriteGifHeader(ms, screenWidth: 40000, screenHeight: 40000, globalColorTable: new byte[] { 255, 0, 0, 0, 255, 0 });
        WriteGifImage(ms, left: 0, top: 0, width: 40000, height: 40000, indices: new byte[] { 0, 1 });
        ms.WriteByte(GifTrailer);
        var gif = ms.ToArray();

        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = GifDecoder.Decode(gif);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.Should().BeNull();
        allocated.Should().BeLessThan(1024 * 1024);
    }

    [Fact]
    public void Bmp_32BitAllAlphaBytesZero_RenderedOpaque()
    {
        var data = CreateBmpFile(width: 1, height: 1, bitDepth: 32, pixelBytes: 4);
        WriteBgra(data, BmpFileHeaderSize + BmpInfoHeaderSize, 10, 20, 30, 0);

        var image = PdfImage.FromBmp("test", data);

        image.Should().NotBeNull();
        image!.SMaskData.Should().BeNull("Chrome treats a 32-bit BMP whose alpha bytes are all zero as opaque");
        image.Data.Should().Equal(10, 20, 30);
    }

    [Fact]
    public void Bmp_32BitBitfields_ChannelsComeFromTheMasks()
    {
        var data = CreateBmp(BmpInfoHeaderSize, width: 1, height: 1, bitDepth: 32, compression: BmpBitfields,
            masks: new uint[] { 0x000000FF, 0x0000FF00, 0x00FF0000 },
            pixelRows: new byte[] { 10, 20, 30, 0 });

        var image = PdfImage.FromBmp("test", data);

        image.Should().NotBeNull();
        image!.SMaskData.Should().BeNull();
        image.Data.Should().Equal(10, 20, 30);
    }

    [Fact]
    public void Bmp_16Bit565Bitfields_Decoded()
    {
        var data = CreateBmp(BmpInfoHeaderSize, width: 2, height: 1, bitDepth: 16, compression: BmpBitfields,
            masks: new uint[] { 0xF800, 0x07E0, 0x001F },
            pixelRows: new byte[] { 0x00, 0xF8, 0xE0, 0x07 });

        var image = PdfImage.FromBmp("test", data);

        image.Should().NotBeNull();
        image!.Data.Should().Equal(255, 0, 0, 0, 255, 0);
    }

    [Fact]
    public void Bmp_16BitWithoutMasks_Uses555Layout()
    {
        var data = CreateBmp(BmpInfoHeaderSize, width: 2, height: 1, bitDepth: 16, compression: 0,
            masks: Array.Empty<uint>(),
            pixelRows: new byte[] { 0x00, 0x7C, 0x10, 0x02 });

        var image = PdfImage.FromBmp("test", data);

        image.Should().NotBeNull();
        image!.Data.Should().Equal(255, 0, 0, 0, 132, 132);
    }

    [Fact]
    public void Bmp_V5HeaderWithAlphaMask_KeepsAlpha()
    {
        const int V5HeaderSize = 124;
        var data = CreateBmp(V5HeaderSize, width: 1, height: 1, bitDepth: 32, compression: BmpBitfields,
            masks: new uint[] { 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000 },
            pixelRows: new byte[] { 30, 20, 10, 128 });

        var image = PdfImage.FromBmp("test", data);

        image.Should().NotBeNull();
        image!.Data.Should().Equal(10, 20, 30);
        image.SMaskData.Should().Equal(128);
    }

    [Fact]
    public void Bmp_HeaderClaimingHugeImage_ReturnsNullWithoutAllocatingIt()
    {
        var data = CreateBmpFile(width: 20000, height: 20000, bitDepth: 32, pixelBytes: 16);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = BmpDecoder.Decode(data);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.Should().BeNull();
        allocated.Should().BeLessThan(1024 * 1024);
    }

    [Fact]
    public void Bmp_24Bit_Decoded()
    {
        byte[] bmpData = CreateBmp24BottomUp2x2RedGreenOverBlueWhite();

        var image = PdfImage.FromBmp("test", bmpData);

        image.Should().NotBeNull();
        image!.Width.Should().Be(2);
        image.Height.Should().Be(2);
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.SMaskData.Should().BeNull();
        image.Data.Should().Equal(
            255, 0, 0,
            0, 255, 0,
            0, 0, 255,
            255, 255, 255);
    }

    [Fact]
    public void Bmp_32Bit_HasAlpha()
    {
        byte[] bmpData = CreateBmp32BottomUp2x2TopLeftRedAlpha128();

        var image = PdfImage.FromBmp("test", bmpData);

        image.Should().NotBeNull();
        image!.Width.Should().Be(2);
        image.Height.Should().Be(2);
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.SMaskData.Should().NotBeNull();
        image.SMaskData!.Length.Should().Be(4);
        image.Data[0].Should().Be(255);
        image.Data[1].Should().Be(0);
        image.Data[2].Should().Be(0);
        image.SMaskData[0].Should().Be(128);
    }

    [Fact]
    public void Bmp_InvalidData_ReturnsNull()
    {
        var image = PdfImage.FromBmp("test", new byte[] { 0x00, 0x01, 0x02, 0x03 });
        image.Should().BeNull();
    }

    [Fact]
    public void Bmp_NullData_ReturnsNull()
    {
        var image = PdfImage.FromBmp("test", null!);
        image.Should().BeNull();
    }

    [Fact]
    public void Bmp_BottomUp_FirstFileRowBecomesBottomRow()
    {
        byte[] bmpData = CreateBmp24BottomUp2x2RedGreenOverBlueWhite();

        var image = PdfImage.FromBmp("test", bmpData);

        image.Should().NotBeNull();
        image!.Data[0].Should().Be(255);
        image.Data[1].Should().Be(0);
        image.Data[2].Should().Be(0);
        image.Data[6].Should().Be(0);
        image.Data[7].Should().Be(0);
        image.Data[8].Should().Be(255);
    }

    [Fact]
    public async Task GifDataUri_RendersInPdf()
    {
        byte[] gifData = CreateGif2x2RedGreenBlueYellow();
        string base64 = Convert.ToBase64String(gifData);
        string html = $"<html><body><img src=\"data:image/gif;base64,{base64}\" width=\"50\" height=\"50\" /></body></html>";

        var pdfBytes = await HtmlToPdf.RenderAsync(html);

        pdfBytes.Should().NotBeNull();
        pdfBytes.Length.Should().BeGreaterThan(0);
        var text = Encoding.ASCII.GetString(pdfBytes);
        text.Should().Contain("%PDF");
    }

    [Fact]
    public async Task BmpDataUri_RendersInPdf()
    {
        byte[] bmpData = CreateBmp24BottomUp2x2RedGreenOverBlueWhite();
        string base64 = Convert.ToBase64String(bmpData);
        string html = $"<html><body><img src=\"data:image/bmp;base64,{base64}\" width=\"50\" height=\"50\" /></body></html>";

        var pdfBytes = await HtmlToPdf.RenderAsync(html);

        pdfBytes.Should().NotBeNull();
        pdfBytes.Length.Should().BeGreaterThan(0);
        var text = Encoding.ASCII.GetString(pdfBytes);
        text.Should().Contain("%PDF");
    }

    private static byte[] CreateGif2x2RedGreenBlueYellow()
    {
        using var ms = new MemoryStream();
        WriteGifHeader(ms, screenWidth: 2, screenHeight: 2, globalColorTable: new byte[]
        {
            255, 0, 0,
            0, 255, 0,
            0, 0, 255,
            255, 255, 0,
        });
        WriteGifImage(ms, left: 0, top: 0, width: 2, height: 2, indices: new byte[] { 0, 1, 2, 3 });
        ms.WriteByte(GifTrailer);
        return ms.ToArray();
    }

    private static byte[] CreateGif2x1RedThenTransparent()
    {
        using var ms = new MemoryStream();
        WriteGifHeader(ms, screenWidth: 2, screenHeight: 1, globalColorTable: new byte[]
        {
            255, 0, 0,
            0, 255, 0,
        });
        WriteGifTransparentIndex(ms, 1);
        WriteGifImage(ms, left: 0, top: 0, width: 2, height: 1, indices: new byte[] { 0, 1 });
        ms.WriteByte(GifTrailer);
        return ms.ToArray();
    }

    private static void WriteGifHeader(MemoryStream ms, int screenWidth, int screenHeight, byte[] globalColorTable)
    {
        int entries = globalColorTable.Length / 3;
        int sizeField = 0;
        while ((2 << sizeField) < entries) sizeField++;

        ms.Write(Encoding.ASCII.GetBytes("GIF89a"), 0, 6);
        WriteInt16LE(ms, screenWidth);
        WriteInt16LE(ms, screenHeight);
        ms.WriteByte((byte)(0x80 | sizeField));
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.Write(globalColorTable, 0, globalColorTable.Length);
        for (int i = entries; i < (2 << sizeField); i++)
            ms.Write(new byte[3], 0, 3);
    }

    private static void WriteGifTransparentIndex(MemoryStream ms, int transparentIndex)
    {
        const byte TransparentColorFlag = 0x01;
        ms.WriteByte(GifExtensionIntroducer);
        ms.WriteByte(GifGraphicControlLabel);
        ms.WriteByte(4);
        ms.WriteByte(TransparentColorFlag);
        WriteInt16LE(ms, 0);
        ms.WriteByte((byte)transparentIndex);
        ms.WriteByte(0);
    }

    private static void WriteGifImage(MemoryStream ms, int left, int top, int width, int height, byte[] indices)
    {
        const int MinCodeSize = 2;
        ms.WriteByte(GifImageSeparator);
        WriteInt16LE(ms, left);
        WriteInt16LE(ms, top);
        WriteInt16LE(ms, width);
        WriteInt16LE(ms, height);
        ms.WriteByte(0);
        ms.WriteByte(MinCodeSize);

        byte[] lzwData = LzwEncode(indices, MinCodeSize);
        for (int offset = 0; offset < lzwData.Length; offset += 255)
        {
            int blockSize = Math.Min(255, lzwData.Length - offset);
            ms.WriteByte((byte)blockSize);
            ms.Write(lzwData, offset, blockSize);
        }
        ms.WriteByte(0);
    }

    private static byte[] LzwEncode(byte[] data, int minCodeSize)
    {
        const int MaxCode = 4095;
        int clearCode = 1 << minCodeSize;
        int eoiCode = clearCode + 1;
        var bits = new List<bool>();
        int codeSize = minCodeSize + 1;
        int nextTableCode = eoiCode + 1;

        WriteBitsLsbFirst(bits, clearCode, codeSize);
        for (int i = 0; i < data.Length; i++)
        {
            if (nextTableCode >= MaxCode)
            {
                WriteBitsLsbFirst(bits, clearCode, codeSize);
                codeSize = minCodeSize + 1;
                nextTableCode = eoiCode + 1;
            }
            WriteBitsLsbFirst(bits, data[i], codeSize);
            if (nextTableCode >= (1 << codeSize) && codeSize < 12)
                codeSize++;
            nextTableCode++;
        }
        WriteBitsLsbFirst(bits, eoiCode, codeSize);

        byte[] result = new byte[(bits.Count + 7) / 8];
        for (int i = 0; i < bits.Count; i++)
        {
            if (bits[i])
                result[i / 8] |= (byte)(1 << (i % 8));
        }
        return result;
    }

    private static void WriteBitsLsbFirst(List<bool> bits, int value, int count)
    {
        for (int i = 0; i < count; i++)
            bits.Add((value & (1 << i)) != 0);
    }

    private static byte[] CreateBmp24BottomUp2x2RedGreenOverBlueWhite()
    {
        const int RowStride = 8;
        var data = CreateBmpFile(width: 2, height: 2, bitDepth: 24, pixelBytes: RowStride * 2);
        int bottomRow = BmpFileHeaderSize + BmpInfoHeaderSize;
        int topRow = bottomRow + RowStride;

        WriteBgr(data, bottomRow, 0, 0, 255);
        WriteBgr(data, bottomRow + 3, 255, 255, 255);
        WriteBgr(data, topRow, 255, 0, 0);
        WriteBgr(data, topRow + 3, 0, 255, 0);
        return data;
    }

    private static byte[] CreateBmp32BottomUp2x2TopLeftRedAlpha128()
    {
        const int RowStride = 8;
        var data = CreateBmpFile(width: 2, height: 2, bitDepth: 32, pixelBytes: RowStride * 2);
        int bottomRow = BmpFileHeaderSize + BmpInfoHeaderSize;
        int topRow = bottomRow + RowStride;

        WriteBgra(data, bottomRow, 0, 0, 255, 255);
        WriteBgra(data, bottomRow + 4, 255, 255, 255, 255);
        WriteBgra(data, topRow, 255, 0, 0, 128);
        WriteBgra(data, topRow + 4, 0, 255, 0, 200);
        return data;
    }

    private static byte[] CreateBmpFile(int width, int height, int bitDepth, int pixelBytes)
    {
        const int PixelsPerMeterAt72Dpi = 2835;
        int dataOffset = BmpFileHeaderSize + BmpInfoHeaderSize;
        int fileSize = dataOffset + pixelBytes;
        var data = new byte[fileSize];

        data[0] = (byte)'B';
        data[1] = (byte)'M';
        WriteInt32LE(data, 2, fileSize);
        WriteInt32LE(data, 10, dataOffset);
        WriteInt32LE(data, 14, BmpInfoHeaderSize);
        WriteInt32LE(data, 18, width);
        WriteInt32LE(data, 22, height);
        WriteInt16LE(data, 26, 1);
        WriteInt16LE(data, 28, bitDepth);
        WriteInt32LE(data, 30, 0);
        WriteInt32LE(data, 34, pixelBytes);
        WriteInt32LE(data, 38, PixelsPerMeterAt72Dpi);
        WriteInt32LE(data, 42, PixelsPerMeterAt72Dpi);
        return data;
    }

    private static byte[] CreateBmp(int headerSize, int width, int height, int bitDepth, int compression, uint[] masks, byte[] pixelRows)
    {
        const int MasksOffset = BmpFileHeaderSize + BmpInfoHeaderSize;
        int dataOffset = Math.Max(BmpFileHeaderSize + headerSize, MasksOffset + masks.Length * 4);
        var data = new byte[dataOffset + pixelRows.Length];

        data[0] = (byte)'B';
        data[1] = (byte)'M';
        WriteInt32LE(data, 2, data.Length);
        WriteInt32LE(data, 10, dataOffset);
        WriteInt32LE(data, 14, headerSize);
        WriteInt32LE(data, 18, width);
        WriteInt32LE(data, 22, height);
        WriteInt16LE(data, 26, 1);
        WriteInt16LE(data, 28, bitDepth);
        WriteInt32LE(data, 30, compression);
        WriteInt32LE(data, 34, pixelRows.Length);
        for (int i = 0; i < masks.Length; i++)
            WriteInt32LE(data, MasksOffset + i * 4, unchecked((int)masks[i]));
        pixelRows.CopyTo(data, dataOffset);
        return data;
    }

    private static void WriteBgr(byte[] data, int offset, byte r, byte g, byte b)
    {
        data[offset] = b;
        data[offset + 1] = g;
        data[offset + 2] = r;
    }

    private static void WriteBgra(byte[] data, int offset, byte r, byte g, byte b, byte a)
    {
        WriteBgr(data, offset, r, g, b);
        data[offset + 3] = a;
    }

    private static void WriteInt16LE(MemoryStream ms, int value)
    {
        ms.WriteByte((byte)(value & 0xFF));
        ms.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private static void WriteInt16LE(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32LE(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
