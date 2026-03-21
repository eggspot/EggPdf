# Spec: Core Primitives

Foundational types used across all pipeline stages. These live in `EggPdf.Core`.

## Units

### Length

All internal measurements are in **CSS pixels (px)**. Conversions happen at resolution time.

```csharp
readonly struct CssLength : IEquatable<CssLength>
{
    public float Value { get; }
    public CssUnit Unit { get; }

    // Resolve to px given context
    public float ToPx(LengthContext ctx);

    // Pre-resolved (already in px)
    public static CssLength Px(float value);
    public static CssLength Zero;
    public static CssLength Auto;       // special sentinel
}

enum CssUnit : byte
{
    // Absolute
    Px,         // 1px = 1/96 inch
    Cm,         // 1cm = 96/2.54 px = 37.795... px
    Mm,         // 1mm = 96/25.4 px = 3.7795... px
    Q,          // 1Q = 1/4 mm = 0.945... px
    In,         // 1in = 96px
    Pc,         // 1pc = 1/6 in = 16px
    Pt,         // 1pt = 1/72 in = 1.333... px

    // Font-relative
    Em,         // relative to element's font-size
    Rem,        // relative to root element's font-size
    Ex,         // x-height of the font
    Ch,         // width of '0' character
    Cap,        // cap height of the font
    Ic,         // width of CJK ideograph character
    Lh,         // line-height of the element
    Rlh,        // line-height of the root element

    // Viewport
    Vw,         // 1% of viewport (page content) width
    Vh,         // 1% of viewport (page content) height
    Vi,         // 1% of viewport inline axis
    Vb,         // 1% of viewport block axis
    Vmin,       // min(vw, vh)
    Vmax,       // max(vw, vh)
    // Small/Large/Dynamic variants (svw, lvh, dvw, etc.)
    // For print: all viewport variants resolve against page content area

    // Container query
    Cqw,        // 1% of container width
    Cqh,        // 1% of container height
    Cqi,        // 1% of container inline size
    Cqb,        // 1% of container block size
    Cqmin,
    Cqmax,

    // Special
    Percent,    // relative to containing block (context-dependent)
    Fr,         // fractional unit (grid only)
    Auto,       // auto keyword
    None,       // none keyword (for max-width/max-height)
}
```

### Unit Conversion Table (to px)

| Unit | Formula | Example |
|------|---------|---------|
| px | 1 | 16px = 16px |
| pt | value * 96/72 | 12pt = 16px |
| pc | value * 96/6 | 1pc = 16px |
| in | value * 96 | 1in = 96px |
| cm | value * 96/2.54 | 1cm = 37.8px |
| mm | value * 96/25.4 | 1mm = 3.78px |
| Q | value * 96/101.6 | 1Q = 0.945px |
| em | value * parent_font_size_px | 1.5em at 16px = 24px |
| rem | value * root_font_size_px | 1rem = 16px (default) |
| % | value/100 * reference_value | 50% of 600px = 300px |
| vw | value/100 * page_content_width | 100vw = page width |
| vh | value/100 * page_content_height | 100vh = page height |

### LengthContext (for resolving relative units)

```csharp
class LengthContext
{
    float FontSize { get; }              // current element's font-size in px
    float RootFontSize { get; }          // <html> font-size in px (default: 16)
    float ContainingBlockWidth { get; }  // for % width resolution
    float ContainingBlockHeight { get; } // for % height resolution
    float ViewportWidth { get; }         // page content area width in px
    float ViewportHeight { get; }        // page content area height in px
    float LineHeight { get; }            // for lh unit
    float RootLineHeight { get; }        // for rlh unit
    FontMetrics FontMetrics { get; }     // for ex, ch, cap, ic units
    float? ContainerWidth { get; }       // for cqw, cqi (null if no container)
    float? ContainerHeight { get; }      // for cqh, cqb
}
```

## Color

```csharp
readonly struct Color : IEquatable<Color>
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte A { get; }      // 255 = fully opaque, 0 = fully transparent

    // Constructors
    public static Color FromRgb(byte r, byte g, byte b);
    public static Color FromRgba(byte r, byte g, byte b, byte a);
    public static Color FromHex(string hex);          // "#fff", "#ff0000", "#ff000080"
    public static Color FromHsl(float h, float s, float l);
    public static Color FromHsla(float h, float s, float l, float a);
    public static Color FromHwb(float h, float w, float b);
    public static Color FromOklch(float l, float c, float h);
    public static Color FromOklab(float l, float a, float b);
    public static Color FromName(string name);         // "red", "blue", "rebeccapurple"
    public static Color? TryParseNamed(string name);

    // Special values
    public static Color Transparent;    // rgba(0, 0, 0, 0)
    public static Color CurrentColor;   // sentinel: resolved during style computation
    public static Color Black;
    public static Color White;

    // Conversion
    public (float C, float M, float Y, float K) ToCmyk();
    public float ToGray();              // luminance
    public string ToHex();

    // Color mixing
    public static Color Mix(Color a, Color b, float ratio, ColorSpace space = ColorSpace.Srgb);

    public bool IsTransparent => A == 0;
    public bool IsOpaque => A == 255;
}

enum ColorSpace
{
    Srgb,
    Oklch,
    Oklab,
    Hsl,
    Hwb,
    DisplayP3
}
```

