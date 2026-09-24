using System.Globalization;

namespace EggPdf.Fluent;

/// <summary>Formats numbers and strings for emission into CSS, so no call site can produce malformed output.</summary>
internal static class CssText
{
    /// <summary>
    /// A float as plain decimal CSS text. The default <c>float.ToString()</c> switches to exponent
    /// notation for very small/large values (<c>1E-05</c>, <c>1E+20</c>), which is not a valid CSS
    /// number and would make the engine silently drop the whole declaration.
    /// </summary>
    public static string Number(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>A quoted CSS string literal with backslashes, quotes and line breaks escaped, so free text can never break out of the string.</summary>
    public static string Quote(string? s)
        => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\d ").Replace("\n", "\\a ") + "\"";
}
