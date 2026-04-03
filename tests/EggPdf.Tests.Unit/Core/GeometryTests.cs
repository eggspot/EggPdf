using EggPdf.Core;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Core;

public class GeometryTests
{
    [Fact]
    public void RectF_Properties_Correct()
    {
        var rect = new RectF(10, 20, 100, 50);

        rect.X.Should().Be(10);
        rect.Y.Should().Be(20);
        rect.Width.Should().Be(100);
        rect.Height.Should().Be(50);
        rect.Left.Should().Be(10);
        rect.Top.Should().Be(20);
        rect.Right.Should().Be(110);
        rect.Bottom.Should().Be(70);
    }

    [Fact]
    public void RectF_Contains_PointInside_ReturnsTrue()
    {
        var rect = new RectF(0, 0, 100, 100);
        var point = new PointF(50, 50);

        rect.Contains(point).Should().BeTrue();
    }

    [Fact]
    public void RectF_Contains_PointOutside_ReturnsFalse()
    {
        var rect = new RectF(0, 0, 100, 100);
        var point = new PointF(150, 50);

        rect.Contains(point).Should().BeFalse();
    }

    [Fact]
    public void RectF_Intersects_Overlapping_ReturnsTrue()
    {
        var a = new RectF(0, 0, 100, 100);
        var b = new RectF(50, 50, 100, 100);

        a.Intersects(b).Should().BeTrue();
    }

    [Fact]
    public void RectF_Intersects_NonOverlapping_ReturnsFalse()
    {
        var a = new RectF(0, 0, 100, 100);
        var b = new RectF(200, 200, 100, 100);

        a.Intersects(b).Should().BeFalse();
    }

    [Fact]
    public void EdgeSizes_Zero_AllZero()
    {
        var edges = EdgeSizes.Zero;

        edges.Top.Should().Be(0);
        edges.Right.Should().Be(0);
        edges.Bottom.Should().Be(0);
        edges.Left.Should().Be(0);
        edges.Horizontal.Should().Be(0);
        edges.Vertical.Should().Be(0);
    }

    [Fact]
    public void EdgeSizes_Uniform_AllSame()
    {
        var edges = EdgeSizes.Uniform(10);

        edges.Top.Should().Be(10);
        edges.Right.Should().Be(10);
        edges.Bottom.Should().Be(10);
        edges.Left.Should().Be(10);
        edges.Horizontal.Should().Be(20);
        edges.Vertical.Should().Be(20);
    }

    [Fact]
    public void PageSizes_A4_CorrectDimensions()
    {
        var a4 = PageSizes.A4;

        // A4 = 210 x 297 mm = 595.28 x 841.89 pt (at 72dpi / PDF points)
        a4.Width.Should().BeApproximately(595.28f, 0.01f);
        a4.Height.Should().BeApproximately(841.89f, 0.01f);
    }

    [Fact]
    public void PageSizes_Letter_CorrectDimensions()
    {
        var letter = PageSizes.Letter;

        // Letter = 8.5 x 11 in = 612 x 792 pt (at 72dpi / PDF points)
        letter.Width.Should().Be(612f);
        letter.Height.Should().Be(792f);
    }

    [Fact]
    public void PageSizes_Landscape_SwapsWidthHeight()
    {
        var portrait = PageSizes.A4;
        var landscape = PageSizes.Landscape(portrait);

        landscape.Width.Should().Be(portrait.Height);
        landscape.Height.Should().Be(portrait.Width);
    }

    [Fact]
    public void PdfCoordinates_PxToPt_CorrectFactor()
    {
        // 1px = 1/96 inch, 1pt = 1/72 inch
        // px to pt = 72/96 = 0.75
        PdfCoordinates.PxToPt.Should().BeApproximately(0.75f, 0.001f);
    }

    [Fact]
    public void PdfCoordinates_ToPdfLength_ConvertsCorrectly()
    {
        // 96px = 72pt = 1 inch
        PdfCoordinates.ToPdfLength(96f).Should().BeApproximately(72f, 0.01f);
    }
}
