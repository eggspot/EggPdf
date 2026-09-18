using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

/// <summary>
/// A small software rasterizer: fills polygons (already-flattened vector shapes) into an
/// RGBA pixel buffer with anti-aliasing, for effects PDF has no vector primitive for (a
/// real Gaussian blur needs a bitmap to convolve -- see <see cref="GaussianBlurRaster"/>).
/// Straight (non-premultiplied) alpha throughout; not a general-purpose renderer -- only
/// flat single-color polygon fills, which is what SVG shape blur and CSS background/border
/// blur need.
/// </summary>
public sealed class RasterCanvas
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>RGBA bytes, row-major, straight alpha. Length = Width*Height*4.</summary>
    public byte[] Pixels { get; }

    public RasterCanvas(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        Pixels = new byte[Width * Height * 4];
    }

    /// <summary>Source-over alpha blend a single-color sample onto one pixel.</summary>
    private void BlendPixel(int x, int y, byte r, byte g, byte b, float coverageAlpha)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || coverageAlpha <= 0f) return;
        int idx = (y * Width + x) * 4;

        float srcA = Math.Min(1f, coverageAlpha);
        float dstA = Pixels[idx + 3] / 255f;
        float outA = srcA + dstA * (1f - srcA);
        if (outA <= 0f)
        {
            Pixels[idx] = 0; Pixels[idx + 1] = 0; Pixels[idx + 2] = 0; Pixels[idx + 3] = 0;
            return;
        }

        float dstR = Pixels[idx] / 255f, dstG = Pixels[idx + 1] / 255f, dstB = Pixels[idx + 2] / 255f;
        float outR = (r / 255f * srcA + dstR * dstA * (1f - srcA)) / outA;
        float outG = (g / 255f * srcA + dstG * dstA * (1f - srcA)) / outA;
        float outB = (b / 255f * srcA + dstB * dstA * (1f - srcA)) / outA;

        Pixels[idx] = (byte)Math.Round(Math.Min(1f, outR) * 255f);
        Pixels[idx + 1] = (byte)Math.Round(Math.Min(1f, outG) * 255f);
        Pixels[idx + 2] = (byte)Math.Round(Math.Min(1f, outB) * 255f);
        Pixels[idx + 3] = (byte)Math.Round(outA * 255f);
    }

    /// <summary>Even-odd point-in-polygon test (standard ray-casting crossing count).</summary>
    private static bool PointInPolygon(float x, float y, IReadOnlyList<(float x, float y)> pts)
    {
        bool inside = false;
        int n = pts.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = pts[i].x, yi = pts[i].y;
            float xj = pts[j].x, yj = pts[j].y;
            bool crosses = ((yi > y) != (yj > y)) &&
                (x < (xj - xi) * (y - yi) / (yj - yi) + xi);
            if (crosses) inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Fill a closed polygon (even-odd rule) with a single flat color. Anti-aliased via
    /// NxN supersampling per pixel -- simple and easy to verify, not the fastest approach,
    /// but filter-effect canvases are small and infrequent so raw speed isn't the concern.
    /// </summary>
    public void FillPolygon(IReadOnlyList<(float x, float y)> pts, byte r, byte g, byte b, byte a, int supersample = 4)
    {
        if (pts.Count < 3 || Width == 0 || Height == 0) return;

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in pts)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        int xStart = Math.Max(0, (int)Math.Floor(minX));
        int xEnd = Math.Min(Width - 1, (int)Math.Ceiling(maxX));
        int yStart = Math.Max(0, (int)Math.Floor(minY));
        int yEnd = Math.Min(Height - 1, (int)Math.Ceiling(maxY));
        if (xStart > xEnd || yStart > yEnd) return;

        int ss = Math.Max(1, supersample);
        int totalSamples = ss * ss;

        for (int py = yStart; py <= yEnd; py++)
        {
            for (int px = xStart; px <= xEnd; px++)
            {
                int hits = 0;
                for (int sy = 0; sy < ss; sy++)
                {
                    float sampleY = py + (sy + 0.5f) / ss;
                    for (int sx = 0; sx < ss; sx++)
                    {
                        float sampleX = px + (sx + 0.5f) / ss;
                        if (PointInPolygon(sampleX, sampleY, pts)) hits++;
                    }
                }
                if (hits == 0) continue;
                float coverage = hits / (float)totalSamples;
                BlendPixel(px, py, r, g, b, coverage * (a / 255f));
            }
        }
    }

    /// <summary>Fill an axis-aligned ellipse (circle when rx == ry) with a flat color.</summary>
    public void FillEllipse(float cx, float cy, float rx, float ry, byte r, byte g, byte b, byte a, int segments = 64)
    {
        if (rx <= 0 || ry <= 0) return;
        var pts = new List<(float x, float y)>(segments);
        for (int i = 0; i < segments; i++)
        {
            double theta = 2 * Math.PI * i / segments;
            pts.Add((cx + rx * (float)Math.Cos(theta), cy + ry * (float)Math.Sin(theta)));
        }
        FillPolygon(pts, r, g, b, a);
    }

    /// <summary>Flatten one cubic Bezier segment into line-segment points (De Casteljau via the direct formula) and append them to <paramref name="output"/>.</summary>
    public static void FlattenCubicBezier(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3,
        List<(float x, float y)> output, int steps = 16)
    {
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float mt = 1 - t;
            float x = mt * mt * mt * x0 + 3 * mt * mt * t * x1 + 3 * mt * t * t * x2 + t * t * t * x3;
            float y = mt * mt * mt * y0 + 3 * mt * mt * t * y1 + 3 * mt * t * t * y2 + t * t * t * y3;
            output.Add((x, y));
        }
    }
}
