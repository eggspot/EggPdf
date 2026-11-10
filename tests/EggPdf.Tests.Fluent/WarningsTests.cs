using System;
using System.Linq;
using System.Threading.Tasks;
using EggPdf.Core;
using EggPdf.Fluent;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Fluent;

/// <summary>
/// <see cref="DocumentDescriptor.CollectWarnings"/>: the fluent surface of the render-warning sink
/// (same pipeline as <see cref="PdfRenderOptions.Warnings"/>).
/// </summary>
public class WarningsTests
{
    private const string PlaneSixteenPrivateUseCharacterNoFontMaps = "\U0010FFFD";

    [Fact]
    public void CollectWarnings_ReportsCharactersNoFontCovers()
    {
        var warnings = new WarningCollector();
        var doc = Document.New().CollectWarnings(warnings);
        doc.AddPage().Content().Item().Text("Ref " + PlaneSixteenPrivateUseCharacterNoFontMaps);

        byte[] pdf = doc.Render();

        pdf.Length.Should().BeGreaterThan(0);
        warnings.Warnings.Should().ContainSingle(w => w.Code == WarningCodes.GlyphMissing)
            .Which.Message.Should().Contain("U+10FFFD");
    }

    [Fact]
    public async Task CollectWarnings_AlsoWorksForRenderAsync()
    {
        var warnings = new WarningCollector();
        var doc = Document.New().CollectWarnings(warnings);
        doc.AddPage().Content().Item().Text(PlaneSixteenPrivateUseCharacterNoFontMaps);

        await doc.RenderAsync();

        warnings.Warnings.Should().Contain(w => w.Code == WarningCodes.GlyphMissing);
    }

    [Fact]
    public void CollectWarnings_NoProblems_LeavesCollectorEmpty()
    {
        var warnings = new WarningCollector();
        var doc = Document.New().CollectWarnings(warnings);
        doc.AddPage().Content().Item().Text("Plain text");

        doc.Render();

        warnings.HasWarnings.Should().BeFalse();
    }

    [Fact]
    public void CollectWarnings_NullCollector_Throws()
    {
        Action act = () => Document.New().CollectWarnings(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CollectWarnings_AfterRender_Throws()
    {
        var doc = Document.New();
        doc.AddPage().Content().Item().Text("x");
        doc.Render();

        Action act = () => doc.CollectWarnings(new WarningCollector());

        act.Should().Throw<InvalidOperationException>("a built document is frozen");
    }
}
