using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit;

/// <summary>
/// <see cref="PdfRenderOptions.Statistics"/>: the render reports how many pages it wrote, so callers (e.g.
/// billing per page) never have to parse the PDF bytes for it.
/// </summary>
public class RenderStatisticsTests
{
    private const string ThreePages =
        "<html><body><div style=\"page-break-after:always\">one</div>" +
        "<div style=\"page-break-after:always\">two</div><div>three</div></body></html>";

    private static string Latin1(byte[] pdf) => Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

    [Fact]
    public void Options_Statistics_DefaultsToNull()
    {
        new PdfRenderOptions().Statistics.Should().BeNull();
    }

    [Fact]
    public void Render_WithStatistics_ReportsPagesWritten()
    {
        var statistics = new RenderStatistics();

        var pdf = HtmlToPdf.Render(ThreePages, new PdfRenderOptions { Statistics = statistics });

        statistics.PageCount.Should().Be(3);
        PdfAssert.PageCount(Latin1(pdf)).Should().Be(3, "the reported count matches the pages in the PDF");
    }

    [Fact]
    public async Task RenderAsync_WithStatistics_ReportsPagesWritten()
    {
        var statistics = new RenderStatistics();

        await HtmlToPdf.RenderAsync(ThreePages, new PdfRenderOptions { Statistics = statistics });

        statistics.PageCount.Should().Be(3);
    }

    [Fact]
    public void Render_NamedPageGroups_CountsEveryGroup()
    {
        var statistics = new RenderStatistics();
        const string html = "<html><head><style>@page wide { size: A4 landscape; } .w { page: wide; }</style></head>" +
                            "<body><div>portrait</div><div class=\"w\">landscape</div></body></html>";

        var pdf = HtmlToPdf.Render(html, new PdfRenderOptions { Statistics = statistics });

        statistics.PageCount.Should().Be(PdfAssert.PageCount(Latin1(pdf))).And.BeGreaterThan(1);
    }

    [Fact]
    public void Render_ReusedStatistics_ReportsOnlyTheLatestRender()
    {
        var statistics = new RenderStatistics();
        HtmlToPdf.Render(ThreePages, new PdfRenderOptions { Statistics = statistics });

        HtmlToPdf.Render("<p>one page</p>", new PdfRenderOptions { Statistics = statistics });

        statistics.PageCount.Should().Be(1);
    }

    [Fact]
    public async Task RenderAsync_CancelledBeforeStart_LeavesNoStaleCount()
    {
        var statistics = new RenderStatistics();
        HtmlToPdf.Render(ThreePages, new PdfRenderOptions { Statistics = statistics });
        using var cancelled = new System.Threading.CancellationTokenSource();
        cancelled.Cancel();

        Func<Task> act = () => HtmlToPdf.RenderAsync("<p>x</p>", new PdfRenderOptions { Statistics = statistics }, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        statistics.PageCount.Should().Be(0);
    }

    [Fact]
    public void Render_FailingRender_LeavesNoStaleCount()
    {
        var statistics = new RenderStatistics();
        HtmlToPdf.Render(ThreePages, new PdfRenderOptions { Statistics = statistics });

        // PDF/A-1 forbids transparency: this render throws before writing anything
        Action act = () => HtmlToPdf.Render("<p style=\"opacity:0.5\">x</p>", new PdfRenderOptions
        {
            Statistics = statistics,
            Conformance = EggPdf.Pdf.PdfAConformance.PdfA1b,
        });

        act.Should().Throw<Exception>();
        statistics.PageCount.Should().Be(0);
    }
}
