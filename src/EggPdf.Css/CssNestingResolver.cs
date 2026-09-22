using System.Collections.Generic;
using EggPdf.Css.Parser;

namespace EggPdf.Css;

/// <summary>
/// Resolves CSS nesting (Chrome 120+) by flattening nested rules.
/// Converts: "div { &amp; p { color: red } }" into two rules:
/// - "div { }" (own declarations)
/// - "div p { color: red }" (expanded nested rule)
/// The &amp; selector is replaced with the parent selector.
/// </summary>
public static class CssNestingResolver
{
    /// <summary>
    /// Expand a nested selector by replacing &amp; with the parent selector.
    /// If no &amp; is present, prepend the parent selector as a descendant.
    /// </summary>
    public static string ExpandNestedSelector(string parentSelector, string nestedSelector)
    {
        var nested = nestedSelector.Trim();

        // If & is present, replace it with the parent selector
        if (nested.IndexOf('&') >= 0)
        {
            return nested.Replace("&", parentSelector);
        }

        // No & — treat as descendant: "parent nested"
        return parentSelector + " " + nested;
    }

    /// <summary>
    /// Pre-process CSS text to flatten nesting before parsing.
    /// Scans for nested rule blocks and expands them.
    /// </summary>
    public static string PreprocessNesting(string cssText)
    {
        if (string.IsNullOrEmpty(cssText) || !HasNestedBraces(cssText))
            return cssText;

        // Simple approach: find & selectors inside rule blocks and expand them
        // This is a pre-parse step that runs before the CSS parser
        var result = new System.Text.StringBuilder(cssText.Length);
        int i = 0;

        while (i < cssText.Length)
        {
            // Find next rule block
            int selectorStart = i;
            int braceOpen = IndexOfUnquoted(cssText, '{', i);
            if (braceOpen < 0)
            {
                result.Append(cssText, i, cssText.Length - i);
                break;
            }

            string selector = cssText.Substring(selectorStart, braceOpen - selectorStart).Trim();
            i = braceOpen + 1;

            // Find matching closing brace (handle nesting, skipping braces inside quoted strings
            // e.g. content: "{" so a literal brace in a declaration value isn't mistaken for a rule)
            int blockStart = i;
            i = MatchingBraceEnd(cssText, i);
            string block = cssText.Substring(blockStart, i - blockStart);
            i++; // skip closing brace

            // A nested rule is any unquoted '{' inside a non-at-rule block -- CSS nesting doesn't
            // require '&': "div { p { color: blue } }" nests implicitly as "div p { color: blue }",
            // same as "div { & p { ... } }". At-rule blocks (@media, @page, ...) are left untouched:
            // their own nested rule sets aren't CSS-nesting syntax and are handled elsewhere.
            if (IndexOfUnquoted(block, '{', 0) >= 0 && !selector.StartsWith("@"))
            {
                // Split block into own declarations and nested rules
                var (ownDecls, nestedRules) = SplitNestedBlock(block, selector);

                // Emit own declarations
                if (!string.IsNullOrWhiteSpace(ownDecls))
                    result.AppendLine($"{selector} {{ {ownDecls} }}");

                // Emit expanded nested rules
                result.Append(nestedRules);
            }
            else
            {
                // No nesting — pass through
                result.AppendLine($"{selector} {{ {block} }}");
            }
        }

        return result.ToString();
    }

    private static (string ownDecls, string nestedRules) SplitNestedBlock(string block, string parentSelector)
    {
        var ownDecls = new System.Text.StringBuilder();
        var nested = new System.Text.StringBuilder();

        int i = 0;
        while (i < block.Length)
        {
            SkipWhitespace(block, ref i);
            if (i >= block.Length) break;

            // Check if this is a nested rule (contains an unquoted '{')
            int nextBrace = IndexOfUnquoted(block, '{', i);
            int nextSemicolon = IndexOfUnquoted(block, ';', i);

            if (nextBrace >= 0 && (nextSemicolon < 0 || nextBrace < nextSemicolon))
            {
                // Nested rule
                string nestedSelector = block.Substring(i, nextBrace - i).Trim();
                i = nextBrace + 1;

                int ruleStart = i;
                i = MatchingBraceEnd(block, i);
                string ruleBody = block.Substring(ruleStart, i - ruleStart);
                i++; // skip }

                string expanded = ExpandNestedSelector(parentSelector, nestedSelector);
                nested.AppendLine($"{expanded} {{ {ruleBody} }}");
            }
            else if (nextSemicolon >= 0)
            {
                // Own declaration
                ownDecls.Append(block, i, nextSemicolon - i + 1);
                ownDecls.Append(' ');
                i = nextSemicolon + 1;
            }
            else
            {
                break;
            }
        }

        return (ownDecls.ToString().Trim(), nested.ToString());
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    /// <summary>
    /// Cheap pre-check: true when some rule block contains another unquoted '{' before its closing
    /// '}' (brace depth reaches 2+). Covers both "&"-based and implicit nesting; a false positive
    /// from an ordinary at-rule like @media is harmless since the main pass leaves at-rule blocks
    /// untouched, and quoted braces (e.g. content: "{") are skipped so they can't trigger it.
    /// </summary>
    private static bool HasNestedBraces(string css)
    {
        int depth = 0;
        char? quote = null;
        foreach (char c in css)
        {
            if (quote.HasValue) { if (c == quote.Value) quote = null; continue; }
            if (IsQuote(c)) { quote = c; continue; }
            if (c == '{') { depth++; if (depth >= 2) return true; }
            else if (c == '}' && depth > 0) depth--;
        }
        return false;
    }

    private static bool IsQuote(char c) => c == '\'' || c == '"';

    /// <summary>
    /// The index of the first occurrence of <paramref name="target"/> at or after <paramref name="start"/> that is
    /// not inside a quoted string (so a literal brace or semicolon in a declaration value, e.g. <c>content: "{"</c>,
    /// is never mistaken for CSS syntax). Quote tracking is simple toggling with no escape-sequence handling,
    /// matching the rest of this pre-parser's simplifications.
    /// </summary>
    private static int IndexOfUnquoted(string s, char target, int start)
    {
        char? quote = null;
        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            if (quote.HasValue) { if (c == quote.Value) quote = null; continue; }
            if (IsQuote(c)) { quote = c; continue; }
            if (c == target) return i;
        }
        return -1;
    }

    /// <summary>
    /// Given <paramref name="s"/> positioned just after an opening '{' (i.e. brace depth 1), returns the index of
    /// its matching closing '}', skipping nested braces and any braces inside quoted strings. Returns
    /// <c>s.Length</c> (an unterminated block) if no matching brace is found, so this parser never throws.
    /// </summary>
    private static int MatchingBraceEnd(string s, int i)
    {
        int depth = 1;
        char? quote = null;
        while (i < s.Length && depth > 0)
        {
            char c = s[i];
            if (quote.HasValue) { if (c == quote.Value) quote = null; }
            else if (IsQuote(c)) quote = c;
            else if (c == '{') depth++;
            else if (c == '}') depth--;
            if (depth > 0) i++;
        }
        return i;
    }
}
