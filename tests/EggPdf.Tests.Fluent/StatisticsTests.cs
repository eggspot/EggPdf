using System;
using System.Threading.Tasks;
using EggPdf.Fluent;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Fluent;

/// <summary>
/// <see cref="DocumentDescriptor.CollectStatistics"/>: the fluent surface of <see cref="PdfRenderOptions.Statistics"/>.
/// </summary>
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
    public void CollectStatistics_Null_Throws()
    {
        Action act = () => Document.New().CollectStatistics(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
