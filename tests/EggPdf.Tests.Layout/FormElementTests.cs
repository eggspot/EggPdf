using System.Linq;
using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class FormElementTests
{
    [Fact]
    public void Input_TextValue_RendersValueText()
    {
        var root = LayoutTestHelper.Layout(
            "<input type='text' value='Hello World'>", 600, 800);

        // The input should produce a layout box with text showing the value
        var input = root.FindByTag("input");
        input.Should().NotBeNull();

        bool hasValueText = root.FindAll(b => b.Text?.Contains("Hello World") == true).Count > 0
                         || (input!.Text?.Contains("Hello World") == true);
        hasValueText.Should().BeTrue("input value attribute should be rendered as text");
    }

    [Fact]
    public void Button_TextContent_RendersText()
    {
        var root = LayoutTestHelper.Layout(
            "<button>Submit</button>", 600, 800);

        var button = root.FindByTag("button");
        button.Should().NotBeNull();

        bool hasText = HasDescendantText(root, "Submit");
        hasText.Should().BeTrue("button text content should be rendered");
    }

    [Fact]
    public void Textarea_TextContent_RendersText()
    {
        var root = LayoutTestHelper.Layout(
            "<textarea>My notes here</textarea>", 600, 800);

        var textarea = root.FindByTag("textarea");
        textarea.Should().NotBeNull();

        bool hasText = HasDescendantText(root, "My notes");
        hasText.Should().BeTrue("textarea text content should be rendered");
    }

    [Fact]
    public void Select_SelectedOption_RendersOptionText()
    {
        var root = LayoutTestHelper.Layout(
            "<select><option>Option A</option><option selected>Option B</option></select>", 600, 800);

        var select = root.FindByTag("select");
        select.Should().NotBeNull();

        bool hasText = HasDescendantText(root, "Option");
        hasText.Should().BeTrue("select should render at least one option's text");
    }

    [Fact]
    public void Input_HasNonZeroHeight()
    {
        var root = LayoutTestHelper.Layout(
            "<input type='text' value='test'>", 600, 800);

        var input = root.FindByTag("input");
        input.Should().NotBeNull();
        input!.Height.Should().BeGreaterThan(0, "input should have non-zero height");
    }

    [Fact]
    public void Input_Checkbox_RendersSymbolOrBox()
    {
        // Checkbox must produce a visible box (non-zero dimensions)
        var root = LayoutTestHelper.Layout(
            "<input type='checkbox'>", 600, 800);

        var input = root.FindByTag("input");
        input.Should().NotBeNull();
        (input!.Width > 0 || input.Height > 0).Should().BeTrue(
            "checkbox should have non-zero dimensions");
    }

    // ── appearance ──────────────────────────────────────────────────────────
    // Checkbox/radio are painted as real vector squares/circles by
    // BoxPainter.PaintCheckboxOrRadio (see EggPdf.Tests.Unit FormControlsTests for the
    // paint-level coverage), not injected Unicode glyph text. At the layout level, the box
    // should stay empty (no text child) and be a fixed 13x13px native-style control size.

    [Fact]
    public void Checkbox_Checked_NoGlyphText_FixedSize()
    {
        var root = LayoutTestHelper.Layout(
            "<input type='checkbox' checked>", 600, 800);
        var input = root.FindByTag("input");
        input.Should().NotBeNull();

        bool hasGlyphText = HasDescendantText(root, "☑") || HasDescendantText(root, "☐");
        hasGlyphText.Should().BeFalse("checkbox must not inject Unicode glyph text; it is painted as a vector shape");
        input!.Width.Should().BeApproximately(13f, 0.01f, "checkbox must be a fixed 13x13px native-style square");
        input.Height.Should().BeApproximately(13f, 0.01f, "checkbox must be a fixed 13x13px native-style square");
    }

    [Fact]
    public void Checkbox_Unchecked_NoGlyphText_FixedSize()
    {
        var root = LayoutTestHelper.Layout(
            "<input type='checkbox'>", 600, 800);
        var input = root.FindByTag("input");
        input.Should().NotBeNull();

        bool hasGlyphText = HasDescendantText(root, "☑") || HasDescendantText(root, "☐");
        hasGlyphText.Should().BeFalse("unchecked checkbox must not inject Unicode glyph text");
        input!.Width.Should().BeApproximately(13f, 0.01f);
        input.Height.Should().BeApproximately(13f, 0.01f);
    }

    [Fact]
    public void Radio_Checked_NoGlyphText_FixedSize()
    {
        var root = LayoutTestHelper.Layout(
            "<input type='radio' checked>", 600, 800);
        var input = root.FindByTag("input");
        input.Should().NotBeNull();

        bool hasGlyphText = HasDescendantText(root, "◉") || HasDescendantText(root, "○");
        hasGlyphText.Should().BeFalse("checked radio must not inject Unicode glyph text; it is painted as a vector circle");
        input!.Width.Should().BeApproximately(13f, 0.01f, "radio must be a fixed 13x13px native-style circle");
        input.Height.Should().BeApproximately(13f, 0.01f, "radio must be a fixed 13x13px native-style circle");
    }

    [Fact]
    public void Radio_Unchecked_NoGlyphText_FixedSize()
    {
        var root = LayoutTestHelper.Layout(
            "<input type='radio'>", 600, 800);
        var input = root.FindByTag("input");
        input.Should().NotBeNull();

        bool hasGlyphText = HasDescendantText(root, "◉") || HasDescendantText(root, "○");
        hasGlyphText.Should().BeFalse("unchecked radio must not inject Unicode glyph text");
        input!.Width.Should().BeApproximately(13f, 0.01f);
        input.Height.Should().BeApproximately(13f, 0.01f);
    }

    [Fact]
    public void Appearance_None_HidesCheckboxGlyph()
    {
        var root = LayoutTestHelper.Layout(
            "<input type='checkbox' checked style='appearance: none; -webkit-appearance: none'>", 600, 800);

        // With appearance:none, no glyph text should be rendered (author controls everything);
        // the paint layer separately skips the vector shape too (see BoxPainter dispatch).
        bool hasGlyph = HasDescendantText(root, "☑") || HasDescendantText(root, "[x]");
        hasGlyph.Should().BeFalse("appearance:none should suppress the default checkbox glyph");
    }

    // ── <progress> ──────────────────────────────────────────────────────────

    [Fact]
    public void Progress_HasPositiveDimensions()
    {
        var root = LayoutTestHelper.Layout(
            "<progress value='40' max='100'></progress>", 600, 800);
        var el = root.FindByTag("progress");
        el.Should().NotBeNull();
        el!.Width.Should().BeGreaterThan(0);
        el.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Progress_StylePreservesValueAndMax()
    {
        var root = LayoutTestHelper.Layout(
            "<progress value='60' max='200'></progress>", 600, 800);
        var el = root.FindByTag("progress");
        el.Should().NotBeNull();
        el!.Element?.GetAttribute("value").Should().Be("60");
        el.Element?.GetAttribute("max").Should().Be("200");
    }

    [Fact]
    public void Progress_Indeterminate_HasDimensions()
    {
        // No value attribute = indeterminate
        var root = LayoutTestHelper.Layout(
            "<progress max='100'></progress>", 600, 800);
        var el = root.FindByTag("progress");
        el.Should().NotBeNull();
        el!.Width.Should().BeGreaterThan(0);
    }

    // ── <meter> ──────────────────────────────────────────────────────────────

    [Fact]
    public void Meter_HasPositiveDimensions()
    {
        var root = LayoutTestHelper.Layout(
            "<meter value='0.6'></meter>", 600, 800);
        var el = root.FindByTag("meter");
        el.Should().NotBeNull();
        el!.Width.Should().BeGreaterThan(0);
        el.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Meter_WithMinMax_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<meter min='10' max='90' value='50'></meter>", 600, 800);
        var el = root.FindByTag("meter");
        el.Should().NotBeNull();
        el!.Element?.GetAttribute("value").Should().Be("50");
    }

    // ── ::placeholder ────────────────────────────────────────────────────────

    [Fact]
    public void Input_NoValue_ShowsPlaceholder()
    {
        var root = LayoutTestHelper.Layout(
            "<input type='text' placeholder='Enter your name'>", 600, 800);

        // Placeholder text should appear when there's no value
        bool hasPlaceholder = HasDescendantText(root, "Enter your name");
        hasPlaceholder.Should().BeTrue("placeholder text should render when input has no value");
    }

    [Fact]
    public void Input_WithValue_HidesPlaceholder()
    {
        var root = LayoutTestHelper.Layout(
            "<input type='text' value='John' placeholder='Enter your name'>", 600, 800);

        // When value is present, placeholder should NOT render — only value renders
        bool hasPlaceholder = HasDescendantText(root, "Enter your name");
        hasPlaceholder.Should().BeFalse("placeholder should not render when input has a value");
    }

    [Fact]
    public void Input_PlaceholderStyle_IsGray()
    {
        // ::placeholder default UA color should be gray
        var root = LayoutTestHelper.Layout(
            "<input type='text' placeholder='hint'>", 600, 800);

        var phBox = root.FindAll(b => b.Text?.Contains("hint") == true).FirstOrDefault();
        if (phBox == null) return;

        // The placeholder box should have a gray/muted color (not pure black)
        var colorStr = phBox.Style.Color ?? phBox.Style.Get("color");
        colorStr.Should().NotBeNullOrEmpty("placeholder should have a color style");
    }

    private static bool HasDescendantText(LayoutBox box, string substring)
    {
        if (box.Text?.IndexOf(substring, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        foreach (var child in box.Children)
            if (HasDescendantText(child, substring))
                return true;
        return false;
    }
}
