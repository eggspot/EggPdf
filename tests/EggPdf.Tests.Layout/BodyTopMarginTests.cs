using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class BodyTopMarginTests
{
    private const float H1TopMargin = 32f * 0.67f;

    private static float FirstY(string html) =>
        LayoutTestHelper.Layout(html, 600, 800).FindById("first")!.Y;

    [Fact]
    public void DefaultBodyMargin_FirstChildWithoutMargin_StartsEightPixelsDown()
    {
        FirstY("<html><body><div id='first' style='height:10px'></div></body></html>").Should().BeApproximately(8f, 0.01f);
    }

    [Fact]
    public void DefaultBodyMargin_CollapsesWithALargerFirstChildMargin()
    {
        FirstY("<html><body><h1 id='first'>Title</h1></body></html>").Should().BeApproximately(H1TopMargin, 0.01f);
    }

    [Fact]
    public void CustomBodyMargin_CollapsesWithTheFirstChildMarginInsteadOfAdding()
    {
        FirstY("<html><body style='margin:20px'><h1 id='first'>Title</h1></body></html>").Should().BeApproximately(H1TopMargin, 0.01f);
        FirstY("<html><body style='margin:30px'><h1 id='first'>Title</h1></body></html>").Should().BeApproximately(30f, 0.01f);
    }

    [Fact]
    public void BodyPaddingTop_StopsTheCollapse()
    {
        FirstY("<html><body style='padding-top:5px'><div id='first' style='margin-top:4px;height:10px'></div></body></html>")
            .Should().BeApproximately(8f + 5f + 4f, 0.01f);
    }

    [Fact]
    public void ZeroBodyMargin_FirstChildStartsAtTheTop()
    {
        FirstY("<html><body style='margin:0'><div id='first' style='height:10px'></div></body></html>").Should().BeApproximately(0f, 0.01f);
    }
}
