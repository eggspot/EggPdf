using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// SVG feGaussianBlur: PDF has no vector blur primitive, so a blurred shape is
/// rasterized to a bitmap (SvgRenderer.Blur.cs) and embedded as an image XObject with
/// an alpha soft mask instead of the normal vector path operators.
/// </summary>
public class SvgBlurFilterTests
{
    private static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public async Task Circle_WithGaussianBlurFilter_EmbedsRasterImageWithSoftMask()
    {
        var html = @"
            <svg width='100' height='100'>
                <defs>
                    <filter id='blur1'><feGaussianBlur stdDeviation='4'/></filter>
                </defs>
                <circle cx='50' cy='50' r='30' fill='red' filter='url(#blur1)'/>
            </svg>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = Latin1(pdf);

        text.Should().Contain("/Subtype /Image", "a blurred shape must rasterize and embed as an image XObject");
        text.Should().Contain("/SMask", "the blurred shape's alpha channel must carry through as a soft mask so it composites correctly");
    }

    [Fact]
    public async Task Rect_WithGaussianBlurFilter_EmbedsRasterImage()
    {
        var html = @"
            <svg width='100' height='100'>
                <defs><filter id='b'><feGaussianBlur stdDeviation='3'/></filter></defs>
                <rect x='10' y='10' width='40' height='40' fill='blue' filter='url(#b)'/>
            </svg>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Latin1(pdf).Should().Contain("/Subtype /Image");
    }

    [Fact]
    public async Task RoundedRect_WithGaussianBlurFilter_EmbedsRasterImage()
    {
        var html = @"
            <svg width='100' height='100'>
                <defs><filter id='b'><feGaussianBlur stdDeviation='2'/></filter></defs>
                <rect x='10' y='10' width='40' height='30' rx='8' fill='green' filter='url(#b)'/>
            </svg>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Latin1(pdf).Should().Contain("/Subtype /Image");
    }

    [Fact]
    public async Task Path_WithGaussianBlurFilter_EmbedsRasterImage()
    {
        var html = @"
            <svg width='100' height='100'>
                <defs><filter id='b'><feGaussianBlur stdDeviation='3'/></filter></defs>
                <path d='M10 10 L50 10 L50 50 L10 50 Z' fill='purple' filter='url(#b)'/>
            </svg>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Latin1(pdf).Should().Contain("/Subtype /Image");
    }

    [Fact]
    public async Task Shape_WithoutFilter_DoesNotEmbedImage()
    {
        var html = "<svg width='100' height='100'><circle cx='50' cy='50' r='30' fill='red'/></svg>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Latin1(pdf).Should().NotContain("/Subtype /Image", "an unfiltered shape must keep painting via the normal vector path");
    }

    [Fact]
    public async Task Shape_WithFillNone_UnderBlurFilter_RasterizesStrokeAsImage()
    {
        var html = @"
            <svg width='100' height='100'>
                <defs><filter id='b'><feGaussianBlur stdDeviation='3'/></filter></defs>
                <circle cx='50' cy='50' r='30' fill='none' stroke='red' filter='url(#b)'/>
            </svg>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("/Subtype /Image", "the blurred stroke is rasterized and embedded as an image");
        text.Should().MatchRegex(@"/SvgFx\w+ Do", "the raster is drawn via its SVG filter XObject");
    }

    [Fact]
    public async Task MultiplePrimitiveFilter_ChainsIntoOneRasterImage()
    {
        var html = @"
            <svg width='100' height='100'>
                <defs>
                    <filter id='multi'>
                        <feGaussianBlur stdDeviation='3'/>
                        <feOffset dx='2' dy='2'/>
                    </filter>
                </defs>
                <circle cx='50' cy='50' r='30' fill='red' filter='url(#multi)'/>
            </svg>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = Latin1(pdf);
        text.Should().Contain("/Subtype /Image", "the whole primitive chain runs over a raster of the circle");
        text.Should().NotContain("1.00 0.00 0.00 rg", "the circle is no longer painted as an unfiltered vector fill");
    }

    [Fact]
    public async Task UnsupportedPrimitiveFilter_ShapeRendersNormallyWithoutCrash()
    {
        // An unknown primitive is not implemented; the shape must still render (via the normal
        // unfiltered vector path), not crash or silently vanish.
        var html = @"
            <svg width='100' height='100'>
                <defs><filter id='noise'><feFoo/></filter></defs>
                <circle cx='50' cy='50' r='30' fill='red' filter='url(#noise)'/>
            </svg>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = Latin1(pdf);
        text.Should().NotContain("/Subtype /Image");
        text.Should().Contain("1.00 0.00 0.00 rg", "the shape must still paint (unfiltered) via the normal vector fill color");
    }

    [Fact]
    public async Task BlurredShape_InsideTransformedGroup_PositionsCorrectly()
    {
        var html = @"
            <svg width='200' height='200'>
                <defs><filter id='b'><feGaussianBlur stdDeviation='3'/></filter></defs>
                <g transform='translate(50,50) rotate(15)'>
                    <rect x='0' y='0' width='30' height='30' fill='orange' filter='url(#b)'/>
                </g>
            </svg>";

        var act = async () => await HtmlToPdf.RenderAsync(html);
        var pdf = await act();
        Latin1(pdf).Should().Contain("/Subtype /Image",
            "a blurred shape nested inside a transformed <g> must still rasterize using the combined matrix");
    }

    [Fact]
    public async Task UnknownFilterReference_IgnoresFilterAndPaintsShapeAsVector()
    {
        var html = "<svg width='100' height='100'><circle cx='50' cy='50' r='30' fill='red' filter='url(#doesNotExist)'/></svg>";
        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("1.00 0.00 0.00 rg", "the circle keeps its red fill");
        text.Should().Contain("80.00 50.00 m 80.00 66.57 66.57 80.00 50.00 80.00 c", "the circle is emitted as Bezier path data");
        text.Should().NotContain("/Subtype /Image", "an unresolvable filter reference must not trigger rasterization");
    }
}
