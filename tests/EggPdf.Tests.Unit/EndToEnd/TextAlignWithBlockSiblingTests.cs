using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// A text node whose sibling is a &lt;span&gt; explicitly styled <c>display: block</c> (a common
/// pattern -- e.g. a caption line followed by a URL/link line, like a QR code caption) must still
/// honor text-align on its container. The tag-name-based "does this parent have inline siblings"
/// check treated every &lt;span&gt; as inline regardless of its computed display, routing the text
/// through the word-by-word inline-flow path -- which has no text-align centering support at all
/// (each word's LayoutBox.Width equals its own ContentWidth, so PdfRenderer's
/// `box.ContentWidth &lt; box.Width` centering gate never fires) -- instead of the normal
/// per-line text path that does.
/// </summary>
public class TextAlignWithBlockSiblingTests
{
    [Fact]
    public async Task TextBeforeBlockStyledSpanSibling_StillCenters()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><div style=\"width:300px\">" +
            "<div style=\"text-align:center\">Short caption" +
            "<span style=\"display:block\">a block-level sibling line</span></div>" +
            "</div></body></html>");

        var content = Encoding.ASCII.GetString(pdf);
        var m = Regex.Match(content, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(Short caption\) Tj");
        m.Success.Should().BeTrue("'Short caption' should be painted as a single line, not split word-by-word");

        float x = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        x.Should().BeGreaterThan(20f,
            "the caption must be centered within the 300px container, not flush against its left edge");
    }
}
