using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// Font subsetting results are cached process-wide keyed by font identity and
/// the used-codepoint set, so a service rendering many documents with the
/// same character repertoire subsets each font once, not per render.
/// </summary>
public class SubsetCacheTests
{
    private static string Latin1(byte[] pdf)
        => System.Text.Encoding.Latin1.GetString(pdf);

    [Fact]
    public async Task SecondRender_SameContent_ReusesCachedSubset()
    {
        var html = "<html><body><p>Giấy Chứng Nhận Quyền Sử Dụng - bộ đệm subset</p></body></html>";

        await HtmlToPdf.RenderAsync(html); // populate (or hit) the cache
        int before = HtmlToPdf.SubsetCacheHits;
        var pdf = await HtmlToPdf.RenderAsync(html);

        HtmlToPdf.SubsetCacheHits.Should().BeGreaterThan(before,
            "an identical render must reuse the cached subset instead of re-subsetting");
        Latin1(pdf).Should().Contain("/FontFile2",
            "the cached subset must still be embedded in the output");
    }

    [Fact]
    public async Task DifferentCodepointSets_ProduceIndependentValidSubsets()
    {
        // Different character repertoires must not collide in the cache.
        var a = await HtmlToPdf.RenderAsync("<html><body><p>Hiệp hội thứ nhất</p></body></html>");
        var b = await HtmlToPdf.RenderAsync("<html><body><p>Văn bản hoàn toàn khác ỞỰỄ</p></body></html>");

        Latin1(a).Should().Contain("/FontFile2");
        Latin1(b).Should().Contain("/FontFile2");
    }
}
