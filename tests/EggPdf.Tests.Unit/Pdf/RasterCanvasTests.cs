using System.Collections.Generic;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class RasterCanvasTests
{
    private static (byte r, byte g, byte b, byte a) GetPixel(RasterCanvas c, int x, int y)
    {
        int idx = (y * c.Width + x) * 4;
        return (c.Pixels[idx], c.Pixels[idx + 1], c.Pixels[idx + 2], c.Pixels[idx + 3]);
    }

    [Fact]
    public void FillPolygon_Square_InteriorIsFullyOpaqueInRequestedColor()
    {
        var canvas = new RasterCanvas(20, 20);
        var square = new List<(float x, float y)> { (5, 5), (15, 5), (15, 15), (5, 15) };
        canvas.FillPolygon(square, 255, 0, 0, 255);

        var center = GetPixel(canvas, 10, 10);
        center.r.Should().Be(255);
        center.g.Should().Be(0);
        center.b.Should().Be(0);
        center.a.Should().Be(255);
    }

    [Fact]
    public void FillPolygon_Square_ExteriorStaysFullyTransparent()
    {
        var canvas = new RasterCanvas(20, 20);
        var square = new List<(float x, float y)> { (5, 5), (15, 5), (15, 15), (5, 15) };
        canvas.FillPolygon(square, 255, 0, 0, 255);

        var corner = GetPixel(canvas, 1, 1);
        corner.a.Should().Be(0, "pixels outside the polygon must remain fully transparent");
    }

    [Fact]
    public void FillPolygon_EdgePixel_HasPartialCoverageForAntiAliasing()
    {
        var canvas = new RasterCanvas(20, 20);
        // A triangle with a diagonal edge crossing pixel (10,10) partially
        var tri = new List<(float x, float y)> { (2, 2), (18, 2), (2, 18) };
        canvas.FillPolygon(tri, 0, 0, 255, 255);

        var edgePixel = GetPixel(canvas, 10, 9); // near the hypotenuse
        // Should be neither fully 0 nor fully 255 for a pixel straddling the diagonal edge
        (edgePixel.a > 0 && edgePixel.a < 255).Should().BeTrue(
            "a pixel straddling a diagonal polygon edge should get fractional (anti-aliased) coverage");
    }

    [Fact]
    public void FillPolygon_TooFewPoints_LeavesTheCanvasTransparent()
    {
        var canvas = new RasterCanvas(10, 10);
        canvas.FillPolygon(new List<(float, float)> { (1, 1), (2, 2) }, 255, 0, 0, 255);
        canvas.Pixels.Should().OnlyContain(b => b == 0, "a two-point polygon has no area to fill");
    }

    [Fact]
    public void FillEllipse_CenterIsOpaque_FarOutsideIsTransparent()
    {
        var canvas = new RasterCanvas(40, 40);
        canvas.FillEllipse(20, 20, 10, 10, 0, 255, 0, 255);

        GetPixel(canvas, 20, 20).a.Should().Be(255);
        GetPixel(canvas, 1, 1).a.Should().Be(0);
    }

    [Fact]
    public void FlattenCubicBezier_ProducesRequestedNumberOfPoints()
    {
        var output = new List<(float x, float y)>();
        RasterCanvas.FlattenCubicBezier(0, 0, 0, 10, 10, 10, 10, 0, output, steps: 8);
        output.Should().HaveCount(8);
    }

    [Fact]
    public void FlattenCubicBezier_EndpointMatchesCurveEnd()
    {
        var output = new List<(float x, float y)>();
        RasterCanvas.FlattenCubicBezier(0, 0, 0, 10, 10, 10, 10, 0, output, steps: 16);
        output[output.Count - 1].Item1.Should().BeApproximately(10f, 0.01f);
        output[output.Count - 1].Item2.Should().BeApproximately(0f, 0.01f);
    }
}
