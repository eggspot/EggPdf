using System.Collections.Generic;
using System.Linq;
using EggPdf.Html;
using EggPdf.Html.Dom;
using EggPdf.Pdf;
using EggPdf.Svg;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Svg;

/// <summary>
/// SVG filters end to end through SvgRenderer: the rasterized result is inspected pixel by pixel
/// (the embedded PdfImage's RGB + alpha planes) and the emitted placement operator is checked.
/// A 100x100 viewBox at scale 1 gives 2 raster pixels per user unit.
/// </summary>
public class SvgFilterRenderTests
{
    private const float PxPerUnit = 2f;

    private sealed class Result
    {
        public string Commands = "";
        public List<string> Images = new List<string>();
        public PdfDocument Doc = new PdfDocument();
        public PdfImage Image(int i = 0) => Doc.GetImage(Images[i])!;
    }

    private static Result Render(string svgInner, string defs = "", float x = 0, float y = 0)
    {
        var html = "<html><body><svg width='100' height='100' viewBox='0 0 100 100'>" + defs + svgInner + "</svg></body></html>";
        var doc = HtmlParser.Parse(html);
        var svgEl = doc.Body!.ChildNodes.OfType<HtmlElement>().First(e => e.TagName == "svg");
        var pdf = new PdfDocument();
        var (commands, images) = SvgRenderer.Render(SvgParser.Parse(svgEl)!, x, y, 100, 100, pdf);
        return new Result { Commands = commands, Images = images, Doc = pdf };
    }

    private static byte Alpha(PdfImage img, int px, int py) => img.SMaskData![py * img.Width + px];

    private static byte[] Rgb(PdfImage img, int px, int py)
    {
        int i = (py * img.Width + px) * 3;
        return new[] { img.Data[i], img.Data[i + 1], img.Data[i + 2] };
    }

    [Fact]
    public void Blur_ImageIsPlacedInLocalSpace_NotDoubleTransformed()
    {
        // Region = bbox (20,30,40,20) plus 10%: x=16 y=28 w=48 h=24. The CTM already carries the
        // viewBox mapping (here placed at 10,20), so the image must be positioned in local units.
        var r = Render("<rect x='20' y='30' width='40' height='20' fill='red' filter='url(#b)'/>",
            "<defs><filter id='b'><feGaussianBlur stdDeviation='2'/></filter></defs>", x: 10, y: 20);

        r.Images.Should().HaveCount(1);
        r.Commands.Should().Contain("48.00 0 0 -24.00 16.00 52.00 cm",
            "width/height in local units, y flipped: the image's top edge sits at local y=28 (28+24=52 in the flipped square)");
        r.Image().Width.Should().Be((int)(48 * PxPerUnit));
        r.Image().Height.Should().Be((int)(24 * PxPerUnit));
    }

    [Fact]
    public void Blur_SoftensTheEdge_AndKeepsTheCentreOpaque()
    {
        var r = Render("<rect x='20' y='30' width='40' height='20' fill='red' filter='url(#b)'/>",
            "<defs><filter id='b' x='-50%' y='-50%' width='200%' height='200%'><feGaussianBlur stdDeviation='3'/></filter></defs>");

        var img = r.Image();
        // Region origin = (20-20, 30-10) = (0, 20); local (40,40) is the rect centre
        Alpha(img, (int)(40 * PxPerUnit), (int)((40 - 20) * PxPerUnit)).Should().BeGreaterThan(240);
        // 2 units outside the left edge: partially covered, strictly between 0 and full
        byte edge = Alpha(img, (int)(18 * PxPerUnit), (int)((40 - 20) * PxPerUnit));
        edge.Should().BeInRange(5, 120);
        Rgb(img, (int)(40 * PxPerUnit), (int)((40 - 20) * PxPerUnit))[0].Should().BeGreaterThan(240, "red survives the blur un-darkened");
    }

    [Fact]
    public void DropShadow_PaintsATintedOffsetCopyBesideTheShape()
    {
        var r = Render("<rect x='20' y='20' width='20' height='20' fill='red' filter='url(#s)'/>",
            "<defs><filter id='s' x='0' y='0' width='2' height='2'>" +
            "<feDropShadow dx='10' dy='10' stdDeviation='0' flood-color='#0000ff'/></filter></defs>");

        var img = r.Image();
        int Px(float local) => (int)((local - 20) * PxPerUnit); // region origin is (20,20)

        var source = Rgb(img, Px(25), Px(25));
        source[0].Should().BeGreaterThan(240, "the source rect stays red on top");
        Alpha(img, Px(25), Px(25)).Should().Be(255);

        Alpha(img, Px(45), Px(45)).Should().Be(255, "45,45 lies in the rect shifted by (10,10)");
        var shadow = Rgb(img, Px(45), Px(45));
        shadow[2].Should().BeGreaterThan(240);
        shadow[0].Should().BeLessThan(15);

        Alpha(img, Px(55), Px(55)).Should().Be(0, "beyond the shadow");
    }

    [Fact]
    public void FilterOnGroup_RasterizesAllChildrenIntoOneImage()
    {
        var r = Render(
            "<g filter='url(#o)'><rect x='10' y='10' width='20' height='20' fill='red'/>" +
            "<circle cx='70' cy='70' r='10' fill='blue'/></g>",
            "<defs><filter id='o'><feOffset dx='0' dy='0'/></filter></defs>");

        r.Images.Should().HaveCount(1, "a filtered group is one filter input, not one image per child");
        var img = r.Image();
        // bbox = (10,10)-(80,80); region = (3,3)-(87,87)
        int Px(float local) => (int)((local - 3) * PxPerUnit);
        Rgb(img, Px(20), Px(20))[0].Should().BeGreaterThan(240);
        Rgb(img, Px(70), Px(70))[2].Should().BeGreaterThan(240);
        Alpha(img, Px(50), Px(50)).Should().Be(0, "the gap between the two shapes is empty");
    }

