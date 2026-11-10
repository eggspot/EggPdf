using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class InlineBreakAllTests
{
    [Fact]
    public async Task LongWordInSpan_BreakAll_SplitsAcrossLines()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"width:120px;word-break:break-all\">" +
            "intro <span>DEV.VCRRM.ORG/LICENSES/VERIFY/?C=VCRRM-2026-6QDNKQ</span></div></body></html>");

        var text = Encoding.ASCII.GetString(pdf);

        var pieces = Regex.Matches(text, @"\(([^)]*VCRRM[^)]*|[^)]*VERIFY[^)]*|[^)]*DEV\.[^)]*)\) Tj").Count;
        pieces.Should().BeGreaterThan(1,
            "a 50-char URL cannot fit a 120px column in one piece, and break-all must split it in the inline-runs path too");

        foreach (Match m in Regex.Matches(text, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(([^)]+)\) Tj"))
        {
            float x = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            x.Should().BeLessThan(100f, "fragments start within the 120px (90pt) column");
        }
    }
}
