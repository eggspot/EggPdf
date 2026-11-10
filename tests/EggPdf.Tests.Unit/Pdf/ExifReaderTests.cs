using System.Collections.Generic;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class ExifReaderTests
{
    private static byte[] Segment(byte marker, byte[] payload)
    {
        int length = payload.Length + 2;
        var bytes = new List<byte> { 0xFF, marker, (byte)(length >> 8), (byte)length };
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    private static byte[] ExifWithOrientation(int orientation)
    {
        var payload = new List<byte>(Encoding.ASCII.GetBytes("Exif\0\0"));
        payload.AddRange(new byte[] { (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8 });
        payload.AddRange(new byte[] { 0, 1 });
        payload.AddRange(new byte[] { 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, (byte)orientation, 0, 0 });
        payload.AddRange(new byte[] { 0, 0, 0, 0 });
        return Segment(0xE1, payload.ToArray());
    }

    private static byte[] Jpeg(params byte[][] segments)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        foreach (var segment in segments) bytes.AddRange(segment);
        bytes.AddRange(new byte[] { 0xFF, 0xDA, 0, 2 });
        return bytes.ToArray();
    }

    [Fact]
    public void GetOrientation_ExifSegment_ReadsTheTag()
    {
        ExifReader.GetOrientation(Jpeg(ExifWithOrientation(6))).Should().Be(6);
    }

    [Fact]
    public void GetOrientation_XmpSegmentBeforeExif_StillFindsTheTag()
    {
        var xmp = Segment(0xE1, Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta/>"));

        ExifReader.GetOrientation(Jpeg(xmp, ExifWithOrientation(8))).Should().Be(8);
    }
}
