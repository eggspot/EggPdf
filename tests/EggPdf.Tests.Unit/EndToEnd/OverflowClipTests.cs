using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// overflow:hidden/clip and contain:paint/strict must clip descendant painting, not just
/// the clipping box's own background/border. Painting walks a flat, pre-collected box list
/// rather than a recursive tree (each box gets its own independent PaintBox call), so a
/// clipping ancestor's bounds have to be looked up explicitly per descendant (PdfRenderer's
/// ancestor map + GetClipAncestors) and wrapped around that descendant's own paint call --
/// this is what actually makes the clip apply to children, not just the container itself.
/// </summary>
public class OverflowClipTests
{
    private static string Ascii(byte[] pdf) => Encoding.ASCII.GetString(pdf);

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    [Fact]
    public async Task NoOverflowHidden_NoClipOperatorsEmitted()
    {
        var html = "<div style='width:100px; height:100px; background-color: red'>Plain</div>";
        var pdf = await HtmlToPdf.RenderAsync(html);
        Ascii(pdf).Should().NotContain("re W n", "no clip should be emitted when nothing establishes one");
    }

    [Fact]
    public async Task OverflowHidden_WithNoOwnBackground_StillClipsOverflowingChild()
    {
        // The container itself has no background/border, so it's never independently
        // painted (PageFragmenter's hasPaint filter drops it) -- the pre-existing
        // self-clip mechanism (inside BoxPainter.PaintBox) never fires for it at all.
        // The child's paint call must still be wrapped in the container's clip via the
        // ancestor map, or nothing clips it.
        var html = "<div style='overflow: hidden; width: 50px; height: 50px'>" +
                   "<div style='width: 200px; height: 200px; background-color: blue'>Content</div>" +
                   "</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Ascii(pdf).Should().Contain("re W n",
            "the overflowing child must be clipped even though the container has no paint of its own");
    }

    [Fact]
    public async Task OverflowHidden_WithOwnBackground_ClipsBothItselfAndChild()
    {
        var html = "<div style='overflow: hidden; width: 50px; height: 50px; background-color: white'>" +
                   "<div style='width: 200px; height: 200px; background-color: blue'>Content</div>" +
                   "</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = Ascii(pdf);
        CountOccurrences(text, "re W n").Should().BeGreaterThan(1,
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
        var text = Ascii(pdf);
        CountOccurrences(text, "re W n").Should().BeGreaterThanOrEqualTo(2,
            "a doubly-nested overflow:hidden ancestry must apply both clip rects to the innermost content");
    }

    [Fact]
    public async Task ZIndexPositionedChild_StillGetsClippedByOverflowAncestor()
    {
        // z-index sorting reorders positioned boxes within the flat paint list, so clip
        // reconstruction can't rely on list order/adjacency -- it must use real tree
        // ancestry (PdfRenderer's ancestor map) regardless of where z-index moved this
        // box to in the paint order.
        var html = "<div style='overflow: hidden; width: 50px; height: 50px; position: relative'>" +
                   "<div style='position: absolute; z-index: 5; width: 200px; height: 200px; background-color: purple'></div>" +
                   "</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Ascii(pdf).Should().Contain("re W n",
            "a z-index-positioned descendant must still be clipped by its overflow:hidden ancestor");
    }

    [Fact]
    public async Task ContainStrict_ClipsOverflowingChild()
    {
        var html = "<div style='contain: strict; width: 50px; height: 50px'>" +
                   "<div style='width: 200px; height: 200px; background-color: orange'>Content</div>" +
                   "</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Ascii(pdf).Should().Contain("re W n", "contain:strict must clip overflowing descendant content, same as overflow:hidden");
    }

    [Fact]
    public async Task OverflowVisible_DoesNotClipChild()
    {
        var html = "<div style='overflow: visible; width: 50px; height: 50px'>" +
                   "<div style='width: 200px; height: 200px; background-color: teal'>Content</div>" +
                   "</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Ascii(pdf).Should().NotContain("re W n", "overflow:visible must not clip anything");
    }
}
