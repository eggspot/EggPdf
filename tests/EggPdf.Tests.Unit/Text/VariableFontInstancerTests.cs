using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// Variable-font instancing, checked against Chrome using Windows' Bahnschrift (wght 300-700, wdth 75-100).
/// Tests return early on machines that don't have the font. The reference numbers were measured in Edge:
/// the width of a vertical stem ('I' at 400px) at each weight, and text widths at 1000px.
/// </summary>
public class VariableFontInstancerTests
{
    private static string FontPath(string file) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);

    private static FontData? Load(string file)
    {
        var path = FontPath(file);
        return File.Exists(path) ? TtfParser.Parse(File.ReadAllBytes(path)) : null;
    }

    /// <summary>Bounding box of a glyph from its glyf header.</summary>
    private static (int xMin, int yMin, int xMax, int yMax) GlyphBox(FontData font, ushort gid)
    {
        var d = font.RawData;
        int tables = (d[4] << 8) | d[5];
        int glyf = 0, loca = 0, head = 0;
        for (int i = 0; i < tables; i++)
        {
            int r = 12 + i * 16;
            string tag = "" + (char)d[r] + (char)d[r + 1] + (char)d[r + 2] + (char)d[r + 3];
            int off = (d[r + 8] << 24) | (d[r + 9] << 16) | (d[r + 10] << 8) | d[r + 11];
            if (tag == "glyf") glyf = off; else if (tag == "loca") loca = off; else if (tag == "head") head = off;
        }
        bool longLoca = ((d[head + 50] << 8) | d[head + 51]) != 0;
        int start = longLoca ? (d[loca + gid * 4] << 24) | (d[loca + gid * 4 + 1] << 16) | (d[loca + gid * 4 + 2] << 8) | d[loca + gid * 4 + 3]
                             : ((d[loca + gid * 2] << 8) | d[loca + gid * 2 + 1]) * 2;
        int end = longLoca ? (d[loca + gid * 4 + 4] << 24) | (d[loca + gid * 4 + 5] << 16) | (d[loca + gid * 4 + 6] << 8) | d[loca + gid * 4 + 7]
                           : ((d[loca + gid * 2 + 2] << 8) | d[loca + gid * 2 + 3]) * 2;
        if (end <= start) return (0, 0, 0, 0);
        short S(int o) => (short)((d[glyf + start + o] << 8) | d[glyf + start + o + 1]);
        return (S(2), S(4), S(6), S(8));
    }

    [Fact]
    public void GetAxes_Bahnschrift_ReportsWeightAndWidth()
    {
        var font = Load("bahnschrift.ttf");
        if (font == null) return;

        var axes = VariableFontInstancer.GetAxes(font);
        axes.Should().Contain(a => a.Tag == "wght" && a.Min == 300 && a.Default == 400 && a.Max == 700);
        axes.Should().Contain(a => a.Tag == "wdth" && a.Min == 75 && a.Default == 100);
    }

    [Fact]
    public void GetAxes_StaticFont_IsEmpty()
    {
        var arial = Load("arial.ttf");
        if (arial == null) return;
        VariableFontInstancer.GetAxes(arial).Should().BeEmpty();
    }

    [Theory]
    [InlineData(300, 28.13)]
    [InlineData(400, 39.84)]
    [InlineData(500, 43.75)]
    [InlineData(650, 51.56)]   // between masters: exercises avar remapping and tuple interpolation
    [InlineData(700, 55.47)]
    public void InstanceForWeight_StemWidthMatchesChrome(int weight, double chromeStemPx)
    {
        var font = Load("bahnschrift.ttf");
        if (font == null) return;

        var instance = VariableFontInstancer.InstanceForWeight(font, weight);
        var (xMin, _, xMax, _) = GlyphBox(instance, instance.GetGlyphId('I'));

        ((xMax - xMin) * 400.0 / instance.UnitsPerEm).Should().BeApproximately(chromeStemPx, 0.1,
            $"the 'I' stem of Bahnschrift at wght {weight}, measured in Chrome at 400px");
    }

    [Fact]
    public void InstanceForWeight_DefaultWeight_ReproducesTheStaticFont()
    {
        var font = Load("bahnschrift.ttf");
        if (font == null) return;

        var instance = VariableFontInstancer.InstanceForWeight(font, 400);

        instance.NumGlyphs.Should().Be(font.NumGlyphs);
        instance.UnitsPerEm.Should().Be(font.UnitsPerEm);
        for (ushort g = 0; g < Math.Min(font.NumGlyphs, (ushort)600); g++)
        {
            instance.GetAdvanceWidth(g).Should().Be(font.GetAdvanceWidth(g), $"glyph {g} advance at the default location");
            GlyphBox(instance, g).Should().Be(GlyphBox(font, g), $"glyph {g} bounds at the default location");
        }
    }

    [Fact]
    public void InstanceForWeight_AdvancesMatchChrome_WhichKeepsWidthConstantAcrossWeights()
    {
        var font = Load("bahnschrift.ttf");
        if (font == null) return;

        // Chrome: "Hamburgefonstiv" is 7648.93 wide at 1000px at every weight (this font's widths don't vary).
        // Chrome applies pair kerning (-36.6 here) that MeasureTextWidth leaves out, hence the constant offset.
        double first = 0;
        foreach (int weight in new[] { 300, 400, 650, 700 })
        {
            var instance = VariableFontInstancer.InstanceForWeight(font, weight);
            double width = instance.MeasureTextWidth("Hamburgefonstiv") * 1000.0 / instance.UnitsPerEm;
            if (first == 0) first = width;
            width.Should().BeApproximately(first, 0.01, "advances are identical at every weight");
            width.Should().BeInRange(7648.93, 7648.93 + 45, $"weight {weight}");
        }
    }

    [Fact]
    public void Instantiate_WidthAxis_NarrowsAdvancesLikeChromesCondensedStretch()
    {
        var font = Load("bahnschrift.ttf");
        if (font == null) return;

        var bytes = VariableFontInstancer.Instantiate(font.RawData, new Dictionary<string, float> { ["wdth"] = 75 });
        bytes.Should().NotBeNull();
        var condensed = TtfParser.Parse(bytes!)!;

        // Chrome: font-stretch 75% -> 5597.17 vs 7648.93 at 1000px, a ratio of 0.7318 (kerning aside)
        double narrow = condensed.MeasureTextWidth("Hamburgefonstiv") * 1000.0 / condensed.UnitsPerEm;
        double normal = font.MeasureTextWidth("Hamburgefonstiv") * 1000.0 / font.UnitsPerEm;
        (narrow / normal).Should().BeApproximately(5597.17 / 7648.93, 0.005);
    }

    [Fact]
    public void InstanceFor_WidthAxis_NarrowsTheFont_AndIsCached()
    {
        var font = Load("bahnschrift.ttf");
        if (font == null) return;

        var axes = new Dictionary<string, float> { ["wdth"] = 75 };
        var narrow = VariableFontInstancer.InstanceFor(font, 400, axes);

        narrow.Should().NotBeSameAs(font);
        (narrow.MeasureTextWidth("Hamburgefonstiv") / (double)narrow.UnitsPerEm)
            .Should().BeLessThan(font.MeasureTextWidth("Hamburgefonstiv") / (double)font.UnitsPerEm * 0.8);
        VariableFontInstancer.InstanceFor(font, 400, new Dictionary<string, float> { ["wdth"] = 75 }).Should().BeSameAs(narrow);
    }

    [Fact]
    public void InstanceFor_ExplicitWghtAxis_OverridesTheCssWeight()
    {
        var font = Load("bahnschrift.ttf");
        if (font == null) return;

        var viaAxis = VariableFontInstancer.InstanceFor(font, 400, new Dictionary<string, float> { ["wght"] = 700 });
        var viaWeight = VariableFontInstancer.InstanceFor(font, 700, null);
        GlyphBox(viaAxis, viaAxis.GetGlyphId('I')).Should().Be(GlyphBox(viaWeight, viaWeight.GetGlyphId('I')));
    }

    [Fact]
    public void InstanceFor_AxesTheFontLacks_ReturnTheFontUnchanged()
    {
        var arial = Load("arial.ttf");
        if (arial == null) return;
        VariableFontInstancer.InstanceFor(arial, 400, new Dictionary<string, float> { ["wdth"] = 75 }).Should().BeSameAs(arial);
    }

    [Fact]
    public async Task FontStretch_OnAnInstalledVariableFont_NarrowsTextSoItWrapsLater()
    {
        if (Load("bahnschrift.ttf") == null) return;

        string Html(string stretch) => "<html><head><style>@page{size:600px 400px;margin:0}body{margin:0}" +
            "div{width:210px;font-family:Bahnschrift;font-size:30px;line-height:40px;font-stretch:" + stretch + "}</style></head>" +
            "<body><div>Hamburg fonstiv</div></body></html>";

        int Lines(string pdf) => System.Text.RegularExpressions.Regex.Matches(pdf, @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Td")
            .Select(m => m.Groups[2].Value).Distinct().Count();

        var normal = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(Html("100%")));
        var condensed = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(Html("75%")));

        Lines(normal).Should().BeGreaterThan(Lines(condensed), "at 75% width the two words fit on one 210px line");
    }

    [Fact]
    public void InstanceForWeight_CompositeGlyph_FollowsItsComponents()
    {
        var font = Load("bahnschrift.ttf");
        if (font == null) return;

        ushort eAcute = font.GetGlyphId('é'); // composite: e + acute
        eAcute.Should().BeGreaterThan(0);

        var light = VariableFontInstancer.InstanceForWeight(font, 300);
        var bold = VariableFontInstancer.InstanceForWeight(font, 700);
        var (lx0, _, lx1, _) = GlyphBox(light, eAcute);
        var (bx0, _, bx1, _) = GlyphBox(bold, eAcute);

        (bx1 - bx0).Should().BeGreaterThan(lx1 - lx0, "the bold accented e is wider than the light one");
        bold.GetAdvanceWidth(eAcute).Should().Be(bold.GetAdvanceWidth(bold.GetGlyphId('e')));
    }

    [Fact]
    public void InstanceForWeight_ClampsToTheAxisRange()
    {
        var font = Load("bahnschrift.ttf");
        if (font == null) return;

        var over = VariableFontInstancer.InstanceForWeight(font, 900);
        var top = VariableFontInstancer.InstanceForWeight(font, 700);
        GlyphBox(over, over.GetGlyphId('I')).Should().Be(GlyphBox(top, top.GetGlyphId('I')));

        var under = VariableFontInstancer.InstanceForWeight(font, 100);
        var bottom = VariableFontInstancer.InstanceForWeight(font, 300);
        GlyphBox(under, under.GetGlyphId('I')).Should().Be(GlyphBox(bottom, bottom.GetGlyphId('I')));
    }

    [Fact]
    public void InstanceForWeight_IsCachedPerFontAndWeight()
    {
        var font = Load("bahnschrift.ttf");
        if (font == null) return;

        VariableFontInstancer.InstanceForWeight(font, 600).Should().BeSameAs(VariableFontInstancer.InstanceForWeight(font, 600));
        VariableFontInstancer.InstanceForWeight(font, 600).Should().NotBeSameAs(VariableFontInstancer.InstanceForWeight(font, 500));
    }

    [Fact]
    public void InstanceForWeight_StaticFont_ReturnsTheSameInstance()
    {
        var arial = Load("arial.ttf");
        if (arial == null) return;
        VariableFontInstancer.InstanceForWeight(arial, 700).Should().BeSameAs(arial);
    }

    [Fact]
    public void Instantiate_NonVariableOrGarbageData_ReturnsNull()
    {
        var coords = new Dictionary<string, float> { ["wght"] = 700 };
        VariableFontInstancer.Instantiate(new byte[] { 1, 2, 3 }, coords).Should().BeNull();

        var arial = Load("arial.ttf");
        if (arial != null) VariableFontInstancer.Instantiate(arial.RawData, coords).Should().BeNull();
    }

    [Fact]
    public async Task WebFontWithWeightRange_RendersWeightsAsDifferentInstances()
    {
        if (Load("bahnschrift.ttf") == null) return;

        string Html(int weight) => "<html><head><style>@font-face{font-family:'VF';src:local('Bahnschrift');font-weight:300 700}" +
            "p{font-family:'VF';font-size:30px;font-weight:" + weight + "}</style></head><body><p>Hamburg</p></body></html>";

        var light = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(Html(300)));
        var bold = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(Html(700)));

        light.Should().Contain("/FontFile2");
        bold.Should().Contain("/FontFile2");
        light.Should().NotBe(bold, "the embedded subsets are cut from different instances of the variable font");
    }
}
