using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>shape-outside: url() through the whole pipeline: text wraps around the image's opaque pixels.</summary>
public class ShapeOutsideUrlE2ETests
{
    /// <summary>A 2x2 RGBA PNG whose left column is opaque black and right column fully transparent.</summary>
    private static string LeftHalfPngDataUri()
    {
        var raw = new List<byte>();
        for (int y = 0; y < 2; y++)
        {
            raw.Add(0);
            raw.AddRange(new byte[] { 0, 0, 0, 255 });   // left pixel: opaque
            raw.AddRange(new byte[] { 0, 0, 0, 0 });     // right pixel: transparent
        }

        var zlib = new List<byte> { 0x78, 0x01, 0x01 };
        int len = raw.Count;
        zlib.Add((byte)len); zlib.Add((byte)(len >> 8)); zlib.Add((byte)~len); zlib.Add((byte)(~len >> 8));
        zlib.AddRange(raw);
        uint a = 1, b = 0;
        foreach (byte x in raw) { a = (a + x) % 65521; b = (b + a) % 65521; }
        uint adler = (b << 16) | a;
        zlib.Add((byte)(adler >> 24)); zlib.Add((byte)(adler >> 16)); zlib.Add((byte)(adler >> 8)); zlib.Add((byte)adler);

        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        void Chunk(string type, byte[] data)
        {
            png.Add((byte)(data.Length >> 24)); png.Add((byte)(data.Length >> 16)); png.Add((byte)(data.Length >> 8)); png.Add((byte)data.Length);
            var body = new List<byte>();
            foreach (char c in type) body.Add((byte)c);
            body.AddRange(data);
            png.AddRange(body);
            uint crc = 0xFFFFFFFF;
            foreach (byte x in body) { crc ^= x; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1; }
            crc ^= 0xFFFFFFFF;
            png.Add((byte)(crc >> 24)); png.Add((byte)(crc >> 16)); png.Add((byte)(crc >> 8)); png.Add((byte)crc);
        }
        Chunk("IHDR", new byte[] { 0, 0, 0, 2, 0, 0, 0, 2, 8, 6, 0, 0, 0 });
        Chunk("IDAT", zlib.ToArray());
        Chunk("IEND", new byte[0]);
        return "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
    }

    private static List<float> TextXs(string pdf)
        => Regex.Matches(pdf, @"BT [^\r\n]*? (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Td \([^\r\n]*\) Tj")
            .Where(m => float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) > 225f) // lines beside the 100px float (page is 300pt tall)
            .Select(m => float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();

    private static string Html(string floatStyle, string uri) =>
        "<html><head><style>@page{size:600px 400px;margin:0}body{margin:0}</style></head><body>" +
        "<img src='" + uri + "' width='100' height='100' style='float:left;" + floatStyle + "'>" +
        "<p style='margin:0'>" + string.Join(" ", Enumerable.Repeat("word", 40)) + "</p></body></html>";

    [Fact]
    public async Task ShapeOutsideUrl_WrapsTextAgainstTheOpaquePixelsOnly()
    {
        var uri = LeftHalfPngDataUri();
        var plain = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(Html("", uri)));
        var shaped = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(Html("shape-outside:url(" + uri + ");", uri)));

        var plainX = TextXs(plain).Min();
        var shapedX = TextXs(shaped).Min();

        plainX.Should().BeApproximately(75f, 1f, "the plain float excludes its whole 100px (75pt) box");
        shapedX.Should().BeApproximately(37.5f, 1f, "only the opaque left half (50px = 37.5pt) is excluded");
    }
}
