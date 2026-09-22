using System.Collections.Generic;
using EggPdf.Svg;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Svg;

/// <summary>Pixel-level tests of the SVG filter graph (SvgFilter + FilterPixels), independent of PDF output.</summary>
public class SvgFilterGraphTests
{
    private static SvgElement El(string tag, params (string k, string v)[] attrs)
    {
        var e = new SvgElement { TagName = tag };
        foreach (var (k, v) in attrs) e.Attributes[k] = v;
        return e;
    }

    private static SvgFilter Filter(params SvgElement[] primitives)
    {
        var f = El("filter", ("id", "f"));
        f.Children.AddRange(primitives);
        return SvgFilter.TryParse(f)!;
    }

    /// <summary>A w x h image with an opaque sRGB rectangle (x0,y0)-(x1,y1) in the given color.</summary>
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

    private static FilterRunContext Ctx(float scale = 1f) => new FilterRunContext(scale, scale, scale, 0, 0, scale, 0, 0);

    private static float[] At(FilterImage img, int x, int y)
    {
        int i = (y * img.Width + x) * 4;
        return new[] { img.Px[i], img.Px[i + 1], img.Px[i + 2], img.Px[i + 3] };
    }

    [Fact]
    public void Offset_ShiftsPixelsByDxDy()
    {
        var src = Rect(20, 20, 2, 2, 6, 6, 1, 0, 0);
        var result = Filter(El("feoffset", ("dx", "5"), ("dy", "3"))).Run(src, Ctx());

        At(result, 8, 6)[3].Should().BeApproximately(1f, 0.001f, "the rect's top-left moved from (2,2) to (7,5)");
        At(result, 3, 3)[3].Should().Be(0f, "the original position is now empty");
    }

    [Fact]
    public void Offset_ScalesWithUnitToPixelFactor()
    {
        var src = Rect(40, 20, 2, 2, 6, 6, 1, 0, 0);
        var result = Filter(El("feoffset", ("dx", "5"))).Run(src, Ctx(2f));

        At(result, 12, 3)[3].Should().BeApproximately(1f, 0.001f, "dx of 5 user units at 2px/unit shifts 10 pixels");
    }

    [Fact]
    public void Flood_FillsWholeCanvasWithColorAndOpacity_InSrgbWhenRequested()
    {
        var src = new FilterImage(4, 4);
        var f = Filter(El("feflood", ("flood-color", "#ff0000"), ("flood-opacity", "0.5"),
            ("color-interpolation-filters", "sRGB")));
        var px = At(f.Run(src, Ctx()), 1, 1);

        px[3].Should().BeApproximately(0.5f, 0.01f);
        px[0].Should().BeApproximately(0.5f, 0.01f, "premultiplied red = 1 * 0.5");
        px[1].Should().Be(0f);
    }

    [Fact]
    public void Flood_ReadsColorFromStyleAttribute()
    {
        var src = new FilterImage(2, 2);
        var f = Filter(El("feflood", ("style", "flood-color: #00ff00; flood-opacity: 1"), ("color-interpolation-filters", "sRGB")));
        var px = At(f.Run(src, Ctx()), 0, 0);
        px[1].Should().BeApproximately(1f, 0.01f);
        px[0].Should().Be(0f);
    }

    [Fact]
    public void ColorMatrix_Saturate0_ProducesGrayscale()
    {
        var src = Rect(2, 2, 0, 0, 2, 2, 1, 0, 0);
        var f = Filter(El("fecolormatrix", ("type", "saturate"), ("values", "0"), ("color-interpolation-filters", "sRGB")));
        var px = At(f.Run(src, Ctx()), 0, 0);

        px[0].Should().BeApproximately(px[1], 0.001f);
        px[1].Should().BeApproximately(px[2], 0.001f);
        px[0].Should().BeApproximately(0.213f, 0.001f, "luminance of pure red under the saturate matrix");
    }

