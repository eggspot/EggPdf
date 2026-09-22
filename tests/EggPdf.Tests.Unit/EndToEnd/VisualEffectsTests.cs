using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class VisualEffectsTests
{
    [Fact]
    public async Task BoxShadow_OffsetShadowPaintedBehindBackgroundWithAlpha()
    {
        var html = "<div style='box-shadow: 2px 2px 5px rgba(0,0,0,0.3); width: 200px; height: 100px; background-color: white'>Shadow box</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Shadow box");

        text.Should().MatchRegex(@"/GS(29|30) gs", "the 0.3 alpha shadow must set a matching ExtGState");
        text.Should().Contain("/GS100 gs", "opacity is reset after the shadow");
        // Shadow rect is the box offset by (2px,2px) = (1.5pt,-1.5pt); the box itself sits at 6,766.89
        int shadow = text.IndexOf("7.50 765.39 150.00 75.00 re f");
        int box = text.IndexOf("6.00 766.89 150.00 75.00 re f");
        shadow.Should().BeGreaterThan(-1, "the offset shadow rect must be filled");
        box.Should().BeGreaterThan(shadow, "the element background paints over the shadow");
    }

    [Fact]
    public async Task BorderRadius_BackgroundPaintedAsBezierPathNotPlainRect()
    {
        var html = "<div style='border-radius: 10px; background-color: blue; width: 100px; height: 100px'></div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().Contain("0.00 0.00 1.00 rg");
        text.Should().Contain(" c\n", "rounded corners are Bezier curves");
        text.Should().Contain("h f", "the rounded path is closed and filled");
        text.Should().NotContain("75.00 75.00 re f", "the box must not be painted as a square rect");
    }

    [Fact]
    public async Task Opacity_WrapsBoxAndTextInHalfAlphaState()
    {
        var html = "<div style='opacity: 0.5; background-color: red; width: 100px; height: 100px'>Semi-transparent</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Semi-transparent");

        text.Should().Contain("/GS50 gs");
        text.IndexOf("/GS100 gs").Should().BeGreaterThan(text.IndexOf("/GS50 gs"), "alpha must be reset after painting");
        text.Should().Contain("1.00 0.00 0.00 rg");
    }

    [Fact]
    public async Task Transform_Rotate_EmitsNonIdentityRotationMatrix()
    {
        var html = "<div style='transform: rotate(5deg); width: 100px; height: 100px'>Rotated</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Rotated");

        // cos5 = 1.00 (0.996), sin5 = 0.09
        text.Should().Contain("1.00 -0.09 0.09 1.00");
        text.Should().MatchRegex(@"1\.00 -0\.09 0\.09 1\.00 -?\d+\.\d+ -?\d+\.\d+ cm");
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
    public async Task TextShadow_PaintsTextTwiceAtOffsetPositions()
    {
        var html = "<h1 style='text-shadow: 2px 2px 4px #000'>Shadow text</h1>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Shadow text");

        PdfAssert.Count(text, "(Shadow text) Tj").Should().Be(2, "one shadow copy plus the real text");
        // Shadow is drawn first, offset by 2px (1.5pt) right and 2px down from the real text.
        var shadow = PdfAssert.TextPosition(text, "Shadow text");
        text.IndexOf("(Shadow text) Tj").Should().BeLessThan(text.LastIndexOf("(Shadow text) Tj"));
        shadow.X.Should().BeApproximately(7.50f, 0.01f);
    }

    [Fact]
    public async Task MultiColumn_TwoBlocksFlowIntoSideBySideColumns()
    {
        var html = "<div style='column-count: 2; column-gap: 20px'><p>Alpha</p><p>Bravo</p></div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Alpha", "Bravo");

        var alpha = PdfAssert.TextPosition(text, "Alpha");
        var bravo = PdfAssert.TextPosition(text, "Bravo");
        bravo.X.Should().BeGreaterThan(alpha.X + 100f, "the second block starts in the second column");
        bravo.Y.Should().BeApproximately(alpha.Y, 0.01f, "both columns start at the same top edge");
    }

    [Fact]
    public async Task CssNesting_AmpersandSelector_AppliesNestedRuleToChild()
    {
        var html = "<style>div { & p { color: red; } }</style><div><p>Nested CSS</p></div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "(Nested CSS) Tj");

        PdfAssert.Count(text, "(Nested CSS) Tj").Should().Be(1);
        PdfAssert.PageCount(text).Should().Be(1);
        PdfAssert.TextPaintedWith(text, "Nested CSS", "1.00 0.00 0.00 rg")
            .Should().BeTrue("\"div { & p { color: red } }\" must expand to \"div p { color: red }\"");
    }

    [Fact]
    public async Task CssNesting_ImplicitDescendant_ExpandsWithoutAmpersand()
    {
        // No "&": the nested selector is treated as a descendant of the parent, same as Chrome.
        var html = "<style>div { p { color: blue; } }</style><div><p>Implicit nest</p></div><p>Outside</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "(Implicit nest) Tj", "(Outside) Tj");

        PdfAssert.TextPaintedWith(text, "Implicit nest", "0.00 0.00 1.00 rg")
            .Should().BeTrue("\"div { p { color: blue } }\" must expand to \"div p { color: blue }\"");
        PdfAssert.TextPaintedWith(text, "Outside", "0.00 0.00 1.00 rg")
            .Should().BeFalse("the nested rule must not leak to a <p> outside the <div>");
    }

    [Fact]
    public async Task CssNesting_LiteralBraceInContentValue_DoesNotCorruptFollowingRule()
    {
        // A declaration value can legitimately contain a brace character (e.g. content: "{"). The
        // nesting pre-processor's implicit-nesting detection must not mistake it for a nested rule
        // and eat the unrelated rule that follows it.
        var html = "<style>p::before { content: \"{\"; } p { color: blue; }</style><p>After</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "(After) Tj");

        PdfAssert.TextPaintedWith(text, "After", "0.00 0.00 1.00 rg")
            .Should().BeTrue("the literal '{' in the content value must not swallow the following \"p { color: blue }\" rule");
    }

    [Fact]
    public async Task ContainerQuery_MinWidthSatisfied_AppliesRule()
    {
        var html = "<style>@container (min-width: 300px) { p { color: blue; } }</style><p>Container query</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Container query");

        PdfAssert.TextPaintedWith(text, "Container query", "0.00 0.00 1.00 rg").Should().BeTrue();
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
    public async Task CjkText_MixedWithLatin_EmitsOneTextRun()
    {
        var html = "<p>English text and some Chinese: 你好世界</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf);

        // Non-Latin-1 text is emitted as a glyph-id (hex) run rather than a literal string.
        text.Should().MatchRegex(@"BT [^\n]*(\) Tj|> Tj|\] TJ) ET", "the paragraph must be painted as a text run");
    }

    [Fact]
    public async Task Emoji_MixedWithLatin_EmitsTextRun()
    {
        // Emoji may not render in color without a color emoji font, but the run must still be painted.
        var html = "<p>Hello World 🌍🎉</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf);

        text.Should().MatchRegex(@"BT [^\n]*(\) Tj|> Tj|\] TJ)[^\n]* ET", "the paragraph must be painted as a text run");
    }

    [Fact]
    public async Task BackdropFilter_TranslucentBackgroundPaintedWithHalfAlpha()
    {
        var html = "<div style='backdrop-filter: blur(8px) saturate(180%); background-color: rgba(255,255,255,0.5); width:200px; height:100px'>Glass</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Glass");

        text.Should().Contain("/GS50 gs", "the rgba(...,0.5) background is still painted translucent");
        text.Should().Contain("150.00 75.00 re f");
    }

    [Fact]
    public async Task ImageSet_UnloadableCandidates_TextStillRendersWithoutImage()
    {
        // Neither image-set() candidate exists: the box still paints its text, and no image is embedded.
        var html = "<div style=\"background-image: image-set(url('low.png') 1x, url('high.png') 2x); width:100px; height:100px\">IS</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "IS");

        text.Should().NotContain("/Subtype /Image");
    }

    // 1x1 red pixel PNG as base64 (same fixture used by ImageTests.cs).
    private const string RedPixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

    [Fact]
    public async Task ImageSet_PrefersOneXCandidateAndActuallyEmbedsIt()
    {
        // PDF is a fixed 1x print context (no real screen density) — image-set()
        // must resolve to a real, loadable URL, not just be stored inertly in the
        // computed style. Listing the 2x candidate first proves selection is by
        // density match, not "pick whichever comes first". The 2x candidate points
        // at a path that doesn't exist, so if 1x weren't actually preferred/loaded,
        // no image would end up embedded at all.
        var html = $"<div style=\"background-image: image-set(url('missing-2x.png') 2x, url('data:image/png;base64,{RedPixelPng}') 1x); width:100px; height:100px\">IS</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().Contain("/Subtype /Image", "the 1x candidate must resolve, load, and get embedded as a real image XObject");
    }

    [Fact]
    public async Task ImageSet_QuotedUrlWithoutUrlWrapper_StillResolves()
    {
        // image-set() candidates may be bare quoted strings instead of url(...).
        var html = $"<div style=\"background-image: image-set('data:image/png;base64,{RedPixelPng}' 1x); width:100px; height:100px\">IS2</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().Contain("/Subtype /Image", "a bare-quoted (non-url()) candidate must still resolve and embed");
    }

    // ── CSS Images Level 4 image() ───────────────────────────────────────────

    [Fact]
    public async Task ImageFunction_PlainUrl_ResolvesAndEmbeds()
    {
        var html = $"<div style=\"background-image: image(url('data:image/png;base64,{RedPixelPng}')); width:100px; height:100px\">I</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().Contain("/Subtype /Image", "image(url(...)) with no direction tags must resolve and embed like a plain url()");
    }

    [Fact]
    public async Task ImageFunction_RtlTag_PicksRtlCandidateInRtlContext()
    {
        // The ltr candidate points at a path that doesn't exist; only the rtl candidate is a
        // real, loadable data: URL. In an RTL context, if the rtl-tagged candidate weren't
        // actually selected, nothing would end up embedded.
        var html = $"<div style=\"direction: rtl; background-image: " +
            $"image(ltr url('missing-ltr.png'), rtl url('data:image/png;base64,{RedPixelPng}')); " +
            "width:100px; height:100px\">I</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().Contain("/Subtype /Image", "the rtl-tagged candidate must be selected under direction:rtl");
    }

    [Fact]
    public async Task ImageFunction_LtrTag_PicksLtrCandidateByDefault()
    {
        var html = $"<div style=\"background-image: " +
            $"image(ltr url('data:image/png;base64,{RedPixelPng}'), rtl url('missing-rtl.png')); " +
            "width:100px; height:100px\">I</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().Contain("/Subtype /Image", "the ltr-tagged candidate must be selected in the default (ltr) direction");
    }

    [Fact]
    public async Task ImageFunction_ColorFallback_PaintsSolidFillWhenUrlMissing()
    {
        // No image-src at all, just a <color> fallback -- must paint the solid color, not crash
        // or paint nothing.
        var html = "<div style=\"background-image: image(red); width:50px; height:50px\">I</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.Latin1.GetString(pdf);

        text.Should().Contain("1.00 0.00 0.00 rg", "image()'s color fallback must paint as a solid red fill");
    }

    [Fact]
    public async Task BackgroundClip_Text_ClipsGradientBandsAndHidesGlyphFill()
    {
        // background-clip: text creates gradient text — PDF approximates by rendering the gradient bands clipped to the box
        var html = @"<h1 style='background-image: linear-gradient(to right, red, blue);
            background-clip: text; -webkit-background-clip: text;
            color: transparent; font-size: 32px'>Gradient Text</h1>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Gradient Text");

        text.Should().Contain("re W n", "the gradient must be clipped");
        text.Should().Contain("1.00 0.00 0.00 rg", "the red start of the gradient");
        text.Should().Contain("0.98 0.00 0.03 rg", "intermediate gradient band");
        text.Should().Contain("/GS0 gs", "the transparent glyph fill is painted with zero alpha");
    }

    // ── text-emphasis ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TextEmphasis_Dot_PaintsOneRedMarkPerCharacterAboveText()
    {
        // text-emphasis: dot paints a half-size mark above each character
        var html = "<p style='text-emphasis-style: dot; text-emphasis-color: red'>Hello</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Hello");

        PdfAssert.Count(text, "1.00 0.00 0.00 rg").Should().Be(5, "one red mark for each of the 5 characters");
        Regex.Matches(text, @"Helvetica 6\.00 Tf").Count.Should().Be(5, "marks are half the 12pt font size");
        var baseText = PdfAssert.TextPosition(text, "Hello");
        var mark = Regex.Match(text, @"Helvetica 6\.00 Tf 0\.00 Tc 0\.00 Tw (-?\d+\.\d+) (-?\d+\.\d+) Td");
        float.Parse(mark.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(baseText.Y, "over-position marks sit above the baseline");
    }

    [Fact]
    public async Task TextEmphasis_Circle_PaintsBlueMarkPerCharacter()
    {
        var html = "<p style='text-emphasis-style: open circle; text-emphasis-color: blue'>Hi</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Hi");

        PdfAssert.Count(text, "0.00 0.00 1.00 rg").Should().Be(2, "one blue mark for each of the 2 characters");
        Regex.Matches(text, @"Helvetica 6\.00 Tf").Count.Should().Be(2);
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
        var text = PdfAssert.ValidPdf(pdf, "ABC");

        text.Should().NotContain("Helvetica 6.00 Tf", "no half-size emphasis marks are painted");
    }

    [Fact]
    public async Task TextEmphasis_PositionUnder_PaintsMarksBelowBaseline()
    {
        var html = "<p style='text-emphasis-style: filled dot; text-emphasis-position: under right'>Ruby</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Ruby");

        Regex.Matches(text, @"Helvetica 6\.00 Tf").Count.Should().Be(4, "one mark per character");
        var baseText = PdfAssert.TextPosition(text, "Ruby");
        var mark = Regex.Match(text, @"Helvetica 6\.00 Tf 0\.00 Tc 0\.00 Tw (-?\d+\.\d+) (-?\d+\.\d+) Td");
        float.Parse(mark.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeLessThan(baseText.Y, "under-position marks sit below the baseline");
    }

    // ── box-shadow: inset ─────────────────────────────────────────────────────

    [Fact]
    public async Task BoxShadow_Inset_ClipsShadowToBoxAndPaintsItBeforeBackground()
    {
        var html = "<div style='box-shadow: inset 2px 2px 5px rgba(0,0,0,0.5); width: 200px; height: 100px; background: white'>Inset</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Inset");

        text.Should().Contain("6.00 766.89 150.00 75.00 re W n", "the inset shadow is clipped to the padding box");
        text.Should().Contain("/GS50 gs");
    }

    [Fact]
    public async Task BoxShadow_Inset_PaintsInsideElement()
    {
        // Inset shadow: a translucent red rect (offset 4px = 3pt) clipped to the box
        var html = "<div style='box-shadow: inset 4px 4px 0px rgba(255,0,0,0.8); width: 200px; height: 100px; background: white'>I</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "I");

        text.Should().Contain("6.00 766.89 150.00 75.00 re W n");
        text.Should().Contain("/GS80 gs");
        text.Should().Contain("1.00 0.00 0.00 rg");
        text.Should().Contain("9.00 769.89 150.00 75.00 re f", "the shadow rect is shifted by the 4px offset");
    }

    [Fact]
    public async Task BoxShadow_MultipleValues_PaintsOuterThenInsetThenBackground()
    {
        // Multiple comma-separated shadows
        var html = "<div style='box-shadow: 2px 2px 4px black, inset 1px 1px 2px white; width: 100px; height: 50px; background: gray'>M</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "M");

        int outer = text.IndexOf("0.00 0.00 0.00 rg\n7.50 802.89 75.00 37.50 re f");
        int inset = text.IndexOf("1.00 1.00 1.00 rg\n6.75 805.14 75.00 37.50 re f");
        int background = text.IndexOf("0.50 0.50 0.50 rg\n6.00 804.39 75.00 37.50 re f");
        outer.Should().BeGreaterThan(-1, "outer black shadow");
        inset.Should().BeGreaterThan(outer, "inset white shadow follows the outer one");
        background.Should().BeGreaterThan(inset, "gray background painted last");
    }

    // ── text-decoration-style ────────────────────────────────────────────────

    [Fact]
    public async Task TextDecorationStyle_Wavy_EmitsBezierCurves()
    {
        var html = "<p style='text-decoration: underline; text-decoration-style: wavy; text-decoration-color: red'>Wavy</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = System.Text.Encoding.Latin1.GetString(pdf).Replace("\r\n", "\n");
        text.Should().Contain("Wavy");
        // Wavy uses Bezier curves; verify 'c' operator (curveto) is present
        text.Should().Contain(" c\n", "wavy line should emit Bezier curveto operators");
    }

    [Fact]
    public async Task TextDecorationStyle_Dashed_StrokesWithDashPattern()
    {
        var html = "<p style='text-decoration: underline; text-decoration-style: dashed'>Dashed</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Dashed");
        text.Should().Contain("[2.40 2.40] 0 d", "dashed underline uses a 2-on/2-off dash array");
        text.Should().Contain("816.57 l S");
    }

    [Fact]
    public async Task TextDecorationStyle_Dotted_StrokesWithRoundCapDots()
    {
        var html = "<p style='text-decoration: underline; text-decoration-style: dotted'>Dotted</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Dotted");
        text.Should().Contain("[0 1.50] 0 d 1 J", "dotted underline uses zero-length dashes with round caps");
    }

    [Fact]
    public async Task TextDecorationStyle_Double_StrokesTwoParallelLines()
    {
        var html = "<p style='text-decoration: underline; text-decoration-style: double'>Double</p>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Double");
        text.Should().Contain("816.97 l S", "upper line of the double underline");
        text.Should().Contain("816.17 l S", "lower line of the double underline");
    }

    // ── mix-blend-mode ───────────────────────────────────────────────────────

    [Fact]
    public async Task MixBlendMode_Multiply_WrapsBoxInBlendModeState()
    {
        var html = "<div style='mix-blend-mode: multiply; background: red; width: 100px; height: 100px'>Blend</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Blend");

        text.Should().Contain("/BM /Multiply");
        text.Should().MatchRegex(@"q\n/GSBM_multiply gs\n1\.00 0\.00 0\.00 rg\n[^\n]*re f\nQ", "the red box is painted inside a saved graphics state using the blend mode");
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
    public async Task BackgroundBlendMode_AppliesBlendOnlyToBackgroundNotText()
    {
        var html = "<div style='background: red; background-blend-mode: multiply; width: 100px; height: 100px'>BB</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "BB");

        text.Should().MatchRegex(@"q\n/GSBM_multiply gs\n1\.00 0\.00 0\.00 rg\n[^\n]*re f\nQ");
        PdfAssert.TextPaintedWith(text, "BB", "0.00 0.00 0.00 rg").Should().BeTrue();
        text.Should().NotContain("/GSBM_multiply gs\n0.00 0.00 0.00 rg\nBT", "text is not blended by background-blend-mode");
    }

    // ── light-dark() ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LightDark_DefaultsToLightValue_BlackText()
    {
        var html = "<div style='color: light-dark(black, white); background: light-dark(white, black)'>LD</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "LD");

        PdfAssert.TextPaintedWith(text, "LD", "0.00 0.00 0.00 rg").Should().BeTrue("the light-scheme value (black) is used for the text color");
        PdfAssert.TextPaintedWith(text, "LD", "1.00 1.00 1.00 rg").Should().BeFalse();
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
    public async Task AccentColor_Checkbox_FillsCheckedBoxWithAccentColor()
    {
        var html = "<input type='checkbox' checked style='accent-color: purple'>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        // purple = #800080 = (128,0,128) -> 128/255 = 0.50
        text.Should().Contain("0.50 0.00 0.50 rg", "accent-color must set the checked checkbox's fill color");
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
    public async Task Isolation_Isolate_ParentBackgroundPaintedBeforeMultiplyChild()
    {
        var html = "<div style='isolation: isolate; background: white; width: 100px; height: 100px'><div style='mix-blend-mode: multiply; background: red'>X</div></div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "X");

        int parent = text.IndexOf("1.00 1.00 1.00 rg\n6.00 766.89 75.00 75.00 re f");
        int child = text.IndexOf("/GSBM_multiply gs\n1.00 0.00 0.00 rg");
        parent.Should().BeGreaterThan(-1, "the isolated white parent background is painted");
        child.Should().BeGreaterThan(parent, "the multiply-blended child paints over the parent background");
    }

    // ── background-clip: text ────────────────────────────────────────────────

    [Fact]
    public async Task BackgroundClipText_VerticalGradient_ClipsHorizontalBands()
    {
        var html = "<h1 style='background: linear-gradient(red, blue); background-clip: text; -webkit-background-clip: text; color: transparent; font-size: 32px'>Gradient Text</h1>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Gradient Text");

        text.Should().Contain("re W n");
        text.Should().Contain("0.98 0.00 0.03 rg", "intermediate gradient band");
        text.Should().Contain("6.00 797.01 583.28 1.22 re f", "vertical gradient bands are full-width strips");
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
    public async Task BorderImage_Gradient_ClipsGradientBandsToBorderBox()
    {
        var html = "<div style='width: 150px; height: 80px; border: 8px solid; border-image: linear-gradient(red, blue) 1'>Content</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Content");

        text.Should().Contain("re W n", "the border-image gradient is clipped to the border box");
        text.Should().Contain("1.00 0.00 0.00 rg");
        text.Should().Contain("0.98 0.00 0.03 rg", "intermediate gradient band");
    }

    [Fact]
    public async Task BorderImage_Gradient_ProducesColorOutput()
    {
        // linear-gradient(red, blue): should produce both red (1 0 0) and blue (0 0 1) rg ops
        var html = "<div style='width: 150px; height: 80px; border: 10px solid; border-image: linear-gradient(red, blue) 1'>Hi</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);
        // Gradient rendering produces multiple color stops; at minimum it produces a rect
        text.Should().Contain("re f", "border-image gradient must emit rectangle fill commands");
        // Red stop: 1.00 0.00 0.00 rg
        text.Should().Contain("1.00 0.00 0.00 rg", "red gradient stop must appear in border-image");
    }

    [Fact]
    public async Task BorderImage_None_FallsBackToSolidRedBorder()
    {
        var html = "<div style='border-image: none; border: 2px solid red; width: 100px; height: 50px'>X</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "X");

        text.Should().Contain("1.50 w", "2px border stroke width");
        text.Should().Contain("1.00 0.00 0.00 RG", "border-image:none keeps the regular red border");
        text.Should().Contain("re S");
    }

    [Fact]
    public async Task BorderImage_Url_MissingImage_DegradesToStrokedBorder()
    {
        // border-image: url() with a non-existent image should degrade gracefully to the normal border
        var html = "<div style='width: 120px; height: 80px; border: 10px solid; " +
                   "border-image: url(missing.png) 30 fill stretch'>Content</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "Content");

        text.Should().Contain("7.50 w", "10px border stroke width");
        text.Should().Contain("re S");
        text.Should().NotContain("/Subtype /Image");
    }

    [Fact]
    public async Task BorderImage_Url_MissingImage_FallsBackToNormalBorder()
    {
        // When border-image URL can't be loaded, a normal border should still paint
        var html = "<div style='width: 120px; height: 80px; border: 3px solid; " +
                   "border-image: url(missing.png) 10 stretch'>Content</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var pdfText = Encoding.ASCII.GetString(pdf);
        // Should still have a stroked rectangle (normal border fallback)
        pdfText.Should().Contain("re S", "border fallback must still emit a stroked rectangle");
    }

    // ── multiple background layers ────────────────────────────────────────────

    [Fact]
    public async Task MultipleBackgrounds_ShorthandWithTwoGradients_PaintsBothLayersClippedToBox()
    {
        // The `background` shorthand must keep every comma-separated layer (not just the first),
        // same as the background-image longhand (see MultipleBackgrounds_BothLayersRendered).
        var html = "<div style='width:200px; height:100px; background: linear-gradient(red,blue), linear-gradient(green,yellow)'>X</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "X");

        text.Should().Contain("6.00 766.89 150.00 75.00 re W n", "the gradient is clipped to the 200x100px box");
        text.Should().Contain("1.00 0.00 0.00 rg", "top layer starts red");
        text.Should().Contain("0.00 0.00 1.00 rg", "top layer ends blue");
        text.Should().Contain("0.00 0.50 0.00 rg", "second (bottom) layer -- green -- must also render");
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
    public async Task LinearGradient_HorizontalDirection_PaintsFullHeightVerticalStrips()
    {
        var html = "<div style='width:300px; height:100px; background: linear-gradient(to right, red, blue)'>X</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "X");

        text.Should().Contain("6.12 75.00 re f", "'to right' gradients are painted as narrow strips spanning the full 100px height");
        text.Should().Contain("6.00 766.89 225.00 75.00 re W n", "clipped to the 300px-wide box");
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
    public async Task ViewportUnit_Vw_ResolvesToHalfPageWidth()
    {
        var html = "<div style='width: 50vw; height: 100px; background: red'>vw test</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "vw test");

        text.Should().Contain("297.64 75.00 re f", "50vw is half of the 595.28pt page width");
    }

    [Fact]
    public async Task ViewportUnit_Vh_ResolvesToHalfPageHeight()
    {
        var html = "<div style='height: 50vh; width: 200px; background: red'>vh test</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "vh test");

        text.Should().Contain("150.00 420.95 re f", "50vh is half of the 841.89pt page height");
    }

    [Fact]
    public async Task Unit_Ch_ResolvesToCharacterAdvanceWidth()
    {
        var html = "<div style='width: 20ch; height: 100px; background: red'>ch unit</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "ch unit");

        text.Should().Contain("120.00 75.00 re f", "20ch resolves to 160px (8px per ch at 16px)");
    }

    [Fact]
    public async Task Unit_Pc_MarginTopOfTwoPicasShiftsContentByTwentyFourPoints()
    {
        var html = "<div style='margin-top: 2pc; width: 200px; background: red'>pc unit</div>";
        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf, "pc unit");

        // Without the margin the first line baseline is at y=830.37; 2pc = 24pt pushes it down.
        PdfAssert.TextPosition(text, "pc unit").Y.Should().BeApproximately(830.37f - 24f, 0.02f);
    }
}
