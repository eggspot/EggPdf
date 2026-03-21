using System;
using System.Linq;
using EggPdf.Html.Dom;

namespace EggPdf.Css.Selectors;

/// <summary>
/// Matches CSS selectors against DOM elements. Supports type, class, ID,
/// attribute, descendant, child combinators, and basic pseudo-classes.
/// </summary>
public static class SelectorMatcher
{
    /// <summary>Check if a selector matches an element.</summary>
    public static bool Matches(string selector, HtmlElement element)
    {
        if (string.IsNullOrWhiteSpace(selector)) return false;

        selector = selector.Trim();

        // Child combinator: A > B
        if (selector.Contains(" > "))
        {
            var parts = selector.Split(new[] { " > " }, 2, StringSplitOptions.None);
            var parentSelector = parts[0].Trim();
            var childSelector = parts[1].Trim();

            if (!MatchesSimple(childSelector, element)) return false;

            var parent = element.Parent as HtmlElement;
            return parent != null && Matches(parentSelector, parent);
        }

        // Descendant combinator: A B (space-separated)
        if (selector.Contains(' '))
        {
            int lastSpace = selector.LastIndexOf(' ');
            var ancestorSelector = selector.Substring(0, lastSpace).Trim();
            var descendantSelector = selector.Substring(lastSpace + 1).Trim();

            if (!MatchesSimple(descendantSelector, element)) return false;

            // Walk up ancestors
            var ancestor = element.Parent as HtmlElement;
            while (ancestor != null)
            {
                if (Matches(ancestorSelector, ancestor)) return true;
                ancestor = ancestor.Parent as HtmlElement;
            }
            return false;
        }

        return MatchesSimple(selector, element);
    }

    /// <summary>Match a simple/compound selector (no combinators).</summary>
    private static bool MatchesSimple(string selector, HtmlElement element)
    {
        if (string.IsNullOrEmpty(selector)) return false;

        // Parse compound selector into parts
        int i = 0;
        while (i < selector.Length)
        {
            if (selector[i] == '*')
            {
                i++;
                continue; // universal matches everything
            }

            if (selector[i] == '.')
            {
                // Class selector
                i++;
                var className = ConsumeIdent(selector, ref i);
                if (!element.ClassList.Contains(className, StringComparer.OrdinalIgnoreCase))
                    return false;
            }
            else if (selector[i] == '#')
            {
                // ID selector
                i++;
                var id = ConsumeIdent(selector, ref i);
                if (!string.Equals(element.Id, id, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else if (selector[i] == '[')
            {
                // Attribute selector
                i++;
                if (!MatchAttribute(selector, ref i, element))
                    return false;
            }
            else if (selector[i] == ':')
            {
                // Pseudo-class
                i++;
                if (i < selector.Length && selector[i] == ':')
                {
                    i++; // pseudo-element (::before etc) - skip for matching
                    ConsumeIdent(selector, ref i);
                }
                else
                {
                    var pseudo = ConsumeIdent(selector, ref i);
                    if (!MatchPseudoClass(pseudo, element))
                        return false;
                }
            }
            else if (char.IsLetterOrDigit(selector[i]) || selector[i] == '-' || selector[i] == '_')
            {
                // Type selector
                var tagName = ConsumeIdent(selector, ref i);
                if (!string.Equals(element.TagName, tagName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else
            {
                i++; // skip unknown char
            }
        }

        return true;
    }

    /// <summary>Calculate CSS specificity as (a, b, c).</summary>
    public static (int A, int B, int C) CalculateSpecificity(string selector)
    {
        int a = 0, b = 0, c = 0;

        // Split by combinators and process each part
        var parts = selector.Split(new[] { ' ', '>', '+', '~' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            int i = 0;
            while (i < part.Length)
            {
                if (part[i] == '#') { a++; i++; ConsumeIdent(part, ref i); }
                else if (part[i] == '.') { b++; i++; ConsumeIdent(part, ref i); }
                else if (part[i] == '[') { b++; SkipBracket(part, ref i); }
                else if (part[i] == ':') { b++; i++; if (i < part.Length && part[i] == ':') { i++; c++; b--; } ConsumeIdent(part, ref i); }
                else if (part[i] == '*') { i++; }
                else if (char.IsLetterOrDigit(part[i]) || part[i] == '-' || part[i] == '_') { c++; ConsumeIdent(part, ref i); }
                else { i++; }
            }
        }

        return (a, b, c);
    }

    private static bool MatchAttribute(string selector, ref int i, HtmlElement element)
    {
        var attrName = new System.Text.StringBuilder();
        while (i < selector.Length && selector[i] != ']' && selector[i] != '=')
        {
            attrName.Append(selector[i]);
            i++;
        }

        var name = attrName.ToString().Trim();

        if (i < selector.Length && selector[i] == '=')
        {
            i++; // skip =
            // Read value
            char quote = (i < selector.Length && (selector[i] == '\'' || selector[i] == '"')) ? selector[i] : '\0';
            if (quote != '\0') i++;

            var value = new System.Text.StringBuilder();
            while (i < selector.Length && selector[i] != ']' && (quote == '\0' || selector[i] != quote))
            {
                value.Append(selector[i]);
                i++;
            }
            if (quote != '\0' && i < selector.Length) i++; // skip closing quote
            if (i < selector.Length && selector[i] == ']') i++;

            var attrValue = element.GetAttribute(name);
            return string.Equals(attrValue, value.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        // Existence check
        if (i < selector.Length && selector[i] == ']') i++;
        return element.HasAttribute(name);
    }

    private static bool MatchPseudoClass(string pseudo, HtmlElement element)
    {
        switch (pseudo.ToLowerInvariant())
        {
            case "first-child":
                var parent = element.Parent;
                if (parent == null) return false;
                return parent.ChildNodes.OfType<HtmlElement>().FirstOrDefault() == element;

            case "last-child":
                var parent2 = element.Parent;
                if (parent2 == null) return false;
                return parent2.ChildNodes.OfType<HtmlElement>().LastOrDefault() == element;

            case "root":
                return element.TagName == "html";

            case "empty":
                return element.ChildNodes.Count == 0;

            case "link":
                return element.TagName == "a" && element.HasAttribute("href");

            default:
                return true; // Unknown pseudo-class: don't reject
        }
    }

    private static string ConsumeIdent(string s, ref int i)
    {
        var sb = new System.Text.StringBuilder();
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '-' || s[i] == '_'))
        {
            sb.Append(s[i]);
            i++;
        }
        return sb.ToString();
    }

    private static void SkipBracket(string s, ref int i)
    {
        while (i < s.Length && s[i] != ']') i++;
        if (i < s.Length) i++;
    }
}
