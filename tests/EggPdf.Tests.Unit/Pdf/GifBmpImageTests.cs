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
        int clearCode = 1 << minCodeSize;
        int eoiCode = clearCode + 1;
        var bits = new List<bool>();
        int codeSize = minCodeSize + 1;

        WriteBitsLsbFirst(bits, clearCode, codeSize);
        for (int i = 0; i < data.Length; i++)
            WriteBitsLsbFirst(bits, data[i], codeSize);
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
