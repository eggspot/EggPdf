using System;
using System.Collections.Generic;
using System.Text;
using EggPdf.Html.Dom;

namespace EggPdf.Css.Selectors;

public static partial class SelectorMatcher
{
    private static readonly HashSet<string> CaseInsensitiveHtmlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "accept", "accept-charset", "align", "alink", "axis", "bgcolor", "charset", "checked", "clear",
        "codetype", "color", "compact", "declare", "defer", "dir", "direction", "disabled", "enctype",
        "face", "frame", "hreflang", "http-equiv", "lang", "language", "link", "media", "method",
        "multiple", "nohref", "noresize", "noshade", "nowrap", "readonly", "rel", "rev", "rules",
        "scope", "scrolling", "selected", "shape", "target", "text", "type", "valign", "valuetype", "vlink"
    };

    internal static int FindAttributeSelectorEnd(string s, int openBracket)
    {
        int i = openBracket + 1;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == ']') return i;
            if (c == '\\') i += 2;
            else if (c == '"' || c == '\'') i = SkipQuotedString(s, i);
            else i++;
        }
        return s.Length;
    }

    internal static int RightmostCompoundStart(string selector)
    {
        int start = 0;
        int i = 0;
        while (i < selector.Length)
        {
            char c = selector[i];
            if (c == '\\')
            {
                i = CssStringEscapes.SkipEscape(selector, i);
            }
            else if (c == '[')
            {
                i = AfterAttributeSelector(selector, i);
            }
            else if (c == '(')
            {
                int closeParen = FindMatchingParen(selector, i + 1);
                i = closeParen < selector.Length ? closeParen + 1 : selector.Length;
            }
            else
            {
                if (c == ' ' || c == '>' || c == '+' || c == '~') start = i + 1;
                i++;
            }
        }
        return start;
    }

    private static int AfterAttributeSelector(string s, int openBracket)
    {
        int end = FindAttributeSelectorEnd(s, openBracket);
        return end < s.Length ? end + 1 : end;
    }

    private static int SkipQuotedString(string s, int openQuote)
    {
        char quote = s[openQuote];
        int i = openQuote + 1;
        while (i < s.Length)
        {
            if (s[i] == '\\') i += 2;
            else if (s[i] == quote) return i + 1;
            else i++;
        }
        return s.Length;
    }

    private static bool MatchAttribute(string selector, ref int i, HtmlElement element)
    {
        int close = FindAttributeSelectorEnd(selector, i - 1);
        bool matched = MatchAttributeSelector(selector, i, close, element);
        i = close < selector.Length ? close + 1 : close;
        return matched;
    }

    private static bool MatchAttributeSelector(string s, int start, int close, HtmlElement element)
    {
        int p = SkipSelectorWhitespace(s, start, close);
        int nameStart = p;
        while (p < close && IsAttributeNameChar(s[p])) p++;
        if (p == nameStart) return false;
        var name = s.Substring(nameStart, p - nameStart);

        p = SkipSelectorWhitespace(s, p, close);
        if (p >= close) return element.HasAttribute(name);

        char operatorKind = s[p];
        if (operatorKind == '=')
        {
            p++;
        }
        else if (IsAttributeOperatorPrefix(operatorKind) && p + 1 < close && s[p + 1] == '=')
        {
            p += 2;
        }
        else
        {
            return false;
        }

        p = SkipSelectorWhitespace(s, p, close);
        var expected = ReadAttributeSelectorValue(s, ref p, close);

        p = SkipSelectorWhitespace(s, p, close);
        bool ignoreCase = CaseInsensitiveHtmlAttributes.Contains(name);
        if (p < close)
        {
            char flag = s[p];
            if (flag == 'i' || flag == 'I') ignoreCase = true;
            else if (flag == 's' || flag == 'S') ignoreCase = false;
            else return false;
            if (SkipSelectorWhitespace(s, p + 1, close) < close) return false;
        }

        var actual = element.GetAttribute(name);
        return actual != null &&
            AttributeValueMatches(operatorKind, actual, expected, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string ReadAttributeSelectorValue(string s, ref int p, int close)
    {
        if (p < close && (s[p] == '"' || s[p] == '\''))
            return CssStringEscapes.ReadQuoted(s, ref p);

        var value = new StringBuilder();
        while (p < close && !IsSelectorWhitespace(s[p]))
        {
            if (s[p] == '\\')
            {
                p++;
                CssStringEscapes.AppendEscape(s, ref p, value);
            }
            else
            {
                value.Append(s[p]);
                p++;
            }
        }
        return value.ToString();
    }

    private static bool AttributeValueMatches(char operatorKind, string actual, string expected, StringComparison comparison)
    {
        switch (operatorKind)
        {
            case '=':
                return string.Equals(actual, expected, comparison);
            case '~':
                return ContainsWhitespaceSeparatedWord(actual, expected, comparison);
            case '|':
                return string.Equals(actual, expected, comparison) ||
                    (actual.Length > expected.Length && actual[expected.Length] == '-' &&
                     string.Compare(actual, 0, expected, 0, expected.Length, comparison) == 0);
            case '^':
                return expected.Length > 0 && actual.StartsWith(expected, comparison);
            case '$':
                return expected.Length > 0 && actual.EndsWith(expected, comparison);
            case '*':
                return expected.Length > 0 && actual.IndexOf(expected, comparison) >= 0;
            default:
                return false;
        }
    }

    private static bool ContainsWhitespaceSeparatedWord(string list, string word, StringComparison comparison)
    {
        if (word.Length == 0) return false;
        for (int i = 0; i < word.Length; i++)
            if (IsSelectorWhitespace(word[i])) return false;

        int wordStart = 0;
        for (int i = 0; i <= list.Length; i++)
        {
            if (i < list.Length && !IsSelectorWhitespace(list[i])) continue;
            if (i - wordStart == word.Length && string.Compare(list, wordStart, word, 0, word.Length, comparison) == 0)
                return true;
            wordStart = i + 1;
        }
        return false;
    }

    private static bool IsAttributeOperatorPrefix(char c) => c == '~' || c == '|' || c == '^' || c == '$' || c == '*';

    private static bool IsAttributeNameChar(char c) =>
        !IsSelectorWhitespace(c) && c != '=' && c != ']' && c != '"' && c != '\'' && !IsAttributeOperatorPrefix(c);

    private static bool IsSelectorWhitespace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';

    private static int SkipSelectorWhitespace(string s, int p, int limit)
    {
        while (p < limit && IsSelectorWhitespace(s[p])) p++;
        return p;
    }
}
