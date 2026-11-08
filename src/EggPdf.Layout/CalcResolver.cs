using System;
using System.Globalization;

namespace EggPdf.Layout;

/// <summary>
/// Evaluates CSS math functions (calc and the CSS Values 4 functions) with mixed units
/// (px, em, rem, %, pt, cm, mm, in, viewport units, angles) to a single number. Infallible:
/// malformed input evaluates to 0.
/// </summary>
public static class CalcResolver
{
    private const int MaxNestingDepth = 32;

    [ThreadStatic]
    private static int _nestingDepth;

    private static readonly string[] _mathFunctions = {
        "calc(", "min(", "max(", "clamp(",
        "sin(", "cos(", "tan(", "asin(", "acos(", "atan(", "atan2(",
        "sqrt(", "pow(", "hypot(", "abs(", "sign(",
        "round(", "mod(", "rem(", "log(", "exp("
    };

    /// <summary>
    /// True when the value starts with a CSS math function: calc, min, max, clamp, the trigonometric
    /// functions (sin, cos, tan, asin, acos, atan, atan2), sqrt, pow, hypot, abs, sign, round, mod,
    /// rem, log or exp.
    /// </summary>
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
    /// Evaluate a CSS value that may be or contain a math function (see <see cref="IsMathFunction"/>)
    /// and return it in pixels; angles come back in degrees and plain numbers unchanged. Never throws:
    /// malformed input evaluates to 0.
    /// </summary>
    public static float Resolve(string value, float containingSize, float fontSize)
    {
        if (string.IsNullOrEmpty(value) || _nestingDepth >= MaxNestingDepth)
            return 0;

        _nestingDepth++;
        try
        {
            return ResolveFunctionOrTerm(value.Trim(), containingSize, fontSize);
        }
        finally
        {
            _nestingDepth--;
        }
    }

    private static float ResolveFunctionOrTerm(string value, float containingSize, float fontSize)
    {
        if (!IsSingleFunctionCall(value))
            return EvaluateExpression(value, containingSize, fontSize);

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

        if (StartsWithIgnoreCase(value, "sin(") && value[value.Length - 1] == ')')
            return (float)Math.Sin(AngleArgumentInRadians(value.Substring(4, value.Length - 5), containingSize, fontSize));
        if (StartsWithIgnoreCase(value, "cos(") && value[value.Length - 1] == ')')
            return (float)Math.Cos(AngleArgumentInRadians(value.Substring(4, value.Length - 5), containingSize, fontSize));
        if (StartsWithIgnoreCase(value, "tan(") && value[value.Length - 1] == ')')
            return (float)Math.Tan(AngleArgumentInRadians(value.Substring(4, value.Length - 5), containingSize, fontSize));
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
            var ra = SplitArgs(value.Substring(6, value.Length - 7));
            int vi = 0;
            string strategy = "nearest";
            if (ra.Length >= 1 && IsRoundingStrategy(ra[0].Trim()))
            { strategy = ra[0].Trim().ToLowerInvariant(); vi = 1; }
            if (ra.Length > vi)
            {
                float rv = Resolve(ra[vi].Trim(), containingSize, fontSize);
                float step = ra.Length > vi + 1 ? Resolve(ra[vi + 1].Trim(), containingSize, fontSize) : 1f;
                if (step == 0) return rv;
                float n = rv / step;
                float rounded = strategy == "up" ? (float)Math.Ceiling(n) :
                                strategy == "down" ? (float)Math.Floor(n) :
                                strategy == "to-zero" ? (float)Math.Truncate(n) :
                                (float)Math.Floor((double)n + 0.5);
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
                float remainderWithDividendSign = mv % ms;
                bool signDiffersFromDivisor = (remainderWithDividendSign < 0 && ms > 0) || (remainderWithDividendSign > 0 && ms < 0);
                return signDiffersFromDivisor ? remainderWithDividendSign + ms : remainderWithDividendSign;
            }
        }
        if (StartsWithIgnoreCase(value, "rem(") && value[value.Length - 1] == ')')
        {
            var rea = SplitArgs(value.Substring(4, value.Length - 5));
            if (rea.Length >= 2)
            {
                float rev = Resolve(rea[0].Trim(), containingSize, fontSize);
                float res = Resolve(rea[1].Trim(), containingSize, fontSize);
                return res == 0 ? rev : rev % res;
            }
        }
        if (StartsWithIgnoreCase(value, "log(") && value[value.Length - 1] == ')')
        {
            var la = SplitArgs(value.Substring(4, value.Length - 5));
            if (la.Length == 0) return 0;
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

        return 0;
    }

