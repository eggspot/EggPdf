using System;
using System.Globalization;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// Timestamps written into a PDF are parsed by readers and signature validators; the host's
/// calendar, digits and separators must never leak into them.
/// </summary>
public class PdfDatesTests
{
    private static readonly DateTime Moment = new DateTime(2026, 3, 9, 7, 5, 4, DateTimeKind.Utc);

    [Fact]
    public void PdfDate_FormatsTheDocumentInfoDate()
    {
        PdfDates.PdfDate(Moment).Should().Be("D:20260309070504Z");
    }

    [Fact]
    public void XmpDate_FormatsIso8601()
    {
        PdfDates.XmpDate(Moment).Should().Be("2026-03-09T07:05:04Z");
    }

    [Fact]
    public void Asn1UtcTime_FormatsTwoDigitYear()
    {
        PdfDates.Asn1UtcTime(Moment).Should().Be("260309070504Z");
    }

    [Theory]
    [InlineData("th-TH")]  // Buddhist calendar: year 2569
    [InlineData("ar-SA")]  // Hijri calendar and Arabic-Indic digits
    [InlineData("fi-FI")]  // "." as the time separator
    [InlineData("fa-IR")]  // Persian calendar
    public void AllFormats_UnderOtherCultures_AreIdenticalToTheInvariantOnes(string cultureName)
    {
        CultureInfo culture;
        try { culture = new CultureInfo(cultureName); }
        catch (CultureNotFoundException) { return; } // invariant-globalization host: every culture is the invariant one

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            PdfDates.PdfDate(Moment).Should().Be("D:20260309070504Z");
            PdfDates.XmpDate(Moment).Should().Be("2026-03-09T07:05:04Z");
            PdfDates.Asn1UtcTime(Moment).Should().Be("260309070504Z");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
