using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfRoundedRectTests
{
    private const float A4Width = 595.28f, A4Height = 841.89f;
    private const string BezierCurve = @"\d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ \d+\.\d+ c";

    [Fact]
    public void RoundedRect_UniformRadius_ContainsCurveOperators()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(A4Width, A4Height);
        page.AddRoundedRectangle(72, 700, 200, 100, 1f, 0f, 0f, 10, 10, 10, 10);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().MatchRegex(BezierCurve, "rounded corners use Bézier curve operators");
        text.Should().Contain("h f", "path should be closed and filled");
    }

    [Fact]
    public void RoundedRect_PerCorner_DifferentRadii()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(A4Width, A4Height);
        page.AddRoundedRectangle(72, 700, 200, 100, 0f, 0.5f, 0f, 5, 10, 15, 20);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        Regex.Matches(text, BezierCurve).Count.Should().Be(4, "each of the four corners is its own curve");
        text.Should().Contain("h f");
    }

    [Fact]
    public void RoundedRect_ZeroRadius_NoCurves()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(A4Width, A4Height);
        page.AddRoundedRectangle(72, 700, 200, 100, 0f, 0f, 1f, 0, 0, 0, 0);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().NotMatchRegex(BezierCurve, "zero-radius corners are straight lines, not curves");
        text.Should().Contain("h f", "path should still be closed and filled");
    }

    [Fact]
    public void RoundedRect_RadiusClamped_NoExceedHalfSize()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(A4Width, A4Height);
        page.AddRoundedRectangle(72, 700, 40, 20, 1f, 0f, 0f, 50, 50, 50, 50);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().MatchRegex(BezierCurve, "clamped radii should still produce curves");
        text.Should().Contain("82.00 720.00 m",
            "radii of 50 on a 40x20 box are scaled down to 10, half the height, so the path starts 10 right of the top-left corner");
        text.Should().Contain("h f");
    }

    [Fact]
    public void RoundedRect_BackgroundAndBorder_BothRounded()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(A4Width, A4Height);

        page.AddRoundedRectangle(72, 700, 200, 100, 0.9f, 0.9f, 0.9f, 10, 10, 10, 10);
        page.AddStrokeRoundedRectangle(72, 700, 200, 100, 0f, 0f, 0f, 1f, 10, 10, 10, 10);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("h f", "background should be filled");
        text.Should().Contain("h S", "border should be stroked");
        Regex.Matches(text, BezierCurve).Count.Should().Be(8, "the background and the border each round four corners");
    }

    [Fact]
    public void StrokeRoundedRect_ContainsStrokeOperators()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(A4Width, A4Height);
        page.AddStrokeRoundedRectangle(72, 700, 200, 100, 0f, 0f, 0f, 2f, 15, 15, 15, 15);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("RG", "stroke color should be set");
        text.Should().MatchRegex(BezierCurve, "corners should use curves");
        text.Should().Contain("h S", "path should be closed and stroked");
    }

    [Fact]
    public async Task BorderRadius_InPdf_ContainsCurves()
    {
        var html = "<div style='background-color: #eee; border: 2px solid black; border-radius: 10px; width: 200px; height: 100px'>Rounded</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Rounded", "text content should be present");
        text.Should().MatchRegex(BezierCurve, "border-radius should produce Bézier curve operators");
    }
}
