# Spec: PDF Content Stream Operators

Complete mapping from paint commands to PDF content stream operators.

## Graphics State Operators

| Operator | Operands | Paint Command | Description |
|----------|----------|---------------|-------------|
| `q` | - | `PushClip`, `PushOpacity`, `PushTransform` | Save graphics state |
| `Q` | - | `PopClip`, `PopOpacity`, `PopTransform` | Restore graphics state |
| `cm` | a b c d e f | `PushTransform(matrix)` | Concatenate transformation matrix |
| `w` | lineWidth | `StrokeRect`, `StrokePath` | Set line width |
| `J` | lineCap | - | Set line cap style (0=butt, 1=round, 2=square) |
| `j` | lineJoin | - | Set line join style (0=miter, 1=round, 2=bevel) |
| `M` | miterLimit | - | Set miter limit |
| `d` | dashArray dashPhase | `StrokeRect` (dashed borders) | Set line dash pattern |
| `gs` | name | `PushOpacity` | Set graphics state from ExtGState resource |

## Color Operators

| Operator | Operands | Paint Command | Description |
|----------|----------|---------------|-------------|
| `rg` | r g b | `FillRect`, `FillPath`, `DrawText` | Set fill color (RGB, 0-1 range) |
| `RG` | r g b | `StrokeRect`, `StrokePath` | Set stroke color (RGB) |
| `k` | c m y k | `FillRect` (CMYK mode) | Set fill color (CMYK, 0-1 range) |
| `K` | c m y k | `StrokeRect` (CMYK mode) | Set stroke color (CMYK) |
| `g` | gray | `FillRect` (grayscale) | Set fill color (gray, 0-1) |
| `G` | gray | `StrokeRect` (grayscale) | Set stroke color (gray) |
| `cs` | name | - | Set fill color space (for ICCBased, Separation) |
| `CS` | name | - | Set stroke color space |
| `sc` | c1 ... cn | - | Set fill color in current color space |
| `SC` | c1 ... cn | - | Set stroke color in current color space |
| `scn` | c1 ... cn [name] | - | Set fill color (with pattern/separation name) |
| `SCN` | c1 ... cn [name] | - | Set stroke color (with pattern/separation name) |

### Color Conversion

```csharp
// CSS Color -> PDF operators
static class PdfColorWriter
{
    static void WriteFillColor(PdfContentStreamBuilder builder, Color color)
    {
        if (color.A < 255)
        {
            // Need ExtGState for transparency
            var gs = GetOrCreateExtGState(color.A / 255f);
            builder.SetGraphicsState(gs);  // gs operator
        }

        // RGB: r g b rg
        builder.Emit($"{color.R / 255f:F3} {color.G / 255f:F3} {color.B / 255f:F3} rg");
    }
}
```

## Path Operators

| Operator | Operands | Paint Command | Description |
|----------|----------|---------------|-------------|
| `m` | x y | `FillPath`, `StrokePath` | Move to point (start new subpath) |
| `l` | x y | `FillPath`, `StrokePath` | Line to point |
| `c` | x1 y1 x2 y2 x3 y3 | `FillPath`, `StrokePath` | Cubic Bezier curve |
| `v` | x2 y2 x3 y3 | - | Cubic Bezier (first control point = current point) |
| `y` | x1 y1 x3 y3 | - | Cubic Bezier (second control point = end point) |
| `h` | - | - | Close subpath (line to start) |
| `re` | x y w h | `FillRect`, `StrokeRect`, `PushClip` | Rectangle |
| `S` | - | `StrokeRect`, `StrokePath` | Stroke path |
| `s` | - | - | Close and stroke |
| `f` | - | `FillRect`, `FillPath` | Fill path (non-zero winding) |
| `f*` | - | - | Fill path (even-odd rule) |
| `B` | - | - | Fill and stroke |
| `B*` | - | - | Fill (even-odd) and stroke |
| `b` | - | - | Close, fill, and stroke |
| `n` | - | `PushClip` | End path without filling or stroking |
| `W` | - | `PushClip` | Set clipping path (non-zero) |
| `W*` | - | - | Set clipping path (even-odd) |

### Border Drawing

```
CSS border -> PDF path operators

Solid border:
  x y w h re S                          (rectangle stroke)

Dashed border:
  [dashLen gapLen] 0 d                   (set dash pattern)
  x y m (x+w) y l S                     (stroke top)
  [dashLen gapLen] 0 d                   (reset for each side)
  ... (repeat for each side)

Dotted border:
  [1 spacing] 0 d  1 J                  (round line cap for dots)
  x y m (x+w) y l S

Double border:
  (draw two parallel lines with gap)

Rounded corners (border-radius):
  (use cubic bezier curves to approximate arcs)
  Kappa constant: 0.5522847498
  For quarter circle: c (x+r*kappa) y x (y+r*kappa) x (y+r)
```

## Text Operators

| Operator | Operands | Paint Command | Description |
|----------|----------|---------------|-------------|
| `BT` | - | `DrawText` (begin) | Begin text object |
| `ET` | - | `DrawText` (end) | End text object |
| `Tf` | font size | `DrawText` | Set font and size |
| `Td` | tx ty | `DrawText` | Move text position |
| `Tm` | a b c d e f | `DrawText` | Set text matrix (for positioned/rotated text) |
| `Tj` | string | `DrawText` | Show text string |
| `TJ` | array | `DrawText` | Show text with kerning adjustments |
| `Tc` | charSpace | - | Set character spacing |
| `Tw` | wordSpace | - | Set word spacing |
| `Tz` | scale | - | Set horizontal scaling |
| `TL` | leading | - | Set text leading (line spacing) |
| `Tr` | render | - | Set text rendering mode (0=fill, 1=stroke, 2=fill+stroke) |
| `Ts` | rise | - | Set text rise (for superscript/subscript) |

