using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class ListMarkerRenderTests
{
    [Fact]
    public async Task OrderedListStart_PaintsMarkersFromStart()
    {
        var pdf = await HtmlToPdf.RenderAsync("<html><body><ol start=\"3\"><li>Three</li><li>Four</li></ol></body></html>");

        var content = Encoding.ASCII.GetString(pdf);
        content.Should().Contain("(3.) Tj");
        content.Should().Contain("(4.) Tj");
        content.Should().NotContain("(1.) Tj");
    }
}
