using EggPdf.Svg;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Svg;

/// <summary>feTurbulence, feConvolveMatrix, feDisplacementMap, feTile, feDiffuse/SpecularLighting.</summary>
public class SvgFilterAdvancedTests
{
    private static SvgElement El(string tag, params (string k, string v)[] attrs)
    {
        var e = new SvgElement { TagName = tag };
        foreach (var (k, v) in attrs) e.Attributes[k] = v;
        return e;
    }

    private static SvgFilter Filter(params SvgElement[] primitives)
    {
        var f = El("filter", ("id", "f"), ("color-interpolation-filters", "sRGB"));
        f.Children.AddRange(primitives);
        return SvgFilter.TryParse(f)!;
    }

    private static FilterImage Rect(int w, int h, int x0, int y0, int x1, int y1, float r, float g, float b)
    {
        var img = new FilterImage(w, h);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = (y * w + x) * 4;
                img.Px[i] = r; img.Px[i + 1] = g; img.Px[i + 2] = b; img.Px[i + 3] = 1f;
            }
        return img;
    }

    private static FilterRunContext Ctx() => new FilterRunContext(1, 1, 1, 0, 0, 1, 0, 0);
    private static float[] At(FilterImage img, int x, int y)
    {
        int i = (y * img.Width + x) * 4;
        return new[] { img.Px[i], img.Px[i + 1], img.Px[i + 2], img.Px[i + 3] };
    }

    // ── feTurbulence ─────────────────────────────────────────────────────────

    [Fact]
    public void Turbulence_IsDeterministicForASeed_AndDiffersBetweenSeeds()
    {
        var src = new FilterImage(32, 32);
        var a = Filter(El("feturbulence", ("basefrequency", "0.1"), ("numoctaves", "2"), ("seed", "3"))).Run(src, Ctx());
        var b = Filter(El("feturbulence", ("basefrequency", "0.1"), ("numoctaves", "2"), ("seed", "3"))).Run(src, Ctx());
        var c = Filter(El("feturbulence", ("basefrequency", "0.1"), ("numoctaves", "2"), ("seed", "9"))).Run(src, Ctx());

        a.Px.Should().Equal(b.Px);
        a.Px.Should().NotEqual(c.Px);
    }

    [Fact]
    public void Turbulence_FractalNoise_IsCenteredAroundHalfGrayAndSpreads()
    {
        var f = Filter(El("feturbulence", ("type", "fractalNoise"), ("basefrequency", "0.05"), ("numoctaves", "3")));
        var result = f.Run(new FilterImage(64, 64), Ctx());

        double sum = 0; float min = 1, max = 0; int n = 0;
        for (int i = 3; i < result.Px.Length; i += 4)
        {
            float alpha = result.Px[i];
            if (alpha <= 0f) continue;
            float value = result.Px[i - 3] / alpha; // straight red
            sum += value; min = System.Math.Min(min, value); max = System.Math.Max(max, value); n++;
        }
        (sum / n).Should().BeApproximately(0.5, 0.12, "fractal noise is (noise + 1) / 2");
        (max - min).Should().BeGreaterThan(0.2f, "the field varies across the canvas");
    }

    [Fact]
    public void Turbulence_ZeroBaseFrequency_GivesAConstantField()
    {
        var f = Filter(El("feturbulence", ("type", "turbulence"), ("basefrequency", "0")));
        var result = f.Run(new FilterImage(8, 8), Ctx());
        result.Px.Should().OnlyContain(v => v == 0f, "turbulence with no frequency has no energy: transparent black");
    }

    [Fact]
    public void Turbulence_StitchTiles_MakesOppositeEdgesContinuous()
    {
        var f = Filter(El("feturbulence", ("type", "turbulence"), ("basefrequency", "0.07"), ("stitchtiles", "stitch"), ("x", "0"), ("y", "0"), ("width", "40"), ("height", "40")));
        var r = f.Run(new FilterImage(40, 40), Ctx());
        // With stitching the lattice wraps every tile, so column 0 continues from column 39
        float step = System.Math.Abs(At(r, 39, 10)[0] - At(r, 38, 10)[0]);
        float wrap = System.Math.Abs(At(r, 0, 10)[0] - At(r, 39, 10)[0]);
        wrap.Should().BeLessThan(step + 0.06f);
    }

    // ── feConvolveMatrix ─────────────────────────────────────────────────────

    [Fact]
    public void ConvolveMatrix_IdentityKernel_LeavesImageUnchanged()
    {
        var src = Rect(8, 8, 2, 2, 6, 6, 0.4f, 0.6f, 0.8f);
        var f = Filter(El("feconvolvematrix", ("order", "3"), ("kernelmatrix", "0 0 0 0 1 0 0 0 0")));
        var r = f.Run(src, Ctx());
        for (int i = 0; i < src.Px.Length; i++) r.Px[i].Should().BeApproximately(src.Px[i], 0.001f);
    }

    [Fact]
    public void ConvolveMatrix_BoxBlurKernel_SpreadsAlphaToNeighbours()
    {
        var src = Rect(9, 9, 4, 4, 5, 5, 1, 1, 1);
        var f = Filter(El("feconvolvematrix", ("order", "3"), ("kernelmatrix", "1 1 1 1 1 1 1 1 1"), ("edgemode", "none")));
        var r = f.Run(src, Ctx());
        At(r, 4, 4)[3].Should().BeApproximately(1f / 9f, 0.001f);
        At(r, 3, 3)[3].Should().BeApproximately(1f / 9f, 0.001f);
        At(r, 6, 6)[3].Should().Be(0f);
    }

    [Fact]
    public void ConvolveMatrix_KernelIsAppliedFlipped_ShiftingByTheAsymmetry()
    {
        // The spec rotates the kernel 180 degrees: weight in its left column samples the pixel to the right, so content shifts left
        var src = Rect(9, 9, 4, 4, 5, 5, 1, 1, 1);
        var f = Filter(El("feconvolvematrix", ("order", "3"), ("kernelmatrix", "0 0 0 1 0 0 0 0 0"), ("divisor", "1"), ("edgemode", "none")));
        var r = f.Run(src, Ctx());
        At(r, 3, 4)[3].Should().BeApproximately(1f, 0.001f);
        At(r, 4, 4)[3].Should().Be(0f);
    }

    [Fact]
    public void ConvolveMatrix_InvalidKernel_LeavesImageUntouched()
    {
        var src = Rect(6, 6, 1, 1, 4, 4, 1, 0, 0);
        var f = Filter(El("feconvolvematrix", ("order", "3"), ("kernelmatrix", "1 2")));
        var r = f.Run(src, Ctx());
        for (int i = 0; i < src.Px.Length; i++) r.Px[i].Should().BeApproximately(src.Px[i], 0.001f);
    }

    // ── feDisplacementMap ────────────────────────────────────────────────────

    [Fact]
    public void DisplacementMap_ShiftsPixelsByTheMapChannelTimesScale()
    {
        var src = Rect(20, 10, 10, 2, 14, 8, 1, 0, 0);
        // The map is opaque with R = 1.0 everywhere: displacement = scale * (1 - 0.5) = +scale/2 on x
        var flood = El("feflood", ("flood-color", "#ff0000"), ("result", "map"));
        var disp = El("fedisplacementmap", ("in", "SourceGraphic"), ("in2", "map"), ("scale", "6"), ("xchannelselector", "R"), ("ychannelselector", "R"));
        var r = Filter(flood, disp).Run(src, Ctx());

        // Output(x) samples source(x + 3): the red block appears 3px to the left
        At(r, 7, 4)[3].Should().BeApproximately(1f, 0.001f);
        At(r, 13, 4)[3].Should().Be(0f);
    }

    // ── feTile ───────────────────────────────────────────────────────────────

    [Fact]
    public void Tile_RepeatsTheInputSubregionAcrossTheCanvas()
    {
        var src = Rect(20, 20, 0, 0, 5, 5, 1, 0, 0);
        var crop = El("feoffset", ("dx", "0"), ("dy", "0"), ("x", "0"), ("y", "0"), ("width", "10"), ("height", "10"), ("result", "cell"));
        var tile = El("fetile", ("in", "cell"));
        var r = Filter(crop, tile).Run(src, Ctx());

        At(r, 12, 2)[3].Should().BeApproximately(1f, 0.001f, "the red corner of the 10x10 cell repeats at x=10");
        At(r, 7, 2)[3].Should().Be(0f);
        At(r, 12, 12)[3].Should().BeApproximately(1f, 0.001f);
    }

    // ── Lighting ─────────────────────────────────────────────────────────────

    private static FilterImage Bump() // a square plateau of alpha 1 on a transparent field
        => Rect(21, 21, 6, 6, 15, 15, 0, 0, 0);

    [Fact]
    public void DiffuseLighting_FlatSurface_IsLitByNDotL()
    {
        // Flat alpha (all opaque): normal is (0,0,1); elevation 45deg -> N.L = sin(45deg)
        var src = Rect(9, 9, 0, 0, 9, 9, 0, 0, 0);
        var light = El("fedistantlight", ("azimuth", "0"), ("elevation", "45"));
        var d = El("fediffuselighting", ("surfacescale", "1"), ("diffuseconstant", "1"), ("lighting-color", "#ffffff"));
        d.Children.Add(light);
        var px = At(Filter(d).Run(src, Ctx()), 4, 4);

        px[0].Should().BeApproximately((float)System.Math.Sin(System.Math.PI / 4), 0.02f);
        px[3].Should().Be(1f, "diffuse lighting output is opaque");
    }

    [Fact]
    public void DiffuseLighting_LightFromTheLeft_BrightensLeftFacingSlopes()
    {
        var src = Bump();
        var light = El("fedistantlight", ("azimuth", "180"), ("elevation", "30")); // light comes from -x
        var d = El("fediffuselighting", ("surfacescale", "6"), ("diffuseconstant", "1"));
        d.Children.Add(light);
        var r = Filter(d).Run(src, Ctx());

        // Left edge of the plateau faces -x (toward the light); right edge faces away
        At(r, 5, 10)[0].Should().BeGreaterThan(At(r, 15, 10)[0] + 0.1f);
    }

    [Fact]
    public void SpecularLighting_ProducesAHighlightWhereTheNormalHalvesTheLightAndEye()
    {
        var src = Rect(9, 9, 0, 0, 9, 9, 0, 0, 0);
        var light = El("fedistantlight", ("azimuth", "0"), ("elevation", "90")); // straight overhead
        var s = El("fespecularlighting", ("surfacescale", "1"), ("specularconstant", "1"), ("specularexponent", "10"));
        s.Children.Add(light);
        var px = At(Filter(s).Run(src, Ctx()), 4, 4);

        px[0].Should().BeApproximately(1f, 0.02f, "N.H = 1 on a flat surface lit from above");
        px[3].Should().BeApproximately(1f, 0.02f, "specular alpha is the strongest channel");
    }

    [Fact]
    public void PointLight_DimsWithSurfaceAngleAwayFromTheLightPosition()
    {
        var src = Rect(41, 3, 0, 0, 41, 3, 0, 0, 0);
        var point = El("fepointlight", ("x", "20"), ("y", "1"), ("z", "10"));
        var d = El("fediffuselighting", ("surfacescale", "1"), ("diffuseconstant", "1"));
        d.Children.Add(point);
        var r = Filter(d).Run(src, Ctx());

        At(r, 20, 1)[0].Should().BeGreaterThan(At(r, 2, 1)[0], "directly below the light is brighter than far to the side");
    }

    [Fact]
    public void LightingWithoutALightSource_IsUnparseable()
    {
        var f = El("filter", ("id", "x"));
        f.Children.Add(El("fediffuselighting"));
        SvgFilter.TryParse(f).Should().BeNull("a lighting primitive needs a light source child");
    }
}
