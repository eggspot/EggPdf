using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// An auto-width inline-block shrink-wraps its content (CSS 2.1 §10.3.9) and
/// participates in the parent's text-align — the QR frame must hug its image
/// and sit centered in its column, not fill the full width.
/// </summary>
public class InlineBlockShrinkToFitTests
{
    // 1x1 red PNG
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

    [Fact]
    public async Task AutoWidthInlineBlock_ShrinksToContent()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"width:260px\">" +
            "<div style=\"display:inline-block;padding:8px;background:#112233\">" +
            $"<img src='data:image/png;base64,{Png}' style='display:block;width:100px;height:100px'>" +
            "</div></div></body></html>");

        var text = Encoding.ASCII.GetString(pdf);
        // Frame background rect: content 100px + padding 16px = 116px = 87pt wide
        var m = Regex.Match(text, @"(-?[\d.]+) (-?[\d.]+) ([\d.]+) ([\d.]+) re f");
        bool found = false;
        foreach (Match r in Regex.Matches(text, @"(-?[\d.]+) (-?[\d.]+) ([\d.]+) ([\d.]+) re f"))
        {
            float w = float.Parse(r.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            float h = float.Parse(r.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (h > 80 && h < 100 && w > 80)
            {
                w.Should().BeApproximately(87f, 2f, "the frame must shrink-wrap image + padding, not fill 260px");
                found = true;
            }
        }
        found.Should().BeTrue("the inline-block background rect should be painted");
    }

    [Fact]
    public async Task InlineBlock_InCenteredParent_IsCentered()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body style=\"margin:0\"><div style=\"width:260px;text-align:center\">" +
            "<div style=\"display:inline-block;padding:8px;background:#112233\">" +
            $"<img src='data:image/png;base64,{Png}' style='display:block;width:100px;height:100px'>" +
            "</div></div></body></html>");

        var text = Encoding.ASCII.GetString(pdf);
        foreach (Match r in Regex.Matches(text, @"(-?[\d.]+) (-?[\d.]+) ([\d.]+) ([\d.]+) re f"))
        {
            float x = float.Parse(r.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            float w = float.Parse(r.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            float h = float.Parse(r.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (h > 80 && h < 100 && w > 80)
            {
                // container 260px = 195pt; frame 116px = 87pt → centered x = (195-87)/2 = 54pt
                x.Should().BeApproximately(54f, 3f, "text-align:center centers the inline-block in its parent");
            }
        }
    }
}
