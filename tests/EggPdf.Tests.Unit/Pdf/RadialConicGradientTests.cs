using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class RadialConicGradientTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static List<(float R, float G, float B)> Fills(string content) =>
        Regex.Matches(content, @"(\d+\.\d+) (\d+\.\d+) (\d+\.\d+) rg")
            .Cast<Match>()
            .Select(m => (F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value)))
            .ToList();

    [Fact]
    public void Radial_FirstStopIsAtTheCentreAndLastAtTheEdge()
    {
        var content = PdfRadialGradient.Render("radial-gradient(red, blue)", 0, 0, 100, 100)!;

        var fills = Fills(content);
        fills.First().B.Should().BeGreaterThan(fills.First().R, "the outermost band is the last stop");
        fills.Last().R.Should().BeGreaterThan(fills.Last().B, "the innermost band is the first stop");
    }

    [Fact]
    public void Radial_StopPositions_AreHonoured()
    {
        var content = PdfRadialGradient.Render("radial-gradient(red 50%, blue 50%)", 0, 0, 100, 100)!;

        var fills = Fills(content);
        fills.Should().Contain(f => f.R > 0.99f && f.B < 0.01f);
        fills.Should().Contain(f => f.B > 0.99f && f.R < 0.01f);
    }

    [Fact]
    public void Radial_AtLeftTop_CentresTheShapeInTheTopLeftCorner()
    {
        var content = PdfRadialGradient.Render("radial-gradient(circle at left top, red, blue)", 0, 0, 100, 100)!;

        var lastMove = Regex.Matches(content, @"(-?\d+\.\d+) (-?\d+\.\d+) m").Cast<Match>().Last();
        F(lastMove.Groups[2].Value).Should().BeApproximately(100f, 0.5f, "the centre sits on the top edge");
        F(lastMove.Groups[1].Value).Should().BeLessThan(5f, "the innermost ring starts just right of the left edge");
    }

    [Fact]
    public void Radial_TranslucentStops_UseOpacityStatesAndEvenOddRings()
    {
        var states = new List<string>();
        var content = PdfRadialGradient.Render("radial-gradient(rgba(255,0,0,0.5), rgba(0,0,255,0.5))", 0, 0, 100, 100, states)!;

        states.Should().Contain("GS50");
        content.Should().Contain("f*");
    }

    [Fact]
    public void Radial_Repeating_CyclesTheStops()
    {
        var content = PdfRadialGradient.Render("repeating-radial-gradient(circle, red 0px, red 10px, blue 10px, blue 20px)", 0, 0, 200, 200)!;

        var fills = Fills(content);
        int switches = 0;
        for (int i = 1; i < fills.Count; i++)
            if ((fills[i].R > 0.5f) != (fills[i - 1].R > 0.5f)) switches++;
        switches.Should().BeGreaterThan(4);
    }

    [Fact]
    public void Conic_StartsAtTwelveOClockAndGoesClockwise()
    {
        var content = PdfConicGradient.Render("conic-gradient(red, blue)", 0, 0, 100, 100)!;

        var sector = Regex.Match(content, @"(\d+\.\d+) (\d+\.\d+) m (-?\d+\.\d+) (-?\d+\.\d+) l (-?\d+\.\d+) (-?\d+\.\d+) l h f");
        sector.Success.Should().BeTrue();
        float cx = F(sector.Groups[1].Value), cy = F(sector.Groups[2].Value);
        F(sector.Groups[3].Value).Should().BeApproximately(cx, 0.01f);
        F(sector.Groups[4].Value).Should().BeGreaterThan(cy, "the first edge points straight up");
        F(sector.Groups[5].Value).Should().BeGreaterThan(cx, "the sector turns clockwise, to the right");
        Fills(content).First().R.Should().BeGreaterThan(0.9f);
    }

    [Fact]
    public void Radial_EdgeOffsetPosition_CentresTheShapeAtTheOffsets()
    {
        var content = PdfRadialGradient.Render("radial-gradient(circle 1px at right 10pt bottom 20pt, red, blue)", 0, 0, 100, 100)!;

        var lastMove = Regex.Matches(content, @"(-?\d+\.\d+) (-?\d+\.\d+) m").Cast<Match>().Last();
        F(lastMove.Groups[1].Value).Should().BeApproximately(90f, 1f, "the centre is 10pt left of the right edge");
        F(lastMove.Groups[2].Value).Should().BeApproximately(20f, 0.5f, "the centre is 20pt above the bottom edge");
    }

    [Fact]
    public void Radial_NoArguments_ReturnsNull()
    {
        PdfRadialGradient.Render("radial-gradient()", 0, 0, 100, 100).Should().BeNull();
    }

    [Fact]
    public void Conic_CentreOutsideTheBox_SectorsReachTheFarthestCorner()
    {
        var content = PdfConicGradient.Render("conic-gradient(at -100pt 50%, red, blue)", 0, 0, 100, 100)!;

        var sector = Regex.Match(content, @"(-?\d+\.\d+) (-?\d+\.\d+) m (-?\d+\.\d+) (-?\d+\.\d+) l");
        float cx = F(sector.Groups[1].Value), cy = F(sector.Groups[2].Value);
        float dx = F(sector.Groups[3].Value) - cx, dy = F(sector.Groups[4].Value) - cy;
        float reach = (float)System.Math.Sqrt(dx * dx + dy * dy);
        reach.Should().BeGreaterThan((float)System.Math.Sqrt(200 * 200 + 50 * 50));
    }

    [Fact]
    public void Conic_DegreeStops_AreHonoured()
    {
        var content = PdfConicGradient.Render("conic-gradient(red 0deg 180deg, blue 180deg 360deg)", 0, 0, 100, 100)!;

        var fills = Fills(content);
        fills.Take(fills.Count / 2 - 1).Should().OnlyContain(f => f.R > 0.99f);
        fills.Skip(fills.Count / 2 + 1).Should().OnlyContain(f => f.B > 0.99f);
    }
}