## Geometry

```csharp
readonly struct PointF : IEquatable<PointF>
{
    public float X { get; }
    public float Y { get; }
}

readonly struct SizeF : IEquatable<SizeF>
{
    public float Width { get; }
    public float Height { get; }
}

readonly struct RectF : IEquatable<RectF>
{
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }

    public float Left => X;
    public float Top => Y;
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public PointF TopLeft => new(X, Y);
    public PointF BottomRight => new(Right, Bottom);
    public SizeF Size => new(Width, Height);

    public bool Contains(PointF point);
    public bool Intersects(RectF other);
    public RectF Intersect(RectF other);
    public RectF Union(RectF other);
    public RectF Inflate(float dx, float dy);
    public RectF Offset(float dx, float dy);
}

readonly struct EdgeSizes : IEquatable<EdgeSizes>
{
    public float Top { get; }
    public float Right { get; }
    public float Bottom { get; }
    public float Left { get; }

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public static EdgeSizes Zero;
    public static EdgeSizes Uniform(float value);
    public static EdgeSizes Symmetric(float vertical, float horizontal);
}

readonly struct CornerRadii : IEquatable<CornerRadii>
{
    public SizeF TopLeft { get; }       // elliptical: width + height
    public SizeF TopRight { get; }
    public SizeF BottomRight { get; }
    public SizeF BottomLeft { get; }

    public bool IsZero => TopLeft == default && TopRight == default &&
                          BottomRight == default && BottomLeft == default;
    public bool IsUniform => TopLeft == TopRight && TopRight == BottomRight &&
                             BottomRight == BottomLeft;
}

readonly struct Matrix3x2 : IEquatable<Matrix3x2>
{
    // Affine transformation matrix
    // | M11 M12 |
    // | M21 M22 |
    // | M31 M32 |   (translation)

    public float M11, M12, M21, M22, M31, M32;

    public static Matrix3x2 Identity;
    public static Matrix3x2 CreateTranslation(float x, float y);
    public static Matrix3x2 CreateScale(float sx, float sy);
    public static Matrix3x2 CreateRotation(float radians);
    public static Matrix3x2 CreateSkew(float radiansX, float radiansY);

    public Matrix3x2 Multiply(Matrix3x2 other);
    public PointF Transform(PointF point);
}
```

## Page Sizes

```csharp
static class PageSizes
{
    // ISO A series (in px at 96 DPI)
    public static readonly SizeF A3 = new(1122.52f, 1587.40f);   // 297 x 420 mm
    public static readonly SizeF A4 = new(595.28f, 841.89f);     // 210 x 297 mm
    public static readonly SizeF A5 = new(419.53f, 595.28f);     // 148 x 210 mm

    // North American
    public static readonly SizeF Letter = new(612f, 792f);        // 8.5 x 11 in
    public static readonly SizeF Legal = new(612f, 1008f);        // 8.5 x 14 in
    public static readonly SizeF Tabloid = new(792f, 1224f);      // 11 x 17 in

    // Other
    public static readonly SizeF B4 = new(708.66f, 1000.63f);    // 250 x 353 mm
    public static readonly SizeF B5 = new(498.90f, 708.66f);     // 176 x 250 mm

    public static SizeF FromName(string name);                     // "A4", "Letter", etc.
    public static SizeF Custom(float width, float height, CssUnit unit);

    // Landscape = swap width and height
    public static SizeF Landscape(SizeF portrait) => new(portrait.Height, portrait.Width);
}
```

## PDF Coordinate Conversion

```csharp
static class PdfCoordinates
{
    // PDF uses points (1pt = 1/72 inch) with bottom-left origin
    // CSS uses px (1px = 1/96 inch) with top-left origin

    public const float PxToPt = 72f / 96f;   // 0.75
    public const float PtToPx = 96f / 72f;   // 1.333...

    public static float ToPdfX(float cssPx) => cssPx * PxToPt;
    public static float ToPdfY(float cssPx, float pageHeightPx) => (pageHeightPx - cssPx) * PxToPt;
    public static float ToPdfLength(float cssPx) => cssPx * PxToPt;

    public static RectF ToPdfRect(RectF cssRect, float pageHeightPx);
}
```

## Warning System

```csharp
class WarningCollector
{
    List<RenderWarning> Warnings { get; } = new();

    void Add(string code, string message, string? element = null, string? selector = null);
    void AddFontNotFound(string familyName, string fallback);
    void AddImageLoadFailed(string url, string reason);
    void AddCssUnsupported(string property);
    void AddLayoutOverflow(int pageNumber);

    bool HasWarnings => Warnings.Count > 0;
}

record RenderWarning(
    RenderWarningLevel Level,
    string Code,
    string Message,
    string? Selector,
    string? Element
);

enum RenderWarningLevel { Info, Warning, Error }

// Standard warning codes
static class WarningCodes
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
```

## Render Limits

```csharp
class RenderLimits
{
    public int MaxPages { get; set; } = 1000;
    public int MaxElements { get; set; } = 100_000;
    public int MaxCssRules { get; set; } = 50_000;
    public int MaxNestingDepth { get; set; } = 100;
    public int MaxLayoutIterations { get; set; } = 5;   // introspection loop
    public TimeSpan MaxRenderTime { get; set; } = TimeSpan.FromSeconds(30);
}
```
