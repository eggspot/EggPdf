using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace EggPdf.Tests.Unit;

/// <summary>
/// Shared assertions for rendered (uncompressed) PDFs, so end-to-end tests can check
/// feature-specific output instead of only "did not throw".
/// </summary>
internal static class PdfAssert
{
    /// <summary>
    /// Asserts the bytes form a structurally complete PDF (header, trailer, a page, and every
    /// given visible string present) and returns the content as Latin-1 text with LF newlines.
    /// </summary>
    public static string ValidPdf(byte[] pdf, params string[] visibleText)
    {
        pdf.Should().NotBeNullOrEmpty();
        var text = Encoding.Latin1.GetString(pdf).Replace("\r\n", "\n");
        text.Should().StartWith("%PDF-");
        text.TrimEnd().Should().EndWith("%%EOF");
        text.Should().Contain("/Type /Page");
        foreach (var t in visibleText)
            text.Should().Contain(t, $"the text '{t}' must be painted");
        return text;
    }

    /// <summary>Number of physical pages in the rendered PDF.</summary>
    public static int PageCount(string text) => Regex.Matches(text, @"/Type /Page[^s]").Count;

    /// <summary>Number of non-overlapping occurrences of <paramref name="needle"/>.</summary>
    public static int Count(string text, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(needle, idx, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    /// <summary>The (x, y) text-position operands of the "(word) Tj" run containing <paramref name="word"/>.</summary>
    public static (float X, float Y) TextPosition(string text, string word)
    {
        var m = Regex.Match(text, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(" + Regex.Escape(word) + @"\) Tj");
        m.Success.Should().BeTrue($"a positioned text run for '{word}' must exist");
        return (float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>True when the run "(word) Tj" is painted immediately after the given fill-color operator.</summary>
    public static bool TextPaintedWith(string text, string word, string fillOp)
        => Regex.IsMatch(text, Regex.Escape(fillOp) + @"\nBT [^\n]*\(" + Regex.Escape(word) + @"\) Tj");
}
