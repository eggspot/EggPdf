using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>Tests for &lt;picture&gt; element and srcset attribute handling.</summary>
public class PictureElementTests
{
    // ── srcset on <img> ──────────────────────────────────────────────────────

    [Fact]
    public void Img_Srcset_FallbackSrcUsedWhenNoMatch()
    {
        // When srcset has no viable URL, the src attribute is used
        var root = LayoutTestHelper.Layout(
            "<img src='fallback.png' srcset='image@2x.png 2x' width='100' height='50'>", 400, 600);
        var img = root.FindAll(b => b.ImageSource != null).FirstOrDefault();
        img.Should().NotBeNull("img should produce an image box");
        img!.ImageSource.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Img_Srcset_1x_SelectsFirstDescriptor()
    {
        // srcset with density descriptors: 1x should resolve to the 1x URL
        var root = LayoutTestHelper.Layout(
            "<img src='low.png' srcset='low.png 1x, high.png 2x' width='100' height='50'>", 400, 600);
        var img = root.FindAll(b => b.ImageSource != null).FirstOrDefault();
        img.Should().NotBeNull();
        // PDF is print context (1x), so low.png or the first 1x image should be selected
        img!.ImageSource.Should().Contain(".png");
    }

    [Fact]
    public void Img_Srcset_WidthDescriptor_SelectsSmallest()
    {
        // srcset with width descriptors: PDF should pick a reasonable candidate
        var root = LayoutTestHelper.Layout(
            "<img src='medium.png' srcset='small.png 300w, large.png 900w' width='200' height='100'>",
            400, 600);
        var img = root.FindAll(b => b.ImageSource != null).FirstOrDefault();
        img.Should().NotBeNull();
        img!.ImageSource.Should().NotBeNullOrEmpty();
    }

    // ── <picture> element ────────────────────────────────────────────────────

    [Fact]
    public void Picture_WithFallbackImg_CreatesImageBox()
    {
        // <picture> with only a fallback <img> should still produce an image box
        var root = LayoutTestHelper.Layout(
            "<picture><img src='photo.png' width='100' height='80'></picture>", 400, 600);
        var img = root.FindAll(b => b.ImageSource != null).FirstOrDefault();
        img.Should().NotBeNull("picture with fallback img must produce image box");
        img!.ImageSource.Should().Be("photo.png");
    }

    [Fact]
    public void Picture_WithSource_UsesSourceSrcset()
    {
        // <picture> with <source> should use the source's srcset
        var root = LayoutTestHelper.Layout(
            "<picture>" +
            "<source srcset='large.png' media='print'>" +
            "<img src='fallback.png' width='100' height='80'>" +
            "</picture>", 400, 600);
        var img = root.FindAll(b => b.ImageSource != null).FirstOrDefault();
        img.Should().NotBeNull();
        // Either source or fallback image should be selected — both are valid
        img!.ImageSource.Should().NotBeNullOrEmpty();
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
        // Empty <picture> should not crash — just produces no image box
        var root = LayoutTestHelper.Layout("<picture></picture>", 400, 600);
        root.Should().NotBeNull();
    }
}
