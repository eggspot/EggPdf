# Images & SVG

## Supported Formats

| Format | Support |
|--------|---------|
| JPEG | Full (pass-through, no re-encoding) |
| PNG | Full (with transparency/alpha) |
| GIF | First frame |
| SVG | Full (rendered as vectors, not rasterized) |
| WebP | Full |
| BMP | Full |
| Base64 data URIs | All formats |

## Usage

```html
<!-- File path -->
<img src="logo.png" alt="Logo">

<!-- Base64 -->
<img src="data:image/png;base64,iVBOR..." alt="Inline">

<!-- SVG (inline) -->
<svg width="100" height="100">
    <circle cx="50" cy="50" r="40" fill="blue"/>
</svg>

<!-- SVG (external) -->
<img src="chart.svg" alt="Chart">

<!-- CSS background -->
<div style="background-image: url('pattern.png');">...</div>
```

## SVG in PDF

SVG content is rendered as **vector operations** in the PDF, not rasterized. This means:
- Perfect quality at any zoom level
- Small file size
- Crisp printing at 300+ DPI

## Image Optimization

```csharp
var options = new PdfOptions
{
    ImageOptimization = new ImageOptimizationOptions
    {
        MaxImageDpi = 150,          // Downsample hi-res images
        JpegQuality = 85,          // JPEG compression quality
        ConvertPngToJpeg = true,    // Opaque PNGs -> JPEG (smaller)
    }
};
```

## Broken Image Handling

When an image fails to load, EggPdf shows the `alt` text in a placeholder box (same as browsers). A warning is added to `RenderResult.Warnings`.
