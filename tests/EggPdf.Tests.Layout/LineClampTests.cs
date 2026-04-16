using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

/// <summary>Tests for -webkit-line-clamp / line-clamp: truncates text to N lines with ellipsis.</summary>
public class LineClampTests
{
    [Fact]
    public void LineClamp_StylePreserved()
    {
        var root = LayoutTestHelper.Layout(
            "<p style='-webkit-line-clamp:2; overflow:hidden; display:-webkit-box; -webkit-box-orient:vertical'>" +
            "Some long text</p>", 400, 600);
        var p = root.FindByTag("p");
        p.Should().NotBeNull();
        p!.Style.Get("-webkit-line-clamp").Should().Be("2",
            "-webkit-line-clamp should be preserved in computed style");
    }

    [Fact]
    public void LineClamp_2_LimitsTo2TextBoxes()
    {
        // Long text that would normally wrap to 4+ lines at 12px in 120px width
        const string longText = "word1 word2 word3 word4 word5 word6 word7 word8 word9 word10 word11 word12";
        var root = LayoutTestHelper.Layout(
            $"<body style='margin:0'><p style='font-size:12px; width:120px; -webkit-line-clamp:2'>" +
            $"{longText}</p></body>", 400, 600);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        // Count text boxes (lines) inside the paragraph
        var textBoxes = p!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));
        textBoxes.Should().HaveCountLessOrEqualTo(2,
            "-webkit-line-clamp:2 should limit output to at most 2 text lines");
        textBoxes.Count.Should().BeGreaterThan(0, "there should be at least some text");
    }

    [Fact]
    public void LineClamp_LastLineHasEllipsis()
    {
        const string longText = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda";
        var root = LayoutTestHelper.Layout(
            $"<body style='margin:0'><p style='font-size:12px; width:100px; -webkit-line-clamp:2'>" +
            $"{longText}</p></body>", 400, 600);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var textBoxes = p!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));
        textBoxes.Should().NotBeEmpty();

        // The last text box should contain an ellipsis character
        var lastBox = textBoxes[textBoxes.Count - 1];
        lastBox.Text.Should().Contain("\u2026",
            "last clamped line should end with ellipsis (U+2026)");
    }

    [Fact]
    public void LineClamp_TextShorterThanLimit_NotClamped()
    {
        // Text fits in 1 line — no clamping needed even with line-clamp:3
        var root = LayoutTestHelper.Layout(
            "<p style='font-size:12px; width:300px; -webkit-line-clamp:3'>Short</p>", 400, 600);

        var p = root.FindByTag("p");
        p.Should().NotBeNull();

        var textBoxes = p!.Children.FindAll(b => !string.IsNullOrEmpty(b.Text));
        textBoxes.Should().HaveCount(1, "single-line text should not be clamped");

        // Should NOT have ellipsis if not clamped
        textBoxes[0].Text.Should().NotContain("\u2026",
            "short text within the clamp limit should not have ellipsis");
    }
}
