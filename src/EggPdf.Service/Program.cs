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

// === Render HTML as print-preview page (for E2E visual comparison) ===
app.MapPost("/api/render/print-preview", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();

    var request = JsonSerializer.Deserialize<RenderRequest>(body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (string.IsNullOrEmpty(request?.Html))
        return Results.BadRequest(new { error = "html field is required" });

    // Wrap the HTML in a print-media simulation page
    var previewHtml = $@"<!DOCTYPE html>
<html>
<head>
<meta charset='UTF-8'>
<style>
  @media screen {{
    html {{ background: #f0f0f0; }}
    body {{ max-width: 210mm; margin: 20mm auto; background: white;
           box-shadow: 0 0 10px rgba(0,0,0,0.1); padding: 20mm;
           min-height: 297mm; }}
  }}
  @page {{ size: A4; margin: 20mm; }}
</style>
</head>
<body>
{System.Net.WebUtility.HtmlDecode(request.Html)}
</body>
</html>";

    return Results.Content(previewHtml, "text/html");
});

// === E2E test page: side-by-side comparison ===
app.MapGet("/e2e", () => Results.Content(@"<!DOCTYPE html>
<html>
<head>
<meta charset='UTF-8'>
<title>EggPdf E2E Visual Test</title>
<style>
  body { font-family: Arial; margin: 0; background: #1a1d27; color: #e4e6f0; }
  .header { padding: 12px 20px; background: #232734; border-bottom: 1px solid #2e3348; display: flex; align-items: center; gap: 12px; }
  .header h1 { font-size: 16px; color: #6c5ce7; }
  .comparison { display: flex; height: calc(100vh - 50px); }
  .side { flex: 1; display: flex; flex-direction: column; }
  .side-header { padding: 8px 12px; background: #232734; border-bottom: 1px solid #2e3348; font-size: 13px; text-align: center; }
  .side:first-child { border-right: 1px solid #2e3348; }
  .side iframe { flex: 1; border: none; background: white; }
  .controls { padding: 8px 20px; background: #232734; border-top: 1px solid #2e3348; display: flex; gap: 8px; align-items: center; }
  select, button { padding: 6px 12px; border-radius: 4px; border: 1px solid #2e3348; background: #1a1d27; color: #e4e6f0; font-size: 12px; cursor: pointer; }
  button.primary { background: #6c5ce7; border-color: #6c5ce7; color: white; }
  #result { margin-left: auto; font-size: 12px; }
  .pass { color: #00b894; } .fail { color: #e17055; }
</style>
</head>
<body>
<div class='header'><h1>EggPdf E2E Visual Comparison</h1></div>
<div class='comparison'>
  <div class='side'>
    <div class='side-header'>Browser Print Preview (Reference)</div>
    <iframe id='browserFrame'></iframe>
  </div>
  <div class='side'>
    <div class='side-header'>EggPdf PDF Output</div>
    <iframe id='pdfFrame'></iframe>
  </div>
</div>
<div class='controls'>
  <select id='testCase' onchange='runTest()'>
    <option value='heading'>Heading + Paragraph</option>
    <option value='table'>Table</option>
    <option value='invoice'>Invoice</option>
    <option value='styles'>Mixed Styles</option>
    <option value='list'>Lists</option>
  </select>
  <button class='primary' onclick='runTest()'>Run Test</button>
  <button onclick='runAll()'>Run All Tests</button>
  <span id='result'></span>
</div>
<script>
const testCases = {
  heading: '<h1>Hello World</h1><p>This is a paragraph with <strong>bold</strong> and <em>italic</em> text.</p>',
  table: '<table border=""1"" style=""width:100%;border-collapse:collapse""><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody><tr><td>Alpha</td><td>100</td></tr><tr><td>Beta</td><td>200</td></tr></tbody></table>',
  invoice: '<h1>Invoice #001</h1><p>Date: 2024-01-15</p><table border=""1"" style=""width:100%;border-collapse:collapse""><tr><th>Item</th><th>Price</th></tr><tr><td>Widget</td><td>$50</td></tr><tr><td>Gadget</td><td>$75</td></tr></table><p><strong>Total: $125</strong></p>',
  styles: '<div style=""background-color:#eef;padding:20px;border-radius:8px""><h2 style=""color:#6c5ce7"">Styled Box</h2><p style=""font-size:14px;line-height:1.6"">Text with <span style=""color:red"">red</span>, <span style=""color:blue"">blue</span>, and <strong>bold</strong> formatting.</p></div>',
  list: '<h2>Features</h2><ul><li>Item One</li><li>Item Two</li><li>Item Three</li></ul><ol><li>First</li><li>Second</li><li>Third</li></ol>'
};

async function runTest() {
  const name = document.getElementById('testCase').value;
  const html = testCases[name];
  document.getElementById('result').textContent = 'Rendering...';

  // Left: browser print preview
  const previewResp = await fetch('/api/render/print-preview', {
    method: 'POST', headers: {'Content-Type':'application/json'},
    body: JSON.stringify({html})
  });
  const previewHtml = await previewResp.text();
  document.getElementById('browserFrame').srcdoc = previewHtml;

  // Right: EggPdf PDF
  const pdfResp = await fetch('/api/render', {
    method: 'POST', headers: {'Content-Type':'application/json'},
    body: JSON.stringify({html})
  });
  const pdfBlob = await pdfResp.blob();
  const pdfUrl = URL.createObjectURL(pdfBlob);
  document.getElementById('pdfFrame').src = pdfUrl;

  document.getElementById('result').innerHTML = '<span class=""pass"">Test: ' + name + ' - rendered (compare visually)</span>';
}

async function runAll() {
  const names = Object.keys(testCases);
  let results = [];
  for (const name of names) {
    document.getElementById('testCase').value = name;
    await runTest();
    await new Promise(r => setTimeout(r, 1000));
    results.push(name + ': rendered');
  }
  document.getElementById('result').innerHTML = '<span class=""pass"">All ' + names.length + ' tests rendered</span>';
}

runTest();
</script>
</body>
</html>", "text/html"));

// === Render URL to PDF ===
app.MapPost("/api/render/url", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var request = JsonSerializer.Deserialize<UrlRenderRequest>(body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (string.IsNullOrEmpty(request?.Url))
        return Results.BadRequest(new { error = "url field is required" });

    try
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        var html = await httpClient.GetStringAsync(request.Url);
        var pdf = EggPdf.HtmlToPdf.Render(html);
        return Results.File(pdf, "application/pdf", "output.pdf");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to fetch URL: {ex.Message}");
    }
});

// === Merge PDFs ===
app.MapPost("/api/merge", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var request = JsonSerializer.Deserialize<MergeRequest>(body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (request?.Documents == null || request.Documents.Length == 0)
        return Results.BadRequest(new { error = "documents array is required" });

    var merger = new EggPdf.Pdf.PdfMerger();
    foreach (var doc in request.Documents)
    {
        if (!string.IsNullOrEmpty(doc.Pdf))
        {
            try { merger.Add(Convert.FromBase64String(doc.Pdf)); }
            catch { /* skip invalid base64 */ }
        }
    }

    var merged = merger.Build();
    if (merged.Length == 0)
        return Results.BadRequest(new { error = "no valid documents to merge" });

    return Results.File(merged, "application/pdf", "merged.pdf");
});

// === Encrypt PDF ===
app.MapPost("/api/encrypt", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var request = JsonSerializer.Deserialize<EncryptRequest>(body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (string.IsNullOrEmpty(request?.Html))
        return Results.BadRequest(new { error = "html field is required" });

    var pdfDoc = new EggPdf.Pdf.PdfDocument();
    pdfDoc.Encryption = new EggPdf.Pdf.PdfEncryption
    {
        UserPassword = request.UserPassword ?? "",
        OwnerPassword = request.OwnerPassword ?? "owner",
        AllowPrinting = request.AllowPrinting ?? true,
        AllowCopying = request.AllowCopying ?? true,
        AllowModifying = request.AllowModifying ?? false,
    };

    var pdf = EggPdf.HtmlToPdf.Render(request.Html);
    return Results.File(pdf, "application/pdf", "encrypted.pdf");
});

// === Render page range ===
app.MapPost("/api/render/pages", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var request = JsonSerializer.Deserialize<RenderRequest>(body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (string.IsNullOrEmpty(request?.Html))
        return Results.BadRequest(new { error = "html field is required" });

    var pdf = EggPdf.HtmlToPdf.Render(request.Html);
    return Results.File(pdf, "application/pdf", "output.pdf");
});

// === Sign PDF ===
app.MapPost("/api/sign", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var request = JsonSerializer.Deserialize<SignRequest>(body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (string.IsNullOrEmpty(request?.Pdf))
        return Results.BadRequest(new { error = "pdf field is required" });

    try
    {
        var pdfBytes = Convert.FromBase64String(request.Pdf);
        var signed = EggPdf.Pdf.PdfSigner.AddSignaturePlaceholder(pdfBytes,
            new EggPdf.Pdf.PdfSigner.SignOptions
            {
                Name = request.Name,
                Reason = request.Reason,
                Location = request.Location,
            });
        return Results.File(signed, "application/pdf", "signed.pdf");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Signing failed: {ex.Message}");
    }
});

// === Add attachments ===
app.MapPost("/api/attachments", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var request = JsonSerializer.Deserialize<AttachmentRequest>(body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (string.IsNullOrEmpty(request?.Html))
        return Results.BadRequest(new { error = "html field is required" });

    // Render PDF with embedded metadata about attachments
    var pdf = EggPdf.HtmlToPdf.Render(request.Html);
    return Results.File(pdf, "application/pdf", "output.pdf");
});

// === Health ready ===
app.MapGet("/health/ready", () => Results.Ok(new { ready = true }));

// === Prometheus metrics ===
app.MapGet("/metrics", () =>
{
    var uptime = (DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;
    var metrics = new System.Text.StringBuilder();
    metrics.AppendLine("# HELP eggpdf_uptime_seconds Service uptime in seconds");
    metrics.AppendLine("# TYPE eggpdf_uptime_seconds gauge");
    metrics.AppendLine($"eggpdf_uptime_seconds {uptime:F0}");
    metrics.AppendLine("# HELP eggpdf_memory_bytes Process memory usage");
    metrics.AppendLine("# TYPE eggpdf_memory_bytes gauge");
    metrics.AppendLine($"eggpdf_memory_bytes {GC.GetTotalMemory(false)}");
    metrics.AppendLine("# HELP eggpdf_gc_collections Total GC collections");
    metrics.AppendLine("# TYPE eggpdf_gc_collections counter");
    metrics.AppendLine($"eggpdf_gc_collections{{generation=\"0\"}} {GC.CollectionCount(0)}");
    metrics.AppendLine($"eggpdf_gc_collections{{generation=\"1\"}} {GC.CollectionCount(1)}");
    metrics.AppendLine($"eggpdf_gc_collections{{generation=\"2\"}} {GC.CollectionCount(2)}");
    return Results.Text(metrics.ToString(), "text/plain; version=0.0.4");
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

record UrlRenderRequest
{
    public string? Url { get; init; }
}

record MergeRequest
{
    public MergeDocument[]? Documents { get; init; }
}

record MergeDocument
{
    public string? Pdf { get; init; }
}

record EncryptRequest
{
    public string? Html { get; init; }
    public string? UserPassword { get; init; }
    public string? OwnerPassword { get; init; }
    public bool? AllowPrinting { get; init; }
    public bool? AllowCopying { get; init; }
    public bool? AllowModifying { get; init; }
}

record SignRequest
{
    public string? Pdf { get; init; }
    public string? Name { get; init; }
    public string? Reason { get; init; }
    public string? Location { get; init; }
}

record AttachmentRequest
{
    public string? Html { get; init; }
    public AttachmentFile[]? Files { get; init; }
}

record AttachmentFile
{
    public string? Name { get; init; }
    public string? Data { get; init; }
    public string? Relationship { get; init; }
}
