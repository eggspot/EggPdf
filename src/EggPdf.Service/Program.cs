using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();
app.UseCors();

// Serve WebUI static files at root (if wwwroot exists)
var wwwrootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// === Health ===
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    version = "0.1.0",
    uptime = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).ToString(@"d\.hh\:mm\:ss")
}));

// === Info ===
app.MapGet("/api/info", () => Results.Ok(new
{
    version = "0.1.0",
    engine = "EggPdf",
    features = new[] { "html-to-pdf", "multi-page", "css-cascade", "links" },
    limits = new { maxBodySizeMb = 10, timeoutSeconds = 30 }
}));

// === Fonts ===
app.MapGet("/api/fonts", () =>
{
    var fonts = new[] { "Helvetica", "Times-Roman", "Courier",
        "Helvetica-Bold", "Times-Bold", "Courier-Bold" };
    return Results.Ok(new { fonts });
});

// === Render HTML to PDF ===
app.MapPost("/api/render", async (HttpContext ctx) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();

        var request = JsonSerializer.Deserialize<RenderRequest>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (string.IsNullOrEmpty(request?.Html))
            return Results.BadRequest(new { error = "html field is required" });

        var sw = Stopwatch.StartNew();
        var pdf = EggPdf.HtmlToPdf.Render(request.Html);
        sw.Stop();

        ctx.Response.Headers["X-EggPdf-Duration-Ms"] = sw.ElapsedMilliseconds.ToString();
        ctx.Response.Headers["X-EggPdf-Size"] = pdf.Length.ToString();

        return Results.File(pdf, "application/pdf", "output.pdf");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

// === Render HTML to Image ===
app.MapPost("/api/render/image", async (HttpContext ctx) =>
{
    // For now, render to PDF (image rendering will be added later)
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();

    var request = JsonSerializer.Deserialize<RenderRequest>(body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (string.IsNullOrEmpty(request?.Html))
        return Results.BadRequest(new { error = "html field is required" });

    var pdf = EggPdf.HtmlToPdf.Render(request.Html);
    return Results.File(pdf, "application/pdf", "output.pdf");
});

app.Run();

// === Request Models ===
record RenderRequest
{
    public string? Html { get; init; }
    public RenderOptions? Options { get; init; }
}

record RenderOptions
{
    public string? PageSize { get; init; }
    public string? Orientation { get; init; }
    public string? Title { get; init; }
}
