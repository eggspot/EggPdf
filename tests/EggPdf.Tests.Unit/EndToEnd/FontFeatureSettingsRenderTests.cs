using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// font-feature-settings changes which glyph a codepoint maps to (GSUB single
/// substitution), so two elements declaring different active features on the same
/// font family must not share one embedded-font/glyph-map entry -- see
/// StandardFontMetrics.ResolvePdfFontName's "-Feat-..." composite key suffix.
/// </summary>
public class FontFeatureSettingsRenderTests
{
    private static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    [Fact]
    public async Task DifferentFeatureSettings_SameFamily_ProduceDistinctEmbeddedFonts()
    {
        var html = @"
            <p style=""font-family: Arial; font-feature-settings: 'zero' 1"">111</p>
            <p style=""font-family: Arial; font-feature-settings: 'smcp' 1"">AAA</p>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = Latin1(pdf);

        CountOccurrences(text, "/FontFile2").Should().BeGreaterOrEqualTo(2,
            "distinct font-feature-settings values on the same family must embed as separate font entries");
    }

    [Fact]
    public async Task SameFeatureSettings_SameFamily_ShareOneEmbeddedFont()
    {
        var html = @"
            <p style=""font-family: Arial; font-feature-settings: 'zero' 1"">111</p>
            <p style=""font-family: Arial; font-feature-settings: 'zero' 1"">222</p>";

        var pdf = await HtmlToPdf.RenderAsync(html);
        var text = Latin1(pdf);

        CountOccurrences(text, "/FontFile2").Should().Be(1,
            "identical font-feature-settings values must share a single embedded-font entry, not fragment needlessly");
    }

    [Fact]
    public async Task FontFeatureSettings_NoMatchingGsubFeature_StillPaintsUnsubstitutedText()
    {
        // Most installed fonts have no "xyz1"-style custom feature; this must
        // degrade gracefully to the unsubstituted glyph, never throw.
        var html = "<p style=\"font-family: Arial; font-feature-settings: 'xyz1' 1\">Hello</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = PdfAssert.ValidPdf(pdf);

        // Embedded Arial: five glyph ids (4 hex digits each) under the feature-keyed font; standard-font fallback: literal.
        text.Should().MatchRegex(@"\(Hello\) Tj|<[0-9A-F]{20}> Tj", "'Hello' is painted as five unsubstituted glyphs");
    }
}
