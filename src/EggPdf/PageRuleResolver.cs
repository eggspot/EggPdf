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
    /// <summary>Standard page sizes in CSS pixels at 96dpi. Converted to PDF points via PxToPt (×0.75).</summary>
    private static readonly Dictionary<string, (float Width, float Height)> PageSizes =
        new Dictionary<string, (float, float)>(StringComparer.OrdinalIgnoreCase)
        {
            { "a3", (1122.52f, 1587.40f) },  // 297mm × 420mm at 96dpi → ×0.75 = 841.89pt × 1190.55pt
            { "a4", (793.70f, 1122.52f) },   // 210mm × 297mm at 96dpi → ×0.75 = 595.28pt × 841.89pt
            { "a5", (559.37f, 793.70f) },    // 148mm × 210mm at 96dpi → ×0.75 = 419.53pt × 595.28pt
            { "letter", (816f, 1056f) },     // 8.5in × 11in  at 96dpi → ×0.75 = 612pt × 792pt
            { "legal", (816f, 1344f) },      // 8.5in × 14in  at 96dpi → ×0.75 = 612pt × 1008pt
            { "tabloid", (1056f, 1632f) },   // 11in × 17in   at 96dpi → ×0.75 = 792pt × 1224pt
        };

    /// <summary>
    /// Resolve @page rules from all stylesheets. Last rule wins (cascade).
    /// Returns resolved page dimensions and margins in CSS pixels.
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

                // Only process generic @page rules (no selector like :first, :left)
                if (!string.IsNullOrEmpty(rule.PageSelector))
                    continue;

                for (int d = 0; d < rule.Declarations.Count; d++)
                {
                    var decl = rule.Declarations[d];
                    ApplyDeclaration(settings, decl);
                }
            }
        }

        return settings;
    }

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
                settings.MarginTop = ResolvePageLength(decl.Value);
                break;
            case "margin-right":
                settings.MarginRight = ResolvePageLength(decl.Value);
                break;
            case "margin-bottom":
                settings.MarginBottom = ResolvePageLength(decl.Value);
                break;
            case "margin-left":
                settings.MarginLeft = ResolvePageLength(decl.Value);
                break;
        }
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
                // Custom dimension
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
                // Swap width and height
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
            // Single dimension = square page
            settings.PageWidthPx = customW;
            settings.PageHeightPx = customW;
        }

        // Handle landscape/portrait keywords without named size but with current dimensions
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

        float top, right, bottom, left;

        switch (parts.Length)
        {
            case 1:
                top = right = bottom = left = ResolvePageLength(parts[0]);
                break;
            case 2:
                top = bottom = ResolvePageLength(parts[0]);
                right = left = ResolvePageLength(parts[1]);
                break;
            case 3:
                top = ResolvePageLength(parts[0]);
                right = left = ResolvePageLength(parts[1]);
                bottom = ResolvePageLength(parts[2]);
                break;
            default: // 4+
                top = ResolvePageLength(parts[0]);
                right = ResolvePageLength(parts[1]);
                bottom = ResolvePageLength(parts[2]);
                left = ResolvePageLength(parts[3]);
                break;
        }

        settings.MarginTop = top;
        settings.MarginRight = right;
        settings.MarginBottom = bottom;
        settings.MarginLeft = left;
    }

    /// <summary>
    /// Resolve a CSS length value to CSS pixels. Uses BlockLayout.ResolveLength
    /// with 0 containing size and 16px default font size.
    /// </summary>
    private static float ResolvePageLength(string value)
    {
        return BlockLayout.ResolveLength(value, 0, 16f);
    }
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

    /// <summary>Whether any @page rule was found.</summary>
    public bool HasPageSize => PageWidthPx != 793.70f || PageHeightPx != 1122.52f;

    /// <summary>Whether any margin was specified.</summary>
    public bool HasMargins => MarginTop > 0 || MarginRight > 0 || MarginBottom > 0 || MarginLeft > 0;

    /// <summary>Content area width (page width minus horizontal margins).</summary>
    public float ContentWidthPx => PageWidthPx - MarginLeft - MarginRight;

    /// <summary>Content area height (page height minus vertical margins).</summary>
    public float ContentHeightPx => PageHeightPx - MarginTop - MarginBottom;
}