### Text Rendering

```csharp
// Render a GlyphRun as PDF text operators
static void WriteText(PdfContentStreamBuilder builder, DrawText cmd)
{
    builder.Emit("BT");

    // Set font
    string fontName = GetPdfFontName(cmd.Font);
    float pdfSize = cmd.FontSize * PdfCoordinates.PxToPt;
    builder.Emit($"/{fontName} {pdfSize:F2} Tf");

    // Position (PDF coordinates: bottom-left origin)
    float pdfX = PdfCoordinates.ToPdfX(cmd.X);
    float pdfY = PdfCoordinates.ToPdfY(cmd.Y, pageHeight);
    builder.Emit($"{pdfX:F2} {pdfY:F2} Td");

    // Text with kerning adjustments
    if (cmd.Glyphs.HasKerning)
    {
        // TJ array: [(glyphs) kernAdjust (more glyphs) kernAdjust ...]
        // Positive kern value = move LEFT (reduce spacing)
        builder.Emit("[");
        WriteGlyphsWithKerning(builder, cmd.Glyphs, cmd.Font);
        builder.Emit("] TJ");
    }
    else
    {
        // Simple text string (hex-encoded glyph IDs)
        builder.Emit($"<{EncodeGlyphs(cmd.Glyphs)}> Tj");
    }

    builder.Emit("ET");
}

// Glyph encoding for CIDFont (Identity-H)
static string EncodeGlyphs(GlyphRun glyphs)
{
    var sb = new StringBuilder();
    foreach (ushort glyphId in glyphs.GlyphIds)
        sb.Append(glyphId.ToString("X4"));  // 2-byte hex per glyph
    return sb.ToString();
}
```

## Image Operators

| Operator | Operands | Paint Command | Description |
|----------|----------|---------------|-------------|
| `Do` | name | `DrawImage` | Draw XObject (image or form) |

### Image Drawing

```csharp
// Draw an image at a specific position and size
static void WriteImage(PdfContentStreamBuilder builder, DrawImage cmd, float pageHeight)
{
    builder.Emit("q");  // save state

    // Transform: scale and position the image
    // PDF images are 1x1 by default, so we scale to desired size
    float w = cmd.Dest.Width * PdfCoordinates.PxToPt;
    float h = cmd.Dest.Height * PdfCoordinates.PxToPt;
    float x = PdfCoordinates.ToPdfX(cmd.Dest.X);
    float y = PdfCoordinates.ToPdfY(cmd.Dest.Bottom, pageHeight);

    // cm: w 0 0 h x y cm (scale + translate)
    builder.Emit($"{w:F2} 0 0 {h:F2} {x:F2} {y:F2} cm");

    // Draw the image
    string imageName = GetImageResourceName(cmd.Image);
    builder.Emit($"/{imageName} Do");

    builder.Emit("Q");  // restore state
}
```

## Marked Content Operators (Tagged PDF)

| Operator | Operands | Paint Command | Description |
|----------|----------|---------------|-------------|
| `BMC` | tag | `BeginStructureElement` | Begin marked content (simple) |
| `BDC` | tag properties | `BeginStructureElement` | Begin marked content with properties |
| `EMC` | - | `EndStructureElement` | End marked content |

### Tagged PDF Content Stream

```
% Tagged paragraph
/P <</MCID 0>> BDC
  BT /F1 12 Tf 72 720 Td (Hello World) Tj ET
EMC

% Tagged heading
/H1 <</MCID 1>> BDC
  BT /F1 24 Tf 72 700 Td (Chapter 1) Tj ET
EMC

% Tagged image
/Figure <</MCID 2>> BDC
  q 200 0 0 150 72 500 cm /Img1 Do Q
EMC
```

The MCID (Marked Content ID) links content stream elements to the structure tree.

## ExtGState (Extended Graphics State)

For transparency, we define ExtGState resources:

```
% ExtGState for 50% opacity
/GS1 << /Type /ExtGState /ca 0.5 /CA 0.5 >>

% Usage in content stream:
/GS1 gs    % apply 50% opacity
... drawing operators ...
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `/CA` | number | Stroke opacity (0-1) |
| `/ca` | number | Fill opacity (0-1) |
| `/BM` | name | Blend mode (/Normal, /Multiply, /Screen, etc.) |
| `/SMask` | dict | Soft mask (for transparency groups) |

## Coordinate System Transform

Applied at the start of every page content stream:

```
% Flip Y axis so origin is top-left (matches CSS coordinate system)
1 0 0 -1 0 {pageHeightInPt} cm

% Now:
% (0, 0) is top-left
% X increases to the right
% Y increases downward
% All our layout coordinates work directly
```

## Operator Frequency (typical page)

| Operator | Typical Count | Used For |
|----------|--------------|----------|
| `q` / `Q` | 20-50 pairs | Clipping, opacity, transforms |
| `rg` | 10-30 | Fill colors |
| `re` | 10-30 | Backgrounds, borders |
| `f` | 10-30 | Fill rectangles |
| `BT` / `ET` | 5-20 pairs | Text blocks |
| `Tf` | 5-15 | Font changes |
| `Td` | 5-20 | Text positioning |
| `Tj` / `TJ` | 5-20 | Text strings |
| `cm` | 2-10 | Coordinate transforms |
| `Do` | 0-5 | Images |
| `m` / `l` / `c` | 0-50 | Borders, SVG paths |
| `S` | 0-20 | Border strokes |
| `gs` | 0-5 | Opacity changes |
| `BDC` / `EMC` | 0-50 pairs | Tagged PDF (if enabled) |