    private static bool IsSingleFunctionCall(string value) =>
        IsNestedFunction(value, 0) && FindFunctionEnd(value, 0) == value.Length - 1;

    private static float EvaluateExpression(string expr, float containingSize, float fontSize)
    {
        int pos = 0;
        return ParseSum(expr, ref pos, containingSize, fontSize);
    }

    private static float ParseSum(string expr, ref int pos, float containingSize, float fontSize)
    {
        float value = ParseProduct(expr, ref pos, containingSize, fontSize);
        while (true)
        {
            int operatorPos = pos;
            SkipWhitespace(expr, ref operatorPos);
            bool whitespaceBefore = operatorPos > pos;
            if (!whitespaceBefore || operatorPos + 1 >= expr.Length) return value;

            char op = expr[operatorPos];
            if ((op != '+' && op != '-') || !IsCalcWhitespace(expr[operatorPos + 1])) return value;

            pos = operatorPos + 1;
            float right = ParseProduct(expr, ref pos, containingSize, fontSize);
            value = op == '+' ? value + right : value - right;
        }
    }

    private static float ParseProduct(string expr, ref int pos, float containingSize, float fontSize)
    {
        float value = ParseFactor(expr, ref pos, containingSize, fontSize);
        while (true)
        {
            int operatorPos = pos;
            SkipWhitespace(expr, ref operatorPos);
            if (operatorPos >= expr.Length) return value;

            char op = expr[operatorPos];
            if (op != '*' && op != '/') return value;

            pos = operatorPos + 1;
            float right = ParseFactor(expr, ref pos, containingSize, fontSize);
            value = op == '*' ? value * right : (right != 0 ? value / right : 0);
        }
    }

    private static float ParseFactor(string expr, ref int pos, float containingSize, float fontSize)
    {
        SkipWhitespace(expr, ref pos);
        if (pos >= expr.Length) return 0;

        if (expr[pos] == '(')
            return ParseParenthesized(expr, ref pos, containingSize, fontSize);

        if (IsNestedFunction(expr, pos))
        {
            int end = FindFunctionEnd(expr, pos);
            if (end < 0)
            {
                pos = expr.Length;
                return 0;
            }
            float functionValue = Resolve(expr.Substring(pos, end - pos + 1), containingSize, fontSize);
            pos = end + 1;
            return functionValue;
        }

        string term = ExtractTerm(expr, ref pos);
        if (term.Length > 0)
            return ResolveTerm(term, containingSize, fontSize);

        return ParseConstant(expr, ref pos);
    }

    private static float ParseParenthesized(string expr, ref int pos, float containingSize, float fontSize)
    {
        if (_nestingDepth >= MaxNestingDepth)
        {
            pos = expr.Length;
            return 0;
        }

        _nestingDepth++;
        try
        {
            pos++;
            float inner = ParseSum(expr, ref pos, containingSize, fontSize);
            SkipWhitespace(expr, ref pos);
            if (pos < expr.Length && expr[pos] == ')') pos++;
            return inner;
        }
        finally
        {
            _nestingDepth--;
        }
    }

    private static float ParseConstant(string expr, ref int pos)
    {
        int start = pos;
        while (pos < expr.Length && IsAsciiLetter(expr[pos])) pos++;
        int length = pos - start;
        if (length == 2 && string.Compare(expr, start, "pi", 0, 2, StringComparison.OrdinalIgnoreCase) == 0)
            return (float)Math.PI;
        if (length == 1 && (expr[start] == 'e' || expr[start] == 'E'))
            return (float)Math.E;
        return 0;
    }

    private static bool IsCalcWhitespace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r';

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    private static bool IsRoundingStrategy(string value) =>
        string.Equals(value, "nearest", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "up", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "down", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "to-zero", StringComparison.OrdinalIgnoreCase);

    private static double AngleArgumentInRadians(string argument, float containingSize, float fontSize)
    {
        float value = Resolve(argument, containingSize, fontSize);
        return IsAngleExpression(argument) ? value * Math.PI / 180.0 : value;
    }

