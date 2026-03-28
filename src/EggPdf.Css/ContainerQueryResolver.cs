using System;
using System.Collections.Generic;

namespace EggPdf.Css;

/// <summary>
/// CSS Container Queries (@container) support.
/// Evaluates container size queries against ancestor container elements.
/// Container queries allow styling based on parent container size rather than viewport.
/// </summary>
public static class ContainerQueryResolver
{
    /// <summary>
    /// Check if a @container rule's condition matches for a given container size.
    /// Supports: min-width, max-width, min-height, max-height, width, height.
    /// </summary>
    public static bool Evaluate(string condition, float containerWidth, float containerHeight)
    {
        if (string.IsNullOrEmpty(condition)) return false;

        // Parse conditions: (min-width: 400px), (max-width: 800px), (width > 300px)
        var parts = condition.Split(new[] { " and ", " AND " }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim().Trim('(', ')');
            if (!EvaluateSingleCondition(trimmed, containerWidth, containerHeight))
                return false;
        }
        return true;
    }

    private static bool EvaluateSingleCondition(string condition, float containerWidth, float containerHeight)
    {
        // Handle: min-width: 400px
        if (condition.StartsWith("min-width:", StringComparison.OrdinalIgnoreCase))
        {
            float value = ParsePxValue(condition.Substring(10));
            return containerWidth >= value;
        }
        if (condition.StartsWith("max-width:", StringComparison.OrdinalIgnoreCase))
        {
            float value = ParsePxValue(condition.Substring(10));
            return containerWidth <= value;
        }
        if (condition.StartsWith("min-height:", StringComparison.OrdinalIgnoreCase))
        {
            float value = ParsePxValue(condition.Substring(11));
            return containerHeight >= value;
        }
        if (condition.StartsWith("max-height:", StringComparison.OrdinalIgnoreCase))
        {
            float value = ParsePxValue(condition.Substring(11));
            return containerHeight <= value;
        }

        // Handle: width > 300px, width >= 300px
        if (condition.IndexOf("width", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return EvaluateComparison(condition, "width", containerWidth);
        }
        if (condition.IndexOf("height", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return EvaluateComparison(condition, "height", containerHeight);
        }

        return true; // Unknown condition: pass
    }

    private static bool EvaluateComparison(string condition, string property, float actualValue)
    {
        int propIdx = condition.IndexOf(property, StringComparison.OrdinalIgnoreCase);
        if (propIdx < 0) return true;

        string rest = condition.Substring(propIdx + property.Length).Trim();

        if (rest.StartsWith(">="))
            return actualValue >= ParsePxValue(rest.Substring(2));
        if (rest.StartsWith("<="))
            return actualValue <= ParsePxValue(rest.Substring(2));
        if (rest.StartsWith(">"))
            return actualValue > ParsePxValue(rest.Substring(1));
        if (rest.StartsWith("<"))
            return actualValue < ParsePxValue(rest.Substring(1));
        if (rest.StartsWith("="))
            return Math.Abs(actualValue - ParsePxValue(rest.Substring(1))) < 1;

        return true;
    }

    private static float ParsePxValue(string value)
    {
        value = value.Trim().Replace("px", "").Trim();
        float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float result);
        return result;
    }
}
