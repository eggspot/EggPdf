using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Css;

/// <summary>
/// Carries a variable font's design-axis settings (font-stretch, oblique angle, font-variation-settings) through
/// the pipeline as one extra, last entry of the computed <c>font-family</c> list: <c>"__vf:wdth=75;slnt=-10"</c>.
/// Every stage that measures or embeds text already receives the family list, so this reaches all of them without
/// widening dozens of signatures; font lookup strips the entry and instances the font at those axes.
/// </summary>
public static class FontVariationMarker
{
    private const string Prefix = "__vf:";

    /// <summary>
    /// Replace any inherited marker in the style's font-family with one that reflects this element's own
    /// font-stretch / font-style: oblique &lt;angle&gt; / font-variation-settings (none when they are all default).
    /// </summary>
    public static void Apply(ComputedStyle style)
    {
        var family = style.Get("font-family");
        if (string.IsNullOrEmpty(family)) return;

        // Fast path (nearly every element): none of the axis-driving properties is set
        var stretchValue = style.Get("font-stretch");
        var settingsValue = style.Get("font-variation-settings");
        var fontStyle = style.Get("font-style");
        bool anyStretch = stretchValue != null && !stretchValue.Equals("normal", StringComparison.OrdinalIgnoreCase);
        bool anySettings = settingsValue != null && !settingsValue.Equals("normal", StringComparison.OrdinalIgnoreCase);
        bool anyAngle = fontStyle != null && fontStyle.Length > 7 && fontStyle.StartsWith("oblique", StringComparison.OrdinalIgnoreCase);
        if (!anyStretch && !anySettings && !anyAngle)
        {
            if (family!.IndexOf(Prefix, StringComparison.Ordinal) >= 0) style.Set("font-family", Strip(family));
            return;
        }

        var clean = Strip(family!);
        var axes = new SortedDictionary<string, float>(StringComparer.Ordinal);

        var stretch = ParseStretch(stretchValue);
        if (stretch.HasValue && stretch.Value != 100f) axes["wdth"] = stretch.Value;

        if (anyAngle)
        {
            var angle = ParseAngle(fontStyle.Substring(7));
            if (angle.HasValue && angle.Value != 0f) axes["slnt"] = -angle.Value; // CSS oblique angles lean right, slnt negative
        }

        // font-variation-settings wins over the properties above, as the CSS cascade of font features does
        foreach (var kv in ParseSettings(style.Get("font-variation-settings"))) axes[kv.Key] = kv.Value;

        style.Set("font-family", axes.Count == 0 ? clean : clean + ", \"" + Prefix + Format(axes) + "\"");
    }

    /// <summary>The family list without the marker, and the axes it carried (null when there was none).</summary>
    public static string Split(string? familyList, out IReadOnlyDictionary<string, float>? axes)
    {
        axes = null;
        if (string.IsNullOrEmpty(familyList) || familyList!.IndexOf(Prefix, StringComparison.Ordinal) < 0) return familyList ?? "";

        var kept = new List<string>();
        foreach (var part in familyList.Split(','))
        {
            var token = part.Trim().Trim('"', '\'');
            if (token.StartsWith(Prefix, StringComparison.Ordinal)) axes = Parse(token.Substring(Prefix.Length));
            else kept.Add(part.Trim());
        }
        return string.Join(", ", kept);
    }

    /// <summary>A PDF-name-safe suffix identifying the axes in a family list ("" when there are none).</summary>
    public static string NameSuffix(string? familyList)
    {
        Split(familyList, out var axes);
        if (axes == null || axes.Count == 0) return "";

        var sb = new StringBuilder("-VF");
        foreach (var kv in axes)
            sb.Append(kv.Key).Append('_').Append(kv.Value.ToString("0.##", CultureInfo.InvariantCulture)).Append('_');
        return sb.ToString(0, sb.Length - 1);
    }

    private static string Strip(string family) => Split(family, out _);

    private static string Format(SortedDictionary<string, float> axes)
    {
        var sb = new StringBuilder();
        foreach (var kv in axes)
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("0.##", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static IReadOnlyDictionary<string, float> Parse(string body)
    {
        var axes = new SortedDictionary<string, float>(StringComparer.Ordinal);
        foreach (var pair in body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && float.TryParse(pair.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                axes[pair.Substring(0, eq)] = v;
        }
        return axes;
    }

    /// <summary><c>"wdth" 80, "wght" 600</c> to axis values; malformed entries are skipped.</summary>
    private static IEnumerable<KeyValuePair<string, float>> ParseSettings(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value!.Trim().Equals("normal", StringComparison.OrdinalIgnoreCase)) yield break;
        foreach (var part in value.Split(','))
        {
            var t = part.Trim();
            int close = t.LastIndexOfAny(new[] { '"', '\'' });
            if (t.Length < 3 || close < 2) continue;
            var tag = t.Substring(1, close - 1);
            if (tag.Length != 4) continue;
            if (float.TryParse(t.Substring(close + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                yield return new KeyValuePair<string, float>(tag, v);
        }
    }

    /// <summary>font-stretch as a percentage: keywords, a percentage, or null when unset/normal.</summary>
    private static float? ParseStretch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value!.Trim().ToLowerInvariant();
        switch (v)
        {
            case "ultra-condensed": return 50f;
            case "extra-condensed": return 62.5f;
            case "condensed": return 75f;
            case "semi-condensed": return 87.5f;
            case "normal": return 100f;
            case "semi-expanded": return 112.5f;
            case "expanded": return 125f;
            case "extra-expanded": return 150f;
            case "ultra-expanded": return 200f;
        }
        if (v.EndsWith("%", StringComparison.Ordinal) &&
            float.TryParse(v.Substring(0, v.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct)) return pct;
        return null;
    }

    private static float? ParseAngle(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        if (v.Length == 0) return null; // bare `oblique` selects an oblique face; only an explicit angle drives an axis
        if (v.EndsWith("deg", StringComparison.Ordinal)) v = v.Substring(0, v.Length - 3);
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float a) ? a : (float?)null;
    }
}
