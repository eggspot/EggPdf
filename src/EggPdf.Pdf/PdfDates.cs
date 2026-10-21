using System;
using System.Globalization;

namespace EggPdf.Pdf;

/// <summary>
/// Machine-readable timestamps for PDF structures. Always formatted with the invariant culture and
/// the Gregorian calendar: a Thai or Hijri host culture would otherwise write year 2569 (or 1448)
/// and a Finnish one "." instead of ":" into dates that readers and signature validators parse.
/// </summary>
internal static class PdfDates
{
    /// <summary>PDF date string body: <c>D:yyyyMMddHHmmssZ</c> (no enclosing parentheses).</summary>
    internal static string PdfDate(DateTime utc) => "D:" + utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    /// <summary>XMP / ISO 8601 timestamp: <c>yyyy-MM-ddTHH:mm:ssZ</c>.</summary>
    internal static string XmpDate(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>ASN.1 UTCTime body: <c>yyMMddHHmmssZ</c>.</summary>
    internal static string Asn1UtcTime(DateTime utc) => utc.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";
}
