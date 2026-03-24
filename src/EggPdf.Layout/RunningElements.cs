using System;
using System.Collections.Generic;
using EggPdf.Css;

namespace EggPdf.Layout;

/// <summary>
/// CSS Generated Content for Paged Media (GCPM) running headers and footers.
/// Implements position: running(name) and content: element(name).
///
/// Usage in CSS:
///   h1 { position: running(chapter-title); }
///   @page { @top-center { content: element(chapter-title); } }
///
/// The element with position: running() is removed from normal flow
/// and placed in the specified page margin box on each page.
/// </summary>
public class RunningElements
{
    /// <summary>Named running elements: name -> (element content, page where it was set).</summary>
    private readonly Dictionary<string, List<RunningEntry>> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register a running element at a specific page.</summary>
    public void Register(string name, string textContent, int pageIndex)
    {
        if (!_entries.TryGetValue(name, out var list))
        {
            list = new List<RunningEntry>();
            _entries[name] = list;
        }
        list.Add(new RunningEntry { Text = textContent, PageIndex = pageIndex });
    }

    /// <summary>
    /// Get the running element text for a specific page.
    /// Uses "first" assignment semantics: returns the most recent entry at or before the page.
    /// </summary>
    public string? GetForPage(string name, int pageIndex)
    {
        if (!_entries.TryGetValue(name, out var list) || list.Count == 0)
            return null;

        // Find the most recent entry at or before this page
        string? result = null;
        foreach (var entry in list)
        {
            if (entry.PageIndex <= pageIndex)
                result = entry.Text;
        }
        return result;
    }

    /// <summary>Check if a style has position: running(name).</summary>
    public static string? GetRunningName(ComputedStyle? style)
    {
        if (style == null) return null;
        var position = style.Get("position");
        if (string.IsNullOrEmpty(position)) return null;

        // Parse: running(name)
        if (position.StartsWith("running(", StringComparison.OrdinalIgnoreCase) && position.EndsWith(")"))
        {
            return position.Substring(8, position.Length - 9).Trim();
        }
        return null;
    }

    /// <summary>Check if a content value references a running element.</summary>
    public static string? GetElementReference(string? content)
    {
        if (string.IsNullOrEmpty(content)) return null;

        // Parse: element(name)
        if (content.StartsWith("element(", StringComparison.OrdinalIgnoreCase) && content.EndsWith(")"))
        {
            return content.Substring(8, content.Length - 9).Trim();
        }
        return null;
    }

    /// <summary>Get all registered names.</summary>
    public IEnumerable<string> Names => _entries.Keys;
}

internal class RunningEntry
{
    public string Text { get; set; } = "";
    public int PageIndex { get; set; }
}