    [Fact]
    public void ColorMatrix_ExplicitMatrix_SwapsRedAndBlue()
    {
        var src = Rect(2, 2, 0, 0, 2, 2, 1, 0, 0);
        var f = Filter(El("fecolormatrix", ("type", "matrix"),
            ("values", "0 0 1 0 0  0 1 0 0 0  1 0 0 0 0  0 0 0 1 0"), ("color-interpolation-filters", "sRGB")));
        var px = At(f.Run(src, Ctx()), 0, 0);

        px[0].Should().BeApproximately(0f, 0.001f);
        px[2].Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void ColorMatrix_WrongValueCount_IsIdentity()
    {
        var src = Rect(2, 2, 0, 0, 2, 2, 0.2f, 0.4f, 0.6f);
        var f = Filter(El("fecolormatrix", ("type", "matrix"), ("values", "1 2 3"), ("color-interpolation-filters", "sRGB")));
        var px = At(f.Run(src, Ctx()), 0, 0);
        px[0].Should().BeApproximately(0.2f, 0.001f);
        px[2].Should().BeApproximately(0.6f, 0.001f);
    }

    [Fact]
    public void ColorMatrix_LuminanceToAlpha_MovesLuminanceIntoAlpha()
    {
        var src = Rect(2, 2, 0, 0, 2, 2, 1, 1, 1);
        var f = Filter(El("fecolormatrix", ("type", "luminanceToAlpha".ToLowerInvariant()), ("color-interpolation-filters", "sRGB")));
        var px = At(f.Run(src, Ctx()), 0, 0);
        px[3].Should().BeApproximately(0.2125f + 0.7154f + 0.0721f, 0.001f);
        px[0].Should().Be(0f, "colour channels are zeroed");
    }

    [Fact]
    public void ComponentTransfer_LinearOnRed_InvertsChannel()
    {
        var src = Rect(2, 2, 0, 0, 2, 2, 0.25f, 0.5f, 0.5f);
        var func = El("fefuncr", ("type", "linear"), ("slope", "-1"), ("intercept", "1"));
        var ct = El("fecomponenttransfer", ("color-interpolation-filters", "sRGB"));
        ct.Children.Add(func);
        var px = At(Filter(ct).Run(src, Ctx()), 0, 0);

        px[0].Should().BeApproximately(0.75f, 0.001f);
        px[1].Should().BeApproximately(0.5f, 0.001f, "channels without a feFunc are untouched");
    }

    [Fact]
    public void ComponentTransfer_Discrete_QuantizesChannel()
    {
        var src = Rect(2, 2, 0, 0, 2, 2, 0.3f, 0.3f, 0.3f);
        var func = El("fefuncg", ("type", "discrete"), ("tablevalues", "0 1"));
        var ct = El("fecomponenttransfer", ("color-interpolation-filters", "sRGB"));
        ct.Children.Add(func);
        var px = At(Filter(ct).Run(src, Ctx()), 0, 0);
        px[1].Should().BeApproximately(0f, 0.001f, "0.3 falls in the first of two steps");
    }

    [Fact]
    public void Blend_Multiply_MultipliesChannels()
    {
        var src = Rect(2, 2, 0, 0, 2, 2, 0.5f, 0.5f, 0.5f);
        var flood = El("feflood", ("flood-color", "#808080"), ("result", "gray"), ("color-interpolation-filters", "sRGB"));
        var blend = El("feblend", ("in", "SourceGraphic"), ("in2", "gray"), ("mode", "multiply"), ("color-interpolation-filters", "sRGB"));
        var f = Filter(flood, blend);

        var px = At(f.Run(src, Ctx()), 0, 0);
        px[0].Should().BeApproximately(0.5f * (128f / 255f), 0.01f);
    }

    [Fact]
    public void Composite_In_KeepsSourceOnlyWhereSecondInputIsOpaque()
    {
        // in2 = SourceAlpha of a smaller rect via offset; only the overlap survives
        var src = Rect(10, 10, 0, 0, 6, 6, 1, 0, 0);
        var off = El("feoffset", ("in", "SourceAlpha"), ("dx", "4"), ("result", "shifted"));
        var comp = El("fecomposite", ("in", "SourceGraphic"), ("in2", "shifted"), ("operator", "in"));
        var px = Filter(off, comp).Run(src, Ctx());

        At(px, 5, 2)[3].Should().BeApproximately(1f, 0.001f, "x=5 is inside both the source and its shifted copy");
        At(px, 1, 2)[3].Should().Be(0f, "x=1 is outside the shifted copy");
    }

    [Fact]
    public void Composite_Arithmetic_AppliesK1ToK4()
    {
        var src = Rect(2, 2, 0, 0, 2, 2, 0.5f, 0.5f, 0.5f);
        var comp = El("fecomposite", ("in", "SourceGraphic"), ("in2", "SourceGraphic"), ("operator", "arithmetic"),
            ("k1", "0"), ("k2", "0.5"), ("k3", "0.25"), ("k4", "0"), ("color-interpolation-filters", "sRGB"));
        var px = At(Filter(comp).Run(src, Ctx()), 0, 0);

        px[0].Should().BeApproximately(0.5f * 0.5f + 0.5f * 0.25f, 0.001f);
        px[3].Should().BeApproximately(0.75f, 0.001f);
    }

    [Fact]
    public void Merge_StacksNodesInOrder_LastOnTop()
    {
        var src = Rect(2, 2, 0, 0, 2, 2, 0, 0, 1);
        var red = El("feflood", ("flood-color", "#ff0000"), ("result", "red"), ("color-interpolation-filters", "sRGB"));
        var merge = El("femerge", ("color-interpolation-filters", "sRGB"));
        merge.Children.Add(El("femergenode", ("in", "red")));
        merge.Children.Add(El("femergenode", ("in", "SourceGraphic")));
        var px = At(Filter(red, merge).Run(src, Ctx()), 0, 0);

        px[2].Should().BeApproximately(1f, 0.001f, "the opaque blue SourceGraphic node is drawn last, over the red flood");
        px[0].Should().Be(0f);
    }

    [Fact]
    public void Blur_SpreadsAlphaBeyondTheShape_AndConservesTotalAlpha()
    {
        var src = Rect(41, 41, 15, 15, 26, 26, 0, 0, 0);
        var result = Filter(El("fegaussianblur", ("stddeviation", "3"))).Run(src, Ctx());

        At(result, 12, 20)[3].Should().BeGreaterThan(0.01f, "blur bleeds outside the original rectangle");
        At(result, 20, 20)[3].Should().BeLessThan(1f);

        float before = 0f, after = 0f;
        for (int i = 3; i < src.Px.Length; i += 4) { before += src.Px[i]; after += result.Px[i]; }
        after.Should().BeApproximately(before, before * 0.02f, "a normalized blur preserves total coverage");
    }

    [Fact]
    public void Blur_SmallSigma_UsesGaussianKernel()
    {
        var src = Rect(21, 1, 10, 0, 11, 1, 1, 1, 1); // a single bright pixel
        var result = Filter(El("fegaussianblur", ("stddeviation", "1 0"), ("color-interpolation-filters", "sRGB"))).Run(src, Ctx());

        float centre = At(result, 10, 0)[3], side = At(result, 11, 0)[3];
        centre.Should().BeApproximately(0.3989f, 0.02f, "1/(sigma*sqrt(2*pi)) for sigma = 1");
        side.Should().BeApproximately(0.2420f, 0.02f);
    }

    [Fact]
    public void Blur_LargeSigma_UsesBoxApproximationCloseToGaussian()
    {
        var src = Rect(101, 1, 50, 0, 51, 1, 1, 1, 1);
        var result = Filter(El("fegaussianblur", ("stddeviation", "5 0"), ("color-interpolation-filters", "sRGB"))).Run(src, Ctx());

        // Three 9px boxes (the spec's d for sigma 5): the exact triple-box peak is 0.0837, within ~5% of the
        // sigma-5 Gaussian's 1/(5*sqrt(2*pi)) = 0.0798
        At(result, 50, 0)[3].Should().BeApproximately(0.0837f, 0.002f);
        float total = 0f;
        for (int x = 0; x < 101; x++) total += At(result, x, 0)[3];
        total.Should().BeApproximately(1f, 0.01f, "box blurs conserve coverage");
    }

    [Fact]
    public void Blur_ZeroDeviation_LeavesImageUnchanged()
    {
        var src = Rect(6, 6, 2, 2, 4, 4, 1, 0, 0);
        var result = Filter(El("fegaussianblur", ("stddeviation", "0"))).Run(src, Ctx());
        At(result, 2, 2)[3].Should().BeApproximately(1f, 0.001f);
        At(result, 1, 1)[3].Should().Be(0f);
    }

    [Fact]
    public void Morphology_Dilate_GrowsShape_ErodeShrinksIt()
    {
        var src = Rect(20, 20, 8, 8, 12, 12, 0, 0, 0);
        var dilated = Filter(El("femorphology", ("operator", "dilate"), ("radius", "2"))).Run(src, Ctx());
        var eroded = Filter(El("femorphology", ("operator", "erode"), ("radius", "1"))).Run(src, Ctx());

        At(dilated, 6, 10)[3].Should().BeApproximately(1f, 0.001f, "the rectangle grew 2px to the left");
        At(dilated, 5, 10)[3].Should().Be(0f);
        At(eroded, 8, 10)[3].Should().Be(0f, "the outermost ring was eroded away");
        At(eroded, 9, 10)[3].Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void DropShadow_DrawsOffsetTintedCopyUnderTheSource()
    {
        var src = Rect(30, 30, 5, 5, 12, 12, 1, 0, 0);
        var f = Filter(El("fedropshadow", ("dx", "10"), ("dy", "10"), ("stddeviation", "0"),
            ("flood-color", "#0000ff"), ("color-interpolation-filters", "sRGB")));
        var result = f.Run(src, Ctx());

        At(result, 8, 8)[0].Should().BeApproximately(1f, 0.001f, "the source stays on top");
        var shadow = At(result, 18, 18);
        shadow[2].Should().BeApproximately(1f, 0.001f, "the shadow is the flood colour");
        shadow[3].Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void LinearRgbDefault_DiffersFromSrgb_ForMidGrayBlend()
    {
        // 50% black over white: linearRGB gives ~0.735 sRGB, plain sRGB gives 0.5
        var src = new FilterImage(2, 2);
        for (int i = 0; i < src.Px.Length; i += 4) { src.Px[i + 3] = 0.5f; }
        var white = El("feflood", ("flood-color", "white"), ("result", "w"));
        var over = El("femerge");
        over.Children.Add(El("femergenode", ("in", "w")));
        over.Children.Add(El("femergenode", ("in", "SourceGraphic")));

        var linearPx = At(Filter(white, over).Run(src, Ctx()), 0, 0);
        white.Attributes["color-interpolation-filters"] = "sRGB";
        over.Attributes["color-interpolation-filters"] = "sRGB";
        var srgbPx = At(Filter(white, over).Run(src, Ctx()), 0, 0);

        srgbPx[0].Should().BeApproximately(0.5f, 0.01f);
        linearPx[0].Should().BeApproximately(0.735f, 0.01f, "compositing in linearRGB then converting back to sRGB brightens the mix");
    }

    [Fact]
    public void PrimitiveSubregion_ClipsFloodToTheRectangle()
    {
        var src = new FilterImage(20, 20);
        var f = Filter(El("feflood", ("flood-color", "red"), ("x", "5"), ("y", "5"), ("width", "10"), ("height", "10"),
            ("color-interpolation-filters", "sRGB")));
        var result = f.Run(src, Ctx());

        At(result, 10, 10)[3].Should().BeApproximately(1f, 0.001f);
        At(result, 2, 2)[3].Should().Be(0f, "outside the x/y/width/height subregion");
        At(result, 17, 17)[3].Should().Be(0f);
    }

    [Fact]
    public void NamedResults_FeedLaterPrimitives()
    {
        var src = Rect(20, 20, 8, 8, 12, 12, 1, 0, 0);
        var blur = El("fegaussianblur", ("in", "SourceAlpha"), ("stddeviation", "0"), ("result", "a"));
        var off = El("feoffset", ("in", "a"), ("dx", "4"), ("result", "b"));
        var merge = El("femerge");
        merge.Children.Add(El("femergenode", ("in", "b")));
        merge.Children.Add(El("femergenode", ("in", "SourceGraphic")));
        var result = Filter(blur, off, merge).Run(src, Ctx());

        At(result, 14, 10)[3].Should().BeApproximately(1f, 0.001f, "the offset SourceAlpha copy shows to the right of the source");
    }

    [Fact]
    public void UnsupportedPrimitive_MakesFilterUnparseable()
    {
        var f = El("filter", ("id", "t"));
        f.Children.Add(El("fefoo"));
        SvgFilter.TryParse(f).Should().BeNull();
    }

    [Fact]
    public void EmptyFilter_IsUnparseable()
    {
        SvgFilter.TryParse(El("filter", ("id", "e"))).Should().BeNull();
    }

    [Fact]
    public void Region_DefaultsToBoundingBoxPlusTenPercentPerSide()
    {
        var f = Filter(El("feoffset"));
        var (x, y, w, h) = f.ResolveRegion(10, 20, 100, 50);

        x.Should().BeApproximately(0f, 0.001f);
        y.Should().BeApproximately(15f, 0.001f);
        w.Should().BeApproximately(120f, 0.001f);
        h.Should().BeApproximately(60f, 0.001f);
    }

    [Fact]
    public void Region_UserSpaceOnUse_UsesLiteralValues()
    {
        var el = El("filter", ("id", "u"), ("filterunits", "userSpaceOnUse"), ("x", "1"), ("y", "2"), ("width", "30"), ("height", "40"));
        el.Children.Add(El("feoffset"));
        var (x, y, w, h) = SvgFilter.TryParse(el)!.ResolveRegion(100, 100, 5, 5);

        (x, y, w, h).Should().Be((1f, 2f, 30f, 40f));
    }

    [Fact]
    public void Region_ObjectBoundingBoxPercentages_AreFractionsOfTheBox()
    {
        var el = El("filter", ("id", "p"), ("x", "-50%"), ("y", "0%"), ("width", "200%"), ("height", "100%"));
        el.Children.Add(El("feoffset"));
        var (x, y, w, h) = SvgFilter.TryParse(el)!.ResolveRegion(10, 10, 20, 20);

        (x, y, w, h).Should().Be((0f, 10f, 40f, 20f));
    }

    [Fact]
    public void FilterImage_ColorSpaceRoundTrip_IsLossless()
    {
        var img = Rect(2, 2, 0, 0, 2, 2, 0.2f, 0.5f, 0.8f);
        var back = img.InSpace(true).InSpace(false);
        for (int i = 0; i < 3; i++) back.Px[i].Should().BeApproximately(img.Px[i], 0.001f);
    }
}
