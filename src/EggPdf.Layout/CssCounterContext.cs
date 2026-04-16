using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EggPdf.Css.Parser;

namespace EggPdf.Layout;

/// <summary>
/// Tracks user-defined CSS counter state during document layout.
/// Handles counter-reset, counter-increment, and counter() resolution.
/// </summary>
public class CssCounterContext
{
    // Maps counter name -> stack of values (each push by counter-reset adds a new scope).
    private readonly Dictionary<string, Stack<int>> _counters =
        new Dictionary<string, Stack<int>>(StringComparer.OrdinalIgnoreCase);

    // Maps custom counter style name -> rule
    private Dictionary<string, CssCounterStyleRule>? _customStyles;

    /// <summary>Register custom @counter-style rules from parsed stylesheets.</summary>
    public void RegisterCounterStyles(IEnumerable<CssCounterStyleRule> rules)
    {
        if (_customStyles == null)
            _customStyles = new Dictionary<string, CssCounterStyleRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
            _customStyles[rule.Name] = rule;
    }

    /// <summary>Process counter-reset declaration for an element.</summary>
    public void ApplyReset(string? counterReset)
    {
        if (string.IsNullOrWhiteSpace(counterReset) || counterReset == "none") return;

        // Format: "name" or "name value" or "name1 value1 name2 value2"
        var parts = counterReset.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        while (i < parts.Length)
        {
            var name = parts[i++];
            int value = 0;
            if (i < parts.Length && int.TryParse(parts[i], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int v))
            {
                value = v;
                i++;
            }

            if (!_counters.TryGetValue(name, out var stack))
            {
                stack = new Stack<int>();
                _counters[name] = stack;
            }
            stack.Push(value);
        }
    }

    /// <summary>Process counter-increment declaration for an element.</summary>
    public void ApplyIncrement(string? counterIncrement)
    {
        if (string.IsNullOrWhiteSpace(counterIncrement) || counterIncrement == "none") return;

        var parts = counterIncrement.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        while (i < parts.Length)
        {
            var name = parts[i++];
            int increment = 1;
            if (i < parts.Length && int.TryParse(parts[i], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int v))
            {
                increment = v;
                i++;
            }

            if (!_counters.TryGetValue(name, out var stack) || stack.Count == 0)
            {
                // Create counter at 0 if it doesn't exist (implicit creation)
                if (stack == null)
                {
                    stack = new Stack<int>();
                    _counters[name] = stack;
                }
                stack.Push(0);
            }

            var top = stack.Pop();
            stack.Push(top + increment);
        }
    }

    /// <summary>
    /// Process counter-set declaration — sets existing counter(s) to the given value.
    /// Unlike counter-reset, counter-set does NOT create a new scope; it updates the
    /// top-of-stack value.  If the counter doesn't exist it is created.
    /// </summary>
    public void ApplySet(string? counterSet)
    {
        if (string.IsNullOrWhiteSpace(counterSet) || counterSet == "none") return;

        var parts = counterSet.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        while (i < parts.Length)
        {
            var name = parts[i++];
            int value = 0;
            if (i < parts.Length && int.TryParse(parts[i], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int v))
            {
                value = v;
                i++;
            }

            if (!_counters.TryGetValue(name, out var stack) || stack.Count == 0)
            {
                // Counter doesn't exist — create it (no scope push like counter-reset would do)
                if (stack == null)
                {
                    stack = new Stack<int>();
                    _counters[name] = stack;
                }
                stack.Push(value);
            }
            else
            {
                // Replace the top-of-stack value in-place
                stack.Pop();
                stack.Push(value);
            }
        }
    }

    /// <summary>Pop the topmost scope pushed by counter-reset (called after leaving element scope).</summary>
    public void PopReset(string? counterReset)
    {
        if (string.IsNullOrWhiteSpace(counterReset) || counterReset == "none") return;

        var parts = counterReset.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        while (i < parts.Length)
        {
            var name = parts[i++];
            // Skip optional initial value token
            if (i < parts.Length && int.TryParse(parts[i], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out _))
                i++;

            if (_counters.TryGetValue(name, out var stack) && stack.Count > 0)
                stack.Pop();
        }
    }

    /// <summary>Get current value of a named counter (0 if not defined).</summary>
    public int GetValue(string name)
    {
        if (_counters.TryGetValue(name, out var stack) && stack.Count > 0)
            return stack.Peek();
        return 0;
    }

