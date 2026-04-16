using System;
using System.Globalization;

namespace EggPdf.Layout;

/// <summary>
/// Resolves CSS calc(), min(), max(), and clamp() expressions.
/// Converts mixed units (px, em, rem, %, pt, cm, mm, in) to pixel values.
/// Infallible: returns 0 on parse errors.
/// </summary>
public static class CalcResolver
{
    private const float DefaultFontSize = 16f;

    /// <summary>
    /// Check if a CSS value contains a math function (calc, min, max, clamp).
    /// </summary>
    // Known math function names (for IsMathFunction detection)
    private static readonly string[] _mathFunctions = {
        "calc(", "min(", "max(", "clamp(",
        "sin(", "cos(", "tan(", "asin(", "acos(", "atan(", "atan2(",
        "sqrt(", "pow(", "hypot(", "abs(", "sign(",
        "round(", "mod(", "rem(", "log(", "exp("
    };

    public static bool IsMathFunction(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        for (int f = 0; f < _mathFunctions.Length; f++)
        {
            var fn = _mathFunctions[f];
            if (value.Length >= fn.Length &&
                value.Substring(0, fn.Length).Equals(fn, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolve a CSS value that may contain calc(), min(), max(), or clamp().
    /// Returns the computed value in pixels.
    /// </summary>
    public static float Resolve(string value, float containingSize, float fontSize)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        value = value.Trim();

        if (StartsWithIgnoreCase(value, "calc(") && value[value.Length - 1] == ')')
        {
            string inner = value.Substring(5, value.Length - 6);
            return EvaluateExpression(inner, containingSize, fontSize);
        }

        if (StartsWithIgnoreCase(value, "min(") && value[value.Length - 1] == ')')
        {
            string inner = value.Substring(4, value.Length - 5);
            return EvaluateMin(inner, containingSize, fontSize);
        }

        if (StartsWithIgnoreCase(value, "max(") && value[value.Length - 1] == ')')
        {
            string inner = value.Substring(4, value.Length - 5);
            return EvaluateMax(inner, containingSize, fontSize);
        }

        if (StartsWithIgnoreCase(value, "clamp(") && value[value.Length - 1] == ')')
        {
            string inner = value.Substring(6, value.Length - 7);
            return EvaluateClamp(inner, containingSize, fontSize);
        }

        // ── CSS math functions (Level 4) ──────────────────────────────────
        if (StartsWithIgnoreCase(value, "sin(") && value[value.Length - 1] == ')')
            return (float)Math.Sin(ToRadians(Resolve(value.Substring(4, value.Length - 5), containingSize, fontSize), value.Substring(4, value.Length - 5)));
        if (StartsWithIgnoreCase(value, "cos(") && value[value.Length - 1] == ')')
            return (float)Math.Cos(ToRadians(Resolve(value.Substring(4, value.Length - 5), containingSize, fontSize), value.Substring(4, value.Length - 5)));
        if (StartsWithIgnoreCase(value, "tan(") && value[value.Length - 1] == ')')
            return (float)Math.Tan(ToRadians(Resolve(value.Substring(4, value.Length - 5), containingSize, fontSize), value.Substring(4, value.Length - 5)));
        if (StartsWithIgnoreCase(value, "asin(") && value[value.Length - 1] == ')')
            return (float)(Math.Asin(Resolve(value.Substring(5, value.Length - 6), containingSize, fontSize)) * 180.0 / Math.PI);
        if (StartsWithIgnoreCase(value, "acos(") && value[value.Length - 1] == ')')
            return (float)(Math.Acos(Resolve(value.Substring(5, value.Length - 6), containingSize, fontSize)) * 180.0 / Math.PI);
        if (StartsWithIgnoreCase(value, "atan(") && value[value.Length - 1] == ')')
            return (float)(Math.Atan(Resolve(value.Substring(5, value.Length - 6), containingSize, fontSize)) * 180.0 / Math.PI);
        if (StartsWithIgnoreCase(value, "atan2(") && value[value.Length - 1] == ')')
        {
            var a2 = SplitArgs(value.Substring(6, value.Length - 7));
            if (a2.Length >= 2)
                return (float)(Math.Atan2(Resolve(a2[0].Trim(), containingSize, fontSize), Resolve(a2[1].Trim(), containingSize, fontSize)) * 180.0 / Math.PI);
        }
        if (StartsWithIgnoreCase(value, "sqrt(") && value[value.Length - 1] == ')')
        {
            float v = Resolve(value.Substring(5, value.Length - 6), containingSize, fontSize);
            return v >= 0 ? (float)Math.Sqrt(v) : 0;
        }
        if (StartsWithIgnoreCase(value, "pow(") && value[value.Length - 1] == ')')
        {
            var pa = SplitArgs(value.Substring(4, value.Length - 5));
            if (pa.Length >= 2)
                return (float)Math.Pow(Resolve(pa[0].Trim(), containingSize, fontSize), Resolve(pa[1].Trim(), containingSize, fontSize));
        }
        if (StartsWithIgnoreCase(value, "hypot(") && value[value.Length - 1] == ')')
        {
            var ha = SplitArgs(value.Substring(6, value.Length - 7));
            float sum = 0;
            for (int i = 0; i < ha.Length; i++) { float hv = Resolve(ha[i].Trim(), containingSize, fontSize); sum += hv * hv; }
            return (float)Math.Sqrt(sum);
        }
        if (StartsWithIgnoreCase(value, "abs(") && value[value.Length - 1] == ')')
            return Math.Abs(Resolve(value.Substring(4, value.Length - 5), containingSize, fontSize));
        if (StartsWithIgnoreCase(value, "sign(") && value[value.Length - 1] == ')')
        {
            float sv = Resolve(value.Substring(5, value.Length - 6), containingSize, fontSize);
            return sv > 0 ? 1f : sv < 0 ? -1f : 0f;
        }
        if (StartsWithIgnoreCase(value, "round(") && value[value.Length - 1] == ')')
        {
            // round([strategy,] value, step)
            var ra = SplitArgs(value.Substring(6, value.Length - 7));
            int vi = 0;
            string strategy = "nearest";
            if (ra.Length >= 1 && !ra[0].Trim().EndsWith("px") && !char.IsDigit(ra[0].Trim()[0]))
            { strategy = ra[0].Trim(); vi = 1; }
            if (ra.Length >= vi + 2)
            {
                float rv = Resolve(ra[vi].Trim(), containingSize, fontSize);
                float step = Resolve(ra[vi + 1].Trim(), containingSize, fontSize);
                if (step == 0) return rv;
                float n = rv / step;
                float rounded = strategy == "up" ? (float)Math.Ceiling(n) :
                                strategy == "down" ? (float)Math.Floor(n) :
                                strategy == "to-zero" ? (float)Math.Truncate(n) :
                                (float)Math.Round(n, MidpointRounding.AwayFromZero);
                return rounded * step;
            }
        }
        if (StartsWithIgnoreCase(value, "mod(") && value[value.Length - 1] == ')')
        {
            var ma = SplitArgs(value.Substring(4, value.Length - 5));
            if (ma.Length >= 2)
            {
                float mv = Resolve(ma[0].Trim(), containingSize, fontSize);
                float ms = Resolve(ma[1].Trim(), containingSize, fontSize);
                if (ms == 0) return mv;
                float r = mv % ms;
                // mod() returns value with same sign as divisor (like Python %)
                return (r < 0 && ms > 0) || (r > 0 && ms < 0) ? r + ms : r;
            }
        }
        if (StartsWithIgnoreCase(value, "rem(") && value[value.Length - 1] == ')')
        {
            var rea = SplitArgs(value.Substring(4, value.Length - 5));
            if (rea.Length >= 2)
            {
                float rev = Resolve(rea[0].Trim(), containingSize, fontSize);
                float res = Resolve(rea[1].Trim(), containingSize, fontSize);
                return res == 0 ? rev : rev % res; // rem() keeps dividend sign
            }
        }
        if (StartsWithIgnoreCase(value, "log(") && value[value.Length - 1] == ')')
        {
            var la = SplitArgs(value.Substring(4, value.Length - 5));
            float lv = Resolve(la[0].Trim(), containingSize, fontSize);
            if (la.Length >= 2)
            {
                float lb = Resolve(la[1].Trim(), containingSize, fontSize);
                return lb > 0 && lv > 0 ? (float)(Math.Log(lv) / Math.Log(lb)) : 0;
            }
            return lv > 0 ? (float)Math.Log(lv) : 0;
        }
        if (StartsWithIgnoreCase(value, "exp(") && value[value.Length - 1] == ')')
            return (float)Math.Exp(Resolve(value.Substring(4, value.Length - 5), containingSize, fontSize));

        // Not a math function, try resolving as a single term
        return ResolveTerm(value, containingSize, fontSize);
    }

    /// <summary>
    /// Evaluate a calc() expression that may contain +, -, *, / operators and mixed units.
    /// Supports nested calc().
    /// </summary>
    private static float EvaluateExpression(string expr, float containingSize, float fontSize)
    {
        expr = expr.Trim();
        if (string.IsNullOrEmpty(expr))
            return 0;

        // Tokenize the expression into terms and operators
        var tokens = Tokenize(expr, containingSize, fontSize);
        if (tokens.Length == 0)
            return 0;

        return EvaluateTokens(tokens);
    }

    /// <summary>
    /// Tokenize a calc expression into a flat array of numbers and operators.
    /// Each term is resolved to px. Supports nested calc/min/max/clamp.
    /// </summary>
    private static float[] Tokenize(string expr, float containingSize, float fontSize)
    {
        // We'll parse left-to-right, respecting operator precedence (* / before + -)
        // First, collect terms and operators
        var values = new System.Collections.Generic.List<float>();
        var operators = new System.Collections.Generic.List<char>();

        int pos = 0;
        while (pos < expr.Length)
        {
            SkipWhitespace(expr, ref pos);
            if (pos >= expr.Length) break;

            // Check for nested function (calc, min, max, clamp)
            if (IsNestedFunction(expr, pos))
            {
                int funcEnd = FindFunctionEnd(expr, pos);
                if (funcEnd > pos)
                {
                    string funcStr = expr.Substring(pos, funcEnd - pos + 1);
                    float funcVal = Resolve(funcStr, containingSize, fontSize);
                    values.Add(funcVal);
                    pos = funcEnd + 1;
                    continue;
                }
            }

            // Check for operator (but not unary minus at start or after another operator)
            char c = expr[pos];
            if ((c == '+' || c == '-') && values.Count > 0 && values.Count > operators.Count)
            {
                // This is a binary operator if preceded by whitespace on both sides
                // Per CSS spec, + and - must be surrounded by whitespace
                bool leftSpace = pos > 0 && (expr[pos - 1] == ' ' || expr[pos - 1] == '\t');
                bool rightSpace = pos + 1 < expr.Length && (expr[pos + 1] == ' ' || expr[pos + 1] == '\t');
                if (leftSpace && rightSpace)
                {
                    operators.Add(c);
                    pos++;
                    continue;
                }
                // Otherwise it's a sign for the next number - fall through to term parsing
            }
            else if ((c == '*' || c == '/') && values.Count > 0)
            {
                operators.Add(c);
                pos++;
                continue;
            }

            // Parse a term (number with unit, or nested function)
            string term = ExtractTerm(expr, ref pos);
            if (!string.IsNullOrEmpty(term))
            {
                values.Add(ResolveTerm(term, containingSize, fontSize));
            }
            else
            {
                pos++; // skip unrecognized character
            }
        }

        // Now evaluate respecting precedence: first * and /, then + and -
        // Apply * and / first
        for (int i = 0; i < operators.Count; i++)
        {
            if (operators[i] == '*' || operators[i] == '/')
            {
                float left = values[i];
                float right = values[i + 1];
                float result = operators[i] == '*' ? left * right : (right != 0 ? left / right : 0);
                values[i] = result;
                values.RemoveAt(i + 1);
                operators.RemoveAt(i);
                i--;
            }
        }

        // Apply + and -
        float final_result = values.Count > 0 ? values[0] : 0;
        for (int i = 0; i < operators.Count; i++)
        {
            float right = values[i + 1];
            if (operators[i] == '+')
                final_result += right;
            else if (operators[i] == '-')
                final_result -= right;
        }

        return new[] { final_result };
    }

    private static float EvaluateTokens(float[] tokens)
    {
        return tokens.Length > 0 ? tokens[0] : 0;
    }

    private static float EvaluateMin(string inner, float containingSize, float fontSize)
    {
        var args = SplitArgs(inner);
        if (args.Length == 0) return 0;

        float result = Resolve(args[0].Trim(), containingSize, fontSize);
        for (int i = 1; i < args.Length; i++)
        {
            float val = Resolve(args[i].Trim(), containingSize, fontSize);
            if (val < result) result = val;
        }
        return result;
    }

    private static float EvaluateMax(string inner, float containingSize, float fontSize)
    {
        var args = SplitArgs(inner);
        if (args.Length == 0) return 0;

        float result = Resolve(args[0].Trim(), containingSize, fontSize);
        for (int i = 1; i < args.Length; i++)
        {
            float val = Resolve(args[i].Trim(), containingSize, fontSize);
            if (val > result) result = val;
        }
        return result;
    }

    private static float EvaluateClamp(string inner, float containingSize, float fontSize)
    {
        var args = SplitArgs(inner);
        if (args.Length < 3) return 0;

        float min = Resolve(args[0].Trim(), containingSize, fontSize);
        float val = Resolve(args[1].Trim(), containingSize, fontSize);
        float max = Resolve(args[2].Trim(), containingSize, fontSize);

        if (val < min) return min;
        if (val > max) return max;
        return val;
    }

    /// <summary>
    /// Split function arguments by commas, respecting nested parentheses.
    /// </summary>
    private static string[] SplitArgs(string inner)
    {
        var args = new System.Collections.Generic.List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                args.Add(inner.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (start < inner.Length)
            args.Add(inner.Substring(start));

        return args.ToArray();
    }

    /// <summary>
    /// Resolve a single CSS value term (number + unit) to pixels.
    /// </summary>
    private static float ResolveTerm(string term, float containingSize, float fontSize)
    {
        term = term.Trim();
        if (string.IsNullOrEmpty(term))
            return 0;

        // Check for nested function
        if (StartsWithIgnoreCase(term, "calc(") || StartsWithIgnoreCase(term, "min(") ||
            StartsWithIgnoreCase(term, "max(") || StartsWithIgnoreCase(term, "clamp("))
        {
            return Resolve(term, containingSize, fontSize);
        }

        if (term.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 2));

        // Check rem before em since "rem" also EndsWith("em")
        if (term.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 3)) * DefaultFontSize;

        if (term.EndsWith("em", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 2)) * fontSize;

        if (term.EndsWith("%"))
            return ParseFloat(term.Substring(0, term.Length - 1)) / 100f * containingSize;

        if (term.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 2)) * 96f / 72f;

        if (term.EndsWith("cm", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 2)) * 96f / 2.54f;

        if (term.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 2)) * 96f / 25.4f;

        if (term.EndsWith("in", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 2)) * 96f;

        if (term.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 3)); // return degrees as-is for trig

