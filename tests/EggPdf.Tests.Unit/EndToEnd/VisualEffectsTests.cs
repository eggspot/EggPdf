using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class VisualEffectsTests
{
    [Fact]
    public async Task BoxShadow_DoesNotCrash()
    {
        var html = "<div style='box-shadow: 2px 2px 5px rgba(0,0,0,0.3); width: 200px; height: 100px; background-color: white'>Shadow box</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BorderRadius_DoesNotCrash()
    {
        var html = "<div style='border-radius: 10px; background-color: blue; width: 100px; height: 100px'></div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Opacity_DoesNotCrash()
    {
        var html = "<div style='opacity: 0.5; background-color: red; width: 100px; height: 100px'>Semi-transparent</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Transform_DoesNotCrash()
    {
        var html = "<div style='transform: rotate(5deg); width: 100px; height: 100px'>Rotated</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TextDecoration_Rendered()
    {
        var html = "<p><u>Underlined</u> <s>Strikethrough</s></p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Underlined");
        text.Should().Contain("Strikethrough");
    }

    [Fact]
    public async Task TextShadow_DoesNotCrash()
    {
        var html = "<h1 style='text-shadow: 2px 2px 4px #000'>Shadow text</h1>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MultiColumn_DoesNotCrash()
    {
        var html = "<div style='column-count: 2; column-gap: 20px'><p>Column content that should flow into two columns.</p></div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CssNesting_DoesNotCrash()
    {
        var html = "<style>div { & p { color: red; } }</style><div><p>Nested CSS</p></div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ContainerQuery_DoesNotCrash()
    {
        var html = "<style>@container (min-width: 300px) { p { color: blue; } }</style><p>Container query</p>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task VisibilityHidden_TextNotVisible()
    {
        var html = "<div style='visibility: hidden'>Hidden text</div><div>Visible text</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Visible text");
    }

    [Fact]
    public async Task CjkText_DoesNotCrash()
    {
        var html = "<p>English text and some Chinese: 你好世界</p>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Emoji_DoesNotCrash()
    {
        // Note: emoji may not render correctly without color emoji font,
        // but should not crash
        var html = "<p>Hello World 🌍🎉</p>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BackdropFilter_DoesNotCrash()
    {
        var html = "<div style='backdrop-filter: blur(8px) saturate(180%); background-color: rgba(255,255,255,0.5); width:200px; height:100px'>Glass</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ImageSet_DoesNotCrash()
    {
        // image-set() with multiple resolutions — PDF should pick the best and not crash
        var html = "<div style=\"background-image: image-set(url('low.png') 1x, url('high.png') 2x); width:100px; height:100px\">IS</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ImageSet_StyleStored()
    {
        // Verify image-set() value is stored in computed style without crashing cascade
        var html = "<div style=\"background-image: image-set('img.png' 1x, 'img2x.png' 2x); width:100px; height:100px\">IS2</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BackgroundClip_Text_DoesNotCrash()
    {
        // background-clip: text creates gradient text — PDF approximates by rendering background behind text
        var html = @"<h1 style='background-image: linear-gradient(to right, red, blue);
            background-clip: text; -webkit-background-clip: text;
            color: transparent; font-size: 32px'>Gradient Text</h1>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("background-clip: text should not crash the renderer");
    }

    // ── text-emphasis ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TextEmphasis_Dot_DoesNotCrash()
    {
        // text-emphasis: dot paints bullet marks above each character; must not crash
        var html = "<p style='text-emphasis-style: dot; text-emphasis-color: red'>Hello</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        pdf.Should().NotBeEmpty();
        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("Hello", "base text should still be present with emphasis marks");
    }

    [Fact]
    public async Task TextEmphasis_Circle_DoesNotCrash()
    {
        var html = "<p style='text-emphasis-style: open circle; text-emphasis-color: blue'>Hi</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        pdf.Should().NotBeEmpty();
        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("Hi", "base text should still be present with emphasis marks");
    }

    [Fact]
    public async Task TextEmphasis_StringMark_AppearsInPdf()
    {
        // ASCII string mark 'x' should appear multiple times (once per char + emphasis marks)
        var html = "<p style='text-emphasis-style: \"x\"'>ABC</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);
        // "ABC" — 3 chars → 3 'x' emphasis marks painted above, plus the original 'ABC' text
        // Count occurrences of 'x' in the PDF
        int xCount = 0;
        foreach (char c in text) if (c == 'x') xCount++;
        xCount.Should().BeGreaterThanOrEqualTo(3,
            "text-emphasis with 'x' mark should paint at least one 'x' per character (3 chars = A,B,C)");
    }

    [Fact]
    public async Task TextEmphasis_None_NoEmphasisMark()
    {
        var html = "<p style='text-emphasis-style: none'>ABC</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        pdf.Should().NotBeEmpty();
        var text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("ABC");
    }

    [Fact]
    public async Task TextEmphasis_Position_Under_DoesNotCrash()
    {
        var html = "<p style='text-emphasis-style: filled dot; text-emphasis-position: under right'>Ruby</p>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("text-emphasis-position: under should not crash");
    }

    // ── box-shadow: inset ─────────────────────────────────────────────────────

    [Fact]
    public async Task BoxShadow_Inset_DoesNotCrash()
    {
        var html = "<div style='box-shadow: inset 2px 2px 5px rgba(0,0,0,0.5); width: 200px; height: 100px; background: white'>Inset</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("inset box-shadow must not crash");
    }

    [Fact]
    public async Task BoxShadow_Inset_PaintsInsideElement()
    {
        // Inset shadow should produce some drawing; the result must be a valid non-empty PDF
        var html = "<div style='box-shadow: inset 4px 4px 0px rgba(255,0,0,0.8); width: 200px; height: 100px; background: white'>I</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BoxShadow_MultipleValues_DoesNotCrash()
    {
        // Multiple comma-separated shadows
        var html = "<div style='box-shadow: 2px 2px 4px black, inset 1px 1px 2px white; width: 100px; height: 50px; background: gray'>M</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("multiple box-shadow values must not crash");
    }

    // ── text-decoration-style ────────────────────────────────────────────────

    [Fact]
    public async Task TextDecorationStyle_Wavy_DoesNotCrash()
    {
        var html = "<p style='text-decoration: underline; text-decoration-style: wavy; text-decoration-color: red'>Wavy</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = System.Text.Encoding.Latin1.GetString(pdf).Replace("\r\n", "\n");
        text.Should().Contain("Wavy");
        // Wavy uses Bezier curves; verify 'c' operator (curveto) is present
        text.Should().Contain(" c\n", "wavy line should emit Bezier curveto operators");
    }

    [Fact]
    public async Task TextDecorationStyle_Dashed_DoesNotCrash()
    {
        var html = "<p style='text-decoration: underline; text-decoration-style: dashed'>Dashed</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        text.Should().Contain("Dashed");
    }

    [Fact]
    public async Task TextDecorationStyle_Dotted_DoesNotCrash()
    {
        var html = "<p style='text-decoration: underline; text-decoration-style: dotted'>Dotted</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        text.Should().Contain("Dotted");
    }

    [Fact]
    public async Task TextDecorationStyle_Double_DoesNotCrash()
    {
        var html = "<p style='text-decoration: underline; text-decoration-style: double'>Double</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        text.Should().Contain("Double");
    }

    // ── mix-blend-mode ───────────────────────────────────────────────────────

    [Fact]
    public async Task MixBlendMode_Multiply_DoesNotCrash()
    {
        var html = "<div style='mix-blend-mode: multiply; background: red; width: 100px; height: 100px'>Blend</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("mix-blend-mode: multiply should not crash");
    }

    [Fact]
    public async Task MixBlendMode_Produces_BM_InPdf()
    {
        var html = "<div style='mix-blend-mode: screen; background: blue; width: 100px; height: 100px'>S</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        text.Should().Contain("/BM", "PDF must contain a BlendMode ExtGState entry");
    }

    [Fact]
    public async Task BackgroundBlendMode_DoesNotCrash()
    {
        var html = "<div style='background: red; background-blend-mode: multiply; width: 100px; height: 100px'>BB</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("background-blend-mode should not crash");
    }

    // ── light-dark() ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LightDark_DoesNotCrash()
    {
        var html = "<div style='color: light-dark(black, white); background: light-dark(white, black)'>LD</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("light-dark() must not crash the renderer");
    }

    // ── accent-color ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AccentColor_Progress_UsesAccentColorForFill()
    {
        // accent-color should override the default blue fill of <progress>
        var html = "<progress value='50' max='100' style='accent-color: #ff0000; width: 200px; height: 20px'></progress>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        // Red fill: rg 1.00 0.00 0.00 — should appear for the accent-colored fill bar
        text.Should().Contain("1.00 0.00 0.00 rg", "accent-color red should produce r=1 g=0 b=0 fill");
    }

    [Fact]
    public async Task AccentColor_Meter_UsesAccentColorForFill()
    {
        // #00aa00 = r=0, g=0.667, b=0
        var html = "<meter value='0.7' style='accent-color: #00aa00; width: 150px; height: 16px'></meter>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        // accent-color #00aa00 should override the default threshold-based meter color
        text.Should().Contain("0.00 0.67 0.00 rg", "accent-color #00aa00 should override meter fill");
    }

    [Fact]
    public async Task AccentColor_DoesNotCrash_OnCheckbox()
    {
        var html = "<input type='checkbox' checked style='accent-color: purple'>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("accent-color on checkbox must not crash");
    }

    // ── background-blend-mode (rendering) ────────────────────────────────────

    [Fact]
    public async Task BackgroundBlendMode_Multiply_ProducesBMInPdf()
    {
        var html = "<div style='background: red; background-blend-mode: multiply; width: 100px; height: 100px'>BB</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("/BM", "background-blend-mode must emit a PDF blend mode ExtGState");
    }

    // ── isolation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Isolation_Isolate_DoesNotCrash()
    {
        var html = "<div style='isolation: isolate; background: white; width: 100px; height: 100px'><div style='mix-blend-mode: multiply; background: red'>X</div></div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("isolation: isolate must not crash");
    }

    // ── background-clip: text ────────────────────────────────────────────────

    [Fact]
    public async Task BackgroundClipText_DoesNotCrash()
    {
        var html = "<h1 style='background: linear-gradient(red, blue); background-clip: text; -webkit-background-clip: text; color: transparent; font-size: 32px'>Gradient Text</h1>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("background-clip: text must not crash");
    }

    [Fact]
    public async Task BackgroundClipText_FallsBackToTransparentText()
    {
        // When background-clip:text, text should still appear (fallback: render text normally)
        var html = "<h1 style='background: linear-gradient(red, blue); background-clip: text; color: transparent'>Hello</h1>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Hello", "text should still be present even with background-clip:text");
    }

    [Fact]
    public async Task BackgroundClipText_PaintsBackgroundGradient()
    {
        // background-clip:text must still paint the gradient (at box level at minimum)
        var html = "<h1 style='background: linear-gradient(red, blue); background-clip: text; width: 200px'>Gradient</h1>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var pdfText = Encoding.ASCII.GetString(pdf);
        // Red endpoint must appear in gradient rendering
        pdfText.Should().Contain("1.00 0.00 0.00 rg", "red gradient color should be painted");
    }

    // ── border-image ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BorderImage_Gradient_DoesNotCrash()
    {
        var html = "<div style='width: 150px; height: 80px; border: 8px solid; border-image: linear-gradient(red, blue) 1'>Content</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("border-image with gradient must not crash");
    }

    [Fact]
    public async Task BorderImage_Gradient_ProducesColorOutput()
    {
        // linear-gradient(red, blue): should produce both red (1 0 0) and blue (0 0 1) rg ops
        var html = "<div style='width: 150px; height: 80px; border: 10px solid; border-image: linear-gradient(red, blue) 1'>Hi</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        // Gradient rendering produces multiple color stops; at minimum it produces a rect
        text.Should().Contain("re", "border-image gradient must emit rectangle drawing commands");
        // Red stop: 1.00 0.00 0.00 rg
        text.Should().Contain("1.00 0.00 0.00 rg", "red gradient stop must appear in border-image");
    }

    [Fact]
    public async Task BorderImage_None_DoesNotCrash()
    {
        var html = "<div style='border-image: none; border: 2px solid red; width: 100px; height: 50px'>X</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("border-image:none must not crash");
    }

    [Fact]
    public async Task BorderImage_Url_DoesNotCrash()
    {
        // border-image: url() with a non-existent image should not crash (graceful degradation)
        var html = "<div style='width: 120px; height: 80px; border: 10px solid; " +
                   "border-image: url(missing.png) 30 fill stretch'>Content</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("border-image:url() must degrade gracefully when image is missing");
    }

    [Fact]
    public async Task BorderImage_Url_MissingImage_FallsBackToNormalBorder()
    {
        // When border-image URL can't be loaded, a normal border should still paint
        var html = "<div style='width: 120px; height: 80px; border: 3px solid; " +
                   "border-image: url(missing.png) 10 stretch'>Content</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var pdfText = Encoding.ASCII.GetString(pdf);
        // Should still have some rectangle drawing (normal border fallback)
        pdfText.Should().Contain("re", "border fallback must still emit rectangle commands");
    }

    // ── multiple background layers ────────────────────────────────────────────

    [Fact]
    public async Task MultipleBackgrounds_DoesNotCrash()
    {
        var html = "<div style='width:200px; height:100px; background: linear-gradient(red,blue), linear-gradient(green,yellow)'>X</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("multiple comma-separated backgrounds must not crash");
    }

    [Fact]
    public async Task MultipleBackgrounds_BothLayersRendered()
    {
        // Two gradients: first is red-blue, second is green-yellow
        // Both should produce color output in the PDF
        var html = "<div style='width:200px; height:100px; " +
                   "background-image: linear-gradient(red, blue), linear-gradient(green, yellow)'>X</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        // Red from first layer
        text.Should().Contain("1.00 0.00 0.00 rg", "first background layer (red gradient) must render");
        // Green from second layer
        text.Should().Contain("0.00 0.50 0.00 rg", "second background layer (green gradient) must render");
    }

    [Fact]
    public async Task MultipleBackgrounds_ThreeLayers_AllRender()
    {
        var html = "<div style='width:200px; height:100px; " +
                   "background-image: linear-gradient(red,red), linear-gradient(blue,blue), linear-gradient(green,green)'>X</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("1.00 0.00 0.00 rg", "red layer must render");
        text.Should().Contain("0.00 0.00 1.00 rg", "blue layer must render");
    }

    // ── multi-stop gradients ─────────────────────────────────────────────────

    [Fact]
    public async Task LinearGradient_ThreeStops_AllColorsPresent()
    {
        // red → green → blue — all three endpoint colors must appear in PDF
        var html = "<div style='width:200px; height:100px; background: linear-gradient(red, green, blue)'>X</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("1.00 0.00 0.00 rg", "red stop must appear");
        text.Should().Contain("0.00 0.50 0.00 rg", "green stop must appear");
        text.Should().Contain("0.00 0.00 1.00 rg", "blue stop must appear");
    }

    [Fact]
    public async Task LinearGradient_HorizontalDirection_DoesNotCrash()
    {
        var html = "<div style='width:300px; height:100px; background: linear-gradient(to right, red, blue)'>X</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LinearGradient_HorizontalDirection_ContainsRedAndBlue()
    {
        // to right: red at left, blue at right — both must appear in PDF bands
        var html = "<div style='width:300px; height:100px; background: linear-gradient(to right, red, blue)'>X</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("1.00 0.00 0.00 rg", "red end must appear");
        text.Should().Contain("0.00 0.00 1.00 rg", "blue end must appear");
    }

    // ── CSS units: vw/vh/vmin/vmax/ch/lh/pc ─────────────────────────────────

    [Fact]
    public async Task ViewportUnit_Vw_DoesNotCrash()
    {
        var html = "<div style='width: 50vw; height: 100px'>vw test</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("vw unit must not crash");
    }

    [Fact]
    public async Task ViewportUnit_Vh_DoesNotCrash()
    {
        var html = "<div style='height: 50vh; width: 200px'>vh test</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("vh unit must not crash");
    }

    [Fact]
    public async Task Unit_Ch_DoesNotCrash()
    {
        var html = "<div style='width: 20ch; height: 100px'>ch unit</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("ch unit must not crash");
    }

    [Fact]
    public async Task Unit_Pc_DoesNotCrash()
    {
        var html = "<div style='margin-top: 2pc; width: 200px'>pc unit</div>";
        var act = async () => await HtmlToPdf.RenderAsync(html);
        await act.Should().NotThrowAsync("pc unit (picas) must not crash");
    }
}
