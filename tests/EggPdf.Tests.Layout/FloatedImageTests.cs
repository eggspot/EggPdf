using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary><c>&lt;img style="float:left|right"&gt;</c> leaves normal flow and text wraps around it.</summary>
public class FloatedImageTests
{
    private static readonly string Words = string.Join(" ", Enumerable.Repeat("word", 80));

    private static LayoutBox Layout(string html) => LayoutTestHelper.Layout("<body style='margin:0'>" + html + "</body>", 400, 800);

    private static System.Collections.Generic.List<LayoutBox> Lines(LayoutBox root)
        => root.FindByTag("p")!.Children.FindAll(c => !string.IsNullOrEmpty(c.Text));

    [Fact]
    public void LeftFloatedImage_SitsAtTheLeftEdge_AndTextWrapsBesideIt()
    {
        var root = Layout("<img src='x.png' width='100' height='60' style='float:left'><p style='margin:0'>" + Words + "</p>");

        var img = root.FindByTag("img")!;
        img.X.Should().BeApproximately(0f, 0.5f);
        img.Y.Should().BeApproximately(0f, 0.5f);
        img.IsFloat.Should().BeTrue();

        var lines = Lines(root);
        lines.FindAll(l => l.Y + l.Height <= 60.5f).Should().OnlyContain(l => l.X >= 99.5f, "lines beside the image start right of it");
        lines.FindAll(l => l.Y >= 60.5f).Should().OnlyContain(l => l.X < 0.5f, "lines below the image use the full width");
        lines.Any(l => l.Y >= 60.5f).Should().BeTrue();
    }

    [Fact]
    public void RightFloatedImage_SitsAtTheRightEdge_AndTextStopsBeforeIt()
    {
        var root = Layout("<img src='x.png' width='100' height='60' style='float:right'><p style='margin:0'>" + Words + "</p>");

        var img = root.FindByTag("img")!;
        img.X.Should().BeApproximately(300f, 0.5f, "400px container minus the 100px image");

        Lines(root).FindAll(l => l.Y + l.Height <= 60.5f)
            .Should().OnlyContain(l => l.X + l.Width <= 300.5f, "text beside a right float ends before it");
    }

    [Fact]
    public void ImageMargins_PushTextFurtherAway()
    {
        var root = Layout("<img src='x.png' width='100' height='60' style='float:left;margin:0 20px 10px 5px'><p style='margin:0'>" + Words + "</p>");

        root.FindByTag("img")!.X.Should().BeApproximately(5f, 0.5f, "the left margin offsets the image from the edge");
        Lines(root).First().X.Should().BeApproximately(125f, 1f, "5 + 100 + 20 margin-right");
    }

    [Fact]
    public void ClearLeft_MovesContentBelowAFloatedImage()
    {
        var root = Layout("<img src='x.png' width='100' height='60' style='float:left'><p id='below' style='margin:0;clear:left'>after</p>");

        var p = root.FindByTag("p")!;
        p.Y.Should().BeGreaterOrEqualTo(59.5f, "clear:left starts below the float");
    }

    [Fact]
    public void ShapeOutsideCircle_OnAFloatedImage_NarrowsTheExclusionNearTheTop()
    {
        var root = Layout("<img src='x.png' width='100' height='100' style='float:left;shape-outside:circle(50%)'><p style='margin:0'>" + Words + "</p>");

        Lines(root).First().X.Should().BeLessThan(97f, "at the top the circle is narrower than the 100px box");
    }

    [Fact]
    public void UnfloatedImage_StaysInline()
    {
        var root = Layout("<p style='margin:0'><img src='x.png' width='40' height='20'> text</p>");
        root.FindByTag("img")!.IsFloat.Should().BeFalse();
    }
}
