using System;
using System.Collections.Generic;
using System.Text;

namespace EggPdf.Css.Cascade;

public static partial class CssVariableResolver
{
    private const int MaxSubstitutedLength = 1 << 20;

    /// <summary>
    /// Substitutes var() and env() in every property of <paramref name="style"/>. Custom properties are computed first,
    /// on the element that holds them, so children inherit the substituted value as in Chrome; a custom property in a
    /// reference cycle, or referencing a missing property without a fallback, becomes invalid and is removed.
    /// </summary>
    public static void SubstituteVariables(ComputedStyle style)
    {
        List<string>? customProperties = null;
        List<string>? properties = null;
        foreach (var kv in style.All)
        {
            if (kv.Value.IndexOf('(') < 0 || !ContainsReference(kv.Value)) continue;
            if (IsCustomProperty(kv.Key))
                (customProperties ??= new List<string>()).Add(kv.Key);
            else
                (properties ??= new List<string>()).Add(kv.Key);
        }

        if (customProperties != null)
        {
            var computation = new CustomPropertyComputation(style);
            foreach (var name in customProperties)
                computation.Compute(name);
        }

        if (properties == null) return;
        foreach (var name in properties)
        {
            var value = style.Get(name);
            if (value == null) continue;
            var resolved = ResolveVariables(value, style);
            if (resolved != value)
                style.Set(name, resolved);
        }
    }

    /// <summary>
    /// Detect cycles by checking if resolving a custom property would
    /// reference itself (directly or indirectly). Returns true if a cycle exists.
    /// </summary>
    public static bool HasCycle(string propertyName, ComputedStyle style)
    {
        var finished = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        return Visit(propertyName);

        bool Visit(string name)
        {
            if (finished.TryGetValue(name, out bool done))
                return !done;
            finished[name] = false;
            var value = style.Get(name);
            if (value != null)
            {
                int pos = 0;
                while (true)
                {
                    int idx = IndexOfFunction(value, VarPrefix, pos);
                    if (idx < 0) break;
                    int close = FindMatchingParen(value, idx + VarPrefix.Length - 1);
                    if (close < 0) break;
                    ParseVarContent(value.Substring(idx + VarPrefix.Length, close - idx - VarPrefix.Length), out string referenced, out _);
                    if (Visit(referenced)) return true;
                    pos = idx + VarPrefix.Length;
                }
            }
            finished[name] = true;
            return false;
        }
    }

    /// <summary>
    /// Computes an element's custom properties depth-first, each once: every property on a reference cycle is invalid,
    /// while a property that only references the cycle falls back.
    /// </summary>
    private sealed class CustomPropertyComputation
    {
        private readonly ComputedStyle _style;
        private readonly Dictionary<string, bool> _computedValid = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _inProgress = new();
        private readonly HashSet<string> _onCycle = new(StringComparer.OrdinalIgnoreCase);

        public CustomPropertyComputation(ComputedStyle style) => _style = style;

        public string? Compute(string name)
        {
            if (_computedValid.TryGetValue(name, out bool valid))
                return valid ? _style.Get(name) : null;

            int stackIndex = _inProgress.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (stackIndex >= 0)
            {
                for (int k = stackIndex; k < _inProgress.Count; k++)
                    _onCycle.Add(_inProgress[k]);
                return null;
            }

            var value = _style.Get(name);
            if (value == null || !IsCustomProperty(name) || !ContainsReference(value))
                return value;

            _inProgress.Add(name);
            bool substituted = TrySubstitute(value, out string result);
            _inProgress.RemoveAt(_inProgress.Count - 1);

            if (!substituted || _onCycle.Contains(name))
            {
                _computedValid[name] = false;
                _style.Remove(name);
                return null;
            }

            _computedValid[name] = true;
            _style.Set(name, result);
            return result;
        }

        private bool TrySubstitute(string value, out string result)
        {
            result = "";
            value = ResolveEnv(value);
            var output = new StringBuilder(value.Length);
            int pos = 0;
            while (true)
            {
                int idx = IndexOfFunction(value, VarPrefix, pos);
                if (idx < 0)
                {
                    output.Append(value, pos, value.Length - pos);
                    break;
                }

                output.Append(value, pos, idx - pos);
                int close = FindMatchingParen(value, idx + VarPrefix.Length - 1);
                if (close < 0)
                {
                    output.Append(value, idx, value.Length - idx);
                    break;
                }

                ParseVarContent(value.Substring(idx + VarPrefix.Length, close - idx - VarPrefix.Length), out string name, out string? fallback);
                string? referenced = Compute(name);
                if (referenced != null)
                    output.Append(referenced);
                else if (fallback == null || !TrySubstitute(fallback, out string fallbackValue))
                    return false;
                else
                    output.Append(fallbackValue);

                if (output.Length > MaxSubstitutedLength)
                    return false;
                pos = close + 1;
            }

            result = output.ToString();
            return true;
        }
    }
}