    /// <summary>
    /// Resolve a CSS content value, expanding counter() and attr() references.
    /// Returns the string to render, or null if the content value produces nothing.
    /// Pass <paramref name="style"/> to honour the element's <c>quotes</c> property.
    /// </summary>
    public string? ResolveContent(string? contentValue, EggPdf.Html.Dom.HtmlElement? element,
        EggPdf.Css.ComputedStyle? style = null)
    {
        if (string.IsNullOrEmpty(contentValue) || contentValue == "none" || contentValue == "normal")
            return null;

        var result = new StringBuilder();
        var remaining = contentValue.Trim();

        while (remaining.Length > 0)
        {
            remaining = remaining.TrimStart();
            if (remaining.Length == 0) break;

            if (remaining[0] == '"' || remaining[0] == '\'')
            {
                // Quoted string
                char quote = remaining[0];
                int end = remaining.IndexOf(quote, 1);
                if (end < 0) end = remaining.Length - 1;
                result.Append(remaining.Substring(1, end - 1));
                remaining = remaining.Substring(end + 1);
            }
            else if (remaining.StartsWith("counter(", StringComparison.OrdinalIgnoreCase))
            {
                int close = remaining.IndexOf(')');
                if (close < 0) { remaining = ""; break; }
                var args = remaining.Substring(8, close - 8).Trim();
                remaining = remaining.Substring(close + 1);

                // args may be "name" or "name, style"
                int comma = args.IndexOf(',');
                var counterName = comma >= 0 ? args.Substring(0, comma).Trim() : args.Trim();
                var counterStyle = comma >= 0 ? args.Substring(comma + 1).Trim() : "decimal";

                int val = GetValue(counterName);
                result.Append(FormatCounterValue(val, counterStyle));
            }
            else if (remaining.StartsWith("counters(", StringComparison.OrdinalIgnoreCase))
            {
                // counters(name, separator) — simplified: just show current value
                int close = remaining.IndexOf(')');
                if (close < 0) { remaining = ""; break; }
                var args = remaining.Substring(9, close - 9).Trim();
                remaining = remaining.Substring(close + 1);

                var parts2 = args.Split(',');
                var counterName = parts2[0].Trim();
                int val = GetValue(counterName);
                result.Append(val.ToString(CultureInfo.InvariantCulture));
            }
            else if (remaining.StartsWith("attr(", StringComparison.OrdinalIgnoreCase))
            {
                int close = remaining.IndexOf(')');
                if (close < 0) { remaining = ""; break; }
                var attrName = remaining.Substring(5, close - 5).Trim();
                remaining = remaining.Substring(close + 1);

                var attrVal = element?.GetAttribute(attrName);
                if (!string.IsNullOrEmpty(attrVal))
                    result.Append(attrVal);
            }
            else if (remaining.StartsWith("open-quote", StringComparison.OrdinalIgnoreCase))
            {
                result.Append(ResolveQuoteChar(style, open: true));
                remaining = remaining.Substring(10);
            }
            else if (remaining.StartsWith("close-quote", StringComparison.OrdinalIgnoreCase))
            {
                result.Append(ResolveQuoteChar(style, open: false));
                remaining = remaining.Substring(11);
            }
            else
            {
                // Skip unknown token
                int space = remaining.IndexOf(' ');
                remaining = space >= 0 ? remaining.Substring(space + 1) : "";
            }
        }

        var text = result.ToString();
        return text.Length > 0 ? text : null;
    }

    /// <summary>
    /// Return the correct open or close quote string from the element's computed <c>quotes</c>
    /// property, or fall back to U+201C/U+201D smart-quotes.
    /// </summary>
    private static string ResolveQuoteChar(EggPdf.Css.ComputedStyle? style, bool open)
    {
        // Default smart quotes
        string defaultOpen  = "\u201C"; // "
        string defaultClose = "\u201D"; // "

        var quotesVal = style?.Get("quotes");
        if (string.IsNullOrEmpty(quotesVal) || quotesVal == "none" || quotesVal == "auto")
            return open ? defaultOpen : defaultClose;

        // Parse: 'x' 'y' or "x" "y" — take the first pair (outer level)
        var tokens = new System.Collections.Generic.List<string>();
        int i = 0;
        while (i < quotesVal.Length && tokens.Count < 2)
        {
            while (i < quotesVal.Length && quotesVal[i] == ' ') i++;
            if (i >= quotesVal.Length) break;
            char q = quotesVal[i];
            if (q == '\'' || q == '"')
            {
                int end = quotesVal.IndexOf(q, i + 1);
                if (end < 0) end = quotesVal.Length;
                tokens.Add(quotesVal.Substring(i + 1, end - i - 1));
                i = end + 1;
            }
            else
            {
                // Unquoted token — unlikely but handle gracefully
                int end = quotesVal.IndexOf(' ', i);
                if (end < 0) end = quotesVal.Length;
                tokens.Add(quotesVal.Substring(i, end - i));
                i = end;
            }
        }

        if (tokens.Count >= 2)
            return open ? tokens[0] : tokens[1];
        if (tokens.Count == 1)
            return tokens[0];

        return open ? defaultOpen : defaultClose;
    }

    /// <summary>
    /// Format a list item index using a named custom counter style.
    /// Returns null if no custom style with that name exists.
    /// </summary>
    public string? FormatCustomStyle(string styleName, int value)
    {
        if (_customStyles == null || !_customStyles.TryGetValue(styleName, out var rule))
            return null;
        return FormatCustomCounterValue(value, rule);
    }

