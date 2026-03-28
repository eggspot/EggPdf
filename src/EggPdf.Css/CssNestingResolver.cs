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
        if (string.IsNullOrEmpty(cssText) || cssText.IndexOf('&') < 0)
            return cssText;

        // Simple approach: find & selectors inside rule blocks and expand them
        // This is a pre-parse step that runs before the CSS parser
        var result = new System.Text.StringBuilder(cssText.Length);
        int i = 0;

        while (i < cssText.Length)
        {
            // Find next rule block
            int selectorStart = i;
            int braceOpen = cssText.IndexOf('{', i);
            if (braceOpen < 0)
            {
                result.Append(cssText, i, cssText.Length - i);
                break;
            }

            string selector = cssText.Substring(selectorStart, braceOpen - selectorStart).Trim();
            i = braceOpen + 1;

            // Find matching closing brace (handle nesting)
            int depth = 1;
            int blockStart = i;
            while (i < cssText.Length && depth > 0)
            {
                if (cssText[i] == '{') depth++;
                else if (cssText[i] == '}') depth--;
                if (depth > 0) i++;
            }

            string block = cssText.Substring(blockStart, i - blockStart);
            i++; // skip closing brace

            // Check if block contains nested rules (& selector)
            if (block.IndexOf('&') >= 0 && !selector.StartsWith("@"))
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

            // Check if this is a nested rule (contains { )
            int nextBrace = block.IndexOf('{', i);
            int nextSemicolon = block.IndexOf(';', i);

            if (nextBrace >= 0 && (nextSemicolon < 0 || nextBrace < nextSemicolon))
            {
                // Nested rule
                string nestedSelector = block.Substring(i, nextBrace - i).Trim();
                i = nextBrace + 1;

                int depth = 1;
                int ruleStart = i;
                while (i < block.Length && depth > 0)
                {
                    if (block[i] == '{') depth++;
                    else if (block[i] == '}') depth--;
                    if (depth > 0) i++;
                }
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
}
