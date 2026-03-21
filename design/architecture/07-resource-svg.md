# 07 - Resource Resolution & SVG Engine Architecture

## Part 1: Resource Resolution

### Overview

Any URL in HTML or CSS (`<img src>`, `<link href>`, `@font-face src`, `background-image url()`) goes through the resource resolver to fetch the actual bytes.

```
URL string (relative or absolute)
    |
    v
BaseUrlResolver (resolve relative -> absolute)
    |
    v
CompositeResolver (dispatch by scheme)
    |
    +-- https:// -> HttpResourceResolver
    +-- http://  -> HttpResourceResolver
    +-- file://  -> FileResourceResolver
    +-- data:    -> DataUriResolver
    +-- custom   -> IResourceResolver (user-provided)
    |
    v
byte[] (raw resource data)
    |
    v
Format detection + decoding (image, font, CSS, etc.)
```

### IResourceResolver Interface

```csharp
public interface IResourceResolver
{
    Task<ResourceResult?> ResolveAsync(
        string url,
        ResourceType type,
        CancellationToken ct = default);
}

public class ResourceResult
{
    public byte[] Data { get; }
    public string? MimeType { get; }      // from Content-Type header or data URI
    public string? ResolvedUrl { get; }   // final URL after redirects
}

public enum ResourceType
{
    Image,
    Font,
    StyleSheet,
    Other
}
```

### Built-in Resolvers

#### HttpResourceResolver

```csharp
class HttpResourceResolver : IResourceResolver
{
    HttpClient _httpClient;          // shared, reused across requests
    HttpResourceOptions _options;

    async Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct)
    {
        // 1. Validate URL scheme (http/https only)
        // 2. Check domain allowlist (if configured)
        // 3. Send GET request with:
        //    - User-Agent: "EggPdf/{version}"
        //    - Accept: appropriate for resource type
        //    - Timeout from options
        // 4. Follow redirects (max 5)
        // 5. Check response size against limit
        // 6. Read response bytes
        // 7. Return ResourceResult with data + content-type
    }
}

class HttpResourceOptions
{
    string[]? AllowedDomains { get; set; }      // null = all allowed
    int TimeoutSeconds { get; set; } = 10;
    long MaxResponseSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB
    int MaxRedirects { get; set; } = 5;
    string UserAgent { get; set; } = "EggPdf/{version}";
    bool AllowHttp { get; set; } = true;        // allow non-HTTPS
}
```

#### FileResourceResolver

```csharp
class FileResourceResolver : IResourceResolver
{
    string? _baseDirectory;

    async Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct)
    {
        // 1. Resolve to absolute path
        // 2. Security: ensure path is within _baseDirectory (prevent path traversal)
        //    Reject paths containing ".." that escape base directory
        // 3. Check file exists
        // 4. Read file bytes
        // 5. Detect MIME type from extension
    }
}
```

#### DataUriResolver

```csharp
class DataUriResolver : IResourceResolver
{
    Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct)
    {
        // Parse: data:[<mediatype>][;base64],<data>
        // Example: data:image/png;base64,iVBORw0KGgo...
        // 1. Extract MIME type
        // 2. Check if base64 encoded
        // 3. Decode data (Base64 or URL-encoded)
        // 4. Return ResourceResult
    }
}
```

#### CompositeResolver (Default)

```csharp
class CompositeResolver : IResourceResolver
{
    HttpResourceResolver _http;
    FileResourceResolver _file;
    DataUriResolver _dataUri;

    Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct)
    {
        if (url.StartsWith("data:"))          return _dataUri.ResolveAsync(url, type, ct);
        if (url.StartsWith("http://") ||
            url.StartsWith("https://"))       return _http.ResolveAsync(url, type, ct);
        if (url.StartsWith("file://"))        return _file.ResolveAsync(url, type, ct);

        // Relative path -> resolve against base URL, then dispatch
        string resolved = ResolveRelative(url);
        return ResolveAsync(resolved, type, ct);
    }
}
```

### Base URL Resolution

```csharp
class BaseUrlResolver
{
    // Priority:
    // 1. <base href="..."> from HTML document
    // 2. PdfOptions.BaseUrl
    // 3. Current working directory (for file paths)

    string Resolve(string relativeUrl, string? baseHref, string? optionsBaseUrl)
    {
        string baseUrl = baseHref ?? optionsBaseUrl ?? Directory.GetCurrentDirectory();

        // Standard URL resolution per RFC 3986
        // "../images/logo.png" + "https://example.com/reports/" = "https://example.com/images/logo.png"
        // "logo.png" + "https://example.com/reports/" = "https://example.com/reports/logo.png"
        // "/absolute/path.png" + "https://example.com/" = "https://example.com/absolute/path.png"
    }
}
```

### Resource Cache