    private string FormatCounterValue(int value, string style)
    {
        // Check custom @counter-style rules first
        if (_customStyles != null && _customStyles.TryGetValue(style, out var customRule))
            return FormatCustomCounterValue(value, customRule);

        switch (style.Trim().ToLowerInvariant())
        {
            case "decimal":
            case "":
                return value.ToString(CultureInfo.InvariantCulture);
            case "lower-alpha":
            case "lower-latin":
                return value > 0 ? ToAlpha(value, false) : value.ToString(CultureInfo.InvariantCulture);
            case "upper-alpha":
            case "upper-latin":
                return value > 0 ? ToAlpha(value, true) : value.ToString(CultureInfo.InvariantCulture);
            case "lower-roman":
                return value > 0 ? ToRoman(value, false) : value.ToString(CultureInfo.InvariantCulture);
            case "upper-roman":
                return value > 0 ? ToRoman(value, true) : value.ToString(CultureInfo.InvariantCulture);
            default:
                return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private string FormatCustomCounterValue(int value, CssCounterStyleRule rule)
    {
        var system = rule.System.Trim().ToLowerInvariant();
        string core;

        if (system == "extends" && rule.Extends != null)
        {
            // Delegate to the extended style (may be built-in or another custom style)
            core = FormatCounterValue(value, rule.Extends);
            // Strip any existing suffix from the parent format (suffix comes from this rule)
        }
        else if (rule.Symbols.Count == 0)
        {
            core = value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            switch (system)
            {
                case "cyclic":
                    // Cycle through symbols: 1→sym[0], 2→sym[1], ..., N→sym[(N-1)%count]
                    if (rule.Symbols.Count > 0)
                        core = rule.Symbols[(value - 1 + rule.Symbols.Count * 1000) % rule.Symbols.Count];
                    else
                        core = value.ToString(CultureInfo.InvariantCulture);
                    break;

                case "symbolic":
                    // Repeat: 1→sym[0], 2→sym[1], ..., N+1→sym[0]sym[0], ...
                    if (rule.Symbols.Count > 0)
                    {
                        int idx = (value - 1) % rule.Symbols.Count;
                        int reps = (value - 1) / rule.Symbols.Count + 1;
                        var sym = rule.Symbols[idx];
                        var sb2 = new StringBuilder();
                        for (int i = 0; i < reps; i++) sb2.Append(sym);
                        core = sb2.ToString();
                    }
                    else core = value.ToString(CultureInfo.InvariantCulture);
                    break;

                case "alphabetic":
                    // Like alphabet: 1→a, 2→b, ..., 26→z, 27→aa, ...
                    if (rule.Symbols.Count > 0)
                    {
                        int n = value;
                        int base_ = rule.Symbols.Count;
                        var sb3 = new StringBuilder();
                        while (n > 0)
                        {
                            n--;
                            sb3.Insert(0, rule.Symbols[n % base_]);
                            n /= base_;
                        }
                        core = sb3.ToString();
                    }
                    else core = value.ToString(CultureInfo.InvariantCulture);
                    break;

                case "numeric":
                    // Like decimal but with custom digits
                    if (rule.Symbols.Count > 0)
                    {
                        int base2 = rule.Symbols.Count;
                        if (value == 0) { core = rule.Symbols[0]; break; }
                        int n2 = value;
                        bool neg = n2 < 0;
                        if (neg) n2 = -n2;
                        var sb4 = new StringBuilder();
                        while (n2 > 0)
                        {
                            sb4.Insert(0, rule.Symbols[n2 % base2]);
                            n2 /= base2;
                        }
                        if (neg) sb4.Insert(0, rule.Negative ?? "-");
                        core = sb4.ToString();
                    }
                    else core = value.ToString(CultureInfo.InvariantCulture);
                    break;

                case "fixed":
                    // Use each symbol once, fall back to decimal
                    if (value >= 1 && value <= rule.Symbols.Count)
                        core = rule.Symbols[value - 1];
                    else
                        core = value.ToString(CultureInfo.InvariantCulture);
                    break;

                default:
                    core = value.ToString(CultureInfo.InvariantCulture);
                    break;
            }
        }

        return rule.Prefix + core + rule.Suffix;
    }

    private static string ToAlpha(int value, bool upper)
    {
        var sb = new StringBuilder();
        while (value > 0)
        {
            value--;
            sb.Insert(0, (char)((upper ? 'A' : 'a') + value % 26));
            value /= 26;
        }
        return sb.ToString();
    }

    private static string ToRoman(int value, bool upper)
    {
        if (value <= 0 || value > 3999)
            return value.ToString(CultureInfo.InvariantCulture);

        var thousands = upper
            ? new[] { "", "M", "MM", "MMM" }
            : new[] { "", "m", "mm", "mmm" };
        var hundreds = upper
            ? new[] { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" }
            : new[] { "", "c", "cc", "ccc", "cd", "d", "dc", "dcc", "dccc", "cm" };
        var tens = upper
            ? new[] { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" }
            : new[] { "", "x", "xx", "xxx", "xl", "l", "lx", "lxx", "lxxx", "xc" };
        var ones = upper
            ? new[] { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" }
            : new[] { "", "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix" };

        return thousands[value / 1000] + hundreds[value / 100 % 10] +
               tens[value / 10 % 10] + ones[value % 10];
    }
}
