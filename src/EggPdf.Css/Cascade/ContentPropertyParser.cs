using System;
using System.Text;
using EggPdf.Html.Dom;

namespace EggPdf.Css.Cascade;

public static class ContentPropertyParser
{
    public static string? Evaluate(string? contentValue, HtmlElement element)
    {
        if (string.IsNullOrEmpty(contentValue)) return null;
        var trimmed = contentValue.Trim();
        if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "normal", StringComparison.OrdinalIgnoreCase))
            return null;
        var sb = new StringBuilder();
        int i = 0;
        while (i < trimmed.Length)
        {
            while (i < trimmed.Length && (trimmed[i] == ' ' || trimmed[i] == '\t')) i++;
            if (i >= trimmed.Length) break;
            if (trimmed[i] == '"' || trimmed[i] == '\'')
            {
                char q = trimmed[i]; i++;
                while (i < trimmed.Length && trimmed[i] != q)
                {
                    if (trimmed[i] == '\\' && i + 1 < trimmed.Length) { i++; sb.Append(trimmed[i]); }
                    else sb.Append(trimmed[i]);
                    i++;
                }
                if (i < trimmed.Length) i++;
            }
            else if (i + 5 <= trimmed.Length && trimmed.Substring(i, 5).Equals("attr(", StringComparison.OrdinalIgnoreCase))
            {
                i += 5; int ps = i; int d = 1;
                while (i < trimmed.Length && d > 0) { if (trimmed[i] == '(') d++; else if (trimmed[i] == ')') d--; if (d > 0) i++; }
                var av = element.GetAttribute(trimmed.Substring(ps, i - ps).Trim());
                if (av != null) sb.Append(av);
                if (i < trimmed.Length) i++;
            }
            else if (i + 10 <= trimmed.Length && trimmed.Substring(i, 10).Equals("open-quote", StringComparison.OrdinalIgnoreCase))
            { sb.Append("\u201C"); i += 10; }
            else if (i + 11 <= trimmed.Length && trimmed.Substring(i, 11).Equals("close-quote", StringComparison.OrdinalIgnoreCase))
            { sb.Append("\u201D"); i += 11; }
            else
            { while (i < trimmed.Length && trimmed[i] != ' ' && trimmed[i] != '"' && trimmed[i] != '\'') i++; }
        }
        var r = sb.ToString();
        return r.Length > 0 ? r : null;
    }
}
