using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EggPdf.Html.Dom;

namespace EggPdf.Css.Selectors;

/// <summary>
/// Matches CSS selectors against DOM elements. Supports type, class, ID,
/// attribute, descendant, child, adjacent sibling (+), general sibling (~)
/// combinators, and pseudo-classes including :not(), :nth-child(), :nth-of-type(),
/// :only-child, :only-of-type, :is(), :where().
/// </summary>
public static class SelectorMatcher
{
    /// <summary>Check if a selector matches an element.</summary>
    public static bool Matches(string selector, HtmlElement element)
    {
        if (string.IsNullOrWhiteSpace(selector)) return false;

        selector = selector.Trim();

        // Fast path: no comma means single selector — skip List<string> allocation entirely.
        if (selector.IndexOf(',') < 0)
        {
            if (HasPseudoElement(selector)) return false;
            return MatchesComplex(selector, element);
        }

        // Split selector list (comma-separated) - any match succeeds
        var selectorList = SplitSelectorList(selector);
        for (int s = 0; s < selectorList.Count; s++)
        {
            var sel = selectorList[s].Trim();

            // Pseudo-element selectors (::before, ::after) don't match elements directly
            if (HasPseudoElement(sel))
                continue;

            if (MatchesComplex(sel, element))
                return true;
        }
        return false;
    }

    /// <summary>Check if a selector contains a pseudo-element (::before, ::after, etc.).</summary>
    internal static bool HasPseudoElement(string selector)
    {
        int idx = selector.IndexOf("::", StringComparison.Ordinal);
        if (idx < 0) return false;

        int identStart = idx + 2;
        if (identStart >= selector.Length) return false;

        var sb = new System.Text.StringBuilder();
        int pos = identStart;
        while (pos < selector.Length && (char.IsLetterOrDigit(selector[pos]) || selector[pos] == '-'))
        {
            sb.Append(selector[pos]);
            pos++;
        }
        var pseudo = sb.ToString().ToLowerInvariant();
        return pseudo == "before" || pseudo == "after" || pseudo == "first-line" || pseudo == "first-letter" || pseudo == "marker";
    }

