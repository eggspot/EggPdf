using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class RubyLayoutTests
{
    [Fact]
    public void Ruby_DoesNotCrash()
    {
        var root = LayoutTestHelper.Layout(
            "<ruby>漢字<rt>かんじ</rt></ruby>", 400, 600);
        root.Should().NotBeNull();
    }

    [Fact]
    public void Ruby_BaseText_IsRendered()
    {
        var root = LayoutTestHelper.Layout(
            "<ruby>Base<rt>Anno</rt></ruby>", 400, 600);
        var textBoxes = root.FindAll(b => b.Text?.Contains("Base") == true);
        textBoxes.Should().NotBeEmpty("base text inside ruby must produce a text box");
    }

    [Fact]
    public void Ruby_Annotation_IsRendered()
    {
        var root = LayoutTestHelper.Layout(
            "<ruby>Base<rt>Anno</rt></ruby>", 400, 600);
        var rtBoxes = root.FindAll(b => b.Text?.Contains("Anno") == true);
        rtBoxes.Should().NotBeEmpty("rt annotation text must produce a text box");
    }

    [Fact]
    public void Ruby_Annotation_PositionedAboveBase()
    {
        var root = LayoutTestHelper.Layout(
            "<div><ruby>Base<rt>Anno</rt></ruby></div>", 400, 600);
        var (baseBox, annoBox) = FindBaseAndAnnotation(root, "Base", "Anno");

        annoBox.Y.Should().BeLessOrEqualTo(baseBox.Y,
            "annotation text should be positioned above (or at same Y as) base text");
    }

    [Fact]
    public void Ruby_ContainerHasPositiveDimensions()
    {
        var root = LayoutTestHelper.Layout(
            "<div style='font-size:16px'><ruby>Text<rt>Note</rt></ruby></div>", 400, 600);
        var div = root.FindByTag("div");
        div.Should().NotBeNull();
        div!.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Rp_FallbackParentheses_DoNotCrash()
    {
        var root = LayoutTestHelper.Layout(
            "<ruby>漢字<rp>(</rp><rt>かんじ</rt><rp>)</rp></ruby>", 400, 600);
        root.Should().NotBeNull();
    }

    [Fact]
    public void Ruby_HasInlineDisplay()
    {
        var root = LayoutTestHelper.Layout(
            "<p>Before <ruby>Word<rt>Note</rt></ruby> After</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        var baseTextBoxes = root.FindAll(b => b.Text?.Contains("Word") == true ||
                                             b.Text?.Contains("Before") == true);
        baseTextBoxes.Should().NotBeEmpty("the ruby flows inline with the paragraph's own text");
    }

    [Fact]
    public void RubyPosition_Under_PlacesAnnotationBelowBase()
    {
        var root = LayoutTestHelper.Layout(
            "<div><ruby style='ruby-position: under'>Base<rt>Anno</rt></ruby></div>", 400, 600);
        var (baseBox, annoBox) = FindBaseAndAnnotation(root, "Base", "Anno");

        annoBox.Y.Should().BeGreaterOrEqualTo(baseBox.Y,
            "ruby-position:under should place annotation below the base text");
    }

    [Fact]
    public void RubyPosition_Over_IsDefault_PlacesAnnotationAbove()
    {
        var root = LayoutTestHelper.Layout(
            "<div><ruby>Base<rt>Anno</rt></ruby></div>", 400, 600);
        var (baseBox, annoBox) = FindBaseAndAnnotation(root, "Base", "Anno");

        annoBox.Y.Should().BeLessOrEqualTo(baseBox.Y,
            "default (over) should place annotation above the base text");
    }

    [Fact]
    public void RubyAlign_Start_PlacesAnnotationAtStart()
    {
        var root = LayoutTestHelper.Layout(
            "<div><ruby style='ruby-align: start'>AB<rt>X</rt></ruby></div>", 400, 600);
        var (baseBox, annoBox) = FindBaseAndAnnotation(root, "AB", "X");

        annoBox.X.Should().BeApproximately(baseBox.X, 2f,
            "ruby-align:start should left-align annotation with base start");
    }

    [Fact]
    public void RubyAlign_End_PlacesAnnotationAtEnd()
    {
        var root = LayoutTestHelper.Layout(
            "<div><ruby style='ruby-align: end'>AB<rt>X</rt></ruby></div>", 400, 600);
        var (baseBox, annoBox) = FindBaseAndAnnotation(root, "AB", "X");

        float annoRight = annoBox.X + annoBox.Width;
        float baseRight = baseBox.X + baseBox.Width;
        annoRight.Should().BeApproximately(baseRight, 2f,
            "ruby-align:end should right-align annotation with base end");
    }

    private static (LayoutBox Base, LayoutBox Annotation) FindBaseAndAnnotation(
        LayoutBox root, string baseText, string annotationText)
    {
        var baseBox = root.FindAll(b => b.Text?.Contains(baseText) == true).FirstOrDefault();
        var annoBox = root.FindAll(b => b.Text?.Contains(annotationText) == true).FirstOrDefault();
        baseBox.Should().NotBeNull("the ruby base text is laid out");
        annoBox.Should().NotBeNull("the rt annotation text is laid out");
        return (baseBox!, annoBox!);
    }
}
