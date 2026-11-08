using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace EggPdf.Text;

/// <summary>
/// Discovers system fonts on Windows, macOS, and Linux.
/// </summary>
public static class SystemFontLocator
{
    /// <summary>Get platform-specific font directories.</summary>
    public static string[] GetFontDirectories()
    {
        var dirs = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            dirs.Add(@"C:\Windows\Fonts");
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                dirs.Add(Path.Combine(localAppData, @"Microsoft\Windows\Fonts"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            dirs.Add("/System/Library/Fonts");
            dirs.Add("/Library/Fonts");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
                dirs.Add(Path.Combine(home, "Library/Fonts"));
        }
        else // Linux
        {
            dirs.Add("/usr/share/fonts");
            dirs.Add("/usr/local/share/fonts");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                dirs.Add(Path.Combine(home, ".fonts"));
                dirs.Add(Path.Combine(home, ".local/share/fonts"));
            }
        }

        return dirs.Where(Directory.Exists).ToArray();
    }

    /// <summary>
    /// Process-wide cache of discovered font files — the directory walk is
    /// expensive (hundreds of files on Linux with Noto installed) and font
    /// installs during the process lifetime are not a supported scenario.
    /// </summary>
    private static string[]? _cachedFontFiles;
    private static readonly object _fontFilesLock = new object();

    /// <summary>Every .ttf/.otf/.ttc under the system font directories (discovered once per process).</summary>
    internal static string[] GetAllFontFiles()
    {
        var cached = _cachedFontFiles;
        if (cached != null) return cached;
        lock (_fontFilesLock)
        {
            if (_cachedFontFiles != null) return _cachedFontFiles;
            var files = new List<string>();
            foreach (var dir in GetFontDirectories())
            {
                try
                {
                    files.AddRange(Directory.GetFiles(dir, "*.ttf", SearchOption.AllDirectories));
                    files.AddRange(Directory.GetFiles(dir, "*.otf", SearchOption.AllDirectories));
                    files.AddRange(Directory.GetFiles(dir, "*.ttc", SearchOption.AllDirectories));
                }
                catch
                {
                    // Permission denied, etc.
                }
            }
            _cachedFontFiles = files.ToArray();
            return _cachedFontFiles;
        }
    }

    private static readonly string[] FaceSuffixes =
    {
        "", "regular", "bold", "italic", "bolditalic", "oblique", "light", "medium", "semibold", "black", "thin",
        "book", "roman", "b", "i", "z", "bd", "bi", "it",
    };

    /// <summary>
    /// True when a font file for exactly this family is installed: its file name is the family name
    /// (spaces and hyphens ignored) optionally followed by a face suffix such as Regular, Bold or the
    /// Windows short forms (verdanab, georgiaz). Unlike <see cref="FindFont"/>, a family name that only
    /// appears inside another family's file name does not count.
    /// </summary>
    public static bool HasFamily(string familyName) => HasFamilyIn(GetAllFontFiles(), familyName);

    internal static bool HasFamilyIn(IReadOnlyList<string> files, string familyName)
    {
        var family = Normalize(familyName);
        if (family.Length == 0) return false;
        foreach (var file in files)
        {
            var name = Normalize(Path.GetFileNameWithoutExtension(file));
            if (!name.StartsWith(family, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = name.Substring(family.Length);
            foreach (var face in FaceSuffixes)
                if (string.Equals(suffix, face, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string Normalize(string name) => name.Replace(" ", "").Replace("-", "").Replace("_", "");

    /// <summary>Find a font file by family name. Returns null if not found.</summary>
    public static string? FindFont(string familyName)
    {
        if (string.IsNullOrEmpty(familyName)) return null;

        // Resolve generic families first
        var resolved = ResolveGenericFamily(familyName);
        if (resolved != familyName)
            familyName = resolved;

        return FindFontIn(GetAllFontFiles(), familyName);
    }

    /// <summary>
    /// Pick the best-matching file for a family name among <paramref name="files"/>: exact (or space-less)
    /// file-name matches beat substring matches, unrequested Bold/Italic variants are demoted, and
    /// shorter names win ties. Returns null when nothing matches.
    /// </summary>
    internal static string? FindFontIn(IReadOnlyList<string> files, string familyName)
    {
        if (string.IsNullOrEmpty(familyName)) return null;

        var spaceless = familyName.Replace(" ", "");

        // Rank matches so exact names win over substrings ("Arial.ttf" over
        // "Arial Black.ttf") and spaceless forms match ("Segoe UI" -> segoeui.ttf).
        string? best = null;
        int bestRank = int.MaxValue;
        int bestLength = int.MaxValue;

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var nameSpaceless = name.Replace(" ", "").Replace("-", "");

            int rank;
            if (string.Equals(name, familyName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(nameSpaceless, spaceless, StringComparison.OrdinalIgnoreCase))
                rank = 0;
            else if (name.IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     nameSpaceless.IndexOf(spaceless, StringComparison.OrdinalIgnoreCase) >= 0)
                rank = 1;
            else
                continue;

            // Demote variant files (Bold/Italic) the query didn't ask for,
            // so "LiberationSans" finds Regular, not Bold.
            if (name.IndexOf("bold", StringComparison.OrdinalIgnoreCase) >= 0 &&
                familyName.IndexOf("bold", StringComparison.OrdinalIgnoreCase) < 0 &&
                familyName.IndexOf("bd", StringComparison.OrdinalIgnoreCase) < 0)
                rank += 10;
            if ((name.IndexOf("italic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("oblique", StringComparison.OrdinalIgnoreCase) >= 0) &&
                familyName.IndexOf("italic", StringComparison.OrdinalIgnoreCase) < 0 &&
                familyName.IndexOf("oblique", StringComparison.OrdinalIgnoreCase) < 0 &&
                familyName.IndexOf("it", StringComparison.OrdinalIgnoreCase) < 0)
                rank += 10;

            if (rank < bestRank || (rank == bestRank && name.Length < bestLength))
            {
                best = file;
                bestRank = rank;
                bestLength = name.Length;
                if (rank == 0 && name.Length == familyName.Length) return best;
            }
        }

        return best;
    }

    /// <summary>Map generic CSS family names to platform-specific font names.</summary>
    public static string ResolveGenericFamily(string genericName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return genericName.ToLowerInvariant() switch
            {
                "sans-serif" => "Arial",
                "serif" => "Times New Roman",
                "monospace" => "Consolas",
                "cursive" => "Comic Sans MS",
                "fantasy" => "Impact",
                _ => genericName
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return genericName.ToLowerInvariant() switch
            {
                "sans-serif" => "Helvetica",
                "serif" => "Times",
                "monospace" => "Menlo",
                "cursive" => "Apple Chancery",
                "fantasy" => "Papyrus",
                _ => genericName
            };
        }
        else // Linux
        {
            return genericName.ToLowerInvariant() switch
            {
                "sans-serif" => "DejaVuSans",
                "serif" => "DejaVuSerif",
                "monospace" => "DejaVuSansMono",
                "cursive" => "DejaVuSans",
                "fantasy" => "DejaVuSans",
                _ => genericName
            };
        }
    }
}