    /// <summary>Match a single complex selector (with combinators).</summary>
    private static bool MatchesComplex(string selector, HtmlElement element)
    {
        if (string.IsNullOrWhiteSpace(selector)) return false;

        // Tokenize into compound selectors + combinators
        // We process right-to-left: last compound must match the element,
        // then work backwards through combinators
        var parts = TokenizeCombinators(selector);
        if (parts.Count == 0) return false;

        // The rightmost compound must match the element
        if (!MatchesSimple(parts[parts.Count - 1].Compound, element))
            return false;

        // Walk backwards through combinators
        var current = element;
        for (int i = parts.Count - 2; i >= 0; i--)
        {
            var part = parts[i];
            char combinator = parts[i + 1].Combinator;

            switch (combinator)
            {
                case '>': // child combinator
                {
                    var parent = current.Parent as HtmlElement;
                    if (parent == null || !MatchesSimple(part.Compound, parent))
                        return false;
                    current = parent;
                    break;
                }
                case '+': // adjacent sibling combinator
                {
                    var prevSibling = GetPreviousElementSibling(current);
                    if (prevSibling == null || !MatchesSimple(part.Compound, prevSibling))
                        return false;
                    current = prevSibling;
                    break;
                }
                case '~': // general sibling combinator
                {
                    var sibling = GetPreviousElementSibling(current);
                    bool found = false;
                    while (sibling != null)
                    {
                        if (MatchesSimple(part.Compound, sibling))
                        {
                            current = sibling;
                            found = true;
                            break;
                        }
                        sibling = GetPreviousElementSibling(sibling);
                    }
                    if (!found) return false;
                    break;
                }
                case ' ': // descendant combinator
                default:
                {
                    var ancestor = current.Parent as HtmlElement;
                    bool found = false;
                    while (ancestor != null)
                    {
                        if (MatchesSimple(part.Compound, ancestor))
                        {
                            current = ancestor;
                            found = true;
                            break;
                        }
                        ancestor = ancestor.Parent as HtmlElement;
                    }
                    if (!found) return false;
                    break;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Tokenize a complex selector into compound selectors and their preceding combinators.
    /// Returns list of (compound, combinator) where combinator is the combinator BEFORE this compound.
    /// The first entry has combinator = '\0' (no preceding combinator).
    /// </summary>
    private static List<SelectorPart> TokenizeCombinators(string selector)
    {
        var parts = new List<SelectorPart>();
        int len = selector.Length;
        int i = 0;

        // Skip leading whitespace
        while (i < len && selector[i] == ' ') i++;

        int compoundStart = i;
        char pendingCombinator = '\0';

        while (i < len)
        {
            // Skip inside brackets and parentheses
            if (selector[i] == '[')
            {
                i++;
                while (i < len && selector[i] != ']') i++;
                if (i < len) i++;
                continue;
            }
            if (selector[i] == '(')
            {
                int depth = 1;
                i++;
                while (i < len && depth > 0)
                {
                    if (selector[i] == '(') depth++;
                    else if (selector[i] == ')') depth--;
                    i++;
                }
                continue;
            }

            // Check for combinators
            if (selector[i] == '>' || selector[i] == '+' || selector[i] == '~')
            {
                // Emit current compound
                string compound = selector.Substring(compoundStart, i - compoundStart).Trim();
                if (!string.IsNullOrEmpty(compound))
                    parts.Add(new SelectorPart(compound, pendingCombinator));

                pendingCombinator = selector[i];
                i++;

                // Skip whitespace after combinator
                while (i < len && selector[i] == ' ') i++;
                compoundStart = i;
                continue;
            }

            if (selector[i] == ' ')
            {
                // Could be descendant combinator or just whitespace around >, +, ~
                int spaceStart = i;
                while (i < len && selector[i] == ' ') i++;

                if (i < len && (selector[i] == '>' || selector[i] == '+' || selector[i] == '~'))
                {
                    // The space was before an explicit combinator - just continue
                    continue;
                }

                // This is a descendant combinator
                string compound = selector.Substring(compoundStart, spaceStart - compoundStart).Trim();
                if (!string.IsNullOrEmpty(compound))
                    parts.Add(new SelectorPart(compound, pendingCombinator));

                pendingCombinator = ' ';
                compoundStart = i;
                continue;
            }

            i++;
        }

        // Emit final compound
        string lastCompound = selector.Substring(compoundStart).Trim();
        if (!string.IsNullOrEmpty(lastCompound))
            parts.Add(new SelectorPart(lastCompound, pendingCombinator));

        return parts;
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
                if (!ContainsIgnoreCase(element.ClassList, className))
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
                // Pseudo-class or pseudo-element
                i++;
                if (i < selector.Length && selector[i] == ':')
                {
                    i++; // pseudo-element (::before etc) - skip for matching
                    ConsumeIdent(selector, ref i);
                }
                else
                {
                    if (!MatchPseudoClassAtPos(selector, ref i, element))
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

        for (int p = 0; p < parts.Length; p++)
        {
            var part = parts[p];
            int i = 0;
            while (i < part.Length)
            {
                if (part[i] == '#') { a++; i++; ConsumeIdent(part, ref i); }
                else if (part[i] == '.') { b++; i++; ConsumeIdent(part, ref i); }
                else if (part[i] == '[') { b++; SkipBracket(part, ref i); }
                else if (part[i] == ':')
                {
                    i++;
                    if (i < part.Length && part[i] == ':')
                    {
                        // Pseudo-element: contributes to c
                        i++;
                        c++;
                        ConsumeIdent(part, ref i);
                    }
                    else
                    {
                        var pseudo = ConsumeIdent(part, ref i);
                        if (string.Equals(pseudo, "where", StringComparison.OrdinalIgnoreCase))
                        {
                            // :where() has zero specificity
                            if (i < part.Length && part[i] == '(')
                                SkipParenthesis(part, ref i);
                        }
                        else if (string.Equals(pseudo, "not", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(pseudo, "is", StringComparison.OrdinalIgnoreCase))
                        {
                            // :not() and :is() take specificity of their argument
                            if (i < part.Length && part[i] == '(')
                            {
                                i++; // skip (
                                var inner = ConsumeUntilCloseParen(part, ref i);
                                var innerSpec = CalculateSpecificity(inner);
                                a += innerSpec.A;
                                b += innerSpec.B;
                                c += innerSpec.C;
                            }
                        }
                        else if (string.Equals(pseudo, "nth-child", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(pseudo, "nth-last-child", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(pseudo, "nth-of-type", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(pseudo, "nth-last-of-type", StringComparison.OrdinalIgnoreCase))
                        {
                            b++;
                            if (i < part.Length && part[i] == '(')
                                SkipParenthesis(part, ref i);
                        }
                        else
                        {
                            b++;
                        }
                    }
                }
                else if (part[i] == '*') { i++; }
                else if (char.IsLetterOrDigit(part[i]) || part[i] == '-' || part[i] == '_') { c++; ConsumeIdent(part, ref i); }
                else { i++; }
            }
        }

        return (a, b, c);
    }

    /// <summary>
    /// Match a pseudo-class at the current position in the selector string.
    /// Handles functional pseudo-classes like :not(...), :nth-child(...).
    /// </summary>
    private static bool MatchPseudoClassAtPos(string selector, ref int i, HtmlElement element)
    {
        var pseudo = ConsumeIdent(selector, ref i);

        // Check for functional pseudo-class with parenthesis
        if (i < selector.Length && selector[i] == '(')
        {
            i++; // skip (
            var arg = ConsumeUntilCloseParen(selector, ref i);

            return MatchFunctionalPseudo(pseudo, arg, element);
        }

        return MatchPseudoClass(pseudo, element);
    }

    private static bool MatchFunctionalPseudo(string pseudo, string arg, HtmlElement element)
    {
        switch (pseudo.ToLowerInvariant())
        {
            case "not":
                return !Matches(arg.Trim(), element);

            case "is":
            case "matches":
            case "where":
                return Matches(arg.Trim(), element);

            case "nth-child":
                return MatchNthChild(arg.Trim(), element, fromEnd: false);

            case "nth-last-child":
                return MatchNthChild(arg.Trim(), element, fromEnd: true);

            case "nth-of-type":
                return MatchNthOfType(arg.Trim(), element, fromEnd: false);

            case "nth-last-of-type":
                return MatchNthOfType(arg.Trim(), element, fromEnd: true);

            case "lang":
            {
                // Match element or ancestor with matching lang attribute
                var langArg = arg.Trim().Trim('\'', '"');
                HtmlElement? cur = element;
                while (cur != null)
                {
                    var lang = cur.GetAttribute("lang");
                    if (!string.IsNullOrEmpty(lang) &&
                        (string.Equals(lang, langArg, StringComparison.OrdinalIgnoreCase) ||
                         lang.StartsWith(langArg + "-", StringComparison.OrdinalIgnoreCase)))
                        return true;
                    cur = cur.Parent as HtmlElement;
                }
                return false;
            }

            case "has":
                return MatchesHas(element, arg.Trim());

            default:
                return true; // Unknown functional pseudo: don't reject
        }
    }

    private static bool MatchesHas(HtmlElement element, string arg)
    {
        // Support comma-separated list: :has(a, b) = :has(a) OR :has(b)
        var branches = SplitTopLevelCommas(arg);
        foreach (var branch in branches)
        {
            if (MatchesHasBranch(element, branch.Trim()))
                return true;
        }
        return false;
    }

    private static bool MatchesHasBranch(HtmlElement element, string selector)
    {
        var s = selector.TrimStart();
        // Direct child combinator: :has(> sel)
        if (s.Length > 0 && s[0] == '>')
        {
            var childSel = s.Substring(1).Trim();
            foreach (var child in element.ChildNodes)
            {
                if (child is HtmlElement ce && Matches(childSel, ce))
                    return true;
            }
            return false;
        }
        // Adjacent sibling: :has(+ sel)
        if (s.Length > 0 && s[0] == '+')
        {
            var sibSel = s.Substring(1).Trim();
            var parent = element.Parent as HtmlElement;
            if (parent == null) return false;
            bool foundSelf = false;
            foreach (var child in parent.ChildNodes)
            {
                if (child is HtmlElement ce)
                {
                    if (foundSelf)
                        return Matches(sibSel, ce);
                    if (ce == element)
                        foundSelf = true;
                }
            }
            return false;
        }
        // General sibling: :has(~ sel)
        if (s.Length > 0 && s[0] == '~')
        {
            var sibSel = s.Substring(1).Trim();
            var parent = element.Parent as HtmlElement;
            if (parent == null) return false;
            bool foundSelf = false;
            foreach (var child in parent.ChildNodes)
            {
                if (child is HtmlElement ce)
                {
                    if (foundSelf && Matches(sibSel, ce))
                        return true;
                    if (ce == element)
                        foundSelf = true;
                }
            }
            return false;
        }
        // Default: descendant
        return HasDescendantMatching(element, s);
    }

    private static List<string> SplitTopLevelCommas(string value)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(value.Substring(start, i - start));
                start = i + 1;
            }
        }
        result.Add(value.Substring(start));
        return result;
    }

    private static bool HasDescendantMatching(HtmlElement element, string selector)
    {
        foreach (var child in element.ChildNodes)
        {
            if (child is HtmlElement ce)
            {
                if (Matches(selector, ce)) return true;
                if (HasDescendantMatching(ce, selector)) return true;
            }
        }
        return false;
    }

    private static bool IsEditableElement(HtmlElement element)
    {
        var tag = element.TagName;
        if (tag == "textarea") return true;
        if (tag == "input")
        {
            var type = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
            return type != "hidden" && type != "submit" && type != "button" &&
                   type != "reset" && type != "image" && type != "checkbox" && type != "radio";
        }
        return false;
    }

    private static bool MatchPseudoClass(string pseudo, HtmlElement element)
    {
        switch (pseudo.ToLowerInvariant())
        {
            case "first-child":
            {
                var parent = element.Parent;
                if (parent == null) return false;
                for (int c = 0; c < parent.ChildNodes.Count; c++)
                {
                    if (parent.ChildNodes[c] is HtmlElement e)
                        return e == element;
                }
                return false;
            }

            case "last-child":
            {
                var parent = element.Parent;
                if (parent == null) return false;
                for (int c = parent.ChildNodes.Count - 1; c >= 0; c--)
                {
                    if (parent.ChildNodes[c] is HtmlElement e)
                        return e == element;
                }
                return false;
            }

            case "first-of-type":
            {
                var parent = element.Parent;
                if (parent == null) return false;
                for (int c = 0; c < parent.ChildNodes.Count; c++)
                {
                    if (parent.ChildNodes[c] is HtmlElement e && e.TagName == element.TagName)
                        return e == element;
                }
                return false;
            }

            case "last-of-type":
            {
                var parent = element.Parent;
                if (parent == null) return false;
                for (int c = parent.ChildNodes.Count - 1; c >= 0; c--)
                {
                    if (parent.ChildNodes[c] is HtmlElement e && e.TagName == element.TagName)
                        return e == element;
                }
                return false;
            }

            case "only-child":
            {
                var parent = element.Parent;
                if (parent == null) return false;
                int elementCount = 0;
                for (int c = 0; c < parent.ChildNodes.Count; c++)
                {
                    if (parent.ChildNodes[c] is HtmlElement)
                    {
                        elementCount++;
                        if (elementCount > 1) return false;
                    }
                }
                return elementCount == 1;
            }

            case "only-of-type":
            {
                var parent = element.Parent;
                if (parent == null) return false;
                int typeCount = 0;
                for (int c = 0; c < parent.ChildNodes.Count; c++)
                {
                    if (parent.ChildNodes[c] is HtmlElement e && e.TagName == element.TagName)
                    {
                        typeCount++;
                        if (typeCount > 1) return false;
                    }
                }
                return typeCount == 1;
            }

            case "root":
                return element.TagName == "html";

            case "empty":
                return element.ChildNodes.Count == 0;

            case "link":
                return element.TagName == "a" && element.HasAttribute("href");

            case "enabled":
                return !element.HasAttribute("disabled");

            case "disabled":
                return element.HasAttribute("disabled");

            case "checked":
                return element.HasAttribute("checked") || element.HasAttribute("selected");

            case "indeterminate":
                return element.HasAttribute("indeterminate");

            case "required":
                return element.HasAttribute("required");

            case "optional":
                return !element.HasAttribute("required");

            case "read-only":
                return element.HasAttribute("readonly") || element.HasAttribute("disabled");

            case "read-write":
                return !element.HasAttribute("readonly") && !element.HasAttribute("disabled") &&
                       IsEditableElement(element);

            case "placeholder-shown":
                return element.HasAttribute("placeholder") && string.IsNullOrEmpty(element.GetAttribute("value"));

            case "default":
                return element.HasAttribute("checked") || element.HasAttribute("selected") ||
                       element.HasAttribute("default");

            case "hover":
            case "active":
            case "focus":
            case "focus-within":
            case "focus-visible":
            case "visited":
                return false; // Interactive pseudo-classes never match in print

            case "target":
                return false; // URL fragment not available during PDF render

            default:
                return true; // Unknown pseudo-class: don't reject
        }
    }

    /// <summary>Match :nth-child(An+B) or :nth-last-child(An+B).</summary>
    private static bool MatchNthChild(string arg, HtmlElement element, bool fromEnd)
    {
        var parent = element.Parent;
        if (parent == null) return false;

        // CSS4: :nth-child(An+B of <selector>)
        int ofIdx = -1;
        // Find " of " (case-insensitive) at top level
        for (int i = 0; i <= arg.Length - 4; i++)
        {
            if ((arg[i] == ' ' || i == 0) &&
                (i + 4 <= arg.Length) &&
                string.Compare(arg, i, " of ", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                ofIdx = i;
                break;
            }
        }

        if (ofIdx >= 0)
        {
            var formula = arg.Substring(0, ofIdx).Trim();
            var filterSelector = arg.Substring(ofIdx + 4).Trim();

            // First check: element must match the filter selector
            if (!Matches(filterSelector, element)) return false;

            // Count matching siblings (those that match filterSelector)
            int index = 0;
            bool found = false;
            if (fromEnd)
            {
                for (int i = parent.ChildNodes.Count - 1; i >= 0; i--)
                {
                    if (parent.ChildNodes[i] is HtmlElement e && Matches(filterSelector, e))
                    {
                        if (e == element) { found = true; break; }
                        index++;
                    }
                }
            }
            else
            {
                for (int i = 0; i < parent.ChildNodes.Count; i++)
                {
                    if (parent.ChildNodes[i] is HtmlElement e && Matches(filterSelector, e))
                    {
                        if (e == element) { found = true; break; }
                        index++;
                    }
                }
            }
            if (!found) return false;
            return MatchNthFormula(formula, index + 1);
        }

        int simpleIndex = GetElementIndex(parent, element, fromEnd);
        if (simpleIndex < 0) return false;

        return MatchNthFormula(arg, simpleIndex + 1); // 1-based index
    }

    /// <summary>Match :nth-of-type(An+B) or :nth-last-of-type(An+B).</summary>
    private static bool MatchNthOfType(string arg, HtmlElement element, bool fromEnd)
    {
        var parent = element.Parent;
        if (parent == null) return false;

        int index = GetElementOfTypeIndex(parent, element, fromEnd);
        if (index < 0) return false;

        return MatchNthFormula(arg, index + 1); // 1-based index
    }

    /// <summary>Check if a 1-based index matches an An+B formula.</summary>
    private static bool MatchNthFormula(string arg, int index)
    {
        arg = arg.Trim().ToLowerInvariant();

        if (arg == "odd") return index % 2 == 1;
        if (arg == "even") return index % 2 == 0;

        // Parse An+B formula
        int a = 0, b = 0;

        if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int simpleN))
        {
            // Just a number: :nth-child(3)
            return index == simpleN;
        }

        // Parse "An+B" or "An-B" or "n+B" or "-n+B" etc.
        int nPos = arg.IndexOf('n');
        if (nPos >= 0)
        {
            // Part before 'n' is A
            string aPart = arg.Substring(0, nPos).Trim();
            if (string.IsNullOrEmpty(aPart) || aPart == "+") a = 1;
            else if (aPart == "-") a = -1;
            else if (int.TryParse(aPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int aVal)) a = aVal;

            // Part after 'n' is +B or -B — skip spaces without allocating via Replace().
            string bPart = arg.Substring(nPos + 1).Trim();
            if (!string.IsNullOrEmpty(bPart))
            {
                int bStart = 0;
                while (bStart < bPart.Length && bPart[bStart] == ' ') bStart++;
                // Collect sign then digits, skipping any interior spaces
                int bSign = 1;
                if (bStart < bPart.Length && (bPart[bStart] == '+' || bPart[bStart] == '-'))
                {
                    if (bPart[bStart] == '-') bSign = -1;
                    bStart++;
                }
                while (bStart < bPart.Length && bPart[bStart] == ' ') bStart++;
                if (bStart < bPart.Length &&
                    int.TryParse(bPart.Substring(bStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out int bAbs))
                    b = bSign * bAbs;
            }
        }

        if (a == 0) return index == b;

        // Check if (index - b) is a non-negative multiple of a
        int diff = index - b;
        if (diff == 0) return true;
        if (a > 0) return diff > 0 && diff % a == 0;
        // a < 0
        return diff < 0 && (-diff) % (-a) == 0;
    }

    /// <summary>Get the 0-based index of an element among its element siblings.</summary>
    private static int GetElementIndex(HtmlNode parent, HtmlElement element, bool fromEnd)
    {
        if (fromEnd)
        {
            int index = 0;
            for (int i = parent.ChildNodes.Count - 1; i >= 0; i--)
            {
                if (parent.ChildNodes[i] is HtmlElement e)
                {
                    if (e == element) return index;
                    index++;
                }
            }
        }
        else
        {
            int index = 0;
            for (int i = 0; i < parent.ChildNodes.Count; i++)
            {
                if (parent.ChildNodes[i] is HtmlElement e)
                {
                    if (e == element) return index;
                    index++;
                }
            }
        }
        return -1;
    }

    /// <summary>Get the 0-based index of an element among siblings of the same type.</summary>
    private static int GetElementOfTypeIndex(HtmlNode parent, HtmlElement element, bool fromEnd)
    {
        string tagName = element.TagName;
        if (fromEnd)
        {
            int index = 0;
            for (int i = parent.ChildNodes.Count - 1; i >= 0; i--)
            {
                if (parent.ChildNodes[i] is HtmlElement e && e.TagName == tagName)
                {
                    if (e == element) return index;
                    index++;
                }
            }
        }
        else
        {
            int index = 0;
            for (int i = 0; i < parent.ChildNodes.Count; i++)
            {
                if (parent.ChildNodes[i] is HtmlElement e && e.TagName == tagName)
                {
                    if (e == element) return index;
                    index++;
                }
            }
        }
        return -1;
    }

    /// <summary>Get the previous element sibling (skipping text/comment nodes).</summary>
    private static HtmlElement? GetPreviousElementSibling(HtmlElement element)
    {
        var parent = element.Parent;
        if (parent == null) return null;

        HtmlElement? prev = null;
        for (int i = 0; i < parent.ChildNodes.Count; i++)
        {
            if (parent.ChildNodes[i] == element) return prev;
            if (parent.ChildNodes[i] is HtmlElement e) prev = e;
        }
        return null;
    }

    private static bool MatchAttribute(string selector, ref int i, HtmlElement element)
    {
        var attrName = new System.Text.StringBuilder();
        string matchOp = "="; // default: exact match

        while (i < selector.Length && selector[i] != ']' && selector[i] != '=' &&
               selector[i] != '~' && selector[i] != '|' && selector[i] != '^' &&
               selector[i] != '$' && selector[i] != '*')
        {
            attrName.Append(selector[i]);
            i++;
        }

        var name = attrName.ToString().Trim();

        // Check for attribute match operators: ~=, |=, ^=, $=, *=
        if (i < selector.Length && i + 1 < selector.Length && selector[i + 1] == '=')
        {
            if (selector[i] == '~') { matchOp = "~="; i += 2; }
            else if (selector[i] == '|') { matchOp = "|="; i += 2; }
            else if (selector[i] == '^') { matchOp = "^="; i += 2; }
            else if (selector[i] == '$') { matchOp = "$="; i += 2; }
            else if (selector[i] == '*') { matchOp = "*="; i += 2; }
        }
        else if (i < selector.Length && selector[i] == '=')
        {
            matchOp = "=";
            i++; // skip =
        }
        else
        {
            // Existence check only
            if (i < selector.Length && selector[i] == ']') i++;
            return element.HasAttribute(name);
        }

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
        if (attrValue == null) return false;

        string matchValue = value.ToString();

        switch (matchOp)
        {
            case "=": return string.Equals(attrValue, matchValue, StringComparison.OrdinalIgnoreCase);
            case "~=": // contains word — scan without allocating a string[]
            {
                int wStart = 0;
                for (int wi = 0; wi <= attrValue.Length; wi++)
                {
                    if (wi == attrValue.Length || attrValue[wi] == ' ')
                    {
                        if (wi > wStart &&
                            string.Compare(attrValue, wStart, matchValue, 0, wi - wStart, StringComparison.OrdinalIgnoreCase) == 0 &&
                            wi - wStart == matchValue.Length)
                            return true;
                        wStart = wi + 1;
                    }
                }
                return false;
            }
            case "|=": // starts with or equals (hyphen-separated)
                return string.Equals(attrValue, matchValue, StringComparison.OrdinalIgnoreCase) ||
                       attrValue.StartsWith(matchValue + "-", StringComparison.OrdinalIgnoreCase);
            case "^=": // starts with
                return attrValue.StartsWith(matchValue, StringComparison.OrdinalIgnoreCase);
            case "$=": // ends with
                return attrValue.EndsWith(matchValue, StringComparison.OrdinalIgnoreCase);
            case "*=": // contains
                return attrValue.IndexOf(matchValue, StringComparison.OrdinalIgnoreCase) >= 0;
            default:
                return false;
        }
    }

    /// <summary>Split a selector list on commas, respecting parentheses.</summary>
    private static List<string> SplitSelectorList(string selector)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < selector.Length; i++)
        {
            if (selector[i] == '(' || selector[i] == '[') depth++;
            else if (selector[i] == ')' || selector[i] == ']') depth--;
            else if (selector[i] == ',' && depth == 0)
            {
                result.Add(selector.Substring(start, i - start));
                start = i + 1;
            }
        }

        result.Add(selector.Substring(start));
        return result;
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

    private static string ConsumeUntilCloseParen(string s, ref int i)
    {
        int depth = 1;
        int start = i;
        while (i < s.Length && depth > 0)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            if (depth > 0) i++;
        }
        string result = s.Substring(start, i - start);
        if (i < s.Length) i++; // skip closing )
        return result;
    }

    private static void SkipBracket(string s, ref int i)
    {
        while (i < s.Length && s[i] != ']') i++;
        if (i < s.Length) i++;
    }

    private static void SkipParenthesis(string s, ref int i)
    {
        if (i >= s.Length || s[i] != '(') return;
        int depth = 1;
        i++;
        while (i < s.Length && depth > 0)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            i++;
        }
    }

    /// <summary>Case-insensitive array contains check (netstandard2.0 compatible).</summary>
    private static bool ContainsIgnoreCase(string[] array, string value)
    {
        for (int i = 0; i < array.Length; i++)
        {
            if (string.Equals(array[i], value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if a selector targets a pseudo-element (::before or ::after).
    /// If so, returns the pseudo-element name and the base selector (without the pseudo-element part).
    /// </summary>
    public static bool TryGetPseudoElement(string selector, out string pseudoElement, out string baseSelector)
    {
        pseudoElement = "";
        baseSelector = selector;

        if (string.IsNullOrWhiteSpace(selector))
            return false;

        selector = selector.Trim();

        int doubleColonIdx = selector.LastIndexOf("::", StringComparison.Ordinal);
        if (doubleColonIdx < 0)
            return false;

        string pseudoPart = selector.Substring(doubleColonIdx + 2).Trim().ToLowerInvariant();
        if (pseudoPart == "before" || pseudoPart == "after")
        {
            pseudoElement = pseudoPart;
            baseSelector = selector.Substring(0, doubleColonIdx).Trim();
            if (string.IsNullOrEmpty(baseSelector))
                baseSelector = "*";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Check if a selector matches an element for a specific pseudo-element.
    /// </summary>
    public static bool MatchesPseudoElement(string selector, HtmlElement element, string pseudoElement)
    {
        if (!TryGetPseudoElement(selector, out string selectorPseudo, out string baseSelector))
            return false;

        if (!string.Equals(selectorPseudo, pseudoElement, StringComparison.OrdinalIgnoreCase))
            return false;

        return Matches(baseSelector, element);
    }

    private struct SelectorPart
    {
        public string Compound;
        public char Combinator; // ' ', '>', '+', '~', or '\0' for first

        public SelectorPart(string compound, char combinator)
        {
            Compound = compound;
            Combinator = combinator;
        }
    }
}
