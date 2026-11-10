using System;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfImageTests
{
    private const int RgbBytesPerPixel = 3;

    /// <summary>One red pixel: SOI + APP0 + SOF0 + SOS + image data + EOI.</summary>
    private static readonly byte[] MinimalJpeg = CreateMinimalJpeg();

    /// <summary>One red pixel, RGB (color type 2, bit depth 8).</summary>
    private static readonly byte[] Minimal1x1RgbPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, 0x78, 0x01, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0xDB, 0x7D, 0x2E, 0xBC, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82 };

    /// <summary>One red pixel with alpha 128, RGBA (color type 6, bit depth 8).</summary>
    private static readonly byte[] Minimal1x1RgbaPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x01, 0x63, 0xF8, 0xCF, 0xC0, 0xD0,
        0x00, 0x00, 0x04, 0x81, 0x01, 0x80, 0xDE, 0x25, 0x4D, 0x42, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
        0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

    /// <summary>One gray-128 pixel, grayscale (color type 0, bit depth 8).</summary>
    private static readonly byte[] Minimal1x1GrayPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x00, 0x00, 0x00, 0x00, 0x3A, 0x7E, 0x9B,
        0x55, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x01, 0x63, 0x68, 0x00, 0x00, 0x00,
        0x82, 0x00, 0x81, 0x4C, 0x17, 0xD7, 0xDF, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82 };

    /// <summary>One pixel of palette index 0, which is red (color type 3, bit depth 8).</summary>
    private static readonly byte[] Minimal1x1IndexedPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x03, 0x00, 0x00, 0x00, 0x28, 0xCB, 0x34,
        0xBB, 0x00, 0x00, 0x00, 0x03, 0x50, 0x4C, 0x54, 0x45, 0xFF, 0x00, 0x00, 0x19, 0xE2, 0x09, 0x37,
        0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x01, 0x63, 0x60, 0x00, 0x00, 0x00, 0x02,
        0x00, 0x01, 0x73, 0x75, 0x01, 0x18, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42,
        0x60, 0x82 };

    /// <summary>2x2 RGB (color type 2, bit depth 8): red, green / blue, yellow.</summary>
    private static readonly byte[] Minimal2x2RgbPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, 0x08, 0x02, 0x00, 0x00, 0x00, 0xFD, 0xD4, 0x9A,
        0x73, 0x00, 0x00, 0x00, 0x14, 0x49, 0x44, 0x41, 0x54, 0x78, 0x01, 0x63, 0xF8, 0xCF, 0xC0, 0xC0,
        0x00, 0xC2, 0x0C, 0xFF, 0xFF, 0xFF, 0x67, 0x00, 0x00, 0x1E, 0xEF, 0x04, 0xFC, 0x1D, 0xB0, 0xC7,
        0x92, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

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
        byte[] rgb = new byte[2 * 2 * RgbBytesPerPixel];
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
        const int firstPixelAlphaOffset = 3;
        byte[] rgba = new byte[2 * 2 * 4];
        rgba[firstPixelAlphaOffset] = 128;
        var image = PdfImage.FromRgba("test", 2, 2, rgba);

        image.Should().NotBeNull();
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.SMaskData.Should().NotBeNull();
        image.SMaskData!.Length.Should().Be(4, "one alpha byte per pixel of the 2x2 image");
        image.SMaskData[0].Should().Be(128);
        image.Data.Length.Should().Be(2 * 2 * RgbBytesPerPixel, "the alpha is split off, leaving RGB");
    }

    [Fact]
    public void FromPng_InvalidSignature_ReturnsNull()
    {
        var data = new byte[] { 0x00, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        var image = PdfImage.FromPng("test", data);
        image.Should().BeNull();
    }

    [Fact]
    public void FromPng_NullData_ReturnsNull()
    {
        var image = PdfImage.FromPng("test", null!);
        image.Should().BeNull();
    }

    [Fact]
    public void FromPng_EmptyData_ReturnsNull()
    {
        var image = PdfImage.FromPng("test", Array.Empty<byte>());
        image.Should().BeNull();
    }

    [Fact]
    public void FromPng_TruncatedData_ReturnsNull()
    {
        var pngSignatureOnly = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var image = PdfImage.FromPng("test", pngSignatureOnly);
        image.Should().BeNull();
    }

    [Fact]
    public void FromPng_JpegData_ReturnsNull()
    {
        var image = PdfImage.FromPng("test", MinimalJpeg);
        image.Should().BeNull("JPEG data is not decoded as PNG");
    }

    [Fact]
    public void FromPng_1x1Rgb_ReturnsImage()
    {
        var image = PdfImage.FromPng("test", Minimal1x1RgbPng);

        image.Should().NotBeNull();
        image!.Width.Should().Be(1);
        image.Height.Should().Be(1);
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.SMaskData.Should().BeNull();
    }

    [Fact]
    public void FromPng_1x1Rgb_DecodesRedPixel()
    {
        var image = PdfImage.FromPng("test", Minimal1x1RgbPng);

        image.Should().NotBeNull();
        image!.Data.Should().Equal(new byte[] { 255, 0, 0 }, "one red RGB pixel");
    }

    [Fact]
    public void FromPng_2x2Rgb_DecodesCorrectDimensions()
    {
        var image = PdfImage.FromPng("test", Minimal2x2RgbPng);

        image.Should().NotBeNull();
        image!.Width.Should().Be(2);
        image.Height.Should().Be(2);
        image.Data.Length.Should().Be(2 * 2 * RgbBytesPerPixel);
    }

    [Fact]
    public void FromPng_2x2Rgb_DecodesPixelValues()
    {
        var image = PdfImage.FromPng("test", Minimal2x2RgbPng);

        image.Should().NotBeNull();
        image!.Data.Should().Equal(new byte[]
        {
            255, 0, 0, 0, 255, 0,
            0, 0, 255, 255, 255, 0,
        }, "row 0 is red then green, row 1 is blue then yellow");
    }

    [Fact]
    public void FromPng_1x1Rgba_ReturnsImageWithAlpha()
    {
        var image = PdfImage.FromPng("test", Minimal1x1RgbaPng);

        image.Should().NotBeNull();
        image!.Width.Should().Be(1);
        image.Height.Should().Be(1);
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.SMaskData.Should().NotBeNull();
    }

    [Fact]
    public void FromPng_1x1Rgba_DecodesAlphaChannel()
    {
        var image = PdfImage.FromPng("test", Minimal1x1RgbaPng);

        image.Should().NotBeNull();
        image!.Data.Should().Equal(new byte[] { 255, 0, 0 }, "the color part is one red pixel");
        image.SMaskData.Should().Equal(new byte[] { 128 }, "the alpha channel (50% transparent) goes to the soft mask");
    }

    [Fact]
    public void FromPng_1x1Grayscale_ReturnsImage()
    {
        var image = PdfImage.FromPng("test", Minimal1x1GrayPng);

        image.Should().NotBeNull();
        image!.Width.Should().Be(1);
        image.Height.Should().Be(1);
        image.Format.Should().Be(PdfImageFormat.Raw);
        image.SMaskData.Should().BeNull();
    }

    [Fact]
    public void FromPng_1x1Grayscale_DecodesGrayValue()
    {
        var image = PdfImage.FromPng("test", Minimal1x1GrayPng);

        image.Should().NotBeNull();
        image!.Data.Should().Equal(new byte[] { 128, 128, 128 }, "grayscale is expanded to RGB");
    }

    [Fact]
    public void FromPng_1x1Indexed_ReturnsImage()
    {
        var image = PdfImage.FromPng("test", Minimal1x1IndexedPng);

        image.Should().NotBeNull();
        image!.Width.Should().Be(1);
        image.Height.Should().Be(1);
        image.Format.Should().Be(PdfImageFormat.Raw);
    }

    [Fact]
    public void FromPng_1x1Indexed_DecodesPaletteColor()
    {
        var image = PdfImage.FromPng("test", Minimal1x1IndexedPng);

        image.Should().NotBeNull();
        image!.Data.Should().Equal(new byte[] { 255, 0, 0 }, "palette index 0 is red");
    }

    [Fact]
    public void FromPng_EmbeddedInPdf_ContainsXObject()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(612, 792);

        var image = PdfImage.FromPng("PngImg", Minimal1x1RgbPng);
        image.Should().NotBeNull();
        doc.AddImage(image!);

        page.AddImage("PngImg", 100, 100, 50, 50);
        page.AddText("test", 10, 10, "Helvetica", 12);

        byte[] pdfBytes = doc.ToByteArray();
        var text = System.Text.Encoding.ASCII.GetString(pdfBytes);

        text.Should().Contain("/XObject");
        text.Should().Contain("/PngImg");
        text.Should().Contain("Do");
    }

    [Fact]
    public void FromPng_RgbaInPdf_ContainsSMask()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(612, 792);

        var image = PdfImage.FromPng("PngAlpha", Minimal1x1RgbaPng);
        image.Should().NotBeNull();
        image!.SMaskData.Should().NotBeNull("RGBA PNG should have alpha channel");
        doc.AddImage(image);

        page.AddImage("PngAlpha", 100, 100, 50, 50);
        page.AddText("test", 10, 10, "Helvetica", 12);

        byte[] pdfBytes = doc.ToByteArray();
        System.Text.Encoding.ASCII.GetString(pdfBytes).Should().Contain("/SMask", "the PNG's alpha is written as a soft mask");
    }

    [Fact]
    public void HtmlToPdf_PngDataUri_RendersToPdf()
    {
        string base64Png = Convert.ToBase64String(Minimal1x1RgbPng);
        string html = $"<html><body><img src=\"data:image/png;base64,{base64Png}\" width=\"50\" height=\"50\" /></body></html>";

        var pdfBytes = HtmlToPdf.Render(html);

        var text = System.Text.Encoding.ASCII.GetString(pdfBytes);
        text.Should().StartWith("%PDF");
        text.Should().Contain("/Subtype /Image", "the PNG data URI is decoded and embedded");
    }

    [Fact]
    public void HtmlToPdf_JpegDataUri_StillWorks()
    {
        string base64Jpeg = Convert.ToBase64String(MinimalJpeg);
        string html = $"<html><body><img src=\"data:image/jpeg;base64,{base64Jpeg}\" width=\"50\" height=\"50\" /></body></html>";

        var pdfBytes = HtmlToPdf.Render(html);

        System.Text.Encoding.ASCII.GetString(pdfBytes).Should().Contain("/DCTDecode", "the JPEG is embedded as-is with the DCT filter");
    }

    [Fact]
    public void PdfDocument_ImageEmbedded_ContainsXObject()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(612, 792);

        byte[] oneRedPixel = { 255, 0, 0 };
        var image = PdfImage.FromRgb("TestImg", 1, 1, oneRedPixel);
        doc.AddImage(image);

        page.AddImage("TestImg", 100, 100, 50, 50);
        page.AddText("test", 10, 10, "Helvetica", 12);

        byte[] pdfBytes = doc.ToByteArray();
        var text = System.Text.Encoding.ASCII.GetString(pdfBytes);

        text.Should().Contain("/XObject");
        text.Should().Contain("/TestImg");
        text.Should().Contain("/TestImg Do", "the image is painted with the Do operator");
    }

    /// <summary>A pre-built minimal JPEG file: one red pixel in ~107 bytes.</summary>
    private static byte[] CreateMinimalJpeg()
    {
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
