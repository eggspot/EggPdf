using EggPdf.Layout;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Layout;

public class TextMeasurerTests
{
    [Fact]
    public void MeasureWidth_EmptyString_ReturnsZero()
    {
        TextMeasurer.MeasureWidth("", 16, null).Should().Be(0);
    }

    [Fact]
    public void MeasureWidth_Helvetica_NarrowCharsNarrowerThanWideChars()
    {
        float iWidth = TextMeasurer.MeasureWidth("i", 16, null);
        float wWidth = TextMeasurer.MeasureWidth("W", 16, null);

        wWidth.Should().BeGreaterThan(iWidth * 2,
            "W should be significantly wider than i in proportional font");
    }

    [Fact]
    public void MeasureWidth_Courier_AllCharsSameWidth()
    {
        float iWidth = TextMeasurer.MeasureWidth("i", 16, "monospace");
        float wWidth = TextMeasurer.MeasureWidth("W", 16, "monospace");

        iWidth.Should().BeApproximately(wWidth, 0.01f,
            "monospace font should have equal character widths");
    }

    [Fact]
    public void MeasureWidth_FontSizeScalesLinearly()
    {
        float width12 = TextMeasurer.MeasureWidth("Hello", 12, null);
        float width24 = TextMeasurer.MeasureWidth("Hello", 24, null);

        width24.Should().BeApproximately(width12 * 2, 0.1f);
    }

    [Fact]
    public void MeasureWidth_Bold_SlightlyWiderThanRegular()
    {
        float regular = TextMeasurer.MeasureWidth("Hello World", 16, null, null, null);
        float bold = TextMeasurer.MeasureWidth("Hello World", 16, null, "bold", null);

        bold.Should().BeGreaterOrEqualTo(regular, "bold glyphs have thicker strokes and are at least as wide");
    }

    [Fact]
    public void MeasureWidth_Space_HasWidth()
    {
        float spaceWidth = TextMeasurer.MeasureWidth(" ", 16, null);
        spaceWidth.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WrapText_ShortText_SingleLine()
    {
        var lines = TextMeasurer.WrapText("Hello", 16, null, 600);
        lines.Should().HaveCount(1);
        lines[0].Should().Be("Hello");
    }

    [Fact]
    public void WrapText_LongText_MultipleLinesRespectingWidth()
    {
        var lines = TextMeasurer.WrapText(
            "The quick brown fox jumps over the lazy dog and keeps on running far away",
            16, null, 200);

        lines.Count.Should().BeGreaterThan(1, "long text should wrap to multiple lines");
        foreach (var line in lines)
        {
            float width = TextMeasurer.MeasureWidth(line, 16, null);
            width.Should().BeLessOrEqualTo(200 + 1f, "each line should fit within maxWidth");
        }
    }

    [Fact]
    public void WrapText_WithFontWeight_WrapsCorrectly()
    {
        var regularLines = TextMeasurer.WrapText("Hello World Test Text Here", 16, null, null, null, 150);
        var boldLines = TextMeasurer.WrapText("Hello World Test Text Here", 16, null, "bold", null, 150);

        boldLines.Count.Should().BeGreaterOrEqualTo(regularLines.Count, "wider bold glyphs can only add line breaks");
    }

    [Fact]
    public void MeasureWidth_TimesRoman_DifferentFromHelvetica()
    {
        float helvetica = TextMeasurer.MeasureWidth("Hello", 16, "sans-serif");
        float times = TextMeasurer.MeasureWidth("Hello", 16, "serif");

        helvetica.Should().NotBe(times, "serif and sans-serif map to fonts with different metrics");
    }

    [Fact]
    public void GetLineHeight_Normal_Returns1Point2TimesFontSize()
    {
        float lh = TextMeasurer.GetLineHeight(16, null);
        lh.Should().BeApproximately(19.2f, 0.1f);
    }

    [Fact]
    public void GetLineHeight_CustomMultiplier_Applied()
    {
        float lh = TextMeasurer.GetLineHeight(16, "1.5");
        lh.Should().BeApproximately(24f, 0.1f);
    }

    [Fact]
    public void WrapText_Hyphenation_LongWordGetsHyphenated()
    {
        var lines = TextMeasurer.WrapText("hyphenation", 16, null, null, null, 60f, "normal", false, true);
        lines.Should().HaveCountGreaterThan(1, "a long word in a 60px line is split with hyphenation");
        for (int i = 0; i < lines.Count - 1; i++)
            lines[i].Should().EndWith("-", $"line {i} should end with a hyphen when hyphenation is enabled");
    }

    [Fact]
    public void WrapText_Hyphenation_ShortWord_NotHyphenated()
    {
        var lines = TextMeasurer.WrapText("run", 16, null, null, null, 10f, "normal", false, true);
        lines.Should().HaveCount(1, "a word shorter than leftMin + rightMin (5 characters) is never hyphenated");
        lines[0].Should().NotEndWith("-");
    }

    [Fact]
    public void WrapText_Hyphenation_Disabled_NoHyphens()
    {
        var lines = TextMeasurer.WrapText("hyphenation", 16, null, null, null, 60f, "normal", false, false);
        foreach (var line in lines)
            line.Should().NotEndWith("-");
    }
}
