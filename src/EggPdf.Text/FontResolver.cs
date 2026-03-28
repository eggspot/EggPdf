using System;
using System.Collections.Concurrent;
using System.IO;
using EggPdf.Text.TrueType;

namespace EggPdf.Text;

/// <summary>
/// Resolves font family names to FontData objects. Caches parsed fonts.
/// Handles generic families, bold/italic variants, and fallback chain.
/// </summary>
public class FontResolver
{
    private readonly ConcurrentDictionary<string, FontData?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolve a font family name to parsed font data.</summary>
    public FontData? Resolve(string familyName, bool bold = false, bool italic = false)
    {
        // Build cache key
        var key = $"{familyName}|{(bold ? "B" : "")}{(italic ? "I" : "")}";

        return _cache.GetOrAdd(key, _ => ResolveInternal(familyName, bold, italic));
    }

    private FontData? ResolveInternal(string familyName, bool bold, bool italic)
    {
        // Try variant names first (e.g., Arial Bold, Arial Italic)
        string[] candidates;
        if (bold && italic)
            candidates = new[] { familyName + "BoldItalic", familyName + "-BoldItalic", familyName + "BI" };
        else if (bold)
            candidates = new[] { familyName + "Bold", familyName + "-Bold", familyName + "bd" };
        else if (italic)
            candidates = new[] { familyName + "Italic", familyName + "-Italic", familyName + "it" };
        else
            candidates = new[] { familyName };

        foreach (var candidate in candidates)
        {
            var path = SystemFontLocator.FindFont(candidate);
            if (path != null)
            {
                var data = LoadFontFile(path);
                if (data != null) return data;
            }
        }

        // Try base name without variant
        var basePath = SystemFontLocator.FindFont(familyName);
        if (basePath != null)
        {
            var data = LoadFontFile(basePath);
            if (data != null) return data;
        }

        // Try generic family resolution
        var resolved = SystemFontLocator.ResolveGenericFamily(familyName);
        if (resolved != familyName)
        {
            var resolvedPath = SystemFontLocator.FindFont(resolved);
            if (resolvedPath != null)
            {
                var data = LoadFontFile(resolvedPath);
                if (data != null) return data;
            }
        }

        return null;
    }

    /// <summary>Load and parse a font file, handling WOFF decoding if needed.</summary>
    private static FontData? LoadFontFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes == null || bytes.Length < 4) return null;

            // Check for WOFF format and decode
            if (WoffDecoder.IsWoff(bytes))
            {
                bytes = WoffDecoder.Decode(bytes);
                if (bytes == null) return null;
            }

            return TtfParser.Parse(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Get the PDF standard font name for a family + weight + style combination.</summary>
    public static string GetPdfStandardFontName(string? familyName, bool bold, bool italic)
    {
        var family = (familyName ?? "").ToLowerInvariant().Trim();

        // Map to standard 14 fonts
        if (family.Contains("courier") || family.Contains("monospace") || family.Contains("consolas"))
        {
            if (bold && italic) return "Courier-BoldOblique";
            if (bold) return "Courier-Bold";
            if (italic) return "Courier-Oblique";
            return "Courier";
        }

        if (family.Contains("times") || family.Contains("serif") && !family.Contains("sans"))
        {
            if (bold && italic) return "Times-BoldItalic";
            if (bold) return "Times-Bold";
            if (italic) return "Times-Italic";
            return "Times-Roman";
        }

        // Default: Helvetica (sans-serif)
        if (bold && italic) return "Helvetica-BoldOblique";
        if (bold) return "Helvetica-Bold";
        if (italic) return "Helvetica-Oblique";
        return "Helvetica";
    }
}
