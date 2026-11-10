using System;
using System.Text;
using EggPdf.Css.Selectors;

namespace EggPdf.Css;

/// <summary>
/// Resolves CSS nesting (Chrome 120+) by flattening nested rules, at any depth, before parsing.
/// "div { color: red; &amp; p { color: blue } }" becomes "div { color: red; }" and "div p { color: blue }".
/// The &amp; selector stands for the parent selector; a nested selector without &amp; is a descendant of it.
/// A nested @media, @supports, @layer, @container or @starting-style rule wraps the parent selector.
/// </summary>
public static class CssNestingResolver
{
    private const int MaxNestingDepth = 64;
    private static readonly string[] ConditionalGroupRules = { "@media", "@supports", "@layer", "@container", "@starting-style" };

    /// <summary>
    /// Expand a nested selector against its parent. Each &amp; is replaced with the parent selector; a nested selector
    /// without &amp; becomes a descendant of the parent. Selector lists on either side expand to every combination.
    /// </summary>
    public static string ExpandNestedSelector(string parentSelector, string nestedSelector)
    {
        var parents = SelectorMatcher.SplitSelectorList(parentSelector);
        var nestedSelectors = SelectorMatcher.SplitSelectorList(nestedSelector);
        var expanded = new StringBuilder();
        foreach (var nestedPart in nestedSelectors)
        {
            var nested = nestedPart.Trim();
            if (nested.Length == 0) continue;
            foreach (var parentPart in parents)
            {
                var parent = parentPart.Trim();
                if (parent.Length == 0) continue;
                if (expanded.Length > 0) expanded.Append(", ");
                if (IndexOfUnquoted(nested, '&') >= 0)
                    AppendWithNestingSelectorReplaced(expanded, nested, parent);
                else
                    expanded.Append(parent).Append(' ').Append(nested);
            }
        }
        return expanded.ToString();
    }

    /// <summary>
    /// Pre-process CSS text to flatten nesting before parsing. Text without nested blocks is returned unchanged;
    /// nesting deeper than the supported depth is passed through as written.
    /// </summary>
    public static string PreprocessNesting(string cssText)
    {
        if (string.IsNullOrEmpty(cssText) || !HasNestedBraces(cssText))
            return cssText;

        var output = new StringBuilder(cssText.Length);
        FlattenRuleList(cssText, 0, cssText.Length, output, 0);
        return output.ToString();
    }

    private readonly struct Item
    {
        public readonly int Start;
        public readonly int PreludeEnd;
        public readonly int BlockEnd;
        public readonly bool HasBlock;

        public Item(int start, int preludeEnd, int blockEnd, bool hasBlock)
        {
            Start = start;
            PreludeEnd = preludeEnd;
            BlockEnd = blockEnd;
            HasBlock = hasBlock;
        }

        public int BlockStart => PreludeEnd + 1;

        /// <summary>The text before the block: a rule's prelude, or the whole statement when there is no block.</summary>
        public string Text(string css) => css.Substring(Start, PreludeEnd - Start).Trim();
    }

    private static void FlattenRuleList(string css, int start, int end, StringBuilder output, int depth)
    {
        int i = start;
        while (TryReadItem(css, ref i, end, out var item))
        {
            if (!item.HasBlock)
            {
                var statement = item.Text(css);
                if (statement.Length > 0) output.Append(statement).Append('\n');
                continue;
            }

            var prelude = item.Text(css);
            if (prelude.Length > 0 && prelude[0] == '@')
            {
                if (depth < MaxNestingDepth && IsConditionalGroupRule(prelude))
                {
                    output.Append(prelude).Append(" {\n");
                    FlattenRuleList(css, item.BlockStart, item.BlockEnd, output, depth + 1);
                    output.Append("}\n");
                }
                else
                {
                    AppendRule(output, prelude, css, item);
                }
                continue;
            }

            FlattenStyleRule(prelude, css, item, output, depth + 1);
        }
    }

    /// <summary>
    /// Emits a style rule's declarations under its selector and its nested rules under their expanded selectors, in
    /// source order: declarations after a nested rule start a new rule for the same selector.
    /// </summary>
    private static void FlattenStyleRule(string selector, string css, Item rule, StringBuilder output, int depth)
    {
        if (depth > MaxNestingDepth || !HasTopLevelBlock(css, rule.BlockStart, rule.BlockEnd))
        {
            AppendRule(output, selector, css, rule);
            return;
        }

        var declarations = new StringBuilder();
        int i = rule.BlockStart;
        while (TryReadItem(css, ref i, rule.BlockEnd, out var item))
        {
            if (!item.HasBlock)
            {
                var declaration = item.Text(css);
                if (declaration.Length == 0) continue;
                declarations.Append(declaration);
                if (declaration[declaration.Length - 1] != ';') declarations.Append(';');
                declarations.Append(' ');
                continue;
            }

            FlushDeclarations(selector, declarations, output);
            var prelude = item.Text(css);
            if (prelude.Length > 0 && prelude[0] == '@')
            {
                if (IsConditionalGroupRule(prelude))
                {
                    output.Append(prelude).Append(" {\n");
                    FlattenStyleRule(selector, css, item, output, depth + 1);
                    output.Append("}\n");
                }
                continue;
            }

            FlattenStyleRule(ExpandNestedSelector(selector, prelude), css, item, output, depth + 1);
        }
        FlushDeclarations(selector, declarations, output);
    }

