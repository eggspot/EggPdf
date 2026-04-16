using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfGradientTests
{
    // ── repeating-linear-gradient ───────────────────────────────────────────

    [Fact]
    public void RepeatingLinearGradient_DoesNotReturnNull()
    {
        var result = PdfGradient.RenderLinearGradient(
            "repeating-linear-gradient(red, blue 20px)", 0, 0, 100, 100);
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RepeatingLinearGradient_ContainsFillOperation()
    {
        var result = PdfGradient.RenderLinearGradient(
            "repeating-linear-gradient(45deg, red, blue)", 10, 20, 80, 60);
        result.Should().Contain("re f", "must paint filled rectangles");
    }

    // ── repeating-radial-gradient ───────────────────────────────────────────

    [Fact]
    public void RepeatingRadialGradient_DoesNotReturnNull()
    {
        var result = PdfGradient.RenderRepeatingRadialGradient(
            "repeating-radial-gradient(red, blue)", 0, 0, 100, 100);
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RepeatingRadialGradient_ContainsColorCommand()
    {
        var result = PdfGradient.RenderRepeatingRadialGradient(
            "repeating-radial-gradient(red 20%, yellow 40%, blue 60%)", 0, 0, 100, 100);
        // Radial gradient sets color with " rg" and paints circles
        result.Should().Contain(" rg");
    }

    // ── conic-gradient ──────────────────────────────────────────────────────

    [Fact]
    public void ConicGradient_DoesNotReturnNull()
    {
        var result = PdfConicGradient.Render(
            "conic-gradient(red, blue)", 0, 0, 100, 100);
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ConicGradient_ContainsPathFill()
    {
        var result = PdfConicGradient.Render(
            "conic-gradient(red, yellow, blue)", 10, 20, 80, 80);
        result.Should().NotBeNull();
        // Conic gradient renders as filled pie sectors
        result.Should().Contain(" f");
    }

    [Fact]
    public void ConicGradient_FromAngle_IsAccepted()
    {
        var result = PdfConicGradient.Render(
            "conic-gradient(from 90deg, red, blue)", 0, 0, 100, 100);
        result.Should().NotBeNullOrEmpty();
    }
}
