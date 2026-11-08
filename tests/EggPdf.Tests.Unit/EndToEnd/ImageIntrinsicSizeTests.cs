using System;
using System.IO;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class ImageIntrinsicSizeTests
{
    private static byte[] Bmp(int width, int height)
    {
        int rowBytes = (width * 3 + 3) / 4 * 4;
        int pixelBytes = rowBytes * height;
        var bmp = new byte[54 + pixelBytes];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10);
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
        BitConverter.GetBytes(pixelBytes).CopyTo(bmp, 34);
        return bmp;
    }

    private static byte[] JpegHeaderWithOrientation(int width, int height, int orientation)
    {
        using var jpeg = new MemoryStream();
        jpeg.Write(new byte[] { 0xFF, 0xD8 });
        var tiff = new byte[]
        {
            (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8,
            0, 1,
            0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, (byte)orientation, 0, 0,
            0, 0, 0, 0,
        };
        var exif = new byte[6 + tiff.Length];
        "Exif"u8.ToArray().CopyTo(exif, 0);
        tiff.CopyTo(exif, 6);
        int app1Length = exif.Length + 2;
        jpeg.Write(new byte[] { 0xFF, 0xE1, (byte)(app1Length >> 8), (byte)app1Length });
        jpeg.Write(exif);
        jpeg.Write(new byte[]
        {
            0xFF, 0xC0, 0, 17, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3,
            1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1,
        });
        jpeg.Write(new byte[] { 0xFF, 0xD9 });
        return jpeg.ToArray();
    }

    private static string DataUri(string mediaType, byte[] data) => $"data:{mediaType};base64,{Convert.ToBase64String(data)}";

    private static string DrawMatrix(string pdfText)
    {
        var match = Regex.Match(pdfText, @"q\n(-?[\d.]+ -?[\d.]+ -?[\d.]+ -?[\d.]+ -?[\d.]+ -?[\d.]+) cm\n/Img");
        match.Success.Should().BeTrue("the image must be drawn");
        return match.Groups[1].Value;
    }

    [Theory]
    [InlineData("", "3.00 0 0 1.50")]
    [InlineData(" width='100'", "75.00 0 0 37.50")]
    [InlineData(" height='20'", "30.00 0 0 15.00")]
    [InlineData(" style='width:40px'", "30.00 0 0 15.00")]
    [InlineData(" width='100' height='100'", "75.00 0 0 75.00")]
    public void Img_MissingDimensions_UseIntrinsicSizeAndRatio(string attributes, string expectedScale)
    {
        var pdf = HtmlToPdf.Render($"<img src='{DataUri("image/bmp", Bmp(4, 2))}'{attributes}>");

        DrawMatrix(PdfAssert.ValidPdf(pdf)).Should().StartWith(expectedScale);
    }

    [Fact]
    public void Img_LargerThanMaxWidth_ScalesDownKeepingTheRatio()
    {
        var pdf = HtmlToPdf.Render($"<div style='width:200px'><img style='max-width:100%' src='{DataUri("image/bmp", Bmp(800, 400))}'></div>");

        DrawMatrix(PdfAssert.ValidPdf(pdf)).Should().StartWith("150.00 0 0 75.00");
    }

    [Theory]
    [InlineData("<div style='display:flex'>{0}</div>")]
    [InlineData("<div style='display:grid'>{0}</div>")]
    [InlineData("<div style='position:relative;height:50px'><div style='position:absolute;top:0;left:0'>{0}</div></div>")]
    [InlineData("<div style='position:relative;height:50px'>{0}</div>")]
    public void Img_AsFlexGridOrPositionedItem_IsDrawn(string template)
    {
        var img = $"<img src='{DataUri("image/bmp", Bmp(4, 2))}' width='40'>";

        var text = PdfAssert.ValidPdf(HtmlToPdf.Render(string.Format(template, img)));

        PdfAssert.EveryDrawnXObjectIsDefined(text).Should().Be(1);
        DrawMatrix(text).Should().StartWith("30.00 0 0 15.00");
    }

    [Fact]
    public void Img_AbsolutelyPositioned_IsDrawn()
    {
        var img = $"<img style='position:absolute;top:0;left:0' src='{DataUri("image/bmp", Bmp(4, 2))}' width='40'>";

        var text = PdfAssert.ValidPdf(HtmlToPdf.Render("<div style='position:relative;height:50px'>" + img + "</div>"));

        PdfAssert.EveryDrawnXObjectIsDefined(text).Should().Be(1);
    }

    [Fact]
    public void Jpeg_ExifOrientationSix_IsDrawnRotatedWithSwappedSize()
    {
        var pdf = HtmlToPdf.Render($"<img src='{DataUri("image/jpeg", JpegHeaderWithOrientation(4, 2, 6))}'>");

        DrawMatrix(PdfAssert.ValidPdf(pdf)).Should().StartWith("0.00 -3.00 1.50 0.00");
    }

    [Fact]
    public void Jpeg_ImageOrientationNone_IsDrawnAsStored()
    {
        var pdf = HtmlToPdf.Render($"<img style='image-orientation:none' width='4' height='2' src='{DataUri("image/jpeg", JpegHeaderWithOrientation(4, 2, 6))}'>");

        DrawMatrix(PdfAssert.ValidPdf(pdf)).Should().StartWith("3.00 0 0 1.50");
    }

    [Fact]
    public void Jpeg_ImageOrientationNoneWithoutSize_KeepsTheStoredSize()
    {
        var pdf = HtmlToPdf.Render($"<img style='image-orientation:none' src='{DataUri("image/jpeg", JpegHeaderWithOrientation(4, 2, 6))}'>");

        DrawMatrix(PdfAssert.ValidPdf(pdf)).Should().StartWith("3.00 0 0 1.50", "the 4x2 stored image is neither rotated nor squeezed into 2x4");
    }

    [Theory]
    [InlineData(2, "-3.00 0.00 0.00 1.50")]
    [InlineData(3, "-3.00 0.00 0.00 -1.50")]
    [InlineData(8, "0.00 1.50 -3.00 0.00")]
    public void Jpeg_OtherExifOrientations_UseTheMatchingMatrix(int orientation, string expectedMatrixStart)
    {
        bool swap = orientation >= 5;
        int storedWidth = swap ? 2 : 4, storedHeight = swap ? 4 : 2;
        var pdf = HtmlToPdf.Render($"<img src='{DataUri("image/jpeg", JpegHeaderWithOrientation(storedWidth, storedHeight, orientation))}'>");

        DrawMatrix(PdfAssert.ValidPdf(pdf)).Should().StartWith(expectedMatrixStart);
    }

    [Fact]
    public void ImageIntrinsicSize_ReadsGifBmpAndJpegHeaders()
    {
        var gif = new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 7, 0, 3, 0, 0, 0, 0 };

        ImageIntrinsicSize.TryRead(gif, out int gifWidth, out int gifHeight, out _).Should().BeTrue();
        (gifWidth, gifHeight).Should().Be((7, 3));
        ImageIntrinsicSize.TryRead(Bmp(5, 9), out int bmpWidth, out int bmpHeight, out _).Should().BeTrue();
        (bmpWidth, bmpHeight).Should().Be((5, 9));
        ImageIntrinsicSize.TryRead(JpegHeaderWithOrientation(12, 34, 8), out int jpegWidth, out int jpegHeight, out int orientation).Should().BeTrue();
        (jpegWidth, jpegHeight, orientation).Should().Be((12, 34, 8));
    }

    [Fact]
    public void ImageIntrinsicSize_UnknownBytes_ReturnFalse()
    {
        ImageIntrinsicSize.TryRead(new byte[32], out _, out _, out _).Should().BeFalse();
    }
}
