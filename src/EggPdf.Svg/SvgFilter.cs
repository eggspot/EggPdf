using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Core;

namespace EggPdf.Svg;

/// <summary>Geometry the filter graph needs from the renderer: unit scale and the local-to-canvas mapping.</summary>
internal readonly struct FilterRunContext
{
    /// <summary>Canvas pixels per user unit along x and y (used for stdDeviation, dx/dy, radius).</summary>
    public readonly float UnitToPxX, UnitToPxY;
    /// <summary>Affine local-user-space to canvas-pixel map (x' = a x + c y + e, y' = b x + d y + f).</summary>
    public readonly float A, B, C, D, E, F;

    /// <summary>Resolves feImage references (an element in the document, or a bitmap); null when unavailable.</summary>
    public readonly FilterImageSource? Images;

    public FilterRunContext(float unitToPxX, float unitToPxY, float a, float b, float c, float d, float e, float f,
        FilterImageSource? images = null)
    {
        UnitToPxX = unitToPxX; UnitToPxY = unitToPxY; A = a; B = b; C = c; D = d; E = e; F = f; Images = images;
    }
}

/// <summary>Callbacks the renderer provides so feImage can pull in document elements and bitmaps.</summary>
internal sealed class FilterImageSource
{
    /// <summary>Render the element with the given id into a canvas-sized image (its own user space mapped like the source graphic).</summary>
    public Func<string, FilterImage?> RenderElement = _ => null;
    /// <summary>Decode a bitmap reference (a data: URI) at its native size.</summary>
    public Func<string, FilterImage?> LoadBitmap = _ => null;
}

/// <summary>
/// A parsed &lt;filter&gt;: an ordered graph of primitives evaluated over premultiplied float
/// bitmaps, in linearRGB or sRGB per <c>color-interpolation-filters</c> as browsers do.
/// Supported: feGaussianBlur, feOffset, feFlood, feColorMatrix, feComponentTransfer, feMerge,
/// feBlend, feComposite, feMorphology, feDropShadow. A filter using any other primitive
/// (feTurbulence, feImage, feTile, feConvolveMatrix, feDisplacementMap, lighting) is not
/// parsed at all, so its element paints unfiltered rather than with a wrong partial result.
/// </summary>
internal sealed partial class SvgFilter
{
    private sealed class Primitive
    {
        public string Kind = "";
        public SvgElement Element = null!;
        public bool Linear = true;
    }

    private readonly List<Primitive> _primitives = new List<Primitive>();
    private readonly SvgElement _element;

    private SvgFilter(SvgElement element) { _element = element; }

    /// <summary>Parse a &lt;filter&gt; element; null when it has no primitives or uses an unsupported one.</summary>
    public static SvgFilter? TryParse(SvgElement filterElement)
    {
        var filter = new SvgFilter(filterElement);
        bool defaultLinear = ParseLinear(Prop(filterElement, "color-interpolation-filters"), true);

        foreach (var child in filterElement.Children)
        {
            switch (child.TagName)
            {
                case "fegaussianblur": case "feoffset": case "feflood": case "fecolormatrix":
                case "fecomponenttransfer": case "femerge": case "feblend": case "fecomposite":
                case "femorphology": case "fedropshadow": case "feturbulence": case "feconvolvematrix":
                case "fedisplacementmap": case "fetile": case "feimage":
                    filter._primitives.Add(new Primitive
                    {
                        Kind = child.TagName,
                        Element = child,
                        Linear = ParseLinear(Prop(child, "color-interpolation-filters"), defaultLinear),
                    });
                    break;
                case "fediffuselighting": case "fespecularlighting":
                    if (FindLight(child) == null) return null; // a lighting primitive is meaningless without a light
                    filter._primitives.Add(new Primitive
                    {
                        Kind = child.TagName,
                        Element = child,
                        Linear = ParseLinear(Prop(child, "color-interpolation-filters"), defaultLinear),
                    });
                    break;
                case "desc": case "title": case "metadata": case "animate": case "set":
                    break;
                default:
                    return null;
            }
        }
        return filter._primitives.Count == 0 ? null : filter;
    }

