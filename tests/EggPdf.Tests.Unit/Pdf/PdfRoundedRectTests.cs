using System.Text;
using System.Threading.Tasks;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfRoundedRectTests
{
    [Fact]
    public void RoundedRect_UniformRadius_ContainsCurveOperators()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
        page.AddRoundedRectangle(72, 700, 200, 100, 1f, 0f, 0f, 10, 10, 10, 10);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        // Bézier curve operator 'c' should be present for rounded corners
        // Pattern: six float values followed by ' c'
        text.Should().MatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c",
            "rounded corners use Bézier curve operators");
        text.Should().Contain("h f", "path should be closed and filled");
    }

    [Fact]
    public void RoundedRect_PerCorner_DifferentRadii()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
        page.AddRoundedRectangle(72, 700, 200, 100, 0f, 0.5f, 0f, 5, 10, 15, 20);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        // All four corners should have curve operators
        text.Should().MatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c",
            "all corners should use Bézier curves");
        text.Should().Contain("h f");
    }

    [Fact]
    public void RoundedRect_ZeroRadius_NoCurves()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
        page.AddRoundedRectangle(72, 700, 200, 100, 0f, 0f, 1f, 0, 0, 0, 0);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        // With zero radii, corners use lineto instead of curves
        text.Should().NotMatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c",
            "zero radius should not produce curves");
        text.Should().Contain("h f", "path should still be closed and filled");
    }

    [Fact]
    public void RoundedRect_RadiusClamped_NoExceedHalfSize()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
        // Box is 40x20, so max radius should be clamped to 10 (half of 20)
        page.AddRoundedRectangle(72, 700, 40, 20, 1f, 0f, 0f, 50, 50, 50, 50);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        // Should still produce valid curves (with clamped radii)
        text.Should().MatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c",
            "clamped radii should still produce curves");
        text.Should().Contain("h f");
    }

    [Fact]
    public void RoundedRect_BackgroundAndBorder_BothRounded()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);

        // Add filled rounded rect (background)
        page.AddRoundedRectangle(72, 700, 200, 100, 0.9f, 0.9f, 0.9f, 10, 10, 10, 10);
        // Add stroked rounded rect (border)
        page.AddStrokeRoundedRectangle(72, 700, 200, 100, 0f, 0f, 0f, 1f, 10, 10, 10, 10);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("h f", "background should be filled");
        text.Should().Contain("h S", "border should be stroked");
    }

    [Fact]
    public void StrokeRoundedRect_ContainsStrokeOperators()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
        page.AddStrokeRoundedRectangle(72, 700, 200, 100, 0f, 0f, 0f, 2f, 15, 15, 15, 15);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("RG", "stroke color should be set");
        text.Should().MatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c",
            "corners should use curves");
        text.Should().Contain("h S", "path should be closed and stroked");
    }

    [Fact]
    public async Task BorderRadius_InPdf_ContainsCurves()
    {
        var html = "<div style='background-color: #eee; border: 2px solid black; border-radius: 10px; width: 200px; height: 100px'>Rounded</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Rounded", "text content should be present");
        text.Should().MatchRegex(@"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c",
            "border-radius should produce Bézier curve operators");
    }
}