    private static void FlushDeclarations(string selector, StringBuilder declarations, StringBuilder output)
    {
        if (declarations.Length == 0) return;
        output.Append(selector).Append(" { ").Append(declarations).Append("}\n");
        declarations.Clear();
    }

    private static void AppendRule(StringBuilder output, string prelude, string css, Item item)
    {
        output.Append(prelude).Append(" {");
        output.Append(css, item.BlockStart, item.BlockEnd - item.BlockStart);
        output.Append("}\n");
    }

    private static bool IsConditionalGroupRule(string prelude)
    {
        foreach (var name in ConditionalGroupRules)
        {
            if (!prelude.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (prelude.Length == name.Length) return true;
            char next = prelude[name.Length];
            if (char.IsWhiteSpace(next) || next == '(') return true;
        }
        return false;
    }

    /// <summary>
    /// Reads the next statement (text up to a top-level ';') or rule (prelude plus its {} block) in
    /// [<paramref name="i"/>, <paramref name="end"/>).
    /// </summary>
    private static bool TryReadItem(string css, ref int i, int end, out Item item)
    {
        while (i < end && char.IsWhiteSpace(css[i])) i++;
        if (i >= end)
        {
            item = default;
            return false;
        }

        int start = i;
        int stop = FindTopLevel(css, i, end, stopAtSemicolon: true);
        if (stop < 0)
        {
            item = new Item(start, end, end, hasBlock: false);
            i = end;
            return true;
        }

        if (css[stop] != '{')
        {
            item = new Item(start, css[stop] == ';' ? stop + 1 : stop, stop, hasBlock: false);
            i = stop + 1;
            return true;
        }

        int blockEnd = MatchingBraceEnd(css, stop + 1, end);
        item = new Item(start, stop, blockEnd, hasBlock: true);
        i = Math.Min(blockEnd + 1, end);
        return true;
    }

    private static bool HasTopLevelBlock(string css, int start, int end)
    {
        int stop = FindTopLevel(css, start, end, stopAtSemicolon: false);
        return stop >= 0 && css[stop] == '{';
    }

    /// <summary>
    /// The first '{' or '}' (and ';' when <paramref name="stopAtSemicolon"/>) outside comments, quoted strings and
    /// parentheses -- so <c>url(data:image/png;base64,...)</c> and <c>content: "{"</c> are not mistaken for syntax.
    /// </summary>
    private static int FindTopLevel(string css, int start, int end, bool stopAtSemicolon)
    {
        int parens = 0;
        for (int i = start; i < end; i++)
        {
            char c = css[i];
            if (TrySkipCommentOrString(css, ref i, end)) continue;
            if (c == '(') parens++;
            else if (c == ')') { if (parens > 0) parens--; }
            else if (parens == 0 && (c == '{' || c == '}' || (stopAtSemicolon && c == ';'))) return i;
        }
        return -1;
    }

    /// <summary>Given <paramref name="start"/> just after an opening '{', the index of its matching '}', or <paramref name="end"/>.</summary>
    private static int MatchingBraceEnd(string css, int start, int end)
    {
        int depth = 1;
        for (int i = start; i < end; i++)
        {
            if (TrySkipCommentOrString(css, ref i, end)) continue;
            char c = css[i];
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }
        return end;
    }

    /// <summary>Cheap pre-check: true when some block contains another block (brace depth reaches 2).</summary>
    private static bool HasNestedBraces(string css)
    {
        int depth = 0;
        for (int i = 0; i < css.Length; i++)
        {
            if (TrySkipCommentOrString(css, ref i, css.Length)) continue;
            char c = css[i];
            if (c == '{') { if (++depth >= 2) return true; }
            else if (c == '}' && depth > 0) depth--;
        }
        return false;
    }

    /// <summary>
    /// When a comment or quoted string starts at <paramref name="i"/>, moves <paramref name="i"/> to its last character
    /// and returns true. Backslash escapes inside strings are honoured; an unterminated one runs to <paramref name="end"/>.
    /// </summary>
    private static bool TrySkipCommentOrString(string css, ref int i, int end)
    {
        char c = css[i];
        if (c == '/' && i + 1 < end && css[i + 1] == '*')
        {
            int close = css.IndexOf("*/", i + 2, end - i - 2, StringComparison.Ordinal);
            i = close < 0 ? end - 1 : close + 1;
            return true;
        }
        if (c != '"' && c != '\'') return false;

        for (int j = i + 1; j < end; j++)
        {
            if (css[j] == '\\') { j++; continue; }
            if (css[j] == c || css[j] == '\n')
            {
                i = j;
                return true;
            }
        }
        i = end - 1;
        return true;
    }

    private static int IndexOfUnquoted(string s, char target)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (TrySkipCommentOrString(s, ref i, s.Length)) continue;
            if (s[i] == target) return i;
        }
        return -1;
    }

    private static void AppendWithNestingSelectorReplaced(StringBuilder output, string nested, string parent)
    {
        for (int i = 0; i < nested.Length; i++)
        {
            int segmentStart = i;
            if (TrySkipCommentOrString(nested, ref i, nested.Length))
            {
                output.Append(nested, segmentStart, i - segmentStart + 1);
                continue;
            }
            if (nested[i] == '&') output.Append(parent);
            else output.Append(nested[i]);
        }
    }
}