    private static bool ParseLinear(string? value, bool fallback)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        switch (value!.Trim().ToLowerInvariant())
        {
            case "srgb": case "auto": return false;
            case "linearrgb": return true;
            default: return fallback;
        }
    }

    // ── Filter region ─────────────────────────────────────────────────────────

    /// <summary>
    /// The filter region in the filtered element's local coordinates (default -10%/-10%/120%/120%
    /// of the bounding box; userSpaceOnUse values are taken literally).
    /// </summary>
    public (float x, float y, float w, float h) ResolveRegion(float bx, float by, float bw, float bh)
    {
        bool bboxUnits = !string.Equals(_element.GetAttribute("filterunits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);
        if (!bboxUnits)
        {
            float ux = Number(_element.GetAttribute("x"), bx - bw * 0.1f);
            float uy = Number(_element.GetAttribute("y"), by - bh * 0.1f);
            float uw = Number(_element.GetAttribute("width"), bw * 1.2f);
            float uh = Number(_element.GetAttribute("height"), bh * 1.2f);
            return (ux, uy, uw, uh);
        }

        float fx = Fraction(_element.GetAttribute("x"), -0.1f);
        float fy = Fraction(_element.GetAttribute("y"), -0.1f);
        float fw = Fraction(_element.GetAttribute("width"), 1.2f);
        float fh = Fraction(_element.GetAttribute("height"), 1.2f);
        return (bx + fx * bw, by + fy * bh, fw * bw, fh * bh);
    }

    private static float Fraction(string? value, float fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var v = value!.Trim();
        bool percent = v.EndsWith("%", StringComparison.Ordinal);
        if (percent) v = v.Substring(0, v.Length - 1);
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? (percent ? n / 100f : n) : fallback;
    }

    // ── Evaluation ────────────────────────────────────────────────────────────

    /// <summary>Run the primitives over <paramref name="source"/> (the sRGB SourceGraphic) and return the sRGB result.</summary>
    public FilterImage Run(FilterImage source, in FilterRunContext ctx)
    {
        var named = new Dictionary<string, FilterImage>();
        FilterImage? previous = null;
        // Each result's primitive subregion (pixels), needed by feTile
        var subregions = new Dictionary<FilterImage, (int x0, int y0, int x1, int y1)>();

        foreach (var prim in _primitives)
        {
            var el = prim.Element;
            bool linear = prim.Linear;

            FilterImage RawInput(string attr)
            {
                var name = el.GetAttribute(attr);
                switch (name)
                {
                    case "SourceGraphic": return source;
                    case "SourceAlpha": return FilterPixels.AlphaOnly(source);
                    case "BackgroundImage": case "BackgroundAlpha": case "FillPaint": case "StrokePaint":
                        return new FilterImage(source.Width, source.Height);
                    default:
                        return !string.IsNullOrEmpty(name) && named.TryGetValue(name, out var r) ? r : previous ?? source;
                }
            }
            FilterImage Input(string attr) => RawInput(attr).InSpace(linear);

            FilterImage output;
            switch (prim.Kind)
            {
                case "fegaussianblur":
                {
                    var sd = Numbers(el.GetAttribute("stddeviation"));
                    float sx = sd.Length > 0 ? sd[0] : 0f, sy = sd.Length > 1 ? sd[1] : sx;
                    output = sx < 0f || sy < 0f || (sx == 0f && sy == 0f)
                        ? Input("in")
                        : FilterPixels.Blur(Input("in"), sx * ctx.UnitToPxX, sy * ctx.UnitToPxY);
                    break;
                }
                case "feoffset":
                    output = FilterPixels.Offset(Input("in"), Number(el.GetAttribute("dx"), 0f) * ctx.UnitToPxX,
                        Number(el.GetAttribute("dy"), 0f) * ctx.UnitToPxY);
                    break;
                case "feflood":
                    output = Flood(el, source.Width, source.Height, linear);
                    break;
                case "fecolormatrix":
                    output = FilterPixels.ColorMatrix(Input("in"), ColorMatrixValues(el));
                    break;
                case "fecomponenttransfer":
                    output = FilterPixels.ComponentTransfer(Input("in"), TransferFunctions(el));
                    break;
                case "femerge":
                {
                    output = new FilterImage(source.Width, source.Height, linear);
                    foreach (var node in el.Children)
                    {
                        if (node.TagName != "femergenode") continue;
                        var nodeInput = ResolveNamed(node.GetAttribute("in"), named, previous, source).InSpace(linear);
                        output = FilterPixels.Over(nodeInput, output);
                    }
                    break;
                }
                case "feblend":
                    output = FilterPixels.Blend(Input("in"), Input("in2"), el.GetAttribute("mode").Trim().ToLowerInvariant());
                    break;
                case "fecomposite":
                {
                    string op = el.GetAttribute("operator").Trim().ToLowerInvariant();
                    output = FilterPixels.Composite(Input("in"), Input("in2"), op,
                        Number(el.GetAttribute("k1"), 0f), Number(el.GetAttribute("k2"), 0f),
                        Number(el.GetAttribute("k3"), 0f), Number(el.GetAttribute("k4"), 0f));
                    break;
                }
                case "femorphology":
                {
                    var r = Numbers(el.GetAttribute("radius"));
                    float rx = r.Length > 0 ? r[0] : 0f, ry = r.Length > 1 ? r[1] : rx;
                    bool dilate = string.Equals(el.GetAttribute("operator").Trim(), "dilate", StringComparison.OrdinalIgnoreCase);
                    output = rx <= 0f && ry <= 0f || rx < 0f || ry < 0f
                        ? Input("in")
                        : FilterPixels.Morphology(Input("in"), dilate,
                            (int)Math.Round(rx * ctx.UnitToPxX), (int)Math.Round(ry * ctx.UnitToPxY));
                    break;
                }
                case "feturbulence":
                    output = Turbulence(el, source.Width, source.Height, linear, ctx);
                    break;
                case "feconvolvematrix":
                    output = ConvolveMatrix(el, Input("in"));
                    break;
                case "fedisplacementmap":
                {
                    var channels = (el.GetAttribute("xchannelselector").Trim().ToUpperInvariant() + "A", el.GetAttribute("ychannelselector").Trim().ToUpperInvariant() + "A");
                    char xc = "RGBA".IndexOf(channels.Item1[0]) >= 0 ? channels.Item1[0] : 'A';
                    char yc = "RGBA".IndexOf(channels.Item2[0]) >= 0 ? channels.Item2[0] : 'A';
                    output = FilterPixels.DisplacementMap(Input("in"), Input("in2"),
                        Number(el.GetAttribute("scale"), 0f) * ctx.UnitToPxX, xc, yc);
                    break;
                }
                case "fetile":
                {
                    var raw = RawInput("in");
                    var (tx0, ty0, tx1, ty1) = subregions.TryGetValue(raw, out var known) ? known : (0, 0, source.Width, source.Height);
                    output = FilterPixels.Tile(raw.InSpace(linear), tx0, ty0, tx1, ty1);
                    break;
                }
                case "feimage":
                    output = ImageSource(el, source.Width, source.Height, linear, ctx);
                    break;
                case "fediffuselighting": case "fespecularlighting":
                    output = Lighting(el, Input("in"), prim.Kind == "fespecularlighting", linear, ctx);
                    break;
                default: // fedropshadow
                    output = DropShadow(el, Input("in"), linear, ctx);
                    break;
            }

            output = ClipToSubregion(output, el, ctx, out var region);
            output.Linear = linear;
            subregions[output] = region;
            var resultName = el.GetAttribute("result");
            if (!string.IsNullOrEmpty(resultName)) named[resultName] = output;
            previous = output;
        }

        return (previous ?? source).InSpace(false);
    }

    private static FilterImage ResolveNamed(string name, Dictionary<string, FilterImage> named, FilterImage? previous, FilterImage source)
    {
        switch (name)
        {
            case "SourceGraphic": return source;
            case "SourceAlpha": return FilterPixels.AlphaOnly(source);
            case "BackgroundImage": case "BackgroundAlpha": case "FillPaint": case "StrokePaint":
                return new FilterImage(source.Width, source.Height);
            default:
                return !string.IsNullOrEmpty(name) && named.TryGetValue(name, out var r) ? r : previous ?? source;
        }
    }

    private static FilterImage Flood(SvgElement el, int w, int h, bool linear)
    {
        var (r, g, b, a) = FloodColor(el);
        if (linear)
        {
            r = FilterImage.SrgbToLinear(r); g = FilterImage.SrgbToLinear(g); b = FilterImage.SrgbToLinear(b);
        }
        return FilterPixels.Flood(w, h, linear, r, g, b, a);
    }

    private static (float r, float g, float b, float a) FloodColor(SvgElement el)
    {
        var color = Color.TryParse(Prop(el, "flood-color")) ?? Color.Black;
        float opacity = Number(Prop(el, "flood-opacity"), 1f);
        return (color.R / 255f, color.G / 255f, color.B / 255f, FilterPixels.Clamp01(opacity) * (color.A / 255f));
    }

    /// <summary>feDropShadow = the input's alpha, blurred, offset and tinted, drawn under the input.</summary>
    private static FilterImage DropShadow(SvgElement el, FilterImage input, bool linear, in FilterRunContext ctx)
    {
        var sd = Numbers(el.GetAttribute("stddeviation"));
        float sx = sd.Length > 0 ? sd[0] : 2f, sy = sd.Length > 1 ? sd[1] : sx;
        float dx = Number(el.GetAttribute("dx"), 2f), dy = Number(el.GetAttribute("dy"), 2f);

        var shadow = FilterPixels.AlphaOnly(input);
        if (sx > 0f || sy > 0f)
            shadow = FilterPixels.Blur(shadow, Math.Max(0f, sx) * ctx.UnitToPxX, Math.Max(0f, sy) * ctx.UnitToPxY);
        shadow = FilterPixels.Offset(shadow, dx * ctx.UnitToPxX, dy * ctx.UnitToPxY);

        var (r, g, b, a) = FloodColor(el);
        if (linear)
        {
            r = FilterImage.SrgbToLinear(r); g = FilterImage.SrgbToLinear(g); b = FilterImage.SrgbToLinear(b);
        }
        for (int i = 0; i < shadow.Px.Length; i += 4)
        {
            float cover = shadow.Px[i + 3] * a;
            shadow.Px[i] = r * cover; shadow.Px[i + 1] = g * cover; shadow.Px[i + 2] = b * cover; shadow.Px[i + 3] = cover;
        }
        return FilterPixels.Over(input, shadow);
    }

    private static float[] ColorMatrixValues(SvgElement el)
    {
        var values = Numbers(el.GetAttribute("values"));
        switch (el.GetAttribute("type").Trim().ToLowerInvariant())
        {
            case "saturate":
                return FilterPixels.SaturateMatrix(values.Length > 0 ? values[0] : 1f);
            case "huerotate":
                return FilterPixels.HueRotateMatrix(values.Length > 0 ? values[0] : 0f);
            case "luminancetoalpha":
                return FilterPixels.LuminanceToAlphaMatrix;
            default:
                return values.Length == 20 ? values : Identity;
        }
    }

    private static readonly float[] Identity =
    {
        1, 0, 0, 0, 0,
        0, 1, 0, 0, 0,
        0, 0, 1, 0, 0,
        0, 0, 0, 1, 0,
    };

    private static FilterPixels.TransferFunction?[] TransferFunctions(SvgElement el)
    {
        var funcs = new FilterPixels.TransferFunction?[4];
        foreach (var child in el.Children)
        {
            int slot;
            switch (child.TagName)
            {
                case "fefuncr": slot = 0; break;
                case "fefuncg": slot = 1; break;
                case "fefuncb": slot = 2; break;
                case "fefunca": slot = 3; break;
                default: continue;
            }
            funcs[slot] = new FilterPixels.TransferFunction
            {
                Type = child.GetAttribute("type").Trim().ToLowerInvariant(),
                Table = Numbers(child.GetAttribute("tablevalues")),
                Slope = Number(child.GetAttribute("slope"), 1f),
                Intercept = Number(child.GetAttribute("intercept"), 0f),
                Amplitude = Number(child.GetAttribute("amplitude"), 1f),
                Exponent = Number(child.GetAttribute("exponent"), 1f),
                Offset = Number(child.GetAttribute("offset"), 0f),
            };
        }
        return funcs;
    }

    /// <summary>
    /// Clip a primitive's result to its x/y/width/height subregion (user-space numbers); sides
    /// it doesn't specify stay at the canvas edge. Absent attributes leave the image untouched.
    /// </summary>
    private static FilterImage ClipToSubregion(FilterImage img, SvgElement el, in FilterRunContext ctx,
        out (int x0, int y0, int x1, int y1) region)
    {
        region = (0, 0, img.Width, img.Height);
        var xs = el.GetAttribute("x"); var ys = el.GetAttribute("y");
        var ws = el.GetAttribute("width"); var hs = el.GetAttribute("height");
        if (xs.Length == 0 && ys.Length == 0 && ws.Length == 0 && hs.Length == 0) return img;

        // Map the specified user-space rectangle into canvas space; unspecified sides keep the canvas edge
        float x0 = 0, y0 = 0, x1 = img.Width, y1 = img.Height;
        float ux = Number(xs, float.NaN), uy = Number(ys, float.NaN);
        float uw = Number(ws, float.NaN), uh = Number(hs, float.NaN);
        if (!float.IsNaN(ux)) x0 = Map(ctx, ux, 0f).x;
        if (!float.IsNaN(uy)) y0 = Map(ctx, 0f, uy).y;
        if (!float.IsNaN(uw)) x1 = float.IsNaN(ux) ? x0 + uw * ctx.UnitToPxX : Map(ctx, ux + uw, 0f).x;
        if (!float.IsNaN(uh)) y1 = float.IsNaN(uy) ? y0 + uh * ctx.UnitToPxY : Map(ctx, 0f, uy + uh).y;

        int ix0 = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1))), ix1 = Math.Min(img.Width, (int)Math.Ceiling(Math.Max(x0, x1)));
        int iy0 = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1))), iy1 = Math.Min(img.Height, (int)Math.Ceiling(Math.Max(y0, y1)));

        region = (ix0, iy0, ix1, iy1);
        var clipped = new FilterImage(img.Width, img.Height, img.Linear);
        for (int y = iy0; y < iy1; y++)
            for (int x = ix0; x < ix1; x++)
                Array.Copy(img.Px, (y * img.Width + x) * 4, clipped.Px, (y * img.Width + x) * 4, 4);
        return clipped;
    }

    private static (float x, float y) Map(in FilterRunContext c, float x, float y)
        => (c.A * x + c.C * y + c.E, c.B * x + c.D * y + c.F);

    // ── Value parsing ─────────────────────────────────────────────────────────

    /// <summary>An attribute, or the same-named declaration inside a style="" attribute.</summary>
    internal static string Prop(SvgElement el, string name)
    {
        var direct = el.GetAttribute(name);
        if (direct.Length > 0) return direct;

        var style = el.GetAttribute("style");
        if (style.Length == 0) return "";
        foreach (var decl in style.Split(';'))
        {
            int colon = decl.IndexOf(':');
            if (colon > 0 && string.Equals(decl.Substring(0, colon).Trim(), name, StringComparison.OrdinalIgnoreCase))
                return decl.Substring(colon + 1).Trim();
        }
        return "";
    }

    private static float Number(string? value, float fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var v = value!.Trim();
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 2);
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : fallback;
    }

    private static float[] Numbers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<float>();
        var parts = value!.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<float>(parts.Length);
        foreach (var p in parts)
        {
            if (!float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float n)) return Array.Empty<float>();
            result.Add(n);
        }
        return result.ToArray();
    }
}
