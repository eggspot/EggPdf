using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EggPdf.AspNetCore;

/// <summary>
/// IActionResult that renders an HTML string as a PDF download.
/// Usage: return new PdfResult(html, "report.pdf");
/// </summary>
public class PdfResult : IActionResult
{
    private readonly string _html;
    private readonly string _fileName;

    public PdfResult(string html, string fileName = "document.pdf")
    {
        _html = html;
        _fileName = fileName;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        var pdf = HtmlToPdf.Render(_html);

        response.ContentType = "application/pdf";
        response.Headers["Content-Disposition"] = $"attachment; filename=\"{_fileName}\"";
        response.ContentLength = pdf.Length;

        await response.Body.WriteAsync(pdf, 0, pdf.Length);
    }
}

/// <summary>
/// IActionResult that renders a Razor view as a PDF download.
/// Usage: return new RazorPdfResult("Invoice", model, "invoice.pdf");
/// Requires IRazorToPdfConverter to be registered in DI.
/// </summary>
public class RazorPdfResult : IActionResult
{
    private readonly string _viewName;
    private readonly object? _model;
    private readonly string _fileName;
    private readonly Razor.PdfRenderOptions? _options;

    public RazorPdfResult(string viewName, object? model = null,
        string fileName = "document.pdf", Razor.PdfRenderOptions? options = null)
    {
        _viewName = viewName;
        _model = model;
        _fileName = fileName;
        _options = options;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var converter = (Razor.IRazorToPdfConverter?)context.HttpContext.RequestServices
            .GetService(typeof(Razor.IRazorToPdfConverter));

        if (converter == null)
            throw new System.InvalidOperationException(
                "IRazorToPdfConverter not registered. Call services.AddEggPdfRazor() in Startup.");

        var pdf = await converter.RenderViewAsync(_viewName, _model, _options);

        var response = context.HttpContext.Response;
        response.ContentType = "application/pdf";
        response.Headers["Content-Disposition"] = $"attachment; filename=\"{_fileName}\"";
        response.ContentLength = pdf.Length;

        await response.Body.WriteAsync(pdf, 0, pdf.Length);
    }
}
