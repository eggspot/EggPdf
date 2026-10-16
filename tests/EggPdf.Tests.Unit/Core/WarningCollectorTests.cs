using EggPdf.Core;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Core;

public class WarningCollectorTests
{
    [Fact]
    public void NewCollector_HasNoWarnings()
    {
        var collector = new WarningCollector();

        collector.HasWarnings.Should().BeFalse();
        collector.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Add_Warning_Collected()
    {
        var collector = new WarningCollector();

        collector.Add(WarningCodes.FontNotFound, "Arial not found");

        collector.HasWarnings.Should().BeTrue();
        collector.Warnings.Should().HaveCount(1);
        collector.Warnings[0].Code.Should().Be(WarningCodes.FontNotFound);
        collector.Warnings[0].Message.Should().Be("Arial not found");
    }

    [Fact]
    public void AddFontNotFound_CreatesCorrectWarning()
    {
        var collector = new WarningCollector();

        collector.AddFontNotFound("CustomFont", "Helvetica");

        collector.Warnings[0].Code.Should().Be(WarningCodes.FontNotFound);
        collector.Warnings[0].Message.Should().Contain("CustomFont");
        collector.Warnings[0].Message.Should().Contain("Helvetica");
    }

    [Fact]
    public void AddGlyphMissing_CreatesWarningWithGlyphMissingCode()
    {
        var collector = new WarningCollector();

        collector.AddGlyphMissing("No installed font covers U+AC1C");

        collector.Warnings.Should().ContainSingle();
        collector.Warnings[0].Code.Should().Be(WarningCodes.GlyphMissing).And.Be("FONT_GLYPH_MISSING");
        collector.Warnings[0].Level.Should().Be(RenderWarningLevel.Warning);
        collector.Warnings[0].Message.Should().Contain("U+AC1C");
    }
}
