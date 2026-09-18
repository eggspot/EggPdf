using System;

namespace EggPdf.Pdf;

/// <summary>
/// Real separable Gaussian blur over a <see cref="RasterCanvas"/>'s RGBA pixels
/// (horizontal pass then vertical pass, each a 1D convolution -- mathematically
/// equivalent to a full 2D Gaussian convolution). Blurs in premultiplied-alpha space
/// so transparent edge pixels don't pull black into the result (the classic dark-fringe
/// bug of blurring straight-alpha RGBA), then un-premultiplies afterward.
/// </summary>
public static class GaussianBlurRaster
{
    /// <summary>Build a normalized 1D Gaussian kernel covering +/-3*sigma.</summary>
    internal static float[] BuildKernel(float sigma)
    {
        if (sigma <= 0f) return new float[] { 1f };
        int radius = Math.Max(1, (int)Math.Ceiling(sigma * 3f));
        int size = radius * 2 + 1;
        var kernel = new float[size];
        float twoSigmaSq = 2f * sigma * sigma;
        float sum = 0f;
        for (int i = 0; i < size; i++)
        {
            int x = i - radius;
            float w = (float)Math.Exp(-(x * x) / twoSigmaSq);
            kernel[i] = w;
            sum += w;
        }
        for (int i = 0; i < size; i++) kernel[i] /= sum;
        return kernel;
    }

    /// <summary>Apply a Gaussian blur with the given standard deviation, in place.</summary>
    public static void ApplyInPlace(RasterCanvas canvas, float sigma)
    {
        if (sigma <= 0f || canvas.Width == 0 || canvas.Height == 0) return;

        int w = canvas.Width, h = canvas.Height;
        var kernel = BuildKernel(sigma);
        int radius = kernel.Length / 2;

        // Premultiply alpha into float RGBA buffers for correct edge blending.
        var pr = new float[w * h];
        var pg = new float[w * h];
        var pb = new float[w * h];
        var pa = new float[w * h];
        var src = canvas.Pixels;
        for (int i = 0; i < w * h; i++)
        {
            float a = src[i * 4 + 3] / 255f;
            pr[i] = src[i * 4] / 255f * a;
            pg[i] = src[i * 4 + 1] / 255f * a;
            pb[i] = src[i * 4 + 2] / 255f * a;
            pa[i] = a;
        }

        var tr = new float[w * h]; var tg = new float[w * h]; var tb = new float[w * h]; var ta = new float[w * h];

        // Horizontal pass: src -> temp
        for (int y = 0; y < h; y++)
        {
            int rowBase = y * w;
            for (int x = 0; x < w; x++)
            {
                float sr = 0, sg = 0, sb = 0, sa = 0;
                for (int k = 0; k < kernel.Length; k++)
                {
                    int sx = x + (k - radius);
                    if (sx < 0 || sx >= w) continue; // outside canvas = transparent, contributes 0
                    int idx = rowBase + sx;
                    float weight = kernel[k];
                    sr += pr[idx] * weight; sg += pg[idx] * weight; sb += pb[idx] * weight; sa += pa[idx] * weight;
                }
                int outIdx = rowBase + x;
                tr[outIdx] = sr; tg[outIdx] = sg; tb[outIdx] = sb; ta[outIdx] = sa;
            }
        }

        // Vertical pass: temp -> back into pr/pg/pb/pa (reused as the final premultiplied buffers)
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                float sr = 0, sg = 0, sb = 0, sa = 0;
                for (int k = 0; k < kernel.Length; k++)
                {
                    int sy = y + (k - radius);
                    if (sy < 0 || sy >= h) continue;
                    int idx = sy * w + x;
                    float weight = kernel[k];
                    sr += tr[idx] * weight; sg += tg[idx] * weight; sb += tb[idx] * weight; sa += ta[idx] * weight;
                }
                int outIdx = y * w + x;
                pr[outIdx] = sr; pg[outIdx] = sg; pb[outIdx] = sb; pa[outIdx] = sa;
            }
        }

        // Un-premultiply back into the canvas.
        for (int i = 0; i < w * h; i++)
        {
            float a = pa[i];
            if (a <= 0.0001f)
            {
                src[i * 4] = 0; src[i * 4 + 1] = 0; src[i * 4 + 2] = 0; src[i * 4 + 3] = 0;
                continue;
            }
            src[i * 4] = (byte)Math.Round(Math.Min(1f, pr[i] / a) * 255f);
            src[i * 4 + 1] = (byte)Math.Round(Math.Min(1f, pg[i] / a) * 255f);
            src[i * 4 + 2] = (byte)Math.Round(Math.Min(1f, pb[i] / a) * 255f);
            src[i * 4 + 3] = (byte)Math.Round(Math.Min(1f, a) * 255f);
        }
    }
}
