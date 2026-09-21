using System;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>shape-outside: url() image alpha shapes and inset() rounded corners.</summary>
[Collection("ShapeImageLoader")] // the loader is a shared thread-static hook
public class ShapeOutsideUrlTests : IDisposable
{
    public ShapeOutsideUrlTests() { ShapeOutsideParser.ImageLoader = null; }
    public void Dispose() { ShapeOutsideParser.ImageLoader = null; }

    /// <summary>A w x h alpha mask built from a predicate on (column, row).</summary>
    private static (int, int, byte[]) Mask(int w, int h, Func<int, int, byte> alpha)
    {
        var data = new byte[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) data[y * w + x] = alpha(x, y);
        return (w, h, data);
    }

    [Fact]
    public void Url_LeftHalfOpaque_ExcludesOnlyTheLeftHalfOfTheFloat()
    {
        ShapeOutsideParser.ImageLoader = _ => Mask(10, 10, (x, y) => (byte)(x < 5 ? 255 : 0));
        var shape = ShapeOutsideParser.Parse("url(mask.png)", 100, 100, 16);
        shape.Should().NotBeNull();

        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);
        ctx.GetLeftOffset(y: 50, lineHeight: 1).Should().BeApproximately(50f, 1f, "the opaque left half ends at x=50");
    }

    [Fact]
    public void Url_TransparentRows_DoNotIndentText()
    {
        // Only the bottom half of the image has pixels
        ShapeOutsideParser.ImageLoader = _ => Mask(10, 10, (x, y) => (byte)(y >= 5 ? 255 : 0));
        var shape = ShapeOutsideParser.Parse("url(m.png)", 100, 100, 16);

        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);
        ctx.GetLeftOffset(y: 20, lineHeight: 10).Should().Be(0f, "the top rows of the mask are fully transparent");
        ctx.GetLeftOffset(y: 70, lineHeight: 10).Should().BeApproximately(100f, 1f);
    }

    [Fact]
    public void Url_RightFloat_UsesTheLeftEdgeOfTheOpaqueRegion()
    {
        ShapeOutsideParser.ImageLoader = _ => Mask(10, 10, (x, y) => (byte)(x >= 3 ? 255 : 0));
        var shape = ShapeOutsideParser.Parse("url(m.png)", 100, 100, 16);

        var ctx = new FloatContext();
        ctx.AddRightFloat(200, 0, 100, 100, shape); // container right edge at 300
        ctx.GetRightOffset(y: 50, lineHeight: 1, containerRight: 300).Should().BeApproximately(70f, 1f);
    }

    [Fact]
    public void Url_ThresholdRejectsFaintPixels()
    {
        ShapeOutsideParser.ImageLoader = _ => Mask(10, 10, (x, y) => (byte)(x < 5 ? 255 : 128));
        var loose = ShapeOutsideParser.Parse("url(m.png)", 100, 100, 16, imageThreshold: 0f);
        var strict = ShapeOutsideParser.Parse("url(m.png)", 100, 100, 16, imageThreshold: 0.6f);

        var a = new FloatContext(); a.AddLeftFloat(0, 0, 100, 100, loose);
        var b = new FloatContext(); b.AddLeftFloat(0, 0, 100, 100, strict);
        a.GetLeftOffset(50, 1).Should().BeApproximately(100f, 1f, "alpha 128 > 0 counts");
        b.GetLeftOffset(50, 1).Should().BeApproximately(50f, 1f, "alpha 0.5 is not above the 0.6 threshold");
    }

    [Fact]
    public void Url_WithoutALoaderOrWhenItFails_FallsBackToTheRectangle()
    {
        ShapeOutsideParser.Parse("url(m.png)", 100, 100, 16).Should().BeNull("no image source is available at layout time");

        ShapeOutsideParser.ImageLoader = _ => null;
        ShapeOutsideParser.Parse("url(m.png)", 100, 100, 16).Should().BeNull("the image could not be decoded");
    }

    [Fact]
    public void Url_QuotedAndDataUriArguments_ArePassedToTheLoaderUnquoted()
    {
        string? seen = null;
        ShapeOutsideParser.ImageLoader = src => { seen = src; return Mask(2, 2, (x, y) => 255); };
        ShapeOutsideParser.Parse("url('shapes/a b.png')", 10, 10, 16);
        seen.Should().Be("shapes/a b.png");
        ShapeOutsideParser.Parse("url(\"data:image/png;base64,AAAA\")", 10, 10, 16);
        seen.Should().Be("data:image/png;base64,AAAA");
    }

    [Fact]
    public void Inset_RoundedCorners_NarrowTheExclusionNearTheTop()
    {
        var shape = ShapeOutsideParser.Parse("inset(0 round 40px)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);

        ctx.GetLeftOffset(y: 0, lineHeight: 1).Should().BeLessThan(75f, "at the very top the rounded corners have barely started");
        ctx.GetLeftOffset(y: 50, lineHeight: 1).Should().BeApproximately(100f, 0.5f, "mid-height the box is square-sided");
    }

    [Fact]
    public void Inset_RoundPercentage_MakesAnEllipseWhenHalf()
    {
        var shape = ShapeOutsideParser.Parse("inset(0 round 50%)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);

        ctx.GetLeftOffset(y: 50, lineHeight: 1).Should().BeApproximately(100f, 1f);
        ctx.GetLeftOffset(y: 10, lineHeight: 1).Should().BeApproximately(80f, 4f, "50 + sqrt(50^2 - 40^2) = 80 on a circle of radius 50");
    }

    [Fact]
    public void Inset_PerCornerRadii_AreHonoured()
    {
        // Only the top-left corner is rounded: the right edge stays square at the top
        var shape = ShapeOutsideParser.Parse("inset(0 round 50px 0 0 0)", 100, 100, 16);
        var ctx = new FloatContext();
        ctx.AddLeftFloat(0, 0, 100, 100, shape);
        ctx.GetLeftOffset(y: 0.5f, lineHeight: 1).Should().BeApproximately(100f, 1f);

        var right = new FloatContext();
        right.AddRightFloat(200, 0, 100, 100, shape);
        // The right float's left edge at the top is pushed in by the rounded top-left corner
        right.GetRightOffset(y: 0, lineHeight: 1, containerRight: 300).Should().BeLessThan(75f);
    }
}
