using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class CffFontTests
{
    private static byte[] Num(int v)
    {
        if (v >= -107 && v <= 107) return new[] { (byte)(v + 139) };
        return new byte[] { 28, (byte)(v >> 8), (byte)v };
    }

    private static byte[] Cs(params object[] parts)
    {
        var bytes = new List<byte>();
        foreach (var part in parts)
        {
            if (part is int n) bytes.AddRange(Num(n));
            else if (part is byte op) bytes.Add(op);
        }
        return bytes.ToArray();
    }

    private static byte[] Index(params byte[][] items)
    {
        var bytes = new List<byte> { (byte)(items.Length >> 8), (byte)items.Length };
        if (items.Length == 0) return bytes.ToArray();
        const byte twoByteOffsets = 2;
        bytes.Add(twoByteOffsets);
        int offset = 1;
        void Off(int v) { bytes.Add((byte)(v >> 8)); bytes.Add((byte)v); }
        Off(offset);
        foreach (var item in items) { offset += item.Length; Off(offset); }
        foreach (var item in items) bytes.AddRange(item);
        return bytes.ToArray();
    }

    private static byte[] Be16(int v) => new[] { (byte)(v >> 8), (byte)v };
    private static byte[] Be32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    /// <summary>
    /// A four-glyph CFF font, assembled byte by byte so the charstring interpreter is exercised in CI:
    /// .notdef; a rectangle (A) at x 100..200, y 0..700 drawn with alternating h/v lines; a 300-unit square (B)
    /// drawn by global subroutine 0 (300 hlineto, 300 vlineto); and one cubic curve (C) closed by an implicit line.
    /// </summary>
    private static byte[] BuildSyntheticOtf()
    {
        const byte rmoveto = 21, hlineto = 6, vlineto = 7, rrcurveto = 8, callgsubr = 29, endchar = 14, ret = 11;
        const int unitsPerEm = 1000, ascent = 800, numberOfHMetrics = 4;

        var glyphs = new[]
        {
            Cs(endchar),
            Cs(100, 0, rmoveto, 100, 700, -100, hlineto, endchar),
            Cs(0, 0, rmoveto, -107, callgsubr, -300, hlineto, endchar),
            Cs(0, 0, rmoveto, 100, 0, 0, 100, -100, 0, rrcurveto, endchar),
        };
        var gsubrs = Index(Cs(300, hlineto, 300, vlineto, ret));

        var header = new byte[] { 1, 0, 4, 1 };
        var name = Index(new byte[] { (byte)'T' });
        var strings = Index();
        byte[] TopDict(int charStringsOffset) => Index(new[] { (byte)29 }.Concat(Be32((uint)charStringsOffset)).Concat(new byte[] { 17 }).ToArray());

        int topLength = TopDict(0).Length;
        int charStringsAt = header.Length + name.Length + topLength + strings.Length + gsubrs.Length;
        var cff = header.Concat(name).Concat(TopDict(charStringsAt)).Concat(strings).Concat(gsubrs).Concat(Index(glyphs)).ToArray();

        var head = new byte[54];
        head[18] = unitsPerEm >> 8; head[19] = unitsPerEm & 0xFF;
        var hhea = new byte[36];
        hhea[4] = ascent >> 8; hhea[5] = ascent & 0xFF;
        hhea[34] = numberOfHMetrics >> 8; hhea[35] = numberOfHMetrics & 0xFF;
        var maxp = Be32(0x00005000).Concat(Be16(4)).ToArray();
        var hmtx = new List<byte>();
        foreach (int advance in new[] { 500, 400, 600, 500 }) { hmtx.AddRange(Be16(advance)); hmtx.AddRange(Be16(0)); }

        const int cmapVersion = 0, encodingRecordCount = 1;
        var cmap = new List<byte>();
        cmap.AddRange(Be16(cmapVersion)); cmap.AddRange(Be16(encodingRecordCount));
        cmap.AddRange(Be16(3)); cmap.AddRange(Be16(10)); cmap.AddRange(Be32(12));
        cmap.AddRange(Be16(12)); cmap.AddRange(Be16(0)); cmap.AddRange(Be32(28)); cmap.AddRange(Be32(0)); cmap.AddRange(Be32(1));
        cmap.AddRange(Be32(65)); cmap.AddRange(Be32(67)); cmap.AddRange(Be32(1));

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["CFF "] = cff, ["cmap"] = cmap.ToArray(), ["head"] = head, ["hhea"] = hhea, ["hmtx"] = hmtx.ToArray(), ["maxp"] = maxp,
        };
        var file = SfntWriter.WriteFont(tables);
        file[0] = (byte)'O'; file[1] = (byte)'T'; file[2] = (byte)'T'; file[3] = (byte)'O';
        return file;
    }

    private static (float xMin, float yMin, float xMax, float yMax) Bounds(FontData font, int gid)
    {
        var points = GlyphOutlines.Get(font, gid).SelectMany(c => c).ToList();
        return (points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
    }

    [Fact]
    public void Parse_SyntheticOtf_ConvertsToTrueTypeWithMetricsAndCmap()
    {
        var font = TtfParser.Parse(BuildSyntheticOtf());

        font.Should().NotBeNull();
        font!.NumGlyphs.Should().Be(4);
        font.GetGlyphId('A').Should().Be(1);
        font.GetGlyphId('C').Should().Be(3);
        font.GetAdvanceWidth(2).Should().Be(600);
        font.RawData.Take(4).Should().Equal(new byte[] { 0, 1, 0, 0 }, "the converted font is glyf-flavoured (sfnt version 0x00010000)");
    }

    [Fact]
    public void Parse_SyntheticRectangle_HasExactBounds()
    {
        var font = TtfParser.Parse(BuildSyntheticOtf())!;
        var (xMin, yMin, xMax, yMax) = Bounds(font, 1);

        xMin.Should().Be(100); xMax.Should().Be(200);
        yMin.Should().Be(0); yMax.Should().Be(700);
        GlyphOutlines.Get(font, 1).Should().ContainSingle().Which.Count.Should().Be(4);
    }

    [Fact]
    public void Parse_SyntheticSubroutine_ExecutesGlobalSubr()
    {
        var font = TtfParser.Parse(BuildSyntheticOtf())!;
        var (xMin, yMin, xMax, yMax) = Bounds(font, 2);

        (xMin, yMin, xMax, yMax).Should().Be((0f, 0f, 300f, 300f));
    }

    [Fact]
    public void Parse_SyntheticCubic_BecomesQuadraticCloseToTheCurve()
    {
        var font = TtfParser.Parse(BuildSyntheticOtf())!;
        var points = GlyphOutlines.Get(font, 3).SelectMany(c => c).ToList();

        points.Max(p => p.x).Should().BeInRange(74.5f, 75.5f,
            "the cubic (0,0) (100,0) (100,100) (0,100) bulges to x = 75 at t = 0.5 and never passes 75.01");
        points.Max(p => p.y).Should().BeApproximately(100f, 0.6f);
        points.Should().Contain(p => Math.Abs(p.y - 50f) < 3f && p.x > 70f);
    }

    [Fact]
    public void Parse_MalformedCff_DoesNotThrow()
    {
        var data = BuildSyntheticOtf();
        for (int i = data.Length - 40; i < data.Length; i++) data[i] = 0xFF;

        var act = () => TtfParser.Parse(data);
        act.Should().NotThrow();

        var font = act();
        if (font != null)
        {
            font.UnitsPerEm.Should().BeGreaterThan(0, "a corrupted CFF table is rejected or yields a usable font, never a half-built one");
            font.NumGlyphs.Should().BeGreaterThan(0, "a corrupted CFF table is rejected or yields a usable font, never a half-built one");
        }
    }

    /// <summary>
    /// A real font from EGGPDF_TEST_CFF_DIR (or ~/cfftest), or null when it isn't there; the tests that use it
    /// compare against measurements taken in Chrome and are skipped on machines without the fonts.
    /// </summary>
    private static byte[]? LoadFile(string name)
    {
        var dir = Environment.GetEnvironmentVariable("EGGPDF_TEST_CFF_DIR")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "cfftest");
        var path = Path.Combine(dir, name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    [Fact]
    public void Parse_SourceSans3Regular_ConvertsAndMeasures()
    {
        var bytes = LoadFile("SourceSans3-Regular.otf");
        if (bytes == null) return;

        var font = TtfParser.Parse(bytes)!;
        font.UnitsPerEm.Should().Be(1000);
        var gid = font.GetGlyphId('I');
        gid.Should().BeGreaterThan((ushort)0);

        var (xMin, yMin, xMax, yMax) = Bounds(font, gid);
        (xMax - xMin).Should().BeInRange(70f, 110f, "the width of a sans-serif capital I is its vertical stem");
        yMin.Should().BeApproximately(0f, 1f);
        yMax.Should().BeApproximately(660f, 5f, "the cap height");
    }

    [Fact]
    public void Parse_SourceSans3_BoldStemIsWiderThanRegular()
    {
        var regular = LoadFile("SourceSans3-Regular.otf");
        var bold = LoadFile("SourceSans3-Bold.otf");
        if (regular == null || bold == null) return;

        float Stem(byte[] data)
        {
            var font = TtfParser.Parse(data)!;
            var (xMin, _, xMax, _) = Bounds(font, font.GetGlyphId('I'));
            return xMax - xMin;
        }
        Stem(bold).Should().BeGreaterThan(Stem(regular) * 1.4f);
    }

    [Theory]
    [InlineData(200, 30)]
    [InlineData(400, 85)]
    [InlineData(650, 130)]
    [InlineData(900, 175)]
    public void InstanceForWeight_Cff2VariableFont_StemMatchesChrome(int weight, double chromeStemUnits)
    {
        var bytes = LoadFile("SourceSans3VF-Upright.otf");
        if (bytes == null) return;

        var font = TtfParser.Parse(bytes)!;
        VariableFontInstancer.GetAxes(font).Should().Contain(a => a.Tag == "wght");

        float Stem(int w)
        {
            var instance = VariableFontInstancer.InstanceForWeight(font, w);
            var (xMin, _, xMax, _) = Bounds(instance, instance.GetGlyphId('I'));
            return xMax - xMin;
        }
        Stem(weight).Should().BeApproximately((float)chromeStemUnits, 7f,
            "Chrome draws the 'I' stem 6, 17, 26 and 35px wide at 200px for these weights, which is these units of the 1000-unit em");
    }
}
