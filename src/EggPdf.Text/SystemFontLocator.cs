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

        foreach (var dir in GetFontDirectories())
        {
            try
            {
                var files = Directory.GetFiles(dir, "*.ttf", SearchOption.AllDirectories);
                var match = files.FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f)
                        .IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;

                // Also check .otf
                var otfFiles = Directory.GetFiles(dir, "*.otf", SearchOption.AllDirectories);
                match = otfFiles.FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f)
                        .IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;
            }
            catch
            {
                // Permission denied, etc.
            }
        }

        return null;
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
                _ => "Arial"
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
                _ => "Helvetica"
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
                _ => "DejaVuSans"
            };
        }
    }
}
