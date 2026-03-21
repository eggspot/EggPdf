# 06 - PDF Backend Architecture

## Overview

The PDF backend serializes paint commands into a valid PDF file. It handles the entire PDF object model, content streams, font embedding, image embedding, and all business PDF features.

```
PaintCommandList (per page)
    |
    v
PdfDocument (builds PDF object graph)
    |
    v
PdfWriter (serializes to bytes)
    |
    v
byte[] / Stream
```

## PDF Object Model

### Object Types (ISO 32000)

```csharp
abstract class PdfObject
{
    int ObjectNumber { get; set; }     // assigned by PdfReferenceTable
    int GenerationNumber => 0;          // always 0 for new documents
    abstract void WriteTo(PdfWriter writer);
}

class PdfDictionary : PdfObject       // << /Key Value /Key2 Value2 >>
class PdfArray : PdfObject            // [ item1 item2 item3 ]
class PdfStream : PdfObject           // << /Length N >> stream ... endstream
class PdfName                         // /Name (not an indirect object)
class PdfString                       // (text) or <hex>
class PdfNumber                       // 42, 3.14
class PdfBoolean                      // true, false
class PdfNull                         // null
class PdfReference                    // N 0 R (indirect reference)
```

### Document Structure

```
PDF File:
  %PDF-1.7
  %binary marker

  Objects (in any order):
    1 0 obj << /Type /Catalog /Pages 2 0 R /Outlines 10 0 R ... >> endobj
    2 0 obj << /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >> endobj
    3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 5 0 R /Resources 6 0 R >> endobj
    ...

  Cross-Reference Table:
    xref
    0 N
    0000000000 65535 f
    0000000015 00000 n
    ...

  Trailer:
    trailer << /Size N /Root 1 0 R /Info 7 0 R >>
    startxref
    byte-offset-of-xref
    %%EOF
```

### PdfDocument Builder

```csharp
class PdfDocument
{
    PdfCatalog Catalog { get; }
    PdfPageTree PageTree { get; }
    PdfInfoDictionary Info { get; }
    PdfReferenceTable References { get; }

    // Add pages
    PdfPage AddPage(PageSize size);

    // Write to output
    void WriteTo(Stream output);
    byte[] ToByteArray();
}

class PdfReferenceTable
{
    // Assigns object numbers and tracks byte offsets
    int RegisterObject(PdfObject obj);
    void RecordOffset(int objectNumber, long byteOffset);
    void WriteXrefTable(PdfWriter writer);
}
```

## Content Streams

### Converting Paint Commands to PDF Operators

```csharp
class PdfContentStreamBuilder
{
    // Graphics state
    void SaveState()                    // q
    void RestoreState()                 // Q
    void SetTransform(Matrix3x2 m)     // a b c d e f cm

    // Color
    void SetFillColor(Color c)         // r g b rg  (RGB)
    void SetStrokeColor(Color c)       // r g b RG
    void SetFillColorCMYK(c,m,y,k)     // c m y k k

    // Path
    void MoveTo(float x, float y)      // x y m
    void LineTo(float x, float y)      // x y l
    void CurveTo(...)                  // x1 y1 x2 y2 x3 y3 c
    void Rectangle(float x, y, w, h)   // x y w h re
    void ClosePath()                   // h
    void Stroke()                      // S
    void Fill()                        // f
    void FillAndStroke()               // B
    void ClipRect(float x, y, w, h)    // x y w h re W n

    // Text
    void BeginText()                   // BT
    void EndText()                     // ET
    void SetFont(PdfName font, float size) // /FontName size Tf
    void SetTextPosition(float x, y)   // x y Td
    void ShowText(byte[] encoded)      // <hex> Tj
    void ShowTextArray(...)            // [...] TJ  (with kerning adjustments)

    // Images
    void DrawImage(PdfName name)       // /ImageName Do

    // Transparency
    void SetGraphicsState(PdfName gs)  // /GSName gs  (for opacity)

    // Marked content (for tagged PDF)
    void BeginMarkedContent(string tag, PdfDictionary? props) // /Tag BMC or /Tag <<...>> BDC
    void EndMarkedContent()            // EMC

    // Build final stream bytes
    byte[] Build();
}
```

### Coordinate System

PDF coordinate system: origin at bottom-left, Y increases upward.
HTML coordinate system: origin at top-left, Y increases downward.

