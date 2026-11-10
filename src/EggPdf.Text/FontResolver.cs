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
        var key = $"{familyName}|{(bold ? "B" : "")}{(italic ? "I" : "")}";

        return _cache.GetOrAdd(key, _ => ResolveInternal(familyName, bold, italic));
    }

    /// <summary>File/family names a bold and/or italic face of a family is commonly installed under ("Arial Bold", "arialbd", ...).</summary>
    internal static string[] VariantNames(string familyName, bool bold, bool italic)
    {
        if (bold && italic)
            return new[] { familyName + " Bold Italic", familyName + "BoldItalic", familyName + "-BoldItalic", familyName + "bi", familyName + "BI" };
        if (bold)
            return new[] { familyName + " Bold", familyName + "Bold", familyName + "-Bold", familyName + "bd" };
        if (italic)
            return new[] { familyName + " Italic", familyName + "Italic", familyName + "-Italic", familyName + "it" };
        return new[] { familyName };
    }

    private FontData? ResolveInternal(string familyName, bool bold, bool italic)
    {
        foreach (var candidate in VariantNames(familyName, bold, italic))
        {
            var path = SystemFontLocator.FindFont(candidate);
            if (path != null)
            {
                var data = LoadFontFile(path);
                if (data != null) return data;
            }
        }

        var basePath = SystemFontLocator.FindFont(familyName);
        if (basePath != null)
        {
            var data = LoadFontFile(basePath);
            if (data != null) return data;
        }

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

            var fontBytes = WoffDecoder.DecodeIfWoff(bytes);
            return fontBytes == null ? null : TtfParser.Parse(fontBytes);
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

        if (family.Contains("courier") || family.Contains("monospace") || family.Contains("consolas"))
        {
            if (bold && italic) return "Courier-BoldOblique";
            if (bold) return "Courier-Bold";
            if (italic) return "Courier-Oblique";
            return "Courier";
        }

        if (family.Contains("times") || family.Contains("georgia") || family.Contains("serif") && !family.Contains("sans"))
        {
            if (bold && italic) return "Times-BoldItalic";
            if (bold) return "Times-Bold";
            if (italic) return "Times-Italic";
            return "Times-Roman";
        }

        if (bold && italic) return "Helvetica-BoldOblique";
        if (bold) return "Helvetica-Bold";
        if (italic) return "Helvetica-Oblique";
        return "Helvetica";
    }
}
