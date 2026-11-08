using System;
using System.Collections.Generic;
using EggPdf.Css.Parser;
using EggPdf.Html.Dom;

namespace EggPdf;

public static partial class HtmlToPdf
{
    private static readonly HashSet<string> GenericOrStandardFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif",
        "ui-monospace", "ui-rounded", "emoji", "math", "fangsong", "inherit", "initial", "unset",
        "arial", "helvetica", "helvetica neue", "times", "times new roman", "courier", "courier new",
    };

    private static Func<string, string?> CreateInstalledFamilyResolver(Func<string, bool> isInstalled)
    {
        var known = new Dictionary<string, string?>(StringComparer.Ordinal);
        return familyList =>
        {
            if (known.TryGetValue(familyList, out var cached)) return cached;
            var found = FirstInstalledFamily(familyList, isInstalled);
            known[familyList] = found;
            return found;
        };
    }

    internal static string? FirstInstalledFamily(string familyList, Func<string, bool> isInstalled)
    {
        if (familyList.IndexOf("__vf:", StringComparison.Ordinal) >= 0) return null;
        foreach (var raw in familyList.Split(','))
        {
            var family = raw.Trim().Trim('"', '\'').Trim();
            if (family.Length == 0) continue;
            if (GenericOrStandardFamilies.Contains(family)) return null;
            if (isInstalled(family)) return family;
        }
        return null;
    }

    private const int InstalledFamilyCacheMaxEntries = 1024;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> InstalledFamilyCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool IsInstalledSystemFamily(string family)
    {
        if (InstalledFamilyCache.TryGetValue(family, out bool known)) return known;
        bool installed = Text.SystemFontLocator.HasFamily(family);
        if (InstalledFamilyCache.Count < InstalledFamilyCacheMaxEntries) InstalledFamilyCache.TryAdd(family, installed);
        return installed;
    }

    internal static bool UsesInstalledSystemFamilies(HtmlDocument document, IReadOnlyList<CssStyleSheet>? stylesheets,
        Func<string, bool> isInstalled)
    {
        if (stylesheets != null)
        {
            foreach (var sheet in stylesheets)
            {
                if (DeclaresInstalledFamily(sheet.Rules, isInstalled)) return true;
                foreach (var media in sheet.MediaRules)
                    if (DeclaresInstalledFamily(media.Rules, isInstalled)) return true;
            }
        }

        var pending = new Stack<HtmlNode>();
        pending.Push(document);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is HtmlElement element)
            {
                var face = element.TagName == "font" ? element.GetAttribute("face") : null;
                if (!string.IsNullOrEmpty(face) && FirstInstalledFamily(face!, isInstalled) != null) return true;
                var family = InlineFontFamily(element.GetAttribute("style"));
                if (family != null && FirstInstalledFamily(family, isInstalled) != null) return true;
            }
            for (int i = 0; i < node.ChildNodes.Count; i++)
                pending.Push(node.ChildNodes[i]);
        }
        return false;
    }

    private static bool DeclaresInstalledFamily(List<CssStyleRule> rules, Func<string, bool> isInstalled)
    {
        foreach (var rule in rules)
        {
            foreach (var declaration in rule.Declarations)
            {
                string? families = null;
                if (string.Equals(declaration.Property, "font-family", StringComparison.OrdinalIgnoreCase))
                    families = declaration.Value;
                else if (string.Equals(declaration.Property, "font", StringComparison.OrdinalIgnoreCase))
                    families = FamiliesOfFontShorthand(declaration.Value);
                if (families != null && FirstInstalledFamily(families, isInstalled) != null) return true;
            }
        }
        return false;
    }

    private static string? InlineFontFamily(string? style)
    {
        if (string.IsNullOrEmpty(style)) return null;
        int at = style!.IndexOf("font-family", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        int colon = style.IndexOf(':', at);
        if (colon < 0) return null;
        int end = style.IndexOf(';', colon);
        return (end < 0 ? style.Substring(colon + 1) : style.Substring(colon + 1, end - colon - 1)).Trim();
    }

    private static string? FamiliesOfFontShorthand(string value)
    {
        var tokens = value.Split(' ');
        int lastSize = -1;
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length > 0 && char.IsDigit(token[0]) && token.IndexOf(',') < 0) lastSize = i;
        }
        if (lastSize < 0 || lastSize == tokens.Length - 1) return null;
        return string.Join(" ", tokens, lastSize + 1, tokens.Length - lastSize - 1);
    }
}
