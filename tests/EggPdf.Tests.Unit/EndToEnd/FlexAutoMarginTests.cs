using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Auto margins on flex items absorb positive free space (CSS Flexbox §8.1) —
/// margin-top:auto pushes a footer to the bottom of a column flex container.
/// </summary>
public class FlexAutoMarginTests
{
    private static float FindTextY(byte[] pdf, string text)
    {
        var content = Encoding.ASCII.GetString(pdf);
        var m = Regex.Match(content, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(" + Regex.Escape(text) + @"\) Tj");
        m.Success.Should().BeTrue($"'{text}' should be painted");
        return float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task MarginTopAuto_PushesItemToContainerBottom()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body style=\"margin:0;padding:0\">" +
            "<div style=\"display:flex;flex-direction:column;height:600px\">" +
            "<div>TOPCONTENT</div>" +
            "<div style=\"margin-top:auto\">BOTTOMFOOTER</div>" +
            "</div></body></html>");

        float topY = FindTextY(pdf, "TOPCONTENT");
        float footerY = FindTextY(pdf, "BOTTOMFOOTER");

        // 600px = 450pt container from the page top (841.89): footer should sit
        // near y ≈ 841.89 - 450 + lineHeight ≈ 403, far below the top item.
        (topY - footerY).Should().BeGreaterThan(380f,
            "margin-top:auto must absorb the free space and push the footer to the bottom");
    }
}
