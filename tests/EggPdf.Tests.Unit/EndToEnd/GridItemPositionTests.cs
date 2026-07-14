using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Regression tests for grid item positioning: every grid cell's content must
/// paint at the cell's position, not at the grid container's origin.
/// </summary>
public class GridItemPositionTests
{
    private static (float x, float y) FindTextPosition(byte[] pdf, string text)
    {
        var content = Encoding.ASCII.GetString(pdf);
        var match = Regex.Match(content,
            @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(" + Regex.Escape(text) + @"\) Tj");
        match.Success.Should().BeTrue($"text '{text}' should appear in the content stream with coordinates");
        return (float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Grid_TwoColumns_SecondItemOffsetHorizontally()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"display:grid;grid-template-columns:1fr 195px;gap:24px;width:600px\">" +
            "<div>LEFTCOL</div><div>RIGHTCOL</div></div></body></html>");

        var left = FindTextPosition(pdf, "LEFTCOL");
        var right = FindTextPosition(pdf, "RIGHTCOL");

        // Left column is 600-195-24 = 381px wide; right column starts at 405px ≈ 303.75pt after it
        right.x.Should().BeGreaterThan(left.x + 250,
            "the second grid column's content must be offset to its cell, not painted at the container origin");
        right.y.Should().BeApproximately(left.y, 2f, "both items are on the same grid row");
    }

    [Fact]
    public async Task Grid_ThreeFixedColumns_EachItemInItsCell()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"display:grid;grid-template-columns:100px 100px 100px\">" +
            "<div>AAA</div><div>BBB</div><div>CCC</div></div></body></html>");

        var a = FindTextPosition(pdf, "AAA");
        var b = FindTextPosition(pdf, "BBB");
        var c = FindTextPosition(pdf, "CCC");

        // 100px = 75pt column width
        b.x.Should().BeApproximately(a.x + 75f, 2f);
        c.x.Should().BeApproximately(a.x + 150f, 2f);
    }

    [Fact]
    public async Task Grid_TwoRows_SecondRowOffsetVertically()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"display:grid;grid-template-columns:1fr\">" +
            "<div>ROWONE</div><div>ROWTWO</div></div></body></html>");

        var one = FindTextPosition(pdf, "ROWONE");
        var two = FindTextPosition(pdf, "ROWTWO");

        two.y.Should().BeLessThan(one.y - 5,
            "the second row's content must paint below the first row (PDF Y grows upward)");
    }
}
