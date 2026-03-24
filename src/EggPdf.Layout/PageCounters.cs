using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EggPdf.Layout;

/// <summary>
/// CSS page counters: counter(page), counter(pages), target-counter().
/// Resolves page number references in content properties.
///
/// Usage:
///   @page { @bottom-center { content: "Page " counter(page) " of " counter(pages); } }
///   .toc a::after { content: leader('.') target-counter(attr(href), page); }
/// </summary>
public class PageCounters
{
    private int _totalPages;
    private readonly Dictionary<string, int> _elementPageMap = new(); // element id -> page number

    /// <summary>Set the total page count (known after layout).</summary>
    public void SetTotalPages(int total) => _totalPages = total;

    /// <summary>Register an element's page number (for target-counter).</summary>
    public void RegisterElement(string elementId, int pageNumber)
    {
        if (!string.IsNullOrEmpty(elementId))
            _elementPageMap[elementId] = pageNumber;
    }

    /// <summary>Get page number for an element ID (1-based).</summary>
    public int GetPageForElement(string elementId)
    {
        return _elementPageMap.TryGetValue(elementId, out int page) ? page : 0;
    }

    /// <summary>
    /// Resolve counter references in a content string.
    /// Replaces counter(page), counter(pages), target-counter(attr(href), page).
    /// </summary>
    public string ResolveContent(string content, int currentPage)
    {
        if (string.IsNullOrEmpty(content)) return "";

        var result = content;

        // Replace counter(page)
        result = result.Replace("counter(page)", currentPage.ToString());

        // Replace counter(pages)
        result = result.Replace("counter(pages)", _totalPages.ToString());

        // Replace target-counter(attr(href), page) — simplified
        // Full syntax: target-counter(attr(href, url), page)
        var tcMatch = Regex.Match(result, @"target-counter\(([^,]+),\s*page\)");
        if (tcMatch.Success)
        {
            var targetRef = tcMatch.Groups[1].Value.Trim();
            // If it's attr(href), the caller needs to resolve the attribute value
            // For now, return the placeholder
            result = result.Replace(tcMatch.Value, "[page ref]");
        }

        return result;
    }

    /// <summary>
    /// Generate a leader string (dot leaders for TOC).
    /// CSS: leader('.') produces "............" filling available space.
    /// </summary>
    public static string GenerateLeader(char leaderChar, int approximateLength)
    {
        if (approximateLength <= 0) return "";
        return new string(leaderChar, approximateLength);
    }

    /// <summary>
    /// Resolve a target-counter reference for a specific href.
    /// Returns the page number string, or empty if not found.
    /// </summary>
    public string ResolveTargetCounter(string href)
    {
        if (string.IsNullOrEmpty(href)) return "";

        // Strip # prefix for internal links
        string id = href.StartsWith("#") ? href.Substring(1) : href;

        int page = GetPageForElement(id);
        return page > 0 ? page.ToString() : "";
    }

    /// <summary>Generate page label text (Roman numerals, alphabetic, etc.).</summary>
    public static string FormatPageNumber(int pageNumber, string style)
    {
        switch (style?.ToLowerInvariant())
        {
            case "lower-roman":
                return ToRoman(pageNumber).ToLowerInvariant();
            case "upper-roman":
                return ToRoman(pageNumber);
            case "lower-alpha":
                return pageNumber <= 26 ? ((char)('a' + pageNumber - 1)).ToString() : pageNumber.ToString();
            case "upper-alpha":
                return pageNumber <= 26 ? ((char)('A' + pageNumber - 1)).ToString() : pageNumber.ToString();
            default:
                return pageNumber.ToString();
        }
    }

    private static string ToRoman(int number)
    {
        if (number <= 0 || number > 3999) return number.ToString();

        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var symbols = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            while (number >= values[i])
            {
                result.Append(symbols[i]);
                number -= values[i];
            }
        }
        return result.ToString();
    }
}