    private static bool IsAngleExpression(string expr)
    {
        int i = 0;
        while (i < expr.Length)
        {
            if (!IsAsciiLetter(expr[i]))
            {
                i++;
                continue;
            }

            bool isUnit = i > 0 && (IsAsciiDigit(expr[i - 1]) || expr[i - 1] == '.');
            int start = i;
            if (isUnit)
            {
                while (i < expr.Length && IsAsciiLetter(expr[i])) i++;
                if (IsAngleUnit(expr, start, i - start)) return true;
                continue;
            }

            while (i < expr.Length && (IsAsciiLetter(expr[i]) || IsAsciiDigit(expr[i]) || expr[i] == '-')) i++;
            if (i >= expr.Length || expr[i] != '(') continue;

            var resultType = MathFunctionResultType(expr, start, i - start);
            if (resultType == MathResultType.Angle) return true;
            if (resultType == MathResultType.Number)
            {
                int close = FindFunctionEnd(expr, start);
                i = close < 0 ? expr.Length : close + 1;
            }
        }
        return false;
    }

    private enum MathResultType
    {
        SameAsArguments,
        Number,
        Angle,
    }

    private static readonly string[] _numberResultFunctions = { "sin", "cos", "tan", "sqrt", "pow", "log", "exp", "sign" };

    private static readonly string[] _angleResultFunctions = { "asin", "acos", "atan", "atan2" };

    private static MathResultType MathFunctionResultType(string expr, int start, int length)
    {
        for (int f = 0; f < _angleResultFunctions.Length; f++)
            if (NameEquals(expr, start, length, _angleResultFunctions[f])) return MathResultType.Angle;
        for (int f = 0; f < _numberResultFunctions.Length; f++)
            if (NameEquals(expr, start, length, _numberResultFunctions[f])) return MathResultType.Number;
        return MathResultType.SameAsArguments;
    }

    private static bool NameEquals(string expr, int start, int length, string name) =>
        length == name.Length && string.Compare(expr, start, name, 0, length, StringComparison.OrdinalIgnoreCase) == 0;

    private static bool IsAngleUnit(string expr, int start, int length) =>
        (length == 3 && (string.Compare(expr, start, "deg", 0, 3, StringComparison.OrdinalIgnoreCase) == 0 ||
                         string.Compare(expr, start, "rad", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)) ||
        (length == 4 && (string.Compare(expr, start, "grad", 0, 4, StringComparison.OrdinalIgnoreCase) == 0 ||
                         string.Compare(expr, start, "turn", 0, 4, StringComparison.OrdinalIgnoreCase) == 0));

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

    private static float ResolveTerm(string term, float containingSize, float fontSize)
    {
        term = term.Trim();
        if (string.IsNullOrEmpty(term))
            return 0;

        if (term[term.Length - 1] == '%')
            return ParseFloat(term.Substring(0, term.Length - 1)) / 100f * containingSize;

        if (BlockLayout.TryUnitLengthToPx(term, containingSize, fontSize, out float px))
            return px;

        if (term.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 3));

        if (term.EndsWith("grad", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 4)) * 0.9f;

        if (term.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 3)) * 180f / (float)Math.PI;

        if (term.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
            return ParseFloat(term.Substring(0, term.Length - 4)) * 360f;

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

    private static int FindFunctionEnd(string expr, int pos)
    {
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

    private static string ExtractTerm(string expr, ref int pos)
    {
        SkipWhitespace(expr, ref pos);
        if (pos >= expr.Length) return "";

        int start = pos;

        if (pos < expr.Length && (expr[pos] == '-' || expr[pos] == '+'))
            pos++;

        bool hasDigit = false;
        while (pos < expr.Length && (IsAsciiDigit(expr[pos]) || expr[pos] == '.'))
        {
            hasDigit = true;
            pos++;
        }

        if (!hasDigit)
        {
            pos = start;
            return "";
        }

        if (pos < expr.Length && (expr[pos] == 'e' || expr[pos] == 'E'))
        {
            int exponentDigits = pos + 1;
            if (exponentDigits < expr.Length && (expr[exponentDigits] == '+' || expr[exponentDigits] == '-'))
                exponentDigits++;
            if (exponentDigits < expr.Length && IsAsciiDigit(expr[exponentDigits]))
            {
                pos = exponentDigits;
                while (pos < expr.Length && IsAsciiDigit(expr[pos])) pos++;
            }
        }

        while (pos < expr.Length && (IsAsciiLetter(expr[pos]) || expr[pos] == '%'))
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
