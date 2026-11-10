using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class ImageDecodeCacheTests
{
    private const string RedPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task DataUriImage_SecondRender_ReusesDecodedImage()
    {
        var html = "<html><body><img src='data:image/png;base64," + RedPixelPng +
                   "' width='10' height='10'></body></html>";

        await HtmlToPdf.RenderAsync(html);
        int before = HtmlToPdf.ImageCacheHits;
        var pdf = await HtmlToPdf.RenderAsync(html);

        HtmlToPdf.ImageCacheHits.Should().BeGreaterThan(before,
            "a data: URI's content can't change, so it is decoded once per process, unlike a file path whose file " +
            "can change on disk between renders");
        System.Text.Encoding.Latin1.GetString(pdf).Should().Contain("/XObject",
            "the cached image must still be embedded");
    }
}
