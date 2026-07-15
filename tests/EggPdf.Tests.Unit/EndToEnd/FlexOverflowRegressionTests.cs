using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Flex free-space math must account for item margins — otherwise flex:1
/// over-grows and pushes later items (the certificate's page footer) off the
/// bottom of a fixed-height page.
/// </summary>
public class FlexOverflowRegressionTests
{
    [Fact]
    public async Task FixedHeightColumnFlex_WithMargins_KeepsLastItemOnPage()
    {
        // Mirrors the certificate's .page: full-page column flex, flex:1 body,
        // margined footer. Every text op must stay within the page (y >= 0).
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body style=\"margin:0;padding:0\">" +
            "<div style=\"display:flex;flex-direction:column;min-height:297mm;box-sizing:border-box;padding:16mm 14mm\">" +
            "<header style=\"margin-bottom:14px\">HEADER</header>" +
            "<div style=\"flex:1\">BODY</div>" +
            "<div style=\"margin-top:20px\">SIGROW</div>" +
            "<div style=\"margin-top:14px\">FOOTERLINE</div>" +
            "</div></body></html>");

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("(FOOTERLINE)", "the footer must be painted");

        foreach (Match m in Regex.Matches(text, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \((\w+)\) Tj"))
        {
            float y = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            y.Should().BeGreaterThan(0f,
                $"'{m.Groups[3].Value}' must stay on the page — margins must be part of flex free-space");
        }
    }
}
