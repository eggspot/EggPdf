using System.Globalization;

namespace EggPdf.Layout;

internal static class LengthUnits
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

    internal static bool TryToPx(float number, string value, int unitStart, float containingSize, float fontSize, out float px)
    {
        px = 0f;
        if (UnitIs(value, unitStart, "px")) px = number;
        else if (UnitIs(value, unitStart, "em")) px = number * fontSize;
        else if (UnitIs(value, unitStart, "rem")) px = number * BlockLayout.RootFontSize;
        else if (UnitIs(value, unitStart, "ex")) px = number * fontSize * XHeightPerEm;
        else if (UnitIs(value, unitStart, "ch")) px = number * fontSize * ZeroAdvancePerEm;
        else if (UnitIs(value, unitStart, "lh")) px = number * fontSize * LineHeightPerEm;
        else if (UnitIs(value, unitStart, "pt")) px = number * PxPerInch / 72f;
        else if (UnitIs(value, unitStart, "pc")) px = number * PxPerInch / 6f;
        else if (UnitIs(value, unitStart, "in")) px = number * PxPerInch;
        else if (UnitIs(value, unitStart, "cm")) px = number * PxPerInch / 2.54f;
        else if (UnitIs(value, unitStart, "mm")) px = number * PxPerInch / 25.4f;
        else if (UnitIs(value, unitStart, "q")) px = number * PxPerInch / 101.6f;
        else if (UnitIs(value, unitStart, "vw")) px = number / 100f * ViewportOr(BlockLayout.ViewportWidth, containingSize);
        else if (UnitIs(value, unitStart, "vh")) px = number / 100f * ViewportOr(BlockLayout.ViewportHeight, containingSize);
        else if (UnitIs(value, unitStart, "vmin")) px = number / 100f * ViewportMinOr(containingSize);
        else if (UnitIs(value, unitStart, "vmax")) px = number / 100f * ViewportMaxOr(containingSize);
        else return false;
        return true;
    }

    private static float ViewportOr(float viewportSize, float fallback) => viewportSize > 0f ? viewportSize : fallback;

    private static float ViewportMinOr(float fallback) =>
        BlockLayout.ViewportWidth > 0f && BlockLayout.ViewportHeight > 0f
            ? System.Math.Min(BlockLayout.ViewportWidth, BlockLayout.ViewportHeight)
            : fallback;

    private static float ViewportMaxOr(float fallback) =>
        BlockLayout.ViewportWidth > 0f && BlockLayout.ViewportHeight > 0f
            ? System.Math.Max(BlockLayout.ViewportWidth, BlockLayout.ViewportHeight)
            : fallback;

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
