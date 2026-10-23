using System;
using System.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class JpegColorSpaceTests
{
    private static byte[] JpegHeader(int components, bool adobeMarker)
    {
        var bytes = new System.Collections.Generic.List<byte> { 0xFF, 0xD8 };
        if (adobeMarker)
            bytes.AddRange(new byte[] { 0xFF, 0xEE, 0x00, 0x0E, (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e', 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x02 });
        int length = 8 + 3 * components;
        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, (byte)length, 0x08, 0x00, 0x02, 0x00, 0x02, (byte)components });
        for (int i = 0; i < components; i++) bytes.AddRange(new byte[] { (byte)(i + 1), 0x11, 0x00 });
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });
        return bytes.ToArray();
    }

    private static string Render(byte[] jpeg) => Encoding.GetEncoding("ISO-8859-1").GetString(
        HtmlToPdf.Render($"<img src=\"data:image/jpeg;base64,{Convert.ToBase64String(jpeg)}\" width=\"20\" height=\"20\">"));

    [Fact]
    public void Jpeg_Grayscale_IsWrittenAsDeviceGray()
    {
        Render(JpegHeader(1, adobeMarker: false)).Should().Contain("/ColorSpace /DeviceGray");
    }

    [Fact]
    public void Jpeg_Rgb_IsWrittenAsDeviceRgb()
    {
        Render(JpegHeader(3, adobeMarker: false)).Should().Contain("/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode");
    }

    [Fact]
    public void Jpeg_AdobeCmyk_IsWrittenAsInvertedDeviceCmyk()
    {
        var pdf = Render(JpegHeader(4, adobeMarker: true));

        pdf.Should().Contain("/ColorSpace /DeviceCMYK").And.Contain("/Decode [1 0 1 0 1 0 1 0]");
    }
}
