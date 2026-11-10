using System;
using System.Collections.Generic;
using EggPdf.Css;
using EggPdf.Css.Parser;
using EggPdf.Layout;

namespace EggPdf;

/// <summary>
/// Resolves @page rules from parsed stylesheets to determine page size and margins.
/// Processes size, margin, and orientation declarations.
/// </summary>
internal static class PageRuleResolver
{
    /// <summary>
    /// Standard page sizes (portrait) in CSS pixels at 96dpi, from their millimetre or inch dimensions; PxToPt
    /// (×0.75) turns them into PDF points, e.g. A4 210mm × 297mm = 793.70px × 1122.52px = 595.28pt × 841.89pt.
    /// </summary>
    private static readonly Dictionary<string, (float Width, float Height)> PageSizes =
        new Dictionary<string, (float, float)>(StringComparer.OrdinalIgnoreCase)
        {
            { "a3", (1122.52f, 1587.40f) },
            { "a4", (793.70f, 1122.52f) },
            { "a5", (559.37f, 793.70f) },
            { "letter", (816f, 1056f) },
            { "legal", (816f, 1344f) },
            { "tabloid", (1056f, 1632f) },
            { "ledger", (1056f, 1632f) },
            { "b5", (665.20f, 944.88f) },
            { "b4", (944.88f, 1334.17f) },
            { "jis-b5", (687.87f, 971.34f) },
            { "jis-b4", (971.34f, 1375.75f) },
        };

    private const int Top = 0, Right = 1, Bottom = 2, Left = 3;

    /// <summary>
    /// Resolve @page rules from all stylesheets. Last rule wins (cascade).
    /// Returns resolved page dimensions and margins in CSS pixels. Only the base (unselected) rule's size and margins
    /// apply to the whole document, since layout runs in a single pass; <c>:first</c>/<c>:left</c>/<c>:right</c>
    /// rules contribute margin boxes, resolved per page type at paint time (see MarginBoxRenderer), and
    /// <c>@page &lt;name&gt;</c> rules are kept for <see cref="ForNamedPage"/>.
    /// </summary>
    public static PageSettings Resolve(List<CssStyleSheet> stylesheets)
    {
        var settings = new PageSettings();

        for (int s = 0; s < stylesheets.Count; s++)
        {
            var sheet = stylesheets[s];
            for (int r = 0; r < sheet.PageRules.Count; r++)
            {
                var rule = sheet.PageRules[r];
                Dictionary<string, PageMarginBoxSettings>? targetMarginBoxes;
                if (string.IsNullOrEmpty(rule.PageSelector))
                {
                    for (int d = 0; d < rule.Declarations.Count; d++)
                        ApplyDeclaration(settings, rule.Declarations[d]);
                    targetMarginBoxes = settings.MarginBoxes;
                }
                else if (string.Equals(rule.PageSelector, ":first", StringComparison.OrdinalIgnoreCase))
                    targetMarginBoxes = settings.FirstPageMarginBoxes;
                else if (string.Equals(rule.PageSelector, ":left", StringComparison.OrdinalIgnoreCase))
                    targetMarginBoxes = settings.LeftPageMarginBoxes;
                else if (string.Equals(rule.PageSelector, ":right", StringComparison.OrdinalIgnoreCase))
                    targetMarginBoxes = settings.RightPageMarginBoxes;
                else if (IsNamedPageSelector(rule.PageSelector))
                {
                    string key = rule.PageSelector!.Trim();
                    if (!settings.NamedRules.TryGetValue(key, out var namedList))
                        settings.NamedRules[key] = namedList = new List<CssPageRule>();
                    namedList.Add(rule);
                    targetMarginBoxes = null;
                }
                else
                    targetMarginBoxes = null;

                if (targetMarginBoxes == null)
                    continue;

                CollectMarginBoxes(rule, targetMarginBoxes);
            }
        }

        ResolveMargins(settings);
        return settings;
    }

    private static bool IsNamedPageSelector(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return false;
        var t = selector!.Trim();
        return char.IsLetter(t[0]) && t.IndexOf(':') < 0 && t.IndexOf(' ') < 0;
    }

    /// <summary>Collect a rule's margin-box content and basic text styling into <paramref name="target"/>.</summary>
    private static void CollectMarginBoxes(CssPageRule rule, Dictionary<string, PageMarginBoxSettings> target)
    {
        for (int m = 0; m < rule.MarginBoxes.Count; m++)
        {
            var mb = rule.MarginBoxes[m];
            var mbSettings = new PageMarginBoxSettings();
            bool hasContent = false;
            for (int d = 0; d < mb.Declarations.Count; d++)
            {
                var decl = mb.Declarations[d];
                switch (decl.Property)
                {
                    case "content":
                        mbSettings.Content = decl.Value;
                        hasContent = true;
                        break;
                    case "font-size":
                        mbSettings.FontSize = decl.Value;
                        break;
                    case "color":
                        mbSettings.Color = decl.Value;
                        break;
                    case "font-family":
                        mbSettings.FontFamily = decl.Value;
                        break;
                    case "font-weight":
                        mbSettings.FontWeight = decl.Value;
                        break;
                }
            }
            if (hasContent)
                target[mb.Position] = mbSettings;
        }
    }

