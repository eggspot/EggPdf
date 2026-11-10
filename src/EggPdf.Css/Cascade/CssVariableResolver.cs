using System;

namespace EggPdf.Css.Cascade;

/// <summary>
/// Resolves CSS custom properties (var() references) and env() in computed style values.
/// Handles nested var(), fallback values, and cycle detection.
/// Infallible: returns original value on error.
/// </summary>
public static class CssVariableResolver
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

        bool hasVar = value.IndexOf(VarPrefix, StringComparison.OrdinalIgnoreCase) >= 0;
        bool hasEnv = value.IndexOf(EnvPrefix, StringComparison.OrdinalIgnoreCase) >= 0;

        if (!hasVar && !hasEnv)
            return value;

        string result = hasEnv ? ResolveEnv(value) : value;
        if (result.IndexOf(VarPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
            result = ResolveVariablesRecursive(result, style, 0);

        return result;
    }

    /// <summary>
    /// Known safe-area-inset environment variables — all 0px for PDF rendering.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> SafeAreaVars =
        new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
        if (value.IndexOf(EnvPrefix, StringComparison.OrdinalIgnoreCase) < 0)
            return value;

        int pos = 0;
        var result = new System.Text.StringBuilder(value.Length);

        while (pos < value.Length)
        {
            int startIdx = value.IndexOf(EnvPrefix, pos, StringComparison.OrdinalIgnoreCase);
            if (startIdx < 0)
            {
                result.Append(value, pos, value.Length - pos);
                break;
            }

            result.Append(value, pos, startIdx - pos);

            int openParen = startIdx + EnvPrefix.Length - 1;
            int closeParen = FindMatchingParen(value, openParen);
            if (closeParen < 0)
            {
                result.Append(value, startIdx, value.Length - startIdx);
                break;
            }

            string envContent = value.Substring(openParen + 1, closeParen - openParen - 1).Trim();

            string envName;
            string? fallback;
            ParseVarContent(envContent, out envName, out fallback);

            if (SafeAreaVars.Contains(envName))
                result.Append("0px");
            else if (fallback != null)
                result.Append(fallback.Trim());

            pos = closeParen + 1;
        }

        return result.ToString();
    }

    private static string ResolveVariablesRecursive(string value, ComputedStyle style, int depth)
    {
        if (depth >= MaxDepth)
            return value;

        if (string.IsNullOrEmpty(value))
            return value;

        int startIdx = value.IndexOf(VarPrefix, StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0)
            return value;

        int pos = 0;
        var result = new System.Text.StringBuilder(value.Length);

        while (pos < value.Length)
        {
            startIdx = IndexOfVar(value, pos);
            if (startIdx < 0)
            {
                result.Append(value, pos, value.Length - pos);
                break;
            }

            result.Append(value, pos, startIdx - pos);

            int openParen = startIdx + VarPrefix.Length;
            int closeParen = FindMatchingParen(value, openParen - 1);
            if (closeParen < 0)
            {
                result.Append(value, startIdx, value.Length - startIdx);
                break;
            }

            string varContent = value.Substring(openParen, closeParen - openParen).Trim();

            string varName;
            string? fallback;
            ParseVarContent(varContent, out varName, out fallback);

            string? resolved = style.Get(varName);

            if (resolved != null)
                result.Append(ResolveVariablesRecursive(resolved, style, depth + 1));
            else if (fallback != null)
                result.Append(ResolveVariablesRecursive(fallback.Trim(), style, depth + 1));

            pos = closeParen + 1;
        }

        return result.ToString();
    }

    /// <summary>
    /// Find "var(" in a case-insensitive way starting from pos.
    /// </summary>
    private static int IndexOfVar(string value, int startPos)
    {
        return value.IndexOf(VarPrefix, startPos, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Find matching closing paren for the opening paren at the given position.
    /// </summary>
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

    /// <summary>
    /// Detect cycles by checking if resolving a custom property would
    /// reference itself (directly or indirectly). Returns true if safe.
    /// </summary>
    public static bool HasCycle(string propertyName, ComputedStyle style)
    {
        return HasCycleRecursive(propertyName, style, 0);
    }

    private static bool HasCycleRecursive(string propertyName, ComputedStyle style, int depth)
    {
        if (depth >= MaxDepth)
            return true;

        string? value = style.Get(propertyName);
        if (string.IsNullOrEmpty(value))
            return false;

        int pos = 0;
        while (pos < value.Length)
        {
            int idx = IndexOfVar(value, pos);
            if (idx < 0) break;

            int openParen = idx + VarPrefix.Length;
            int closeParen = FindMatchingParen(value, openParen - 1);
            if (closeParen < 0) break;

            string varContent = value.Substring(openParen, closeParen - openParen).Trim();
            string varName;
            string? fallback;
            ParseVarContent(varContent, out varName, out fallback);

            if (string.Equals(varName, propertyName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (HasCycleRecursive(varName, style, depth + 1))
                return true;

            pos = closeParen + 1;
        }

        return false;
    }
}
