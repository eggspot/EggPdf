using System;
using EggPdf.Core;

namespace EggPdf.Svg;

internal sealed partial class SvgFilter
{
    /// <summary>A lighting primitive's light source element; a lighting primitive is meaningless without one.</summary>
    private static SvgElement? FindLight(SvgElement lighting)
    {
        foreach (var child in lighting.Children)
            if (child.TagName == "fedistantlight" || child.TagName == "fepointlight" || child.TagName == "fespotlight") return child;
        return null;
    }

    /// <summary>
    /// feTurbulence's parameters. A negative frequency is an error, giving transparent black. The noise lives in user
    /// space, so each pixel maps back through the canvas transform; the stitching tile is the primitive subregion,
    /// defaulting to the whole canvas.
    /// </summary>
    private static FilterImage Turbulence(SvgElement el, int width, int height, bool linear, in FilterRunContext ctx)
    {
        var freq = Numbers(el.GetAttribute("basefrequency"));
        float fx = freq.Length > 0 ? freq[0] : 0f, fy = freq.Length > 1 ? freq[1] : fx;
        if (fx < 0f || fy < 0f) return new FilterImage(width, height, linear);

        int octaves = (int)Number(el.GetAttribute("numoctaves"), 1f);
        bool fractal = string.Equals(el.GetAttribute("type").Trim(), "fractalNoise", StringComparison.OrdinalIgnoreCase);
        bool stitch = string.Equals(el.GetAttribute("stitchtiles").Trim(), "stitch", StringComparison.OrdinalIgnoreCase);
        int seed = (int)Math.Round(Number(el.GetAttribute("seed"), 0f));
        if (octaves <= 0) return new FilterImage(width, height, linear);

        float originX = -ctx.E / ctx.A, originY = -ctx.F / ctx.D;
        float unitsPerPx = 1f / ctx.UnitToPxX;
        float tileX = Number(el.GetAttribute("x"), originX), tileY = Number(el.GetAttribute("y"), originY);
        float tileW = Number(el.GetAttribute("width"), width * unitsPerPx), tileH = Number(el.GetAttribute("height"), height * unitsPerPx);

        return FilterPixels.Turbulence(width, height, linear, fx, fy, octaves, seed, fractal, stitch,
            originX, originY, unitsPerPx, tileX, tileY, tileW, tileH);
    }

    /// <summary>
    /// feImage: a referenced element (drawn in user space) or a bitmap fitted into the primitive subregion; the
    /// bitmap is fitted (meet) and centred unless preserveAspectRatio is <c>none</c>.
    /// </summary>
    private static FilterImage ImageSource(SvgElement el, int width, int height, bool linear, in FilterRunContext ctx)
    {
        var href = el.GetAttribute("href");
        if (href.Length == 0) href = el.GetAttribute("xlink:href");
        var result = new FilterImage(width, height);
        if (ctx.Images == null || href.Length == 0) return result.InSpace(linear);

        if (href[0] == '#')
        {
            var rendered = ctx.Images.RenderElement(href.Substring(1));
            if (rendered != null) result = rendered;
        }
        else
        {
            var bitmap = ctx.Images.LoadBitmap(href);
            if (bitmap != null)
            {
                float x = Number(el.GetAttribute("x"), float.NaN), y = Number(el.GetAttribute("y"), float.NaN);
                float w = Number(el.GetAttribute("width"), float.NaN), h = Number(el.GetAttribute("height"), float.NaN);
                float px = float.IsNaN(x) ? 0f : ctx.A * x + ctx.E, py = float.IsNaN(y) ? 0f : ctx.D * y + ctx.F;
                float pw = float.IsNaN(w) ? width - px : w * ctx.UnitToPxX, ph = float.IsNaN(h) ? height - py : h * ctx.UnitToPxY;

                bool stretch = el.GetAttribute("preserveaspectratio").TrimStart().StartsWith("none", StringComparison.OrdinalIgnoreCase);
                if (!stretch)
                {
                    float scale = Math.Min(pw / bitmap.Width, ph / bitmap.Height);
                    float fw = bitmap.Width * scale, fh = bitmap.Height * scale;
                    px += (pw - fw) / 2f; py += (ph - fh) / 2f; pw = fw; ph = fh;
                }
                FilterPixels.DrawBitmap(result, bitmap, px, py, pw, ph);
            }
        }
        result.Linear = false;
        return result.InSpace(linear);
    }

