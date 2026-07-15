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

    /// <summary>Find a font file by family name. Returns null if not found.</summary>
    public static string? FindFont(string familyName)
    {
        if (string.IsNullOrEmpty(familyName)) return null;

        // Resolve generic families first
        var resolved = ResolveGenericFamily(familyName);
        if (resolved != familyName)
            familyName = resolved;

        var spaceless = familyName.Replace(" ", "");

        // Rank matches so exact names win over substrings ("Arial.ttf" over
        // "Arial Black.ttf") and spaceless forms match ("Segoe UI" -> segoeui.ttf).
        string? best = null;
        int bestRank = int.MaxValue;
        int bestLength = int.MaxValue;

        foreach (var dir in GetFontDirectories())
        {
            try
            {
                var files = Directory.GetFiles(dir, "*.ttf", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(dir, "*.otf", SearchOption.AllDirectories));

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
            }
            catch
            {
                // Permission denied, etc.
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
