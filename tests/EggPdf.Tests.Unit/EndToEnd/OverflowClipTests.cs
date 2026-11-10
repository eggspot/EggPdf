using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class OverflowClipTests
{
    private const string ClipToRectangle = "re W n";

    private static string Ascii(byte[] pdf) => Encoding.ASCII.GetString(pdf);

    [Fact]
    public async Task NoOverflowHidden_NoClipOperatorsEmitted()
    {
        var html = "<div style='width:100px; height:100px; background-color: red'>Plain</div>";
        var pdf = await HtmlToPdf.RenderAsync(html);
        Ascii(pdf).Should().NotContain(ClipToRectangle, "no clip should be emitted when nothing establishes one");
    }

    [Fact]
    public async Task OverflowHidden_WithNoOwnBackground_StillClipsOverflowingChild()
    {
        var html = "<div style='overflow: hidden; width: 50px; height: 50px'>" +
                   "<div style='width: 200px; height: 200px; background-color: blue'>Content</div>" +
                   "</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Ascii(pdf).Should().Contain(ClipToRectangle,
            "a container with no background or border is never painted itself, so its clip must still be wrapped " +
            "around each descendant's paint call through the ancestor map");
    }

    [Fact]
    public async Task OverflowHidden_WithOwnBackground_ClipsBothItselfAndChild()
    {
        var html = "<div style='overflow: hidden; width: 50px; height: 50px; background-color: white'>" +
                   "<div style='width: 200px; height: 200px; background-color: blue'>Content</div>" +
                   "</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        PdfAssert.Count(Ascii(pdf), ClipToRectangle).Should().BeGreaterThan(1,
            "both the container's own self-clip and the child's ancestor-clip must be present");
    }

    [Fact]
    public async Task NestedOverflowHidden_ChildGetsClippedByBothAncestors()
    {
        var html = "<div style='overflow: hidden; width: 80px; height: 80px'>" +
                   "  <div style='overflow: hidden; width: 60px; height: 60px'>" +
                   "    <div style='width: 300px; height: 300px; background-color: green'>Deep</div>" +
                   "  </div>" +
                   "</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        PdfAssert.Count(Ascii(pdf), ClipToRectangle).Should().BeGreaterThanOrEqualTo(2,
            "a doubly-nested overflow:hidden ancestry must apply both clip rects to the innermost content");
    }

    [Fact]
    public async Task ZIndexPositionedChild_StillGetsClippedByOverflowAncestor()
    {
        var html = "<div style='overflow: hidden; width: 50px; height: 50px; position: relative'>" +
                   "<div style='position: absolute; z-index: 5; width: 200px; height: 200px; background-color: purple'></div>" +
                   "</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Ascii(pdf).Should().Contain(ClipToRectangle,
            "z-index reorders the flat paint list, so the clip must come from the real tree ancestry, not list order");
    }

    [Fact]
    public async Task ContainStrict_ClipsOverflowingChild()
    {
        var html = "<div style='contain: strict; width: 50px; height: 50px'>" +
                   "<div style='width: 200px; height: 200px; background-color: orange'>Content</div>" +
                   "</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Ascii(pdf).Should().Contain(ClipToRectangle, "contain:strict must clip overflowing descendant content, same as overflow:hidden");
    }

    [Fact]
    public async Task OverflowVisible_DoesNotClipChild()
    {
        var html = "<div style='overflow: visible; width: 50px; height: 50px'>" +
                   "<div style='width: 200px; height: 200px; background-color: teal'>Content</div>" +
                   "</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Ascii(pdf).Should().NotContain(ClipToRectangle, "overflow:visible must not clip anything");
    }
}
