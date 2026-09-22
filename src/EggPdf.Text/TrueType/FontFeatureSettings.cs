using System;
using System.Collections.Generic;
using System.Globalization;

namespace EggPdf.Text.TrueType;

/// <summary>
/// Parses the CSS <c>font-feature-settings</c> property value into the set of
/// active OpenType feature tags, and builds a deterministic PDF-font-name
/// suffix from them so distinct feature declarations on the same font family
/// get distinct embedded-font entries (see <see cref="FontData.GsubFeatures"/>).
/// </summary>
public static class FontFeatureSettings
{
    /// <summary>
    /// Parse a <c>font-feature-settings</c> value (e.g. <c>"smcp" on, "zero" 1, "liga" 0</c>)
    /// into the sorted, deduplicated list of active 4-character feature tags.
    /// A tag with no value, an "on" value, or a nonzero integer value is active;
    /// "off" or 0 is inactive. Returns an empty list for "normal", null, or empty input.
    /// </summary>
    public static List<string> ParseActiveTags(string? value)
    {
        var active = new List<string>();
        if (string.IsNullOrWhiteSpace(value)) return active;
        var trimmed = value.Trim();
        if (trimmed.Equals("normal", StringComparison.OrdinalIgnoreCase)) return active;

        foreach (var rawEntry in trimmed.Split(','))
        {
            var entry = rawEntry.Trim();
            if (entry.Length == 0) continue;

            // Extract the quoted 4-character tag
            if (entry.Length < 2 || (entry[0] != '"' && entry[0] != '\'')) continue;
            char quote = entry[0];
            int closeIdx = entry.IndexOf(quote, 1);
            if (closeIdx < 0) continue;
            string tag = entry.Substring(1, closeIdx - 1);
            if (tag.Length == 0) continue;

            string rest = entry.Substring(closeIdx + 1).Trim();
            bool isActive;
            if (rest.Length == 0)
                isActive = true; // omitted value defaults to 1 (on)
            else if (rest.Equals("on", StringComparison.OrdinalIgnoreCase))
                isActive = true;
            else if (rest.Equals("off", StringComparison.OrdinalIgnoreCase))
                isActive = false;
            else if (int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                isActive = n != 0;
            else
                isActive = true; // unrecognized trailing token: treat tag as present/active

            if (isActive && !active.Contains(tag))
                active.Add(tag);
        }

        active.Sort(StringComparer.Ordinal);
        return active;
    }

    /// <summary>
    /// Build a deterministic PDF-font-name suffix (e.g. <c>-Feat-smcp-zero</c>) from a
    /// raw <c>font-feature-settings</c> value, or "" when no features are active.
    /// </summary>
    public static string BuildFontNameSuffix(string? fontFeatureSettingsValue)
    {
        var tags = ParseActiveTags(fontFeatureSettingsValue);
        if (tags.Count == 0) return "";
        return "-Feat-" + string.Join("-", tags);
    }
}