We transform at the page level:
```
// At the start of each page content stream:
// 1 0 0 -1 0 pageHeight cm
// This flips Y so we can use top-left origin throughout our pipeline
```

## Font Embedding

### Strategy

```
For each font used in the document:
1. Collect all Unicode codepoints used with this font
2. Map codepoints to glyph IDs via cmap table
3. Subset the font: extract only used glyphs
4. Embed as CIDFont Type 2 with Identity-H encoding
5. Write ToUnicode CMap for text extraction
```

### PDF Font Object Structure

```
/Type /Font
/Subtype /Type0
/BaseFont /ABCDEF+Arial
/Encoding /Identity-H
/DescendantFonts [<CIDFont ref>]
/ToUnicode <CMap stream ref>

CIDFont:
  /Type /Font
  /Subtype /CIDFontType2
  /BaseFont /ABCDEF+Arial
  /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >>
  /W [<width array>]
  /FontDescriptor <ref>

FontDescriptor:
  /Type /FontDescriptor
  /FontName /ABCDEF+Arial
  /Flags 32
  /FontBBox [...]
  /ItalicAngle 0
  /Ascent 905
  /Descent -212
  /CapHeight 716
  /StemV 80
  /FontFile2 <stream ref>   // subset TrueType data
```

### Standard 14 Fonts (Fallback)

PDF has 14 built-in fonts that don't need embedding:
Helvetica, Helvetica-Bold, Helvetica-Oblique, Helvetica-BoldOblique,
Times-Roman, Times-Bold, Times-Italic, Times-BoldItalic,
Courier, Courier-Bold, Courier-Oblique, Courier-BoldOblique,
Symbol, ZapfDingbats

Used as last-resort fallback when no TrueType font is available.

## Image Embedding

### JPEG (Pass-Through)

```
/Type /XObject
/Subtype /Image
/Width 800
/Height 600
/ColorSpace /DeviceRGB          // or /DeviceGray, /DeviceCMYK
/BitsPerComponent 8
/Filter /DCTDecode              // JPEG data passed through unchanged
stream
  [raw JPEG bytes]
endstream
```

### PNG (Decode + Re-encode)

```
1. Decode PNG: zlib inflate -> unfilter -> raw RGBA pixels
2. Separate alpha channel into SMask stream
3. Compress RGB data with FlateDecode
4. Write Image XObject + SMask XObject

/Type /XObject
/Subtype /Image
/Width 800
/Height 600
/ColorSpace /DeviceRGB
/BitsPerComponent 8
/Filter /FlateDecode
/SMask <alpha mask ref>        // separate transparency stream
stream
  [flate-compressed RGB data]
endstream
```

### EXIF Orientation

Before embedding JPEG, check EXIF orientation tag (1-8) and apply the corresponding transformation matrix in the content stream (not re-encode the JPEG).

## Hyperlinks and Bookmarks

### Link Annotations

```csharp
class LinkAnnotationWriter
{
    // External link: <a href="https://...">
    void WriteUriLink(PdfPage page, RectF rect, string url)
    {
        // /Type /Annot /Subtype /Link
        // /Rect [x1 y1 x2 y2]
        // /A << /Type /Action /S /URI /URI (https://...) >>
        // /Border [0 0 0]  (no visible border)
    }

    // Internal link: <a href="#section-2">
    void WriteGoToLink(PdfPage page, RectF rect, string anchorId, PdfPage targetPage, float targetY)
    {
        // /Type /Annot /Subtype /Link
        // /Rect [x1 y1 x2 y2]
        // /A << /Type /Action /S /GoTo /D [targetPageRef /XYZ null targetY null] >>
    }
}
```

### Bookmark / Outline Tree

```csharp
class BookmarkGenerator
{
    // Auto-generate from <h1>-<h6>
    PdfOutlines Generate(List<HeadingInfo> headings)
    {
        // Build hierarchical tree: h2 nested under h1, h3 under h2, etc.
        // Each outline item: /Title (text) /Dest [pageRef /XYZ x y zoom]
        // /First, /Last, /Next, /Prev, /Parent, /Count for tree structure
    }
}
```

## Streaming Write

For large documents, we write pages to the output stream progressively:

```
1. Write PDF header (%PDF-1.7)
2. For each page:
   a. Build content stream from paint commands
   b. Write all objects for this page (page dict, content stream, images)
   c. Record byte offsets in reference table
   d. Release page data from memory
3. Write shared resources (fonts -- written once, referenced by all pages)
4. Write cross-reference table (accumulated byte offsets)
5. Write trailer and %%EOF
```

