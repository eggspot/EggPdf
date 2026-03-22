using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class GifBmpImageTests
{
    #region GIF Tests

    [Fact]
    public void Gif_ValidSignature_Decoded()
    {
        // 2x2 GIF with 4 colors: red, green, blue, yellow
        byte[] gifData = CreateMinimal2x2Gif();

        var image = PdfImage.FromGif("test", gifData);

        image.Should().NotBeNull();
        image!.Width.Should().Be(2);
        image.Height.Should().Be(2);
        image.Format.Should().Be(PdfImageFormat.Raw);
        // 2x2 RGB = 12 bytes
        image.Data.Length.Should().Be(12);

        // Pixel (0,0) = red
        image.Data[0].Should().Be(255);
        image.Data[1].Should().Be(0);
        image.Data[2].Should().Be(0);

        // Pixel (1,0) = green
        image.Data[3].Should().Be(0);
        image.Data[4].Should().Be(255);
        image.Data[5].Should().Be(0);

        // Pixel (0,1) = blue
        image.Data[6].Should().Be(0);
        image.Data[7].Should().Be(0);
        image.Data[8].Should().Be(255);

        // Pixel (1,1) = yellow
        image.Data[9].Should().Be(255);
        image.Data[10].Should().Be(255);
        image.Data[11].Should().Be(0);
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
    public void Gif_TruncatedData_ReturnsNull()
    {
        // Just the GIF signature, nothing else
        var image = PdfImage.FromGif("test", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 });
        image.Should().BeNull();
    }

    [Fact]
    public void Gif_Transparency_HasAlpha()
    {
        byte[] gifData = CreateTransparentGif();

        var image = PdfImage.FromGif("test", gifData);

        image.Should().NotBeNull();
        image!.Width.Should().Be(2);
        image.Height.Should().Be(1);
        image.Format.Should().Be(PdfImageFormat.Raw);
        // Has alpha: SMaskData should be present
        image.SMaskData.Should().NotBeNull();
        image.SMaskData!.Length.Should().Be(2); // 2 pixels

        // Pixel 0: red, opaque
        image.Data[0].Should().Be(255); // R
        image.Data[1].Should().Be(0);   // G
        image.Data[2].Should().Be(0);   // B
        image.SMaskData[0].Should().Be(255); // fully opaque

        // Pixel 1: transparent (index 1 = transparent)
        image.SMaskData[1].Should().Be(0); // fully transparent
    }

    #endregion

    #region BMP Tests

    [Fact]
    public void Bmp_24Bit_Decoded()
    {
        // 2x2 BMP, 24-bit, bottom-up
        byte[] bmpData = CreateMinimal2x2Bmp24();

        var image = PdfImage.FromBmp("test", bmpData);

        image.Should().NotBeNull();
        image!.Width.Should().Be(2);
        image.Height.Should().Be(2);
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.SMaskData.Should().BeNull();
        image.Data.Length.Should().Be(12); // 2x2 RGB = 12 bytes

        // Row 0 (top of image) = red, green
        image.Data[0].Should().Be(255); // R
        image.Data[1].Should().Be(0);   // G
        image.Data[2].Should().Be(0);   // B
        image.Data[3].Should().Be(0);   // R
        image.Data[4].Should().Be(255); // G
        image.Data[5].Should().Be(0);   // B

        // Row 1 (bottom of image) = blue, white
        image.Data[6].Should().Be(0);   // R
        image.Data[7].Should().Be(0);   // G
        image.Data[8].Should().Be(255); // B
        image.Data[9].Should().Be(255);  // R
        image.Data[10].Should().Be(255); // G
        image.Data[11].Should().Be(255); // B
    }

    [Fact]
    public void Bmp_32Bit_HasAlpha()
    {
        byte[] bmpData = CreateMinimal2x2Bmp32();

        var image = PdfImage.FromBmp("test", bmpData);

        image.Should().NotBeNull();
        image!.Width.Should().Be(2);
        image.Height.Should().Be(2);
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.SMaskData.Should().NotBeNull();
        image.SMaskData!.Length.Should().Be(4); // 2x2 = 4 alpha values

        // Check first pixel: red with alpha 128
        image.Data[0].Should().Be(255); // R
        image.Data[1].Should().Be(0);   // G
        image.Data[2].Should().Be(0);   // B
        image.SMaskData[0].Should().Be(128); // Alpha
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
    public void Bmp_BottomUp_RowsFlipped()
    {
        // Create a 2x2 bottom-up BMP where:
        // File order (bottom to top): row1=[blue,white], row0=[red,green]
        // Output order (top to bottom): row0=[red,green], row1=[blue,white]
        byte[] bmpData = CreateMinimal2x2Bmp24();

        var image = PdfImage.FromBmp("test", bmpData);

        image.Should().NotBeNull();
        // Top row should be red, green (not blue, white)
        image!.Data[0].Should().Be(255); // R of top-left pixel
        image.Data[1].Should().Be(0);
        image.Data[2].Should().Be(0);

        // Bottom row should be blue, white
        image.Data[6].Should().Be(0);
        image.Data[7].Should().Be(0);
        image.Data[8].Should().Be(255); // B of bottom-left pixel
    }

    #endregion

    #region E2E Data URI Tests

    [Fact]
    public async Task GifDataUri_RendersInPdf()
    {
        byte[] gifData = CreateMinimal2x2Gif();
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
        byte[] bmpData = CreateMinimal2x2Bmp24();
        string base64 = Convert.ToBase64String(bmpData);
        string html = $"<html><body><img src=\"data:image/bmp;base64,{base64}\" width=\"50\" height=\"50\" /></body></html>";

        var pdfBytes = await HtmlToPdf.RenderAsync(html);

        pdfBytes.Should().NotBeNull();
        pdfBytes.Length.Should().BeGreaterThan(0);
        var text = Encoding.ASCII.GetString(pdfBytes);
        text.Should().Contain("%PDF");
    }

    #endregion

    #region Test Data Builders

    /// <summary>
    /// Create a minimal 2x2 GIF89a with 4 colors:
    /// (0,0)=red, (1,0)=green, (0,1)=blue, (1,1)=yellow
    /// </summary>
    private static byte[] CreateMinimal2x2Gif()
    {
        using var ms = new MemoryStream();

        // GIF89a signature
        ms.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, 0, 6);

        // Logical Screen Descriptor
        WriteInt16LE(ms, 2); // width
        WriteInt16LE(ms, 2); // height
        ms.WriteByte(0x81); // packed: global color table flag=1, color resolution=0, sort=0, GCT size=1 (4 colors = 2^(1+1))
        ms.WriteByte(0);    // background color index
        ms.WriteByte(0);    // pixel aspect ratio

        // Global Color Table (4 entries x 3 bytes = 12 bytes)
        // Index 0: Red
        ms.Write(new byte[] { 255, 0, 0 }, 0, 3);
        // Index 1: Green
        ms.Write(new byte[] { 0, 255, 0 }, 0, 3);
        // Index 2: Blue
        ms.Write(new byte[] { 0, 0, 255 }, 0, 3);
        // Index 3: Yellow
        ms.Write(new byte[] { 255, 255, 0 }, 0, 3);

        // Image Descriptor
        ms.WriteByte(0x2C); // image separator
        WriteInt16LE(ms, 0); // left
        WriteInt16LE(ms, 0); // top
        WriteInt16LE(ms, 2); // width
        WriteInt16LE(ms, 2); // height
        ms.WriteByte(0);     // packed: no local color table, not interlaced

        // LZW image data
        // Min code size = 2 (for 4 colors)
        ms.WriteByte(2);

        // LZW encode pixels: 0, 1, 2, 3
        // With min code size 2: clear=4, eoi=5, initial code size=3 bits
        byte[] lzwData = LzwEncode(new byte[] { 0, 1, 2, 3 }, 2);

        // Write as sub-block
        ms.WriteByte((byte)lzwData.Length);
        ms.Write(lzwData, 0, lzwData.Length);
        ms.WriteByte(0); // block terminator

        // Trailer
        ms.WriteByte(0x3B);

        return ms.ToArray();
    }

    /// <summary>
    /// Create a 2x1 GIF89a with transparency:
    /// Pixel 0 = red (opaque), Pixel 1 = transparent (index 1)
    /// </summary>
    private static byte[] CreateTransparentGif()
    {
        using var ms = new MemoryStream();

        // GIF89a signature
        ms.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, 0, 6);

        // Logical Screen Descriptor
        WriteInt16LE(ms, 2); // width
        WriteInt16LE(ms, 1); // height
        ms.WriteByte(0x80); // packed: global color table flag=1, GCT size=0 (2 colors = 2^(0+1))
        ms.WriteByte(0);    // background color index
        ms.WriteByte(0);    // pixel aspect ratio

        // Global Color Table (2 entries x 3 bytes = 6 bytes)
        // Index 0: Red
        ms.Write(new byte[] { 255, 0, 0 }, 0, 3);
        // Index 1: Green (will be transparent)
        ms.Write(new byte[] { 0, 255, 0 }, 0, 3);

        // Graphic Control Extension (for transparency)
        ms.WriteByte(0x21); // extension introducer
        ms.WriteByte(0xF9); // graphic control label
        ms.WriteByte(4);    // block size
        ms.WriteByte(0x01); // packed: transparent color flag = 1
        WriteInt16LE(ms, 0); // delay time
        ms.WriteByte(1);    // transparent color index
        ms.WriteByte(0);    // block terminator

        // Image Descriptor
        ms.WriteByte(0x2C); // image separator
        WriteInt16LE(ms, 0); // left
        WriteInt16LE(ms, 0); // top
        WriteInt16LE(ms, 2); // width
        WriteInt16LE(ms, 1); // height
        ms.WriteByte(0);     // packed

        // LZW image data
        // Min code size = 2 (minimum is 2 per GIF spec even for 2 colors)
        ms.WriteByte(2);

        byte[] lzwData = LzwEncode(new byte[] { 0, 1 }, 2);

        ms.WriteByte((byte)lzwData.Length);
        ms.Write(lzwData, 0, lzwData.Length);
        ms.WriteByte(0); // block terminator

        // Trailer
        ms.WriteByte(0x3B);

        return ms.ToArray();
    }

    /// <summary>
    /// Simple LZW encoder for GIF test data.
    /// Encodes data with the given minimum code size.
    /// </summary>
    private static byte[] LzwEncode(byte[] data, int minCodeSize)
    {
        int clearCode = 1 << minCodeSize;
        int eoiCode = clearCode + 1;

        // Use a simple encoding: clear code, then each pixel as a literal, then EOI
        // This is not optimal but always correct
        var bits = new System.Collections.Generic.List<bool>();
        int codeSize = minCodeSize + 1;

        // Write clear code
        WriteBitsLSB(bits, clearCode, codeSize);

        // Write each pixel as a literal code
        for (int i = 0; i < data.Length; i++)
            WriteBitsLSB(bits, data[i], codeSize);

        // Write EOI
        WriteBitsLSB(bits, eoiCode, codeSize);

        // Convert bits to bytes
        int byteCount = (bits.Count + 7) / 8;
        byte[] result = new byte[byteCount];
        for (int i = 0; i < bits.Count; i++)
        {
            if (bits[i])
                result[i / 8] |= (byte)(1 << (i % 8));
        }

        return result;
    }

    private static void WriteBitsLSB(System.Collections.Generic.List<bool> bits, int value, int count)
    {
        for (int i = 0; i < count; i++)
            bits.Add((value & (1 << i)) != 0);
    }

    /// <summary>
    /// Create a minimal 2x2 BMP, 24-bit, bottom-up.
    /// Top row (in output): red, green. Bottom row: blue, white.
    /// Since BMP is bottom-up, file stores bottom row first.
    /// </summary>
    private static byte[] CreateMinimal2x2Bmp24()
    {
        // Row stride for 2 pixels * 3 bytes = 6 bytes, padded to 4-byte boundary = 8
        int rowStride = 8;
        int pixelDataSize = rowStride * 2;
        int fileSize = 14 + 40 + pixelDataSize; // header + DIB + pixels
        int dataOffset = 14 + 40;

        var data = new byte[fileSize];

        // BITMAPFILEHEADER (14 bytes)
        data[0] = 0x42; // 'B'
        data[1] = 0x4D; // 'M'
        WriteInt32LE(data, 2, fileSize);
        WriteInt32LE(data, 10, dataOffset);

        // BITMAPINFOHEADER (40 bytes)
        WriteInt32LE(data, 14, 40);       // header size
        WriteInt32LE(data, 18, 2);        // width
        WriteInt32LE(data, 22, 2);        // height (positive = bottom-up)
        WriteInt16LE(data, 26, 1);        // planes
        WriteInt16LE(data, 28, 24);       // bit depth
        WriteInt32LE(data, 30, 0);        // compression = BI_RGB
        WriteInt32LE(data, 34, pixelDataSize);
        WriteInt32LE(data, 38, 2835);     // X pixels per meter
        WriteInt32LE(data, 42, 2835);     // Y pixels per meter
        WriteInt32LE(data, 46, 0);        // colors used
        WriteInt32LE(data, 50, 0);        // important colors

        // Pixel data (bottom-up: file row 0 = bottom of image)
        int offset = dataOffset;

        // File row 0 (= output row 1 = bottom): blue, white (BGR format)
        data[offset] = 255; data[offset + 1] = 0; data[offset + 2] = 0;     // Blue in BGR = (255, 0, 0)
        data[offset + 3] = 255; data[offset + 4] = 255; data[offset + 5] = 255; // White in BGR
        // 2 bytes padding (stride=8, data=6)
        offset += rowStride;

        // File row 1 (= output row 0 = top): red, green (BGR format)
        data[offset] = 0; data[offset + 1] = 0; data[offset + 2] = 255;     // Red in BGR = (0, 0, 255)
        data[offset + 3] = 0; data[offset + 4] = 255; data[offset + 5] = 0; // Green in BGR = (0, 255, 0)

        return data;
    }

    /// <summary>
    /// Create a minimal 2x2 BMP, 32-bit (BGRA), bottom-up.
    /// Pixel (0,0) = red with alpha 128.
    /// </summary>
    private static byte[] CreateMinimal2x2Bmp32()
    {
        int rowStride = 2 * 4; // 2 pixels * 4 bytes = 8 (already 4-byte aligned)
        int pixelDataSize = rowStride * 2;
        int fileSize = 14 + 40 + pixelDataSize;
        int dataOffset = 14 + 40;

        var data = new byte[fileSize];

        // BITMAPFILEHEADER
        data[0] = 0x42; data[1] = 0x4D;
        WriteInt32LE(data, 2, fileSize);
        WriteInt32LE(data, 10, dataOffset);

        // BITMAPINFOHEADER
        WriteInt32LE(data, 14, 40);
        WriteInt32LE(data, 18, 2);
        WriteInt32LE(data, 22, 2);  // positive = bottom-up
        WriteInt16LE(data, 26, 1);
        WriteInt16LE(data, 28, 32); // 32-bit
        WriteInt32LE(data, 30, 0);  // BI_RGB
        WriteInt32LE(data, 34, pixelDataSize);
        WriteInt32LE(data, 38, 2835);
        WriteInt32LE(data, 42, 2835);

        int offset = dataOffset;

        // File row 0 (= bottom row in output): BGRA
        // blue pixel, alpha 255
        data[offset] = 255; data[offset + 1] = 0; data[offset + 2] = 0; data[offset + 3] = 255;
        // white pixel, alpha 255
        data[offset + 4] = 255; data[offset + 5] = 255; data[offset + 6] = 255; data[offset + 7] = 255;
        offset += rowStride;

        // File row 1 (= top row in output): BGRA
        // red pixel, alpha 128: B=0, G=0, R=255, A=128
        data[offset] = 0; data[offset + 1] = 0; data[offset + 2] = 255; data[offset + 3] = 128;
        // green pixel, alpha 200: B=0, G=255, R=0, A=200
        data[offset + 4] = 0; data[offset + 5] = 255; data[offset + 6] = 0; data[offset + 7] = 200;

        return data;
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

    #endregion
}
