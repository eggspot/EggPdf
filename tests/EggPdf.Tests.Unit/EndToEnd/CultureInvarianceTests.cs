using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// A PDF is a machine format: the same input must produce the same operators whatever the host's
/// regional settings are. A decimal comma ("0,5 0 0 rg"), a typographic minus sign, another calendar
/// in a date or a different time separator each corrupt the file or its metadata. Rendering under
/// cultures that differ from the invariant one in exactly those ways, and comparing against the
/// invariant render, catches every code path that formats or parses a value without
/// <see cref="CultureInfo.InvariantCulture"/> -- including string concatenation, which no analyzer sees.
/// </summary>
public class CultureInvarianceTests
{
    /// <summary>
    /// de-DE: decimal comma. sv-SE: U+2212 minus sign. fi-FI: "." as the time separator.
    /// th-TH: Buddhist calendar. ar-SA: Hijri calendar and Arabic-Indic digits.
    /// </summary>
    public static TheoryData<string> Cultures => new() { "de-DE", "sv-SE", "fi-FI", "th-TH", "ar-SA" };

    // Fractional and negative values everywhere numbers reach the output: lengths, colors, transforms,
    // gradients, shadows, SVG paths, letter-spacing, counters, font axes.
    private const string Corpus = @"<!DOCTYPE html><html lang='en'><head><style>
        @page { size: A4; margin: 17.5mm 12.3mm; @bottom-center { content: 'Page ' counter(page) ' of ' counter(pages); } }
        body { font-family: Arial, sans-serif; font-size: 10.5pt; line-height: 1.35; margin: 0; }
        h1 { font-size: 1.75em; letter-spacing: 0.35px; word-spacing: 1.5px; margin: -2.5px 0 0.67em; color: rgba(12, 99, 180, 0.65); }
        .box { width: 61.8%; margin: -3.25px auto 7.5px; padding: 4.5px 6.25px; border: 1.5px solid #3a7bd5;
               border-radius: 7.5px 3.25px; background: linear-gradient(37.5deg, rgba(255,0,0,.35) 12.5%, #00d2ff 87.5%);
               box-shadow: 2.5px 3.5px 6.5px rgba(0,0,0,.33); opacity: .85; transform: rotate(-2.5deg) translate(-1.5px, 2.25px) scale(.97, 1.03); }
        .radial { height: 60px; background: radial-gradient(circle at 30.5% 40.25%, #fa0, #05f 70.75%); }
        .conic { height: 60px; background: conic-gradient(from 12.5deg, red, yellow 33.3%, green, blue); }
        .stretch { font-family: Arial; font-stretch: 87.5%; font-variation-settings: 'wght' 612.5; }
        table { border-collapse: collapse; width: 100%; } td, th { border: .75px solid #999; padding: 2.5px; text-align: right; }
        ol li::marker { color: #c33; } .flex { display: flex; gap: 3.5px; } .flex > div { flex: 1.5 1 0; background: #eee; }
        .grid { display: grid; grid-template-columns: 1.5fr 2.25fr; gap: 4.5px; } .sup { vertical-align: super; font-size: 70%; }
        .abs { position: absolute; left: 123.456px; top: -4.5px; } .clip { clip-path: circle(40.5% at 50% 50%); height: 30px; background: #6a1b9a; }
        .dec { text-decoration: underline wavy #c00; text-decoration-thickness: 1.5px; }
      </style></head><body>
      <h1>Invoice #2024-001 — 12,345.67</h1>
      <div class='box'>Fractional everything: 0.1 + 0.2 = 0.30000000000000004, −1.5, 1e3, 1.5e-3.</div>
      <div class='radial'></div><div class='conic'></div>
      <p class='stretch'>Condensed variable text 87.5% <span class='sup'>sup</span> <span class='dec'>wavy</span></p>
      <table><tr><th>Item</th><th>Qty</th><th>Price</th></tr>
        <tr><td>Widget</td><td>3</td><td>1,234.50</td></tr><tr><td>Gadget</td><td>-2</td><td>-0.99</td></tr></table>
      <ol start='-3'><li>Negative start</li><li>Second</li></ol>
      <ul style='list-style-type: lower-roman'><li>one</li><li>two</li></ul>
      <div class='flex'><div>a</div><div>b</div><div>c</div></div><div class='grid'><div>x</div><div>y</div></div>
      <div class='abs'>absolute</div><div class='clip'></div>
      <svg width='120.5' height='60.25' viewBox='0 0 100.5 50.25' xmlns='http://www.w3.org/2000/svg'>
        <defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0.5'><stop offset='0.15' stop-color='#f00'/><stop offset='0.85' stop-color='#00f'/></linearGradient></defs>
        <rect x='1.5' y='2.5' width='40.25' height='20.75' rx='3.5' fill='url(#g)' stroke='#000' stroke-width='.75'/>
        <circle cx='70.5' cy='25.25' r='12.75' fill='rgb(10.5, 160.25, 30)' opacity='.6'/>
        <path d='M10.5,40.25 C20.5,30.5 30.25,50.75 40.5,40.25 S60.5,30.25 70.75,45.5 L80.5,38.25 Z' fill='none' stroke='#333' stroke-width='1.25'/>
        <text x='5.5' y='48.5' font-size='6.5' transform='rotate(-5.5 5.5 48.5)'>Label 1.5</text></svg>
      <p style='page-break-before: always'>Second page <a href='#t'>link</a> <span id='t'>target</span></p>
      </body></html>";

    private static byte[] Render(Func<byte[]> render, string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = new CultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    /// <summary>
    /// The PDF as text with only the values that legitimately differ between two renders masked: the
    /// time of day and the random file identifier. Dates keep their calendar year/month/day and their
    /// separators, so a culture-specific calendar or time separator still shows up as a difference.
    /// </summary>
    private static string Normalize(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf).Replace("\r\n", "\n");
        text = Regex.Replace(text, @"(D:\d{8})\d{6}", "$1HHmmss");
        text = Regex.Replace(text, @"(\d{4}-\d{2}-\d{2}T)\d{2}:\d{2}:\d{2}", "$1HH:mm:ss");
        text = Regex.Replace(text, @"/ID \[<[0-9A-Fa-f]+> <[0-9A-Fa-f]+>\]", "/ID [<id> <id>]");
        text = Regex.Replace(text, @"(?<=<xmpMM:(?:DocumentID|InstanceID)>)[^<]*", "id");
        return text;
    }

    private static void ShouldMatchInvariantRender(Func<byte[]> render, string cultureName)
    {
        // Hosts running with invariant globalization have only the invariant culture: nothing to compare
        try { _ = new CultureInfo(cultureName); }
        catch (CultureNotFoundException) { return; }

        var invariant = Normalize(Render(render, ""));
        var localized = Normalize(Render(render, cultureName));

        if (invariant == localized) return;

        int at = 0;
        while (at < invariant.Length && at < localized.Length && invariant[at] == localized[at]) at++;
        string Around(string s) => s.Substring(Math.Max(0, at - 60), Math.Min(140, s.Length - Math.Max(0, at - 60)));
        Assert.Fail($"Rendering under '{cultureName}' differs from the invariant render at offset {at}.\n" +
                    $"invariant: …{Around(invariant)}…\nlocalized: …{Around(localized)}…");
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Render_UnderRegionalCulture_MatchesTheInvariantRender(string cultureName)
    {
        ShouldMatchInvariantRender(() => HtmlToPdf.Render(Corpus), cultureName);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Render_PdfAAndTagged_UnderRegionalCulture_MatchesTheInvariantRender(string cultureName)
    {
        ShouldMatchInvariantRender(() => HtmlToPdf.Render(Corpus, PdfAConformance.PdfA2b, true), cultureName);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Render_WithPageOptions_UnderRegionalCulture_MatchesTheInvariantRender(string cultureName)
    {
        var options = new PdfRenderOptions
        {
            PageSize = "Letter", Orientation = "landscape", MarginTop = 12.7f, MarginLeft = 9.5f,
            Title = "Invoice 1.5", Author = "Culture Test",
        };
        ShouldMatchInvariantRender(() => HtmlToPdf.Render(Corpus, options), cultureName);
    }

    [Fact]
    public void Render_UnderCommaDecimalCulture_NeverWritesADecimalCommaOperand()
    {
        try { _ = new CultureInfo("de-DE"); }
        catch (CultureNotFoundException) { return; }

        var pdf = Normalize(Render(() => HtmlToPdf.Render(Corpus), "de-DE"));
        Regex.IsMatch(pdf, @"(?m)^\s*-?\d+,\d+ (?:-?\d+,\d+ )*(?:rg|RG|re|m|l|c|cm|Td|Tm|w|Tf|g|G)\b")
            .Should().BeFalse("content-stream operands must use '.' as the decimal separator");
    }

    [Fact]
    public void Render_UnderBuddhistCalendarCulture_StampsTheGregorianYear()
    {
        try { _ = new CultureInfo("th-TH"); }
        catch (CultureNotFoundException) { return; }

        var pdf = Encoding.Latin1.GetString(Render(() => HtmlToPdf.Render(Corpus), "th-TH"));
        pdf.Should().Contain("/CreationDate (D:" + DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture));
    }
}