    /// <summary>feConvolveMatrix's parameters; any invalid attribute disables the primitive, so the input passes through unchanged.</summary>
    private static FilterImage ConvolveMatrix(SvgElement el, FilterImage input)
    {
        var order = Numbers(el.GetAttribute("order"));
        int ox = order.Length > 0 ? (int)order[0] : 3, oy = order.Length > 1 ? (int)order[1] : ox;
        var kernel = Numbers(el.GetAttribute("kernelmatrix"));
        if (ox <= 0 || oy <= 0 || kernel.Length != ox * oy) return input;

        float sum = 0f;
        foreach (float k in kernel) sum += k;
        float divisor = Number(el.GetAttribute("divisor"), sum == 0f ? 1f : sum);
        if (divisor == 0f) divisor = sum == 0f ? 1f : sum;

        int tx = (int)Number(el.GetAttribute("targetx"), ox / 2), ty = (int)Number(el.GetAttribute("targety"), oy / 2);
        if (tx < 0 || tx >= ox || ty < 0 || ty >= oy) return input;

        string edge = el.GetAttribute("edgemode").Trim().ToLowerInvariant();
        bool preserve = string.Equals(el.GetAttribute("preservealpha").Trim(), "true", StringComparison.OrdinalIgnoreCase);
        return FilterPixels.ConvolveMatrix(input, ox, oy, kernel, divisor, Number(el.GetAttribute("bias"), 0f),
            tx, ty, edge, preserve);
    }

    /// <summary>
    /// feDiffuseLighting / feSpecularLighting's parameters, with the light mapped to canvas pixels. A specular
    /// exponent outside 1..128 is an error, giving transparent black.
    /// </summary>
    private static FilterImage Lighting(SvgElement el, FilterImage input, bool specular, bool linear, in FilterRunContext ctx)
    {
        var lightEl = FindLight(el)!;
        const int prefixLength = 2, suffixLength = 5;
        var light = new FilterLight { Kind = lightEl.TagName.Substring(prefixLength, lightEl.TagName.Length - prefixLength - suffixLength) };

        float scaleX = ctx.A, offsetX = ctx.E, scaleY = ctx.D, offsetY = ctx.F, scaleZ = ctx.UnitToPxX;
        float ToX(float ux) => scaleX * ux + offsetX;
        float ToY(float uy) => scaleY * uy + offsetY;
        float ToZ(float uz) => uz * scaleZ;
        light.Azimuth = Number(lightEl.GetAttribute("azimuth"), 0f);
        light.Elevation = Number(lightEl.GetAttribute("elevation"), 0f);
        light.X = ToX(Number(lightEl.GetAttribute("x"), 0f)); light.Y = ToY(Number(lightEl.GetAttribute("y"), 0f)); light.Z = ToZ(Number(lightEl.GetAttribute("z"), 0f));
        light.PointsAtX = ToX(Number(lightEl.GetAttribute("pointsatx"), 0f));
        light.PointsAtY = ToY(Number(lightEl.GetAttribute("pointsaty"), 0f));
        light.PointsAtZ = ToZ(Number(lightEl.GetAttribute("pointsatz"), 0f));
        light.SpotExponent = Number(lightEl.GetAttribute("specularexponent"), 1f);
        float cone = Number(lightEl.GetAttribute("limitingconeangle"), float.NaN);
        if (!float.IsNaN(cone)) light.ConeAngle = Math.Abs(cone);

        var color = Color.TryParse(Prop(el, "lighting-color")) ?? Color.White;
        float r = color.R / 255f, g = color.G / 255f, b = color.B / 255f;
        if (linear) { r = FilterImage.SrgbToLinear(r); g = FilterImage.SrgbToLinear(g); b = FilterImage.SrgbToLinear(b); }

        float surfaceScale = Number(el.GetAttribute("surfacescale"), 1f);
        if (specular)
        {
            float exponent = Number(el.GetAttribute("specularexponent"), 1f);
            if (exponent < 1f || exponent > 128f) return new FilterImage(input.Width, input.Height, linear);
            return FilterPixels.Lighting(input, true, surfaceScale, Number(el.GetAttribute("specularconstant"), 1f), exponent, r, g, b, light);
        }
        return FilterPixels.Lighting(input, false, surfaceScale, Number(el.GetAttribute("diffuseconstant"), 1f), 1f, r, g, b, light);
    }
}
