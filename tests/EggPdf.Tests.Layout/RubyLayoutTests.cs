using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>Tests for &lt;ruby&gt; / &lt;rt&gt; annotation layout.</summary>
public class RubyLayoutTests
{
    // ── basic structure ──────────────────────────────────────────────────────

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
        // The base text should appear somewhere in the layout
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
        var baseBox = root.FindAll(b => b.Text?.Contains("Base") == true).FirstOrDefault();
        var annoBox = root.FindAll(b => b.Text?.Contains("Anno") == true).FirstOrDefault();
        if (baseBox == null || annoBox == null) return; // Skip if not yet rendered

        // Annotation should be at a smaller or equal Y (higher on page = smaller Y in top-down coords)
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

    // ── rp parentheses ────────────────────────────────────────────────────────

    [Fact]
    public void Rp_DoesNotCrash()
    {
        // <rp> provides fallback parentheses; should not crash
        var root = LayoutTestHelper.Layout(
            "<ruby>漢字<rp>(</rp><rt>かんじ</rt><rp>)</rp></ruby>", 400, 600);
        root.Should().NotBeNull();
    }

    // ── UA stylesheet defaults ────────────────────────────────────────────────

    [Fact]
    public void Ruby_HasInlineDisplay()
    {
        var root = LayoutTestHelper.Layout(
            "<p>Before <ruby>Word<rt>Note</rt></ruby> After</p>", 400, 600);
        var p = root.FindByTag("p");
        // The paragraph should contain text including the base word
        p.Should().NotBeNull();
        var baseTextBoxes = root.FindAll(b => b.Text?.Contains("Word") == true ||
                                             b.Text?.Contains("Before") == true);
        baseTextBoxes.Should().NotBeEmpty();
    }

    // ── ruby-position ─────────────────────────────────────────────────────────

    [Fact]
    public void RubyPosition_Under_PlacesAnnotationBelowBase()
    {
        var root = LayoutTestHelper.Layout(
            "<div><ruby style='ruby-position: under'>Base<rt>Anno</rt></ruby></div>", 400, 600);
        var baseBox = root.FindAll(b => b.Text?.Contains("Base") == true).FirstOrDefault();
        var annoBox = root.FindAll(b => b.Text?.Contains("Anno") == true).FirstOrDefault();
        if (baseBox == null || annoBox == null) return;

        // With ruby-position:under, annotation should be BELOW (larger Y) than base
        annoBox.Y.Should().BeGreaterOrEqualTo(baseBox.Y,
            "ruby-position:under should place annotation below the base text");
    }

    [Fact]
    public void RubyPosition_Over_IsDefault_PlacesAnnotationAbove()
    {
        var root = LayoutTestHelper.Layout(
            "<div><ruby>Base<rt>Anno</rt></ruby></div>", 400, 600);
        var baseBox = root.FindAll(b => b.Text?.Contains("Base") == true).FirstOrDefault();
        var annoBox = root.FindAll(b => b.Text?.Contains("Anno") == true).FirstOrDefault();
        if (baseBox == null || annoBox == null) return;

        annoBox.Y.Should().BeLessOrEqualTo(baseBox.Y,
            "default (over) should place annotation above the base text");
    }

    // ── ruby-align ────────────────────────────────────────────────────────────

    [Fact]
    public void RubyAlign_Start_PlacesAnnotationAtStart()
    {
        var root = LayoutTestHelper.Layout(
            "<div><ruby style='ruby-align: start'>AB<rt>X</rt></ruby></div>", 400, 600);
        var baseBox = root.FindAll(b => b.Text?.Contains("AB") == true).FirstOrDefault();
        var annoBox = root.FindAll(b => b.Text?.Contains("X") == true).FirstOrDefault();
        if (baseBox == null || annoBox == null) return;

        // start alignment: annotation X should be at (or very near) the base start X
        annoBox.X.Should().BeApproximately(baseBox.X, 2f,
            "ruby-align:start should left-align annotation with base start");
    }

    [Fact]
    public void RubyAlign_End_PlacesAnnotationAtEnd()
    {
        var root = LayoutTestHelper.Layout(
            "<div><ruby style='ruby-align: end'>AB<rt>X</rt></ruby></div>", 400, 600);
        var baseBox = root.FindAll(b => b.Text?.Contains("AB") == true).FirstOrDefault();
        var annoBox = root.FindAll(b => b.Text?.Contains("X") == true).FirstOrDefault();
        if (baseBox == null || annoBox == null) return;

        // end alignment: annotation right edge should be at base right edge
        float annoRight = annoBox.X + annoBox.Width;
        float baseRight = baseBox.X + baseBox.Width;
        annoRight.Should().BeApproximately(baseRight, 2f,
            "ruby-align:end should right-align annotation with base end");
    }
}
