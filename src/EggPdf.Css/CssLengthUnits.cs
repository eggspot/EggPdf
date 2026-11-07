using System.Globalization;

namespace EggPdf.Css;

internal readonly struct CssLengthContext
{
    public CssLengthContext(float containingSize, float fontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        ContainingSize = containingSize;
        FontSize = fontSize;
        RootFontSize = rootFontSize;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    public float ContainingSize { get; }
    public float FontSize { get; }
    public float RootFontSize { get; }
    public float ViewportWidth { get; }
    public float ViewportHeight { get; }
}

internal static class CssLengthUnits
{
    private const float PxPerInch = 96f;
    private const float XHeightPerEm = 0.5f;
    private const float ZeroAdvancePerEm = 0.5f;
    private const float LineHeightPerEm = 1.2f;

    internal static int UnitStart(string value)
    {
        int unitStart = value.Length;
        while (unitStart > 0 && IsAsciiLetter(value[unitStart - 1]))
            unitStart--;
        return unitStart;
    }

    internal static float ParseNumber(string value, int length)
    {
#if NETSTANDARD2_0
        return float.TryParse(value.Substring(0, length), NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : 0f;
#else
        return float.TryParse(value.AsSpan(0, length), NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : 0f;
#endif
    }

    internal static bool TryParseNumber(string value, int length, out float number)
    {
#if NETSTANDARD2_0
        return float.TryParse(value.Substring(0, length), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
#else
        return float.TryParse(value.AsSpan(0, length), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
#endif
    }

    internal static bool IsViewportUnit(string value, int unitStart) =>
        UnitIs(value, unitStart, "vw") || UnitIs(value, unitStart, "vh") ||
        UnitIs(value, unitStart, "vmin") || UnitIs(value, unitStart, "vmax");

    internal static bool TryToPx(float number, string value, int unitStart, in CssLengthContext context, out float px)
    {
        px = 0f;
        if (UnitIs(value, unitStart, "px")) px = number;
        else if (UnitIs(value, unitStart, "em")) px = number * context.FontSize;
        else if (UnitIs(value, unitStart, "rem")) px = number * context.RootFontSize;
        else if (UnitIs(value, unitStart, "ex")) px = number * context.FontSize * XHeightPerEm;
        else if (UnitIs(value, unitStart, "ch")) px = number * context.FontSize * ZeroAdvancePerEm;
        else if (UnitIs(value, unitStart, "lh")) px = number * context.FontSize * LineHeightPerEm;
        else if (UnitIs(value, unitStart, "pt")) px = number * PxPerInch / 72f;
        else if (UnitIs(value, unitStart, "pc")) px = number * PxPerInch / 6f;
        else if (UnitIs(value, unitStart, "in")) px = number * PxPerInch;
        else if (UnitIs(value, unitStart, "cm")) px = number * PxPerInch / 2.54f;
        else if (UnitIs(value, unitStart, "mm")) px = number * PxPerInch / 25.4f;
        else if (UnitIs(value, unitStart, "q")) px = number * PxPerInch / 101.6f;
        else if (UnitIs(value, unitStart, "vw")) px = number / 100f * ViewportOr(context.ViewportWidth, context.ContainingSize);
        else if (UnitIs(value, unitStart, "vh")) px = number / 100f * ViewportOr(context.ViewportHeight, context.ContainingSize);
        else if (UnitIs(value, unitStart, "vmin")) px = number / 100f * ViewportMinOr(context);
        else if (UnitIs(value, unitStart, "vmax")) px = number / 100f * ViewportMaxOr(context);
        else return false;
        return true;
    }

    private static float ViewportOr(float viewportSize, float fallback) => viewportSize > 0f ? viewportSize : fallback;

    private static float ViewportMinOr(in CssLengthContext context) =>
        context.ViewportWidth > 0f && context.ViewportHeight > 0f
            ? System.Math.Min(context.ViewportWidth, context.ViewportHeight)
            : context.ContainingSize;

    private static float ViewportMaxOr(in CssLengthContext context) =>
        context.ViewportWidth > 0f && context.ViewportHeight > 0f
            ? System.Math.Max(context.ViewportWidth, context.ViewportHeight)
            : context.ContainingSize;

    private static bool UnitIs(string value, int unitStart, string unit)
    {
        if (value.Length - unitStart != unit.Length) return false;
        for (int i = 0; i < unit.Length; i++)
        {
            if ((value[unitStart + i] | 0x20) != unit[i]) return false;
        }
        return true;
    }

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
}
