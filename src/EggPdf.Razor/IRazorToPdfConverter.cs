using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Razor;

/// <summary>
/// Interface for rendering Razor templates to PDF.
/// Register via DI: services.AddEggPdfRazor()
/// </summary>
public interface IRazorToPdfConverter
{
    /// <summary>Render a Razor view to PDF bytes.</summary>
    Task<byte[]> RenderViewAsync(string viewName, object? model = null, PdfRenderOptions? options = null, CancellationToken ct = default);

    /// <summary>Render a Razor template string to PDF bytes.</summary>
    Task<byte[]> RenderStringAsync(string razorTemplate, object? model = null, PdfRenderOptions? options = null, CancellationToken ct = default);

    /// <summary>Render a Razor view to a stream.</summary>
    Task RenderViewToStreamAsync(string viewName, Stream output, object? model = null, PdfRenderOptions? options = null, CancellationToken ct = default);

    /// <summary>Render a Razor view to a file.</summary>
    Task RenderViewToFileAsync(string viewName, string filePath, object? model = null, PdfRenderOptions? options = null, CancellationToken ct = default);
}

/// <summary>Options for PDF rendering from Razor templates.</summary>
public class PdfRenderOptions
{
    /// <summary>Page size (A4, Letter, Legal, etc.).</summary>
    public string? PageSize { get; set; }

    /// <summary>Page orientation (portrait, landscape).</summary>
    public string? Orientation { get; set; }

    /// <summary>Additional CSS to inject.</summary>
    public string? UserStyleSheet { get; set; }

    /// <summary>Document title.</summary>
    public string? Title { get; set; }

    /// <summary>Document author.</summary>
    public string? Author { get; set; }
}
