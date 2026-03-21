using System;
using System.Collections.Generic;
using System.Text;

namespace EggPdf.Text;

/// <summary>
/// Simple word-boundary line breaker. Phase 3 implementation.
/// Full UAX #14 will be added later.
/// </summary>
public static class SimpleLineBreaker
{
    private const float AvgCharWidth = 0.5f; // relative to font size

    /// <summary>Break text into lines that fit within maxWidth.</summary>
    public static List<string> Break(string text, float maxWidth, float fontSize,
        bool preserveNewlines = false)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        // Normalize whitespace
        if (!preserveNewlines)
        {
            text = NormalizeWhitespace(text);
        }

        if (preserveNewlines)
        {
            // Split by newlines first, then wrap each line
            var rawLines = text.Split('\n');
            foreach (var rawLine in rawLines)
            {
                var trimmed = rawLine.TrimEnd('\r');
                var wrapped = WrapLine(trimmed, maxWidth, fontSize);
                lines.AddRange(wrapped);
            }
            return lines;
        }

        return WrapLine(text, maxWidth, fontSize);
    }

    private static List<string> WrapLine(string text, float maxWidth, float fontSize)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }

        var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return lines;

        var currentLine = new StringBuilder(words[0]);

        for (int i = 1; i < words.Length; i++)
        {
            var candidate = currentLine.ToString() + " " + words[i];
            float width = candidate.Length * fontSize * AvgCharWidth;

            if (width <= maxWidth)
            {
                currentLine.Append(' ').Append(words[i]);
            }
            else
            {
                lines.Add(currentLine.ToString());
                currentLine.Clear().Append(words[i]);
            }
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine.ToString());

        return lines;
    }

    private static string NormalizeWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool lastWasSpace = false;

        foreach (char c in text)
        {
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f')
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }
}
