using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class SubsetCacheTests
{
    private static string Latin1(byte[] pdf)
        => System.Text.Encoding.Latin1.GetString(pdf);

    [Fact]
    public async Task SecondRender_SameContent_ReusesCachedSubset()
    {
        var html = "<html><body><p>Giấy Chứng Nhận Quyền Sử Dụng - bộ đệm subset</p></body></html>";

        await HtmlToPdf.RenderAsync(html);
        int before = HtmlToPdf.SubsetCacheHits;
        var pdf = await HtmlToPdf.RenderAsync(html);

        HtmlToPdf.SubsetCacheHits.Should().BeGreaterThan(before,
            "subsets are cached process-wide by font and code point set, so a service rendering the same characters " +
            "again reuses the first render's subset instead of subsetting per render");
        Latin1(pdf).Should().Contain("/FontFile2",
            "the cached subset must still be embedded in the output");
    }

    [Fact]
    public async Task DifferentCodepointSets_ProduceIndependentValidSubsets()
    {
        var a = await HtmlToPdf.RenderAsync("<html><body><p>Hiệp hội thứ nhất</p></body></html>");
        var b = await HtmlToPdf.RenderAsync("<html><body><p>Văn bản hoàn toàn khác ỞỰỄ</p></body></html>");

        Latin1(a).Should().Contain("/FontFile2");
        Latin1(b).Should().Contain("/FontFile2", "a different character set gets its own cache entry, not the first one's subset");
    }
}
