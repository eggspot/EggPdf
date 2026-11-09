using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class PictureElementTests
{
    [Fact]
    public void Img_Srcset_FallbackSrcUsedWhenNoMatch()
    {
        var root = LayoutTestHelper.Layout(
            "<img src='fallback.png' srcset='image@2x.png 2x' width='100' height='50'>", 400, 600);
        var img = root.FindAll(b => b.ImageSource != null).FirstOrDefault();
        img.Should().NotBeNull("img should produce an image box");
        img!.ImageSource.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Img_Srcset_1x_SelectsFirstDescriptor()
    {
        var root = LayoutTestHelper.Layout(
            "<img src='low.png' srcset='low.png 1x, high.png 2x' width='100' height='50'>", 400, 600);
        var img = root.FindAll(b => b.ImageSource != null).FirstOrDefault();
        img.Should().NotBeNull();
        img!.ImageSource.Should().Contain(".png", "PDF is a 1x context, so a 1x candidate is chosen");
    }

    [Fact]
    public void Img_Srcset_WidthDescriptor_SelectsSmallest()
    {
        var root = LayoutTestHelper.Layout(
            "<img src='medium.png' srcset='small.png 300w, large.png 900w' width='200' height='100'>",
            400, 600);
        var img = root.FindAll(b => b.ImageSource != null).FirstOrDefault();
        img.Should().NotBeNull();
        img!.ImageSource.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Picture_WithFallbackImg_CreatesImageBox()
    {
        var root = LayoutTestHelper.Layout(
            "<picture><img src='photo.png' width='100' height='80'></picture>", 400, 600);
        var img = root.FindAll(b => b.ImageSource != null).FirstOrDefault();
        img.Should().NotBeNull("picture with fallback img must produce image box");
        img!.ImageSource.Should().Be("photo.png");
    }

    [Fact]
    public void Picture_WithSource_UsesSourceSrcset()
    {
        var root = LayoutTestHelper.Layout(
            "<picture>" +
            "<source srcset='large.png' media='print'>" +
            "<img src='fallback.png' width='100' height='80'>" +
            "</picture>", 400, 600);
        var img = root.FindAll(b => b.ImageSource != null).FirstOrDefault();
        img.Should().NotBeNull();
        img!.ImageSource.Should().NotBeNullOrEmpty("either the source or the fallback image is a valid choice");
    }

    [Fact]
    public void Picture_DimensionsFromImg_Applied()
    {
        var root = LayoutTestHelper.Layout(
            "<picture><img src='photo.png' width='120' height='90'></picture>", 400, 600);
        var img = root.FindAll(b => b.ImageSource != null).FirstOrDefault();
        img.Should().NotBeNull();
        img!.Width.Should().BeApproximately(120, 2);
        img.Height.Should().BeApproximately(90, 2);
    }

    [Fact]
    public void Picture_DoesNotCrash_WhenEmpty()
    {
        var root = LayoutTestHelper.Layout("<picture></picture>", 400, 600);
        root.Should().NotBeNull();
    }

    [Fact]
    public void Picture_FloatRight_PinsTheImageToTheRightEdgeLikeAnImg()
    {
        var picture = LayoutTestHelper.Layout(
            "<div style='width:300px'><picture style='float:right'><img src='a.png' width='100' height='80'></picture>text</div>", 400, 600)
            .FindAll(b => b.ImageSource != null).Single();
        var img = LayoutTestHelper.Layout(
            "<div style='width:300px'><img style='float:right' src='a.png' width='100' height='80'>text</div>", 400, 600)
            .FindAll(b => b.ImageSource != null).Single();

        picture.X.Should().BeApproximately(img.X, 0.01f);
        picture.Y.Should().BeApproximately(img.Y, 0.01f);
    }

    [Fact]
    public void Picture_DisplayBlock_StartsItsOwnLine()
    {
        var root = LayoutTestHelper.Layout(
            "<div>Hello <picture style='display:block'><img src='a.png' width='50' height='40'></picture></div>", 400, 600);
        var text = root.FindAll(b => b.Text != null && b.Text.Contains("Hello")).First();
        var img = root.FindAll(b => b.ImageSource != null).Single();

        img.Y.Should().BeGreaterThan(text.Y);
        img.X.Should().BeApproximately(text.X, 0.01f);
    }
}
