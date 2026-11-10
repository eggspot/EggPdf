using System;
using System.Collections.Generic;
using System.Text;

namespace EggPdf.Css.Cascade;

/// <summary>
/// Resolves CSS custom properties (var() references) and env() in computed style values.
/// Handles nested var(), fallback values, and cycle detection.
/// Infallible: returns original value on error.
/// </summary>
public static partial class CssVariableResolver
{
    private const int MaxDepth = 10;
    private const string VarPrefix = "var(";
    private const string EnvPrefix = "env(";

    /// <summary>
    /// Resolve all var() and env() references in a CSS property value.
    /// </summary>
    public static string ResolveVariables(string value, ComputedStyle style)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        bool hasVar = IndexOfFunction(value, VarPrefix, 0) >= 0;
        bool hasEnv = IndexOfFunction(value, EnvPrefix, 0) >= 0;

        if (!hasVar && !hasEnv)
            return value;

        string result = hasEnv ? ResolveEnv(value) : value;
        if (IndexOfFunction(result, VarPrefix, 0) >= 0)
            result = ResolveVariablesRecursive(result, style, 0);

        return result;
    }

    /// <summary>
    /// Known safe-area-inset environment variables — all 0px for PDF rendering.
    /// </summary>
    private static readonly HashSet<string> SafeAreaVars =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "safe-area-inset-top", "safe-area-inset-right",
            "safe-area-inset-bottom", "safe-area-inset-left"
        };

    /// <summary>
    /// Resolve env() references in a value.
    /// safe-area-inset-* -> 0px; unknown with fallback -> fallback; unknown without -> empty.
    /// </summary>
    private static string ResolveEnv(string value)
    {
        int pos = 0;
        StringBuilder? result = null;

        while (pos < value.Length)
        {
            int startIdx = IndexOfFunction(value, EnvPrefix, pos);
            if (startIdx < 0)
                break;

            result ??= new StringBuilder(value.Length);
            result.Append(value, pos, startIdx - pos);

            int openParen = startIdx + EnvPrefix.Length - 1;
            int closeParen = FindMatchingParen(value, openParen);
            if (closeParen < 0)
            {
                result.Append(value, startIdx, value.Length - startIdx);
                return result.ToString();
            }

            string envContent = value.Substring(openParen + 1, closeParen - openParen - 1).Trim();
            ParseVarContent(envContent, out string envName, out string? fallback);

            if (SafeAreaVars.Contains(envName))
                result.Append("0px");
            else if (fallback != null)
                result.Append(fallback.Trim());

            pos = closeParen + 1;
        }

        if (result == null)
            return value;
        result.Append(value, pos, value.Length - pos);
        return result.ToString();
    }

    private static string ResolveVariablesRecursive(string value, ComputedStyle style, int depth)
    {
        if (depth >= MaxDepth || string.IsNullOrEmpty(value))
            return value;

        int pos = 0;
        StringBuilder? result = null;

        while (pos < value.Length)
        {
            int startIdx = IndexOfFunction(value, VarPrefix, pos);
            if (startIdx < 0)
                break;

            result ??= new StringBuilder(value.Length);
            result.Append(value, pos, startIdx - pos);

            int openParen = startIdx + VarPrefix.Length;
            int closeParen = FindMatchingParen(value, openParen - 1);
            if (closeParen < 0)
            {
                result.Append(value, startIdx, value.Length - startIdx);
                return result.ToString();
            }

            string varContent = value.Substring(openParen, closeParen - openParen).Trim();
            ParseVarContent(varContent, out string varName, out string? fallback);

            string? resolved = style.Get(varName);
            if (resolved != null)
                result.Append(ResolveVariablesRecursive(resolved, style, depth + 1));
            else if (fallback != null)
                result.Append(ResolveVariablesRecursive(fallback.Trim(), style, depth + 1));

            pos = closeParen + 1;
        }

        if (result == null)
            return value;
        result.Append(value, pos, value.Length - pos);
        return result.ToString();
    }

    /// <summary>
    /// The index of the function <paramref name="prefix"/> ("var(" or "env(") at or after <paramref name="start"/>,
    /// matched case-insensitively, outside quoted strings and not as the tail of a longer name (so "myvar(" is not var()).
    /// </summary>
    private static int IndexOfFunction(string value, string prefix, int start)
    {
        char quote = '\0';
        for (int i = start; i < value.Length; i++)
        {
            char c = value[i];
            if (quote != '\0')
            {
                if (c == '\\') i++;
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c == '"' || c == '\'')
            {
                quote = c;
                continue;
            }
            if (char.ToLowerInvariant(c) == prefix[0] &&
                string.Compare(value, i, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                (i == 0 || !IsNameChar(value[i - 1])))
                return i;
        }
        return -1;
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c > 127;

    private static bool ContainsReference(string value)
        => IndexOfFunction(value, VarPrefix, 0) >= 0 || IndexOfFunction(value, EnvPrefix, 0) >= 0;

    private static int FindMatchingParen(string value, int openParenPos)
    {
        int depth = 0;
        for (int i = openParenPos; i < value.Length; i++)
        {
            if (value[i] == '(')
                depth++;
            else if (value[i] == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Parse "var(--name, fallback)" content into name and fallback.
    /// The fallback is everything after the first comma (may contain commas itself).
    /// </summary>
    private static void ParseVarContent(string content, out string varName, out string? fallback)
    {
        int depth = 0;
        int commaIdx = -1;
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                commaIdx = i;
                break;
            }
        }

        if (commaIdx >= 0)
        {
            varName = content.Substring(0, commaIdx).Trim();
            fallback = content.Substring(commaIdx + 1).Trim();
        }
        else
        {
            varName = content.Trim();
            fallback = null;
        }
    }

    /// <summary>
    /// Check if a property name is a CSS custom property (starts with --).
    /// </summary>
    public static bool IsCustomProperty(string property)
    {
        return property.Length > 2 && property[0] == '-' && property[1] == '-';
    }
}
