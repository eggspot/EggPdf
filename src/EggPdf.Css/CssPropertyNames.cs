using System;
using System.Collections.Generic;

namespace EggPdf.Css;

/// <summary>
/// CSS property names are ASCII case-insensitive, except custom properties ("--name"), which are case-sensitive:
/// <c>--Brand</c> and <c>--brand</c> are two properties.
/// </summary>
internal static class CssPropertyNames
{
    /// <summary>Compares custom property names ordinally and every other property name ignoring case.</summary>
    public static readonly IEqualityComparer<string> Comparer = new PropertyNameComparer();

    /// <summary>The name as the cascade stores it: a custom property keeps its case, any other property is lowercased.</summary>
    public static string Normalize(string name) => IsCustom(name) ? name : name.ToLowerInvariant();

    private static bool IsCustom(string name) => name.Length > 2 && name[0] == '-' && name[1] == '-';

    private sealed class PropertyNameComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y)
        {
            if (x == null || y == null) return x == y;
            return IsCustom(x) || IsCustom(y)
                ? string.Equals(x, y, StringComparison.Ordinal)
                : string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(string name)
            => IsCustom(name) ? StringComparer.Ordinal.GetHashCode(name) : StringComparer.OrdinalIgnoreCase.GetHashCode(name);
    }
}