This keeps memory bounded: only the current page + shared fonts/images are in memory at any time.

## Compression

### Content Stream Compression

```csharp
// All content streams compressed with FlateDecode (zlib)
using var deflate = new DeflateStream(output, CompressionLevel.Optimal);
deflate.Write(contentStreamBytes);

// Stream object: /Filter /FlateDecode /Length compressedLength
```

### Object Streams (PDF 1.5+)

Multiple small objects packed into a single compressed stream:
```
/Type /ObjStm
/N 10           // 10 objects in this stream
/First 50       // byte offset of first object data
/Filter /FlateDecode
```

Reduces file size by 10-30% for documents with many small objects.

## Security

### AES-256 Encryption

```csharp
class PdfEncryptor
{
    // Encryption handler V5 R6 (AES-256)
    void Encrypt(PdfDocument doc, string userPassword, string ownerPassword, PdfPermissions permissions)
    {
        // 1. Generate random encryption key (32 bytes)
        // 2. Compute U, UE (user password validation + encrypted key)
        // 3. Compute O, OE (owner password validation + encrypted key)
        // 4. Set permission flags in /P entry
        // 5. Add /Encrypt dictionary to trailer
        // 6. Encrypt all string and stream objects with AES-256-CBC
    }
}
```

### Digital Signatures

```csharp
class PdfSigner
{
    byte[] Sign(byte[] pdfBytes, X509Certificate2 certificate, SignOptions options)
    {
        // 1. Add signature form field (/FT /Sig)
        // 2. Reserve space for signature value (/Contents with placeholder)
        // 3. Set /ByteRange covering all bytes except the signature placeholder
        // 4. Compute hash of ByteRange bytes
        // 5. Create CMS/PKCS#7 detached signature
        // 6. Insert signature into placeholder
        // 7. Optionally add visible appearance (signer name, date, image)
    }
}
```

## Tagged PDF (PDF/UA)

### Structure Tree

```csharp
class StructureTreeWriter
{
    // Maps paint commands' BeginStructureElement/EndStructureElement
    // to PDF structure tree

    // /StructTreeRoot in document catalog
    //   /K [<StructElem refs>]
    //
    // Each StructElem:
    //   /Type /StructElem
    //   /S /P          (or /H1, /Table, /TR, /TD, /Figure, /Link, etc.)
    //   /P <parent ref>
    //   /K [<children or marked content refs>]
    //   /Alt (alt text)   // for Figure elements
    //   /Lang (en-US)     // language override
}
```

### HTML -> PDF Structure Mapping

| HTML Element | PDF Structure Tag |
|-------------|------------------|
| `<p>` | `/P` |
| `<h1>`-`<h6>` | `/H1`-`/H6` |
| `<div>`, `<section>` | `/Div` or `/Sect` |
| `<table>` | `/Table` |
| `<tr>` | `/TR` |
| `<th>` | `/TH` |
| `<td>` | `/TD` |
| `<ul>`, `<ol>` | `/L` |
| `<li>` | `/LI` |
| `<img>` | `/Figure` (with `/Alt` from alt attribute) |
| `<a>` | `/Link` |
| `<span>`, `<em>`, `<strong>` | `/Span` |
| `<blockquote>` | `/BlockQuote` |
| `<code>` | `/Code` |
| Text content | `/Span` or direct marked content |

## Testing

| Test Area | Approach |
|-----------|----------|
| PDF structure | Parse output PDF, verify xref offsets, trailer, object count |
| Content stream | Verify operators for known input (text, rectangles, images) |
| Font embedding | Subset -> embed -> extract from PDF -> verify glyphs |
| Image embedding | Embed JPEG/PNG -> extract from PDF -> verify pixel data |
| ToUnicode | Extract text from PDF -> compare to input HTML text |
| Links | Verify link annotations exist with correct URLs/destinations |
| Bookmarks | Verify outline tree matches heading structure |
| Encryption | Encrypt -> verify can open with user password, not without |
| Signatures | Sign -> verify signature is valid with certificate |
| Tagged PDF | Verify structure tree matches HTML DOM structure |
| PDF/A | Validate with veraPDF (external validator) |
| Cross-reader | Open in Adobe, Chrome, Firefox, SumatraPDF, macOS Preview |
