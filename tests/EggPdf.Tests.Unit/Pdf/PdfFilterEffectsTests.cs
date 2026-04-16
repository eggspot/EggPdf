using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfFilterEffectsTests
{
    // ── drop-shadow parsing ─────────────────────────────────────────────────

    [Fact]
    public void DropShadow_ParsesOffsets()
    {
        var p = PdfFilterEffects.Parse("drop-shadow(4px 6px black)");
        p.Should().NotBeNull();
        p!.DropShadowX.Should().BeApproximately(4f, 0.01f, "X offset should be 4px");
        p.DropShadowY.Should().BeApproximately(6f, 0.01f, "Y offset should be 6px");
    }

    [Fact]
    public void DropShadow_ParsesBlurRadius()
    {
        var p = PdfFilterEffects.Parse("drop-shadow(2px 3px 8px #333)");
        p.Should().NotBeNull();
        p!.DropShadowBlur.Should().BeApproximately(8f, 0.01f, "blur radius should be 8px");
    }

    [Fact]
    public void DropShadow_DefaultColorIsBlack()
    {
        var p = PdfFilterEffects.Parse("drop-shadow(1px 2px)");
        p.Should().NotBeNull();
        // Default color is black (0, 0, 0)
        p!.DropShadowR.Should().BeApproximately(0f, 0.01f);
        p.DropShadowG.Should().BeApproximately(0f, 0.01f);
        p.DropShadowB.Should().BeApproximately(0f, 0.01f);
    }

    [Fact]
    public void DropShadow_ParsesNamedColor()
    {
        var p = PdfFilterEffects.Parse("drop-shadow(0px 0px 4px red)");
        p.Should().NotBeNull();
        p!.DropShadowR.Should().BeApproximately(1f, 0.01f, "red → R=1");
        p.DropShadowG.Should().BeApproximately(0f, 0.01f);
        p.DropShadowB.Should().BeApproximately(0f, 0.01f);
    }

    [Fact]
    public void DropShadow_HasEffectIsTrue()
    {
        var p = PdfFilterEffects.Parse("drop-shadow(2px 2px black)");
        p!.HasEffect.Should().BeTrue("drop-shadow sets HasEffect");
    }

    [Fact]
    public void OtherFilters_StillWork_WhenCombined()
    {
        var p = PdfFilterEffects.Parse("grayscale(1) drop-shadow(2px 2px 4px black)");
        p.Should().NotBeNull();
        p!.Grayscale.Should().BeApproximately(1f, 0.01f);
        p.DropShadowX.Should().BeApproximately(2f, 0.01f);
    }
}
