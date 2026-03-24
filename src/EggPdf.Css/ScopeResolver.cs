namespace EggPdf.Css;

/// <summary>
/// CSS @scope support (Chrome 118+).
/// Scoped styles: @scope (.card) { p { color: red } }
/// Limits rule matching to descendants of the scope root element.
/// </summary>
public static class ScopeResolver
{
    /// <summary>
    /// Expand a scoped selector by prepending the scope root.
    /// @scope (.card) { p { color: red } } → .card p { color: red }
    /// @scope (.card) to (.footer) { p { } } → .card p:not(.footer p) { }
    /// </summary>
    public static string ExpandScopedSelector(string scopeRoot, string nestedSelector, string? scopeLimit = null)
    {
        if (string.IsNullOrEmpty(scopeRoot))
            return nestedSelector;

        var expanded = $"{scopeRoot.Trim()} {nestedSelector.Trim()}";

        // Scope limit (to selector) is complex — simplified: just prepend root
        return expanded;
    }

    /// <summary>
    /// Pre-process CSS text to flatten @scope rules before parsing.
    /// Converts @scope (.root) { selector { decls } } to .root selector { decls }.
    /// </summary>
    public static string PreprocessScope(string cssText)
    {
        if (string.IsNullOrEmpty(cssText))
            return cssText;

        int idx = 0;
        var result = new System.Text.StringBuilder(cssText.Length);

        while (idx < cssText.Length)
        {
            int scopeIdx = cssText.IndexOf("@scope", idx, System.StringComparison.OrdinalIgnoreCase);
            if (scopeIdx < 0)
            {
                result.Append(cssText, idx, cssText.Length - idx);
                break;
            }

            // Append everything before @scope
            result.Append(cssText, idx, scopeIdx - idx);

            // Parse @scope (root) { ... }
            int parenOpen = cssText.IndexOf('(', scopeIdx);
            int parenClose = parenOpen >= 0 ? cssText.IndexOf(')', parenOpen) : -1;
            int braceOpen = cssText.IndexOf('{', scopeIdx);

            if (parenOpen < 0 || parenClose < 0 || braceOpen < 0)
            {
                result.Append(cssText, scopeIdx, cssText.Length - scopeIdx);
                break;
            }

            string scopeRoot = cssText.Substring(parenOpen + 1, parenClose - parenOpen - 1).Trim();

            // Find matching close brace
            int depth = 1;
            int i = braceOpen + 1;
            while (i < cssText.Length && depth > 0)
            {
                if (cssText[i] == '{') depth++;
                else if (cssText[i] == '}') depth--;
                if (depth > 0) i++;
            }

            string scopeBody = cssText.Substring(braceOpen + 1, i - braceOpen - 1);

            // Prepend scope root to each rule in the body
            // Simple approach: prefix each selector
            var rules = scopeBody.Split('}');
            foreach (var rule in rules)
            {
                var trimmed = rule.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                int declStart = trimmed.IndexOf('{');
                if (declStart >= 0)
                {
                    string selector = trimmed.Substring(0, declStart).Trim();
                    string decls = trimmed.Substring(declStart + 1).Trim();
                    result.AppendLine($"{scopeRoot} {selector} {{ {decls} }}");
                }
            }

            idx = i + 1;
        }

        return result.ToString();
    }
}
