using System.Collections.Generic;

namespace EggPdf.Core;

/// <summary>Collects non-fatal warnings during rendering.</summary>
public class WarningCollector
{
    private readonly List<RenderWarning> _warnings = new();

    public IReadOnlyList<RenderWarning> Warnings => _warnings;
    public bool HasWarnings => _warnings.Count > 0;

    public void Add(string code, string message, string? element = null, string? selector = null)
        => _warnings.Add(new RenderWarning(RenderWarningLevel.Warning, code, message, selector, element));

    public void AddFontNotFound(string familyName, string fallback)
        => Add(WarningCodes.FontNotFound, $"Font '{familyName}' not found, using '{fallback}'");

    public void AddImageLoadFailed(string url, string reason)
        => Add(WarningCodes.ImageLoadFailed, $"Image load failed for '{url}': {reason}");

    public void AddCssUnsupported(string property)
        => Add(WarningCodes.CssUnsupported, $"CSS property '{property}' is not supported");
}

public class RenderWarning
{
    public RenderWarningLevel Level { get; }
    public string Code { get; }
    public string Message { get; }
    public string? Selector { get; }
    public string? Element { get; }

    public RenderWarning(RenderWarningLevel level, string code, string message, string? selector, string? element)
    {
        Level = level;
        Code = code;
        Message = message;
        Selector = selector;
        Element = element;
    }
}

public enum RenderWarningLevel { Info, Warning, Error }

/// <summary>Standard warning codes.</summary>
public static class WarningCodes
{
    public const string FontNotFound = "FONT_NOT_FOUND";
    public const string ImageLoadFailed = "IMAGE_LOAD_FAILED";
    public const string StylesheetLoadFailed = "STYLESHEET_LOAD_FAILED";
    public const string CssUnsupported = "CSS_UNSUPPORTED";
    public const string CssParseError = "CSS_PARSE_ERROR";
    public const string LayoutOverflow = "LAYOUT_OVERFLOW";
    public const string CircularImport = "CSS_CIRCULAR_IMPORT";
    public const string LimitExceeded = "LIMIT_EXCEEDED";
    public const string RenderTimeout = "RENDER_TIMEOUT";
    public const string FontLoadFailed = "FONT_LOAD_FAILED";
    public const string ResourceTimeout = "RESOURCE_TIMEOUT";
}
