using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// word-break: break-all must split an unbreakable word (URL) across lines in
/// the inline-runs path too, not only in plain block text — the QR caption URL
/// previously overflowed its column.
/// </summary>
public class InlineBreakAllTests
{
    [Fact]
    public async Task LongWordInSpan_BreakAll_SplitsAcrossLines()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"width:120px;word-break:break-all\">" +
            "intro <span>DEV.VCRRM.ORG/LICENSES/VERIFY/?C=VCRRM-2026-6QDNKQ</span></div></body></html>");

        var text = Encoding.ASCII.GetString(pdf);

        // The URL must be split into multiple text boxes (multiple Tj containing its fragments)
        var pieces = Regex.Matches(text, @"\(([^)]*VCRRM[^)]*|[^)]*VERIFY[^)]*|[^)]*DEV\.[^)]*)\) Tj").Count;
        pieces.Should().BeGreaterThan(1, "a 50-char URL cannot fit a 120px column in one piece");

        // And no fragment may be wider than the container: check X positions stay in bounds
        foreach (Match m in Regex.Matches(text, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(([^)]+)\) Tj"))
        {
            float x = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            x.Should().BeLessThan(100f, "fragments start within the 120px (90pt) column");
        }
    }
}
