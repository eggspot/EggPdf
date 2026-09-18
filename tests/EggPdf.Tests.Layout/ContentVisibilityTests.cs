using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>
/// content-visibility: hidden skips layout of descendants (unlike display:none, the
/// element's own box still exists and takes up space) -- its size doesn't depend on
/// content it never laid out, matching the spec's implied size containment.
/// </summary>
public class ContentVisibilityTests
{
    [Fact]
    public void ContentVisibilityHidden_WithExplicitHeight_KeepsThatHeight()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='content-visibility: hidden; height: 80px; width: 200px'>" +
            "<p>Some long paragraph content that would normally grow the container</p>" +
            "</div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(80f, 0.5f, "explicit height must still apply even though children are skipped");
    }

    [Fact]
    public void ContentVisibilityHidden_NoExplicitHeight_CollapsesToZero()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='content-visibility: hidden; width: 200px'>" +
            "<p>Content that would normally set the auto height</p>" +
            "</div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeApproximately(0f, 0.5f, "auto height must not be influenced by skipped content");
    }

    [Fact]
    public void ContentVisibilityHidden_ChildrenAreNotLaidOut()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='content-visibility: hidden; height: 50px'><p id='inner'>Hidden text</p></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Children.Should().BeEmpty("descendants must not be laid out at all under content-visibility:hidden");
    }

    [Fact]
    public void ContentVisibilityVisible_LaysOutNormally()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='content-visibility: visible; width: 200px'><p>Normal content</p></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Children.Should().NotBeEmpty("content-visibility:visible must lay out children normally");
    }

    [Fact]
    public void NoContentVisibility_LaysOutNormally()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='width: 200px'><p>Normal content</p></div>", 600, 800);

        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Children.Should().NotBeEmpty();
    }
}
