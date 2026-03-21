# PDF Features

## Bookmarks

Bookmarks are auto-generated from headings (`<h1>` through `<h6>`). They appear as a clickable sidebar in PDF readers.

## Hyperlinks

```html
<!-- External link (clickable in PDF) -->
<a href="https://example.com">Visit</a>

<!-- Internal link (jumps to element) -->
<a href="#section-2">See Section 2</a>
<h2 id="section-2">Section 2</h2>
```

## Table of Contents

```css
.toc-entry a::after {
    content: leader('.') target-counter(attr(href, url), page);
}
```

Or via API:
```csharp
options.GenerateTableOfContents = true;
```

## PDF/A (Archival)

```csharp
var options = new PdfOptions { PdfAConformance = PdfALevel.A2b };
```

Supports PDF/A-1b, PDF/A-2b, PDF/A-3b (with file attachments for ZUGFeRD).

## PDF/UA (Accessibility)

```csharp
var options = new PdfOptions { TaggedPdf = true };
```

Generates a full structure tree from HTML. `<img alt="...">` maps to alt text. `<th scope>` maps to table header scope.

## Digital Signatures

```csharp
var signer = new PdfSigner();
byte[] signed = signer.Sign(pdfBytes, certificate, new SignOptions
{
    Reason = "Contract approval",
    Location = "New York"
});
```

## Encryption

```csharp
var options = new PdfOptions
{
    Encryption = new EncryptionOptions
    {
        UserPassword = "open-password",
        OwnerPassword = "edit-password",
        AllowPrinting = true,
        AllowCopying = false
    }
};
```

## File Attachments

```csharp
options.Attachments.Add("invoice.xml", xmlBytes, "Alternative");
```

For ZUGFeRD/Factur-X e-invoicing, use PDF/A-3 with an XML attachment.

## QR Codes and Barcodes

```html
<div data-eggpdf-qrcode="https://pay.example.com/inv/123"
     style="width: 80px; height: 80px;"></div>
```

Or via API:
```csharp
options.Overlays.Add(new QrCodeOverlay
{
    Content = "https://verify.example.com",
    Position = new Point(150, 50, Unit.Mm),
    Size = 25
});
```

## PDF Merging

```csharp
var merger = new PdfMerger();
merger.Add(coverPdf);
merger.Add(bodyPdf);
merger.Add(appendixPdf);
byte[] combined = merger.Build();
```
