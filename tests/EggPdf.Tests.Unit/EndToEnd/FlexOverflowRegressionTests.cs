using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class FlexOverflowRegressionTests
{
    [Fact]
    public async Task FixedHeightColumnFlex_WithMargins_KeepsLastItemOnPage()
    {
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
                $"'{m.Groups[3].Value}' must stay on the page: item margins count against flex free space, or the " +
                "flex:1 body over-grows and pushes the footer off the fixed-height page");
        }
    }
}
