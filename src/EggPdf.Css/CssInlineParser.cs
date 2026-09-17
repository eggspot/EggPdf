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

        // Split by semicolons, but not ones inside parens (e.g. a function
        // argument) or quotes (e.g. a literal string, or -- the case that
        // exposed this -- a "data:image/png;base64,..." URI, which contains
        // a semicolon that a naive css.Split(';') would wrongly treat as a
        // declaration boundary, truncating the value).
        var parts = SplitDeclarations(css);

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

    /// <summary>Split on ';' at paren-depth 0 and outside quotes.</summary>
    private static List<string> SplitDeclarations(string css)
    {
        var result = new List<string>();
        int depth = 0;
        char? quote = null;
        int start = 0;
        for (int i = 0; i < css.Length; i++)
        {
            char c = css[i];
            if (quote.HasValue)
            {
                if (c == quote.Value) quote = null;
                continue;
            }
            if (c == '\'' || c == '"') { quote = c; continue; }
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (c == ';' && depth == 0)
            {
                result.Add(css.Substring(start, i - start));
                start = i + 1;
            }
        }
        result.Add(css.Substring(start));
        return result;
    }
}
