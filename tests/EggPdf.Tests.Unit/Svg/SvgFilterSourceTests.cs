using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EggPdf.Html;
using EggPdf.Html.Dom;
using EggPdf.Pdf;
using EggPdf.Svg;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Svg;

/// <summary>What a filter's source graphic can contain: glyph outlines, gradients, holes, and feImage references.</summary>
public class SvgFilterSourceTests
{
    private const float PxPerUnit = 2f; // 100x100 viewBox at scale 1

    private static (string commands, List<string> images, PdfDocument doc) Render(string inner, string defs)
    {
        var html = "<html><body><svg width='100' height='100' viewBox='0 0 100 100'>" + defs + inner + "</svg></body></html>";
        var doc = HtmlParser.Parse(html);
        var svg = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "svg");
        var pdf = new PdfDocument();
        var (commands, images) = SvgRenderer.Render(SvgParser.Parse(svg)!, 0, 0, 100, 100, pdf);
        return (commands, images, pdf);
    }

    private static byte Alpha(PdfImage img, int x, int y) => img.SMaskData![y * img.Width + x];
    private static byte[] Rgb(PdfImage img, int x, int y)
    {
        int i = (y * img.Width + x) * 3;
        return new[] { img.Data[i], img.Data[i + 1], img.Data[i + 2] };
    }

    private const string Identity = "<filter id='i'><feOffset dx='0' dy='0'/></filter>";

    [Fact]
    public void Text_RasterizesGlyphOutlines_WhenAFontIsInstalled()
    {
        if (!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"))) return;

        var (commands, images, doc) = Render("<text x='10' y='60' font-family='Arial' font-size='50' fill='black' filter='url(#i)'>I</text>",
            "<defs>" + Identity + "</defs>");

        images.Should().HaveCount(1, "the glyph outlines are the filter's source graphic");
        commands.Should().NotContain("Tj");
        var img = doc.GetImage(images[0])!;
        bool ink = false;
        for (int i = 0; i < img.SMaskData!.Length; i++) ink |= img.SMaskData[i] > 200;
        ink.Should().BeTrue("the letter I has a solid stem");
    }

    [Fact]
    public void Text_CounterHoles_StayOpen()
    {
        if (!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"))) return;

        var (_, images, doc) = Render("<text x='10' y='80' font-family='Arial' font-size='80' fill='black' filter='url(#i)'>O</text>",
            "<defs>" + Identity + "</defs>");

        var img = doc.GetImage(images[0])!;
        // The O's counter is at its centre: transparent, while its stroke on the left is inked
        int cx = img.Width / 2, cy = img.Height / 2;
        Alpha(img, cx, cy).Should().Be(0, "the hole of the O");
        bool ink = false;
        for (int x = 0; x < cx; x++) ink |= Alpha(img, x, cy) > 200;
        ink.Should().BeTrue();
    }

    [Fact]
    public void LinearGradientFill_KeepsItsColours()
    {
        var (_, images, doc) = Render("<rect x='10' y='10' width='80' height='40' fill='url(#g)' filter='url(#i)'/>",
            "<defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient>" + Identity + "</defs>");

        var img = doc.GetImage(images[0])!;
        int x(float local) => (int)((local - 2) * PxPerUnit);   // region x origin = 10 - 8
        int y = (int)((30 - 6) * PxPerUnit);                    // region y origin = 10 - 4
        var left = Rgb(img, x(14), y); var right = Rgb(img, x(86), y);
        left[0].Should().BeGreaterThan(200); left[2].Should().BeLessThan(60);
        right[2].Should().BeGreaterThan(200); right[0].Should().BeLessThan(60);
    }

    [Fact]
    public void RadialGradientWithStopOpacity_FadesTowardTheRim()
    {
        var (_, images, doc) = Render("<circle cx='50' cy='50' r='40' fill='url(#g)' filter='url(#i)'/>",
            "<defs><radialGradient id='g'><stop offset='0' stop-color='#00ff00'/><stop offset='1' stop-color='#00ff00' stop-opacity='0'/></radialGradient>" + Identity + "</defs>");

        var img = doc.GetImage(images[0])!;
        int p(float local) => (int)((local - 2) * PxPerUnit); // bbox 10..90 plus 10% -> origin 2
        Alpha(img, p(50), p(50)).Should().BeGreaterThan(240, "opaque at the centre stop");
        Alpha(img, p(50), p(50)).Should().BeGreaterThan((byte)(Alpha(img, p(80), p(50)) + 60), "fading toward the rim");
    }

    [Fact]
    public void UnresolvableGradientReference_FallsBackToUnfilteredPainting()
    {
        var (_, images, _) = Render("<rect x='10' y='10' width='20' height='20' fill='url(#nope)' filter='url(#i)'/>", "<defs>" + Identity + "</defs>");
        images.Should().BeEmpty();
    }

    [Fact]
    public void PathWithHole_KeepsTheHole()
    {
        var (_, images, doc) = Render("<path d='M20 20 H80 V80 H20 Z M40 40 V60 H60 V40 Z' fill='black' filter='url(#i)'/>", "<defs>" + Identity + "</defs>");

        var img = doc.GetImage(images[0])!;
        int p(float local) => (int)((local - 14) * PxPerUnit);
        Alpha(img, p(30), p(50)).Should().BeGreaterThan(240, "the frame");
        Alpha(img, p(50), p(50)).Should().Be(0, "the hole");
    }

    [Fact]
    public void UseElement_IsFollowedIntoItsTarget()
    {
        var (_, images, doc) = Render("<g filter='url(#i)'><use href='#s' x='30' y='0'/></g>",
            "<defs><rect id='s' x='10' y='10' width='20' height='20' fill='blue'/>" + Identity + "</defs>");

        images.Should().HaveCount(1);
        var img = doc.GetImage(images[0])!;
        // Target is at (10..30) shifted by x=30 -> (40..60); region origin 40 - 2 = 38
        Rgb(img, (int)((50 - 38) * PxPerUnit), (int)((20 - 8) * PxPerUnit))[2].Should().BeGreaterThan(200);
    }

    [Fact]
    public void FeImage_ReferencingAnElement_DrawsThatElementInUserSpace()
    {
        var (_, images, doc) = Render("<rect x='10' y='10' width='80' height='80' fill='red' filter='url(#f)'/>",
            "<defs><filter id='f' x='0' y='0' width='1' height='1'><feImage href='#dot'/></filter>" +
            "<circle id='dot' cx='50' cy='50' r='20' fill='blue'/></defs>");

        var img = doc.GetImage(images[0])!;
        int p(float local) => (int)((local - 10) * PxPerUnit);
        var centre = Rgb(img, p(50), p(50));
        centre[2].Should().BeGreaterThan(200, "the referenced circle is drawn where it sits in user space");
        centre[0].Should().BeLessThan(60, "the filtered element's own red is replaced");
        Alpha(img, p(15), p(15)).Should().Be(0);
    }

    [Fact]
    public void FeImage_WithADataUriBitmap_FitsItIntoTheRegion()
    {
        var b64 = Convert.ToBase64String(SolidPng(0, 200, 0));
        var (_, images, doc) = Render("<rect x='20' y='20' width='60' height='60' fill='red' filter='url(#f)'/>",
            "<defs><filter id='f' x='0' y='0' width='1' height='1'><feImage href='data:image/png;base64," + b64 + "'/></filter></defs>");

        var img = doc.GetImage(images[0])!;
        var px = Rgb(img, img.Width / 2, img.Height / 2);
        px[1].Should().BeGreaterThan(150);
        px[0].Should().BeLessThan(60);
        Alpha(img, img.Width / 2, img.Height / 2).Should().Be(255);
    }

    /// <summary>A 2x2 opaque RGB PNG in one solid colour (stored, uncompressed deflate).</summary>
    private static byte[] SolidPng(byte r, byte g, byte b)
    {
        var raw = new List<byte>();
        for (int y = 0; y < 2; y++) { raw.Add(0); for (int x = 0; x < 2; x++) { raw.Add(r); raw.Add(g); raw.Add(b); } }

        var zlib = new List<byte> { 0x78, 0x01, 0x01 };
        int len = raw.Count;
        zlib.Add((byte)len); zlib.Add((byte)(len >> 8)); zlib.Add((byte)~len); zlib.Add((byte)(~len >> 8));
        zlib.AddRange(raw);
        uint a = 1, s2 = 0;
        foreach (byte x in raw) { a = (a + x) % 65521; s2 = (s2 + a) % 65521; }
        uint adler = (s2 << 16) | a;
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
        Chunk("IHDR", new byte[] { 0, 0, 0, 2, 0, 0, 0, 2, 8, 2, 0, 0, 0 });
        Chunk("IDAT", zlib.ToArray());
        Chunk("IEND", new byte[0]);
        return png.ToArray();
    }
}
