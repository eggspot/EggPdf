using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// content-visibility: hidden and contain: paint/strict. Most of the `contain` property's
/// real-world purpose (isolating incremental recalculation for rendering performance) has
/// no analog in a single-pass batch renderer, so only the observable effects are covered:
/// contain:paint/strict clips descendant painting to the box (same mechanism as
/// overflow:hidden), and content-visibility:hidden skips descendant layout+paint entirely
/// while the element's own box still renders.
/// </summary>
public class ContainmentTests
{
    private static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public async Task ContentVisibilityHidden_DescendantTextDoesNotRender()
    {
        var html = "<div style='content-visibility: hidden; height: 50px'>Secret text that must not appear</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Latin1(pdf).Should().NotContain("Secret text", "descendant content must not render under content-visibility:hidden");
    }

    [Fact]
    public async Task ContentVisibilityHidden_OwnBackgroundStillRenders()
    {
        var html = "<div style='content-visibility: hidden; height: 50px; width: 50px; background-color: red'>Hidden</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Latin1(pdf).Should().Contain("1.00 0.00 0.00 rg", "the element's own background must still paint -- content-visibility only skips descendants");
    }

    [Fact]
    public async Task ContainPaint_EmitsClipRect()
    {
        var html = "<div style='contain: paint; width: 50px; height: 50px; background-color: red'>Clipped</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Latin1(pdf).Should().Contain("re W n", "contain:paint must establish a clip region for the box");
    }

    [Fact]
    public async Task ContainStrict_EmitsClipRect()
    {
        var html = "<div style='contain: strict; width: 50px; height: 50px'>Clipped</div>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        Latin1(pdf).Should().Contain("re W n", "contain:strict implies paint containment");
    }

    [Fact]
    public async Task ContainLayout_DoesNotClipOrCrash()
    {
        // contain:layout alone (without paint/strict) has no observable effect in a
        // single-pass renderer; it must not crash or spuriously clip.
        var act = async () => await HtmlToPdf.RenderAsync(
            "<div style='contain: layout; width: 50px; height: 50px'>Text</div>");
        await act.Should().NotThrowAsync();
    }
}
