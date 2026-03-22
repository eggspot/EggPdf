using System;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfImageTests
{
    // Minimal valid JPEG: 1x1 pixel red
    // SOI + APP0 + SOF0 + SOS + image data + EOI
    private static readonly byte[] MinimalJpeg = CreateMinimalJpeg();

    [Fact]
    public void FromJpeg_ValidJpeg_ReturnsImage()
    {
        var image = PdfImage.FromJpeg("test", MinimalJpeg);
        image.Should().NotBeNull();
        image!.Width.Should().BeGreaterThan(0);
        image.Height.Should().BeGreaterThan(0);
        image.Format.Should().Be(PdfImageFormat.Jpeg);
    }

    [Fact]
    public void FromJpeg_InvalidData_ReturnsNull()
    {
        var image = PdfImage.FromJpeg("test", new byte[] { 0, 1, 2, 3 });
        image.Should().BeNull();
    }

    [Fact]
    public void FromJpeg_NullData_ReturnsNull()
    {
        var image = PdfImage.FromJpeg("test", null!);
        image.Should().BeNull();
    }

    [Fact]
    public void FromRgb_ValidData_CreatesImage()
    {
        byte[] rgb = new byte[3 * 2 * 2]; // 2x2 image, 3 bytes per pixel
        var image = PdfImage.FromRgb("test", 2, 2, rgb);

        image.Should().NotBeNull();
        image.Width.Should().Be(2);
        image.Height.Should().Be(2);
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.SMaskData.Should().BeNull();
    }

    [Fact]
    public void FromRgba_ValidData_SeparatesAlpha()
    {
        byte[] rgba = new byte[4 * 2 * 2]; // 2x2 image, 4 bytes per pixel
        rgba[3] = 128; // first pixel alpha = 128
        var image = PdfImage.FromRgba("test", 2, 2, rgba);

        image.Should().NotBeNull();
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.SMaskData.Should().NotBeNull();
        image.SMaskData!.Length.Should().Be(4); // 2x2 = 4 alpha bytes
        image.SMaskData[0].Should().Be(128);
        image.Data.Length.Should().Be(12); // 2x2*3 = 12 RGB bytes
    }

    [Fact]
    public void PdfDocument_ImageEmbedded_ContainsXObject()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(612, 792);

        byte[] rgb = new byte[3]; // 1x1 red pixel
        rgb[0] = 255;
        var image = PdfImage.FromRgb("TestImg", 1, 1, rgb);
        doc.AddImage(image);

        page.AddImage("TestImg", 100, 100, 50, 50);
        page.AddText("test", 10, 10, "Helvetica", 12);

        byte[] pdfBytes = doc.ToByteArray();
        var text = System.Text.Encoding.ASCII.GetString(pdfBytes);

        text.Should().Contain("/XObject");
        text.Should().Contain("/TestImg");
        text.Should().Contain("Do"); // image paint operator
    }

    /// <summary>Create a minimal valid JPEG for testing (1x1 pixel).</summary>
    private static byte[] CreateMinimalJpeg()
    {
        // This is a pre-built minimal JPEG file (1x1 red pixel, ~107 bytes)
        return new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
            0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
            0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09,
            0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
            0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20,
            0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29,
            0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32,
            0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
            0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x1F, 0x00, 0x00,
            0x01, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x09, 0x0A, 0x0B, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F,
            0x00, 0x7B, 0x40, 0x1B, 0xFF, 0xD9
        };
    }
}
