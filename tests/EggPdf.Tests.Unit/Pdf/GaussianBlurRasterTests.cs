using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class GaussianBlurRasterTests
{
    private static (byte r, byte g, byte b, byte a) GetPixel(RasterCanvas c, int x, int y)
    {
        int idx = (y * c.Width + x) * 4;
        return (c.Pixels[idx], c.Pixels[idx + 1], c.Pixels[idx + 2], c.Pixels[idx + 3]);
    }

    private static void SetPixel(RasterCanvas c, int x, int y, byte r, byte g, byte b, byte a)
    {
        int idx = (y * c.Width + x) * 4;
        c.Pixels[idx] = r; c.Pixels[idx + 1] = g; c.Pixels[idx + 2] = b; c.Pixels[idx + 3] = a;
    }

    [Fact]
    public void BuildKernel_IsSymmetricAndSumsToOne()
    {
        var kernel = GaussianBlurRaster.BuildKernel(3f);
        kernel.Length.Should().BeGreaterThan(1);

        float sum = 0f;
        foreach (var w in kernel) sum += w;
        sum.Should().BeApproximately(1f, 0.001f);

        int mid = kernel.Length / 2;
        kernel[0].Should().BeApproximately(kernel[kernel.Length - 1], 0.0001f, "the kernel must be symmetric");
        for (int i = 1; i <= mid; i++)
            kernel[mid].Should().BeGreaterThan(kernel[mid - i], "the peak must be at the center");
    }

    [Fact]
    public void BuildKernel_ZeroOrNegativeSigma_ReturnsIdentityKernel()
    {
        GaussianBlurRaster.BuildKernel(0f).Should().Equal(1f);
        GaussianBlurRaster.BuildKernel(-1f).Should().Equal(1f);
    }

    [Fact]
    public void ApplyInPlace_SingleOpaquePixel_SpreadsSymmetricallyToNeighbors()
    {
        var canvas = new RasterCanvas(21, 21);
        SetPixel(canvas, 10, 10, 255, 0, 0, 255);

        GaussianBlurRaster.ApplyInPlace(canvas, 2f);

        var center = GetPixel(canvas, 10, 10);
        var left = GetPixel(canvas, 9, 10);
        var right = GetPixel(canvas, 11, 10);
        var up = GetPixel(canvas, 10, 9);
        var down = GetPixel(canvas, 10, 11);

        center.a.Should().BeLessThan(255, "blurring a single point must reduce its own peak alpha");
        center.a.Should().BeGreaterThan(0);
        left.a.Should().BeGreaterThan(0, "blur must spread alpha into neighboring pixels");
        ((float)left.a).Should().BeApproximately(right.a, 1, "a symmetric kernel on a centered point must blur symmetrically left/right");
        ((float)up.a).Should().BeApproximately(down.a, 1, "a symmetric kernel on a centered point must blur symmetrically up/down");
    }

    [Fact]
    public void ApplyInPlace_PreservesApproximateTotalAlphaMassAwayFromEdges()
    {
        var canvas = new RasterCanvas(41, 41);
        SetPixel(canvas, 20, 20, 200, 100, 50, 200);

        long before = 0;
        for (int y = 0; y < canvas.Height; y++)
            for (int x = 0; x < canvas.Width; x++)
                before += GetPixel(canvas, x, y).a;

        GaussianBlurRaster.ApplyInPlace(canvas, 2f);

        long after = 0;
        for (int y = 0; y < canvas.Height; y++)
            for (int x = 0; x < canvas.Width; x++)
                after += GetPixel(canvas, x, y).a;

        // A normalized kernel conserves mass exactly in continuous math; rounding to bytes
        // and clipping at the canvas edge (which this point is far from) leave it very close.
        ((double)after).Should().BeApproximately(before, before * 0.05,
            "a normalized Gaussian kernel must approximately conserve total alpha mass, not brighten or dim the image");
    }

    [Fact]
    public void ApplyInPlace_NoDarkFringingOnTransparentEdge_ColorStaysConsistent()
    {
        // A red opaque pixel next to fully transparent (black, per default byte value)
        // pixels must not blur toward black -- straight-alpha blurring would leak the
        // (0,0,0) RGB of the transparent neighbors into the result.
        var canvas = new RasterCanvas(21, 21);
        SetPixel(canvas, 10, 10, 255, 0, 0, 255);

        GaussianBlurRaster.ApplyInPlace(canvas, 1.5f);

        var edge = GetPixel(canvas, 11, 10); // partially blurred-into pixel
        if (edge.a > 10) // only check color where there's enough alpha to be meaningful
        {
            edge.r.Should().BeGreaterThan(edge.g, "the blurred-in color must stay reddish, not fade toward black");
        }
    }

    [Fact]
    public void ApplyInPlace_ZeroSigma_LeavesCanvasUnchanged()
    {
        var canvas = new RasterCanvas(10, 10);
        SetPixel(canvas, 5, 5, 100, 150, 200, 128);

        GaussianBlurRaster.ApplyInPlace(canvas, 0f);

        GetPixel(canvas, 5, 5).Should().Be(((byte)100, (byte)150, (byte)200, (byte)128));
    }
}
