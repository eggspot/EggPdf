using System;
using System.Threading.Tasks;
using EggPdf.Fluent;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Fluent;

public class StatisticsTests
{
    [Fact]
    public void CollectStatistics_ReportsPagesWritten()
    {
        var statistics = new RenderStatistics();
        var doc = Document.New().CollectStatistics(statistics);
        doc.AddPage().Content().Item().Text("first group");
        doc.AddPage().Content().Item().Text("second group");

        doc.Render();

        statistics.PageCount.Should().Be(2);
    }

    [Fact]
    public async Task CollectStatistics_AlsoWorksForRenderAsync()
    {
        var statistics = new RenderStatistics();
        var doc = Document.New().CollectStatistics(statistics);
        doc.AddPage().Content().Item().Text("only page");

        await doc.RenderAsync();

        statistics.PageCount.Should().Be(1);
    }

    [Fact]
    public async Task CollectStatistics_CancelledRenderAsync_LeavesNoStaleCount()
    {
        var statistics = new RenderStatistics();
        var doc = Document.New().CollectStatistics(statistics);
        doc.AddPage().Content().Item().Text("only page");
        doc.Render();
        using var cancelled = new System.Threading.CancellationTokenSource();
        cancelled.Cancel();

        Func<Task> act = () => doc.RenderAsync(cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        statistics.PageCount.Should().Be(0);
    }

    [Fact]
    public void CollectStatistics_Null_Throws()
    {
        Action act = () => Document.New().CollectStatistics(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
