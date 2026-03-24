using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Razor;

/// <summary>
/// Default implementation of IRazorToPdfConverter.
/// Renders Razor templates to HTML, then converts to PDF via EggPdf.
/// </summary>
public class RazorToPdfConverter : IRazorToPdfConverter
{
    private readonly Func<string, object?, Task<string>>? _viewRenderer;
    private readonly Func<string, object?, Task<string>>? _stringRenderer;

    /// <summary>
    /// Create a converter with custom view and string renderers.
    /// The renderers take (templateNameOrContent, model) and return HTML.
    /// </summary>
    public RazorToPdfConverter(
        Func<string, object?, Task<string>>? viewRenderer = null,
        Func<string, object?, Task<string>>? stringRenderer = null)
    {
        _viewRenderer = viewRenderer;
        _stringRenderer = stringRenderer;
    }

    public async Task<byte[]> RenderViewAsync(string viewName, object? model = null,
        PdfRenderOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (_viewRenderer == null)
            throw new InvalidOperationException("View renderer not configured. Register a Razor view engine.");

        string html = await _viewRenderer(viewName, model);
        html = ApplyOptions(html, options);
        return HtmlToPdf.Render(html);
    }

    public async Task<byte[]> RenderStringAsync(string razorTemplate, object? model = null,
        PdfRenderOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string html;
        if (_stringRenderer != null)
        {
            html = await _stringRenderer(razorTemplate, model);
        }
        else
        {
            // Fallback: treat the template as plain HTML
            html = razorTemplate;
        }

        html = ApplyOptions(html, options);
        return HtmlToPdf.Render(html);
    }

    public async Task RenderViewToStreamAsync(string viewName, Stream output, object? model = null,
        PdfRenderOptions? options = null, CancellationToken ct = default)
    {
        var bytes = await RenderViewAsync(viewName, model, options, ct);
        await output.WriteAsync(bytes, 0, bytes.Length, ct);
    }

    public async Task RenderViewToFileAsync(string viewName, string filePath, object? model = null,
        PdfRenderOptions? options = null, CancellationToken ct = default)
    {
        var bytes = await RenderViewAsync(viewName, model, options, ct);
#if NET6_0_OR_GREATER
        await File.WriteAllBytesAsync(filePath, bytes, ct);
#else
        using var fs = new FileStream(filePath, FileMode.Create);
        await fs.WriteAsync(bytes, 0, bytes.Length, ct);
#endif
    }

    private static string ApplyOptions(string html, PdfRenderOptions? options)
    {
        if (options == null) return html;

        // Inject user stylesheet if provided
        if (!string.IsNullOrEmpty(options.UserStyleSheet))
        {
            int headClose = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (headClose >= 0)
                html = html.Insert(headClose, $"<style>{options.UserStyleSheet}</style>");
            else
                html = $"<html><head><style>{options.UserStyleSheet}</style></head><body>{html}</body></html>";
        }

        return html;
    }
}