    /// <summary>
    /// Page settings for pages holding elements with <c>page: &lt;name&gt;</c>: the base settings with
    /// every <c>@page &lt;name&gt;</c> rule's size, margins and margin boxes applied on top.
    /// </summary>
    public static PageSettings ForNamedPage(PageSettings baseSettings, string name)
    {
        var settings = baseSettings.Clone();
        if (!baseSettings.NamedRules.TryGetValue(name, out var rules)) return settings;
        foreach (var rule in rules)
        {
            for (int d = 0; d < rule.Declarations.Count; d++)
                ApplyDeclaration(settings, rule.Declarations[d]);
            CollectMarginBoxes(rule, settings.MarginBoxes);
        }
        ResolveMargins(settings);
        return settings;
    }

    /// <summary>
    /// Record one @page declaration. Margins are kept as written and resolved by <see cref="ResolveMargins"/> once
    /// the page size is final, so a percentage margin follows the page size wherever <c>size</c> is declared.
    /// </summary>
    private static void ApplyDeclaration(PageSettings settings, CssDeclaration decl)
    {
        switch (decl.Property)
        {
            case "size":
                ParseSize(settings, decl.Value);
                break;
            case "margin":
                ParseMarginShorthand(settings, decl.Value);
                break;
            case "margin-top":
                settings.MarginValues[Top] = decl.Value;
                break;
            case "margin-right":
                settings.MarginValues[Right] = decl.Value;
                break;
            case "margin-bottom":
                settings.MarginValues[Bottom] = decl.Value;
                break;
            case "margin-left":
                settings.MarginValues[Left] = decl.Value;
                break;
        }
    }

    /// <summary>
    /// Resolve the declared margins to CSS pixels; percentages are of the page width for the left and right margins
    /// and of the page height for the top and bottom ones. An undeclared side keeps its value.
    /// </summary>
    private static void ResolveMargins(PageSettings settings)
    {
        var values = settings.MarginValues;
        if (values[Top] != null) settings.MarginTop = ResolvePageLength(values[Top]!, settings.PageHeightPx);
        if (values[Right] != null) settings.MarginRight = ResolvePageLength(values[Right]!, settings.PageWidthPx);
        if (values[Bottom] != null) settings.MarginBottom = ResolvePageLength(values[Bottom]!, settings.PageHeightPx);
        if (values[Left] != null) settings.MarginLeft = ResolvePageLength(values[Left]!, settings.PageWidthPx);
    }

    private static void ParseSize(PageSettings settings, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;

        bool landscape = false;
        bool portrait = false;
        string? namedSize = null;
        float customW = 0, customH = 0;
        int customCount = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (string.Equals(part, "landscape", StringComparison.OrdinalIgnoreCase))
            {
                landscape = true;
            }
            else if (string.Equals(part, "portrait", StringComparison.OrdinalIgnoreCase))
            {
                portrait = true;
            }
            else if (PageSizes.ContainsKey(part))
            {
                namedSize = part;
            }
            else
            {
                float resolved = ResolvePageLength(part);
                if (resolved > 0)
                {
                    if (customCount == 0) customW = resolved;
                    else if (customCount == 1) customH = resolved;
                    customCount++;
                }
            }
        }

        if (namedSize != null)
        {
            var size = PageSizes[namedSize];
            settings.PageWidthPx = size.Width;
            settings.PageHeightPx = size.Height;

            if (landscape)
            {
                settings.PageWidthPx = size.Height;
                settings.PageHeightPx = size.Width;
            }
        }
        else if (customCount >= 2)
        {
            settings.PageWidthPx = customW;
            settings.PageHeightPx = customH;
        }
        else if (customCount == 1)
        {
            settings.PageWidthPx = customW;
            settings.PageHeightPx = customW;
        }

        if (namedSize == null && customCount == 0)
        {
            if (landscape && settings.PageWidthPx < settings.PageHeightPx)
            {
                float temp = settings.PageWidthPx;
                settings.PageWidthPx = settings.PageHeightPx;
                settings.PageHeightPx = temp;
            }
            else if (portrait && settings.PageWidthPx > settings.PageHeightPx)
            {
                float temp = settings.PageWidthPx;
                settings.PageWidthPx = settings.PageHeightPx;
                settings.PageHeightPx = temp;
            }
        }
    }

    private static void ParseMarginShorthand(PageSettings settings, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;

        var values = settings.MarginValues;
        values[Top] = parts[0];
        values[Right] = parts.Length > 1 ? parts[1] : parts[0];
        values[Bottom] = parts.Length > 2 ? parts[2] : parts[0];
        values[Left] = parts.Length > 3 ? parts[3] : values[Right];
    }

    /// <summary>A CSS length in CSS pixels, with percentages of <paramref name="percentBasis"/> and em at 16px.</summary>
    private static float ResolvePageLength(string value, float percentBasis = 0f) =>
        BlockLayout.ResolveLength(value, percentBasis, 16f);
}