```csharp
class ResourceCache
{
    ConcurrentDictionary<string, CacheEntry> _cache;

    // Per-render cache: same URL requested multiple times in one render = one fetch
    // Cross-render cache: configurable TTL, max size

    async Task<ResourceResult?> GetOrFetch(
        string url,
        ResourceType type,
        IResourceResolver resolver,
        CancellationToken ct)
    {
        if (_cache.TryGetValue(url, out var entry) && !entry.IsExpired)
            return entry.Result;

        var result = await resolver.ResolveAsync(url, type, ct);
        if (result != null)
            _cache[url] = new CacheEntry(result, DateTime.UtcNow);

        return result;
    }
}
```

### Image Format Detection

```csharp
static class ImageFormatDetector
{
    // Detect by magic bytes (more reliable than file extension or MIME type)
    static ImageFormat Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
            return ImageFormat.Jpeg;
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G')
            return ImageFormat.Png;
        if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
            return ImageFormat.Gif;
        if (data.Length >= 4 && Encoding.ASCII.GetString(data[..4]) == "RIFF")
            return ImageFormat.WebP;
        if (data.Length >= 4 && data[0] == 'B' && data[1] == 'M')
            return ImageFormat.Bmp;
        if (data.Length >= 5 && (data[0] == '<' || Encoding.UTF8.GetString(data[..5]).TrimStart().StartsWith("<")))
            return ImageFormat.Svg;
        if (data.Length >= 4 && data[0] == 'w' && data[1] == 'O' && data[2] == 'F' && data[3] == 'F')
            return ImageFormat.Woff;
        if (data.Length >= 4 && data[0] == 'w' && data[1] == 'O' && data[2] == 'F' && data[3] == '2')
            return ImageFormat.Woff2;

        return ImageFormat.Unknown;
    }
}
```

---

## Part 2: SVG Engine

### Overview

SVG rendering is a sub-engine that parses SVG XML and converts it to paint commands. SVG content is rendered as **vectors** in PDF (not rasterized).

```
SVG source (inline <svg> or external .svg file)
    |
    v
SvgParser (XML -> SVG DOM)
    |
    v
SvgDocument (tree of SVG elements)
    |
    v
SvgRenderer (SVG DOM -> PaintCommands)
    |
    v
PaintCommandList (vector operations: paths, fills, strokes, text)
```

### SVG Parser

```csharp
class SvgParser
{
    // Parse SVG XML using a lightweight XML parser
    // We don't need a full XML parser -- SVG is well-structured
    // Use the same approach as WeasyPrint's custom SVG parser

    SvgDocument Parse(string svgContent)
    {
        // 1. Parse XML tags, attributes, namespaces
        // 2. Build SVG element tree
        // 3. Resolve <use> references (xlink:href)
        // 4. Parse <defs> for reusable elements
        // 5. Parse CSS in <style> elements within SVG
    }
}
```

### SVG Element Types

```csharp
abstract class SvgElement
{
    string? Id { get; }
    SvgTransform? Transform { get; }
    SvgStyle Style { get; }              // fill, stroke, opacity, etc.
    List<SvgElement> Children { get; }
}

class SvgSvgElement : SvgElement         // <svg> root
{
    float? Width { get; }
    float? Height { get; }
    SvgViewBox? ViewBox { get; }
    PreserveAspectRatio? PreserveAspectRatio { get; }
}

class SvgGroupElement : SvgElement { }   // <g>
class SvgDefsElement : SvgElement { }    // <defs>
class SvgUseElement : SvgElement         // <use>
{
    string Href { get; }                 // reference to element by ID
    float? X { get; }
    float? Y { get; }
}

// Basic shapes
class SvgRectElement : SvgElement        // <rect>
{
    float X, Y, Width, Height;
    float? Rx, Ry;                       // rounded corners
}

class SvgCircleElement : SvgElement      // <circle>
{
    float Cx, Cy, R;
}

class SvgEllipseElement : SvgElement     // <ellipse>
{
    float Cx, Cy, Rx, Ry;
}

class SvgLineElement : SvgElement        // <line>
{
    float X1, Y1, X2, Y2;
}

class SvgPolylineElement : SvgElement    // <polyline>
{
    PointF[] Points;
}

class SvgPolygonElement : SvgElement     // <polygon>
{
    PointF[] Points;
}

// Complex path
class SvgPathElement : SvgElement        // <path>
{
    PathData Data;                       // parsed from d="..."
}

// Text
class SvgTextElement : SvgElement        // <text>
{
    float? X, Y;
    string TextContent;
}

class SvgTspanElement : SvgElement       // <tspan>
{
    float? X, Y, Dx, Dy;
    string TextContent;
}

// Images
class SvgImageElement : SvgElement       // <image>
{
    string Href;                         // image URL
    float X, Y, Width, Height;
}

// Paint servers
class SvgLinearGradientElement : SvgElement
{
    float X1, Y1, X2, Y2;
    GradientUnits GradientUnits;
    List<SvgStopElement> Stops;
}

class SvgRadialGradientElement : SvgElement
{
    float Cx, Cy, R, Fx, Fy;
    List<SvgStopElement> Stops;
}

class SvgStopElement : SvgElement
{
    float Offset;                        // 0.0 - 1.0
    Color StopColor;
    float StopOpacity;
}

class SvgClipPathElement : SvgElement { }
class SvgSymbolElement : SvgElement { }
class SvgMarkerElement : SvgElement { }
```

