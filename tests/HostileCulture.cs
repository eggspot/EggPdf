using System.Globalization;
using System.Runtime.CompilerServices;

/// <summary>
/// Runs every test in the assembly under a deliberately hostile regional setting, so any code that
/// formats or parses a number or date with the machine's culture fails on every developer machine and
/// in CI -- not only on a machine that happens to be set to, say, a decimal-comma locale.
/// Shared by all test assemblies (linked into each project; see the csproj files).
/// </summary>
/// <remarks>
/// The culture differs from <see cref="CultureInfo.InvariantCulture"/> in every way that has corrupted
/// output in practice: decimal comma, "." as thousands separator, U+2212 as the minus sign, "." as the
/// time separator and (where the platform has it) the Buddhist calendar, whose year is 543 ahead.
/// Production code must pass <see cref="CultureInfo.InvariantCulture"/> for anything machine-readable
/// (PDF operators, CSS values, font axes, dates in metadata); tests that need a specific culture set
/// <see cref="CultureInfo.CurrentCulture"/> themselves and restore it.
/// </remarks>
internal static class HostileCulture
{
    [ModuleInitializer]
    internal static void Init()
    {
        var culture = Create();
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    internal static CultureInfo Create()
    {
        CultureInfo template;
        try { template = new CultureInfo("th-TH"); }
        catch (CultureNotFoundException) { template = CultureInfo.InvariantCulture; } // invariant-globalization hosts

        var culture = (CultureInfo)template.Clone();
        var number = culture.NumberFormat;
        number.NumberDecimalSeparator = ",";
        number.NumberGroupSeparator = ".";
        number.PercentDecimalSeparator = ",";
        number.CurrencyDecimalSeparator = ",";
        number.NegativeSign = "−";

        var date = culture.DateTimeFormat;
        date.TimeSeparator = ".";
        date.DateSeparator = "-";
        return culture;
    }
}