/// <summary>
/// Resolved page settings from @page rules. All values in CSS pixels.
/// </summary>
internal class PageSettings
{
    /// <summary>Page width in CSS pixels at 96dpi. Default: A4 width (793.70px → 595.28pt).</summary>
    public float PageWidthPx { get; set; } = 793.70f;

    /// <summary>Page height in CSS pixels at 96dpi. Default: A4 height (1122.52px → 841.89pt).</summary>
    public float PageHeightPx { get; set; } = 1122.52f;

    /// <summary>Top margin in CSS pixels.</summary>
    public float MarginTop { get; set; }

    /// <summary>Right margin in CSS pixels.</summary>
    public float MarginRight { get; set; }

    /// <summary>Bottom margin in CSS pixels.</summary>
    public float MarginBottom { get; set; }

    /// <summary>Left margin in CSS pixels.</summary>
    public float MarginLeft { get; set; }

    /// <summary>The declared margins as written (top, right, bottom, left; null when undeclared), resolved against the final page size.</summary>
    public string?[] MarginValues { get; } = new string?[4];

    /// <summary>Content area width (page width minus horizontal margins).</summary>
    public float ContentWidthPx => PageWidthPx - MarginLeft - MarginRight;

    /// <summary>Content area height (page height minus vertical margins).</summary>
    public float ContentHeightPx => PageHeightPx - MarginTop - MarginBottom;

    /// <summary>
    /// CSS @page margin boxes keyed by position name (e.g. "bottom-center"),
    /// one of the 16 positions the Paged Media spec defines: top/bottom-left/
    /// center/right, top/bottom-left/right-corner, left/right-top/middle/bottom.
    /// </summary>
    public Dictionary<string, PageMarginBoxSettings> MarginBoxes { get; } =
        new Dictionary<string, PageMarginBoxSettings>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Margin-box overrides from <c>@page :first</c> -- applied on physical
    /// page 1 in place of the corresponding position's base box, if any.
    /// Page size/margin declarations on a selector-scoped @page rule are not
    /// applied: this engine lays out in a single pass, so a page-type-specific
    /// page size isn't supported (only its margin-box content is).
    /// </summary>
    public Dictionary<string, PageMarginBoxSettings> FirstPageMarginBoxes { get; } =
        new Dictionary<string, PageMarginBoxSettings>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raw <c>@page &lt;name&gt;</c> rules by page name (see <see cref="PageRuleResolver.ForNamedPage"/>).</summary>
    public Dictionary<string, List<CssPageRule>> NamedRules { get; } =
        new Dictionary<string, List<CssPageRule>>(StringComparer.Ordinal);

    /// <summary>An independent copy: size, margins and every margin-box dictionary.</summary>
    public PageSettings Clone()
    {
        var copy = new PageSettings
        {
            PageWidthPx = PageWidthPx, PageHeightPx = PageHeightPx,
            MarginTop = MarginTop, MarginRight = MarginRight, MarginBottom = MarginBottom, MarginLeft = MarginLeft,
        };
        Array.Copy(MarginValues, copy.MarginValues, MarginValues.Length);
        foreach (var kv in MarginBoxes) copy.MarginBoxes[kv.Key] = kv.Value;
        foreach (var kv in FirstPageMarginBoxes) copy.FirstPageMarginBoxes[kv.Key] = kv.Value;
        foreach (var kv in LeftPageMarginBoxes) copy.LeftPageMarginBoxes[kv.Key] = kv.Value;
        foreach (var kv in RightPageMarginBoxes) copy.RightPageMarginBoxes[kv.Key] = kv.Value;
        foreach (var kv in NamedRules) copy.NamedRules[kv.Key] = kv.Value;
        return copy;
    }

    /// <summary>Margin-box overrides from <c>@page :left</c> -- applied on even physical pages.</summary>
    public Dictionary<string, PageMarginBoxSettings> LeftPageMarginBoxes { get; } =
        new Dictionary<string, PageMarginBoxSettings>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Margin-box overrides from <c>@page :right</c> -- applied on odd physical pages (other than page 1, which :first takes priority for).</summary>
    public Dictionary<string, PageMarginBoxSettings> RightPageMarginBoxes { get; } =
        new Dictionary<string, PageMarginBoxSettings>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A single @page margin box's raw content and basic text styling.</summary>
internal struct PageMarginBoxSettings
{
    /// <summary>Raw (unresolved) CSS content value, e.g. <c>"Page " counter(page)</c>.</summary>
    public string? Content;
    public string? FontSize;
    public string? Color;
    public string? FontFamily;
    public string? FontWeight;
}