        if (term.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 3)) * 180f / (float)Math.PI; // convert to degrees

        if (term.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 4)) * 360f;

        if (term.EndsWith("grad", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 4)) * 0.9f; // grad → degrees

        // Bare number (unitless, e.g. multiplier in calc)
        return ParseFloat(term);
    }

    private static bool IsNestedFunction(string expr, int pos)
    {
        if (pos + 3 >= expr.Length) return false;
        for (int f = 0; f < _mathFunctions.Length; f++)
        {
            if (StartsWithIgnoreCase(expr, pos, _mathFunctions[f])) return true;
        }
        return false;
    }

    /// <summary>Convert a numeric value to radians, detecting deg/rad/turn/grad suffix in original string.</summary>
    private static double ToRadians(float value, string originalExpr)
    {
        var t = originalExpr.Trim().ToLowerInvariant();
        if (t.EndsWith("deg")) return value * Math.PI / 180.0;
        if (t.EndsWith("turn")) return value * 2.0 * Math.PI;
        if (t.EndsWith("grad")) return value * Math.PI / 200.0;
        // rad or unitless: assume already radians if no deg suffix found in expression
        if (t.EndsWith("rad")) return value;
        // Default: treat as degrees (most common in CSS)
        return value * Math.PI / 180.0;
    }

    private static int FindFunctionEnd(string expr, int pos)
    {
        // Find the opening paren
        int openParen = expr.IndexOf('(', pos);
        if (openParen < 0) return -1;

        int depth = 0;
        for (int i = openParen; i < expr.Length; i++)
        {
            if (expr[i] == '(') depth++;
            else if (expr[i] == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Extract a single term (number + unit) from the expression.
    /// </summary>
    private static string ExtractTerm(string expr, ref int pos)
    {
        SkipWhitespace(expr, ref pos);
        if (pos >= expr.Length) return "";

        int start = pos;

        // Handle sign
        if (pos < expr.Length && (expr[pos] == '-' || expr[pos] == '+'))
            pos++;

        // Consume digits and decimal point
        bool hasDigit = false;
        while (pos < expr.Length && (char.IsDigit(expr[pos]) || expr[pos] == '.'))
        {
            hasDigit = true;
            pos++;
        }

        if (!hasDigit)
        {
            pos = start; // reset
            return "";
        }

        // Consume unit suffix
        while (pos < expr.Length && (char.IsLetter(expr[pos]) || expr[pos] == '%'))
            pos++;

        return expr.Substring(start, pos - start);
    }

    private static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\n' || s[pos] == '\r'))
            pos++;
    }

    private static float ParseFloat(string s)
    {
        if (float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            return result;
        return 0;
    }

    private static bool StartsWithIgnoreCase(string s, string prefix)
    {
        if (s.Length < prefix.Length) return false;
        return s.Substring(0, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithIgnoreCase(string s, int startIndex, string prefix)
    {
        if (s.Length - startIndex < prefix.Length) return false;
        return s.Substring(startIndex, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
