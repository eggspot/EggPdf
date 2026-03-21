using System;
using System.Collections.Generic;

namespace EggPdf.Css;

/// <summary>
/// Parses inline CSS from style="" attributes into a list of declarations.
/// Infallible: never throws. Invalid syntax is skipped.
/// </summary>
public static class CssInlineParser
{
    public static List<CssDeclaration> Parse(string? css)
    {
        var declarations = new List<CssDeclaration>();

        if (string.IsNullOrWhiteSpace(css))
            return declarations;

        // Split by semicolons (simple approach for inline styles)
        var parts = css.Split(';');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Find the colon separating property from value
            int colonIndex = trimmed.IndexOf(':');
            if (colonIndex <= 0 || colonIndex >= trimmed.Length - 1)
                continue;

            var property = trimmed.Substring(0, colonIndex).Trim().ToLowerInvariant();
            var value = trimmed.Substring(colonIndex + 1).Trim();

            // Skip invalid property names
            if (string.IsNullOrEmpty(property) || property.Contains(" "))
                continue;

            // Check for !important
            bool important = false;
            if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith("! important", StringComparison.OrdinalIgnoreCase))
            {
                important = true;
                int bangIndex = value.LastIndexOf('!');
                value = value.Substring(0, bangIndex).Trim();
            }

            if (string.IsNullOrEmpty(value))
                continue;

            declarations.Add(new CssDeclaration(property, value, important));
        }

        return declarations;
    }
}
