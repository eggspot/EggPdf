# Performance

## Targets

| Scenario | Target Time | Target Memory |
|----------|-------------|---------------|
| Simple page (1 page, text) | < 50ms | < 10MB |
| Invoice (1 page, table + logo) | < 100ms | < 20MB |
| Report (10 pages, mixed) | < 1s | < 50MB |
| Large table (100 pages) | < 5s | < 200MB |

## Streaming Output

EggPdf writes PDF pages progressively to the output stream. Only the current page is in memory at any time. This enables constant-memory rendering of arbitrarily large documents.

## Font Caching

The converter caches font metrics and parsed font files. Reuse a single `HtmlToPdfConverter` instance across renders for maximum performance.

## Thread Safety

`HtmlToPdfConverter` is thread-safe. Use one instance per application, shared across requests.

## Tips

1. **Reuse the converter** -- don't create a new instance per render
2. **Enable font subsetting** -- reduces PDF size significantly, especially with CJK
3. **Set `MaxImageDpi`** -- downsample unnecessarily high-resolution images
4. **Use `ShrinkToFit`** for wide content instead of scaling in CSS
5. **Use streaming output** for large documents served over HTTP

## Warm-Up

First render is slower due to font discovery. In latency-sensitive applications:

```csharp
await converter.WarmUpAsync();  // Pre-load fonts and UA stylesheet
```

<!-- BENCHMARK_START -->
*Detailed benchmarks will be added as the project matures.*
<!-- BENCHMARK_END -->
