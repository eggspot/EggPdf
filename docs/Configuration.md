# Configuration

## PdfOptions

All rendering is configured through `PdfOptions`:

```csharp
var options = new PdfOptions
{
    // Page
    PageSize = PageSize.A4,               // A4, Letter, Legal, or custom
    Orientation = PageOrientation.Portrait, // Portrait or Landscape
    Margins = new PageMargins(20, 15, 20, 15, Unit.Mm),

    // Typography
    DefaultFont = "Arial",
    DefaultFontSize = 12,

    // Metadata
    Title = "Document Title",
    Author = "Author Name",
    Subject = "Document Subject",
    Keywords = "pdf, html, report",

    // CSS
    MediaType = CssMediaType.Print,        // Print (default) or Screen
    UserStyleSheet = "@page { margin: 2cm; }",  // Additional CSS

    // Fonts
    // options.Fonts.AddDirectory("/usr/share/fonts");
    // options.Fonts.AddFile("./MyFont.ttf");
    // options.Fonts.EnableSubsetting = true;

    // Images
    // options.ImageOptimization.MaxImageDpi = 150;
    // options.ImageOptimization.JpegQuality = 85;

    // Resources
    BaseUrl = "https://example.com/",      // Base URL for relative paths
    // ResourceResolver = new LocalFileResourceResolver("./assets/"),

    // PDF
    PdfVersion = PdfVersion.Pdf17,
    Compression = true,
    Linearize = false,                     // Fast web viewing

    // Headers/Footers (programmatic)
    // Header = new PageHeaderFooter { Center = "{{title}}" },
    // Footer = new PageHeaderFooter { Center = "Page {{page}} of {{pages}}" },

    // Viewer
    // ViewerPreferences = new ViewerPreferences { DisplayDocTitle = true },
};
```

## Reusing the Converter

The converter is **thread-safe** and should be reused:

```csharp
// Register once in DI
services.AddSingleton<IHtmlToPdfConverter>(
    new HtmlToPdfConverter(new PdfOptions { PageSize = PageSize.A4 }));

// Inject and use anywhere -- font cache is shared
public class MyService(IHtmlToPdfConverter converter)
{
    public Task<byte[]> GeneratePdf(string html)
        => converter.RenderAsync(html);
}
```

## Per-Render Overrides

```csharp
// Override options for a single render
byte[] pdf = await converter.RenderAsync(html, new PdfOptions
{
    Orientation = PageOrientation.Landscape
});
```
