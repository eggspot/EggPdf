using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Tests for CSS transform rendering in PDF output.
/// Verifies that transform functions produce correct PDF cm (concat matrix) operators.
/// </summary>
public class TransformE2ETests
{
    [Fact]
    public async Task TranslateX_MovesElement()
    {
        var html = "<div style='transform: translateX(50px); background-color: red; width: 100px; height: 100px'>Moved</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Moved");
        text.Should().Contain(" cm", "translateX should emit a cm (concat matrix) operator");
    }

    [Fact]
    public async Task TranslateY_MovesElement()
    {
        var html = "<div style='transform: translateY(30px); background-color: blue; width: 100px; height: 100px'>Down</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Down");
        text.Should().Contain(" cm", "translateY should emit a cm operator");
    }

    [Fact]
    public async Task Rotate_ProducesValidPdf()
    {
        var html = "<div style='transform: rotate(45deg); background-color: green; width: 100px; height: 100px'>Rotated</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Rotated");
        text.Should().Contain(" cm", "rotate should emit a cm operator");
        // cos(45deg) ~ 0.71
        text.Should().Contain("0.71", "cos(45deg) should appear in the matrix");
    }

    [Fact]
    public async Task Scale_ProducesValidPdf()
    {
        var html = "<div style='transform: scale(2); background-color: orange; width: 50px; height: 50px'>Scaled</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Scaled");
        text.Should().Contain(" cm", "scale should emit a cm operator");
        text.Should().Contain("2.00", "scale(2) should have 2.00 in the matrix");
    }

    [Fact]
    public async Task CombinedTransform_ProducesValidPdf()
    {
        var html = "<div style='transform: translateX(20px) rotate(30deg); background-color: purple; width: 80px; height: 80px'>Combined</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Combined");
        text.Should().Contain(" cm", "combined transforms should emit a cm operator");
    }

    [Fact]
    public async Task TransformOrigin_Center_Default()
    {
        var html = "<div style='transform: rotate(90deg); width: 100px; height: 100px; background-color: yellow'>Origin</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Origin");
        text.Should().Contain(" cm", "rotation with default origin should emit cm");
    }

    [Fact]
    public async Task NoTransform_NoCmOperator()
    {
        var html = "<div style='background-color: red; width: 100px; height: 100px'>Plain</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Plain");
        int cmCount = CountSubstring(text, " cm");
        cmCount.Should().Be(0, "no transform means no cm operator in content stream");
    }

    [Fact]
    public async Task TransformNone_NoCmOperator()
    {
        var html = "<div style='transform: none; background-color: blue; width: 100px; height: 100px'>None</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("None");
        int cmCount = CountSubstring(text, " cm");
        cmCount.Should().Be(0, "transform:none should produce no cm operator");
    }

    [Fact]
    public async Task SkewX_ProducesValidPdf()
    {
        var html = "<div style='transform: skewX(20deg); background-color: teal; width: 100px; height: 100px'>Skewed</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Skewed");
        text.Should().Contain(" cm", "skewX should emit a cm operator");
    }

    [Fact]
    public async Task Matrix_DirectValues_ProducesValidPdf()
    {
        var html = "<div style='transform: matrix(1, 0, 0.5, 1, 0, 0); background-color: pink; width: 100px; height: 100px'>Matrix</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Matrix");
        text.Should().Contain(" cm", "matrix() should emit a cm operator");
    }

    [Fact]
    public async Task Rotate_RadiansUnit_Parsed()
    {
        var html = "<div style='transform: rotate(1.5708rad); background-color: coral; width: 100px; height: 100px'>Rad</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Rad");
        text.Should().Contain(" cm", "rotate with rad unit should emit cm");
    }

    [Fact]
    public async Task Rotate_TurnUnit_Parsed()
    {
        var html = "<div style='transform: rotate(0.25turn); background-color: cyan; width: 100px; height: 100px'>Turn</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Turn");
        text.Should().Contain(" cm", "rotate with turn unit should emit cm");
    }

    [Fact]
    public async Task ScaleXY_DifferentValues()
    {
        var html = "<div style='transform: scale(2, 0.5); background-color: lime; width: 100px; height: 100px'>ScaleXY</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("ScaleXY");
        text.Should().Contain(" cm", "scale with different x/y values should emit cm");
        text.Should().Contain("2.00", "scaleX=2 should appear in the matrix");
        text.Should().Contain("0.50", "scaleY=0.5 should appear in the matrix");
    }

    [Fact]
    public async Task Transform_WithOverflow_BothApplied()
    {
        var html = "<div style='transform: translateX(10px); overflow: hidden; background-color: red; width: 100px; height: 100px'>Both</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Both");
        text.Should().Contain(" cm", "transform should be applied even with overflow:hidden");
        text.Should().StartWith("%PDF");
    }

    // ── 3D transform functions ────────────────────────────────────────────────

    [Fact]
    public async Task RotateZ_SameAsRotate()
    {
        var html = "<div style='transform: rotateZ(45deg); background-color: red; width: 100px; height: 100px'>RZ</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("RZ");
        text.Should().Contain(" cm", "rotateZ should emit a cm operator");
    }

    [Fact]
    public async Task RotateX_FlattenedToPerspective()
    {
        var html = "<div style='transform: rotateX(60deg); background-color: blue; width: 100px; height: 100px'>RX</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("RX");
        text.Should().Contain(" cm", "rotateX flattens to scaleY in 2D");
    }

    [Fact]
    public async Task RotateY_FlattenedToPerspective()
    {
        var html = "<div style='transform: rotateY(60deg); background-color: green; width: 100px; height: 100px'>RY</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("RY");
        text.Should().Contain(" cm", "rotateY flattens to scaleX in 2D");
    }

    [Fact]
    public async Task TranslateZ_NoVisualEffect()
    {
        // translateZ only affects the Z axis; in 2D PDF it has no visual effect
        var act = async () => await HtmlToPdf.RenderAsync(
            "<div style='transform: translateZ(100px); width: 100px; height: 100px'>TZ</div>");
        await act.Should().NotThrowAsync("translateZ should not crash");
    }

    [Fact]
    public async Task Translate3d_AppliesXY()
    {
        var html = "<div style='transform: translate3d(30px, 20px, 0); background-color: teal; width: 100px; height: 100px'>T3D</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("T3D");
        text.Should().Contain(" cm", "translate3d should emit a cm operator for X/Y components");
    }

    [Fact]
    public async Task Scale3d_AppliesXY()
    {
        var html = "<div style='transform: scale3d(2, 0.5, 1); background-color: orange; width: 100px; height: 100px'>S3D</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("S3D");
        text.Should().Contain(" cm", "scale3d should emit a cm operator");
    }

    [Fact]
    public async Task Perspective_DoesNotCrash()
    {
        // perspective() in transform list is purely 3D; PDF ignores it
        var act = async () => await HtmlToPdf.RenderAsync(
            "<div style='transform: perspective(500px) rotateY(30deg); width: 100px; height: 100px'>P3D</div>");
        await act.Should().NotThrowAsync("perspective() should not crash");
    }

    [Fact]
    public async Task Matrix3d_ExtractsTopLeft2x2()
    {
        // matrix3d uses the 2D subset: m0,m1,m4,m5 = a,b,c,d; m12,m13 = e,f
        // matrix3d(2,0,0,0, 0,3,0,0, 0,0,1,0, 10,20,0,1)  → scale(2,3)+translate(10,20)
        var html = "<div style='transform: matrix3d(2,0,0,0,0,3,0,0,0,0,1,0,10,20,0,1); width: 50px; height: 50px'>M3D</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("M3D");
        text.Should().Contain(" cm", "matrix3d should emit a cm operator");
    }

    private static int CountSubstring(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