### SVG Path Data Parser

Parses the `d` attribute of `<path>`:

```csharp
class PathDataParser
{
    // d="M 10 10 L 90 10 L 90 90 Z"
    // d="M10,10 C20,20 40,20 50,10"

    PathData Parse(string d)
    {
        // Commands:
        // M/m = moveto
        // L/l = lineto
        // H/h = horizontal lineto
        // V/v = vertical lineto
        // C/c = cubic bezier curveto
        // S/s = smooth cubic bezier
        // Q/q = quadratic bezier curveto
        // T/t = smooth quadratic bezier
        // A/a = arc
        // Z/z = close path

        // Uppercase = absolute coordinates
        // Lowercase = relative coordinates
    }
}

class PathData
{
    List<PathCommand> Commands { get; }
}

abstract record PathCommand;
record MoveToCommand(float X, float Y) : PathCommand;
record LineToCommand(float X, float Y) : PathCommand;
record CubicBezierCommand(float X1, float Y1, float X2, float Y2, float X, float Y) : PathCommand;
record QuadraticBezierCommand(float X1, float Y1, float X, float Y) : PathCommand;
record ArcCommand(float Rx, float Ry, float Rotation, bool LargeArc, bool Sweep, float X, float Y) : PathCommand;
record ClosePathCommand() : PathCommand;
```

### ViewBox and Coordinate Transformation

```csharp
class ViewBoxResolver
{
    // SVG viewBox maps the SVG coordinate system to the viewport

    // <svg width="200" height="100" viewBox="0 0 400 200">
    // -> SVG content at 400x200 is scaled to fit 200x100 viewport

    Matrix3x2 ComputeTransform(
        SvgViewBox viewBox,
        float viewportWidth, float viewportHeight,
        PreserveAspectRatio? par)
    {
        // 1. Calculate scale factors: sx = viewportWidth / viewBox.Width
        // 2. Apply preserveAspectRatio:
        //    - none: stretch to fill (different sx, sy)
        //    - xMidYMid meet: uniform scale, fit entirely, centered
        //    - xMidYMid slice: uniform scale, fill completely, crop overflow
        //    - xMinYMin, xMaxYMax, etc.: alignment variations
        // 3. Apply viewBox translate: translate(-viewBox.x, -viewBox.y)
        // 4. Return combined matrix
    }
}
```

### SVG Renderer

```csharp
class SvgRenderer
{
    PaintCommandList Render(SvgDocument doc, float viewportWidth, float viewportHeight)
    {
        var commands = new PaintCommandList();

        // 1. Compute viewBox transform
        var transform = ViewBoxResolver.ComputeTransform(
            doc.Root.ViewBox, viewportWidth, viewportHeight, doc.Root.PreserveAspectRatio);

        commands.Add(new PushTransform(transform));

        // 2. Render each child element recursively
        foreach (var child in doc.Root.Children)
            RenderElement(commands, child);

        commands.Add(new PopTransform());

        return commands;
    }

    void RenderElement(PaintCommandList cmds, SvgElement element)
    {
        // 1. Apply element transform if any
        // 2. Apply clipping if clipPath referenced
        // 3. Apply opacity if < 1
        // 4. Render based on element type:
        //    - Shape elements: convert to path, fill + stroke
        //    - Text elements: measure and position glyphs
        //    - Image elements: resolve image, draw at position
        //    - Group elements: render children recursively
        //    - Use elements: resolve reference, render referenced element
    }
}
```

### SVG Style Resolution

SVG elements can be styled via:
1. Presentation attributes: `<rect fill="red" stroke="blue" />`
2. `style` attribute: `<rect style="fill: red" />`
3. `<style>` element within the SVG: `rect { fill: red; }`
4. Inherited from parent: `fill`, `stroke`, `font-*`, `opacity` etc.

Priority: style attribute > `<style>` rules > presentation attributes > inherited

## Testing

| Test Area | Approach |
|-----------|----------|
| HTTP resolver | Mock HTTP server, test timeout, redirects, size limits, domain allowlist |
| File resolver | Test path traversal prevention, missing files, encoding |
| Data URI resolver | Various MIME types, base64 vs url-encoded |
| Base URL resolution | Relative + absolute combinations per RFC 3986 |
| Image format detection | Magic byte detection for all formats |
| Resource cache | Cache hits, TTL expiration, max size eviction |
| SVG parser | Parse known SVGs, verify element tree |
| SVG path data | Parse complex d="" attributes, verify commands |
| ViewBox | Various viewBox + preserveAspectRatio combinations |
| SVG rendering | Known SVGs -> verify paint commands (shapes, positions, colors) |
| SVG in PDF | Render SVG -> verify vector output in PDF (not rasterized) |
| Round-trip | `<img src="test.svg">` -> PDF -> extract vectors -> verify |
