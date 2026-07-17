using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// data: URI images are decoded once per process (their content is immutable
/// by construction); file-path images stay uncached because the file can
/// change on disk between renders.
/// </summary>
public class ImageDecodeCacheTests
{
    // 1x1 red pixel PNG
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task DataUriImage_SecondRender_ReusesDecodedImage()
    {
        var html = "<html><body><img src='data:image/png;base64," + OnePixelPng +
                   "' width='10' height='10'></body></html>";

        await HtmlToPdf.RenderAsync(html); // populate (or hit) the cache
        int before = HtmlToPdf.ImageCacheHits;
        var pdf = await HtmlToPdf.RenderAsync(html);

        HtmlToPdf.ImageCacheHits.Should().BeGreaterThan(before,
            "an identical data: URI must not be base64-decoded and PNG-decoded again");
        System.Text.Encoding.Latin1.GetString(pdf).Should().Contain("/XObject",
            "the cached image must still be embedded");
    }
}