    [Fact]
    public void StrokeOnlyShape_IsRasterized()
    {
        var r = Render("<rect x='20' y='20' width='60' height='60' fill='none' stroke='black' stroke-width='6' filter='url(#i)'/>",
            "<defs><filter id='i'><feOffset dx='0' dy='0'/></filter></defs>");

        var img = r.Image();
        int Px(float local) => (int)((local - 14) * PxPerUnit); // region origin 14
        Alpha(img, Px(20), Px(50)).Should().BeGreaterThan(240, "the left stroke centre line");
        Alpha(img, Px(50), Px(50)).Should().Be(0, "the unfilled interior");
    }

    [Fact]
    public void ArcSegments_AreFlattenedAsCurves_NotStraightChords()
    {
        // Half circle from (10,50) over the top to (90,50): the arc peaks near (50,10)
        var r = Render("<path d='M10 50 A40 40 0 0 1 90 50' fill='none' stroke='black' stroke-width='4' filter='url(#i)'/>",
            "<defs><filter id='i' x='0' y='0' width='1' height='1'><feOffset dx='0' dy='0'/></filter></defs>");

        var img = r.Image();
        // bbox: x 10..90, y 10..50 (the arc's extent) -> region origin (10,10)
        int Px(float local, float origin) => (int)((local - origin) * PxPerUnit);
        Alpha(img, Px(50, 10), Px(11, 10)).Should().BeGreaterThan(200, "the arc's top point (50,10) is stroked");
        Alpha(img, Px(50, 10), Px(30, 10)).Should().Be(0, "a straight chord from end to end would have no ink at y=30");
    }

    [Fact]
    public void FilterFromStyleAttribute_IsApplied()
    {
        var r = Render("<rect x='20' y='20' width='20' height='20' fill='red' style='filter:url(#b)'/>",
            "<defs><filter id='b'><feGaussianBlur stdDeviation='1'/></filter></defs>");
        r.Images.Should().HaveCount(1);
    }

    [Fact]
    public void FilterOnText_FallsBackToUnfilteredPainting()
    {
        var r = Render("<text x='10' y='50' filter='url(#b)'>Hi</text>",
            "<defs><filter id='b'><feGaussianBlur stdDeviation='1'/></filter></defs>");

        r.Images.Should().BeEmpty("text can't be rasterized here, so it paints normally rather than vanishing");
        r.Commands.Should().Contain("Tj");
    }

    [Fact]
    public void UnsupportedPrimitive_LeavesShapeUnfiltered_ButVisible()
    {
        var r = Render("<rect x='20' y='20' width='20' height='20' fill='red' filter='url(#t)'/>",
            "<defs><filter id='t'><feTurbulence baseFrequency='0.05'/></filter></defs>");

        r.Images.Should().BeEmpty();
        r.Commands.Should().Contain(" re", "the rectangle still paints as vector");
    }

    [Fact]
    public void GradientFill_UnderFilter_FallsBackToUnfiltered()
    {
        var r = Render("<rect x='20' y='20' width='20' height='20' fill='url(#g)' filter='url(#b)'/>",
            "<defs><filter id='b'><feGaussianBlur stdDeviation='1'/></filter></defs>");
        r.Images.Should().BeEmpty();
    }

    [Fact]
    public void ElementWithoutFilter_StaysVector()
    {
        var r = Render("<rect x='20' y='20' width='20' height='20' fill='red'/>",
            "<defs><filter id='b'><feGaussianBlur stdDeviation='1'/></filter></defs>");
        r.Images.Should().BeEmpty();
    }

    [Fact]
    public void FilterOnTransformedElement_RasterizesInTheElementsOwnCoordinates()
    {
        var r = Render("<rect x='0' y='0' width='20' height='20' fill='red' transform='translate(50,50)' filter='url(#o)'/>",
            "<defs><filter id='o'><feOffset dx='0' dy='0'/></filter></defs>");

        // Local bbox (0,0,20,20) -> region (-2,-2,24,24); translate(50,50) is applied by the CTM, not baked in
        r.Commands.Should().Contain("24.00 0 0 -24.00 -2.00 22.00 cm");
        r.Commands.Should().Contain("1 0 0 1 50.00 50.00 cm", "the element's transform still applies around the image");
    }

    [Fact]
    public void LinearRgbDefault_MakesAHalfOpaqueBlackShadowLighterThanSrgb()
    {
        // 50% black composited over white: linearRGB merge gives ~0.735, sRGB gives 0.5
        string Filter(string space) =>
            "<defs><filter id='m' x='0' y='0' width='1' height='1' color-interpolation-filters='" + space + "'>" +
            "<feFlood flood-color='white' result='w'/><feMerge><feMergeNode in='w'/><feMergeNode in='SourceGraphic'/></feMerge></filter></defs>";
        const string shape = "<rect x='10' y='10' width='20' height='20' fill='black' fill-opacity='0.5' filter='url(#m)'/>";

        byte linear = Rgb(Render(shape, Filter("linearRGB")).Image(), 20, 20)[0];
        byte srgb = Rgb(Render(shape, Filter("sRGB")).Image(), 20, 20)[0];

        srgb.Should().BeInRange(124, 131);
        linear.Should().BeInRange(182, 192);
    }
}
