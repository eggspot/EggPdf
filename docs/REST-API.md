# REST API (EggPdf.Service)

EggPdf.Service is a standalone HTTP microservice that exposes **100% of the library's features** via REST. Any language (Python, Node.js, Go, Java, Ruby) can generate PDFs by calling these endpoints.

## Deployment

```bash
# Docker
docker run -p 8080:8080 eggspot/eggpdf:latest

# Or from source
cd src/EggPdf.Service && dotnet run
```

## Render HTML to PDF

```bash
curl -X POST http://localhost:8080/api/render \
  -H "Content-Type: application/json" \
  -d '{
    "html": "<h1>Hello World</h1><p>From any language!</p>",
    "options": {
      "pageSize": "A4",
      "margins": { "top": 20, "right": 15, "bottom": 20, "left": 15, "unit": "mm" },
      "title": "My Document",
      "footer": { "center": "Page {{page}} of {{pages}}", "fontSize": 8 }
    }
  }' \
  -o output.pdf
```

## Render to Image

```bash
curl -X POST http://localhost:8080/api/render/image \
  -H "Content-Type: application/json" \
  -d '{
    "html": "<h1>Thumbnail</h1>",
    "imageOptions": { "format": "png", "dpi": 150, "pageNumber": 1 }
  }' \
  -o thumbnail.png
```

## Render from URL

```bash
curl -X POST http://localhost:8080/api/render/url \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://example.com/invoice/123",
    "options": { "pageSize": "A4" }
  }' \
  -o invoice.pdf
```

## Merge PDFs

```bash
curl -X POST http://localhost:8080/api/merge \
  -H "Content-Type: application/json" \
  -d '{
    "documents": [
      { "pdf": "'$(base64 -w0 cover.pdf)'", "label": null },
      { "pdf": "'$(base64 -w0 body.pdf)'", "label": { "style": "decimal", "start": 1 } }
    ]
  }' \
  -o merged.pdf
```

## Sign PDF

```bash
curl -X POST http://localhost:8080/api/sign \
  -H "Content-Type: application/json" \
  -d '{
    "pdf": "'$(base64 -w0 document.pdf)'",
    "certificate": "'$(base64 -w0 cert.pfx)'",
    "password": "cert-password",
    "signOptions": { "reason": "Approved", "visible": true }
  }' \
  -o signed.pdf
```

## Full Options Reference

The `options` object in render endpoints accepts ALL PdfOptions:

```json
{
  "pageSize": "A4",
  "orientation": "portrait",
  "margins": { "top": 20, "right": 15, "bottom": 20, "left": 15, "unit": "mm" },
  "defaultFont": "Arial",
  "defaultFontSize": 12,
  "title": "Document Title",
  "author": "Author Name",
  "mediaType": "print",
  "userStyleSheet": "body { font-size: 14px; }",
  "baseUrl": "https://example.com/",
  "pdfVersion": "1.7",
  "compression": true,
  "linearize": false,
  "header": {
    "left": "Company Name",
    "center": "{{title}}",
    "right": "{{date:yyyy-MM-dd}}",
    "fontSize": 9
  },
  "footer": {
    "center": "Page {{page}} of {{pages}}",
    "fontSize": 8,
    "lineAbove": true
  },
  "imageOptimization": {
    "maxImageDpi": 150,
    "jpegQuality": 85,
    "convertPngToJpeg": false
  },
  "generateTableOfContents": false,
  "shrinkToFit": false,
  "taggedPdf": false,
  "pdfAConformance": null,
  "encryption": {
    "userPassword": "",
    "ownerPassword": "",
    "allowPrinting": true,
    "allowCopying": true
  },
  "watermark": {
    "text": "DRAFT",
    "opacity": 0.3,
    "rotation": -45,
    "fontSize": 72
  },
  "debugLayout": false,
  "resourceOptions": {
    "allowExternalUrls": true,
    "allowedDomains": null,
    "timeoutSeconds": 10,
    "maxResponseSizeMb": 50,
    "cacheEnabled": true
  }
}
```

External images (`<img src="https://...">`), fonts (`@font-face url('https://...')`), and stylesheets (`<link href="https://...">`) are fetched automatically when `allowExternalUrls` is true (default). Use `allowedDomains` to restrict which hosts can be fetched for security.

## Authentication

Off by default. Configure via environment variables:

```bash
# API Key auth
docker run -e AUTH_ENABLED=true -e AUTH_MODE=ApiKey -e AUTH_API_KEYS=my-key-123 \
  -p 8080:8080 eggpdf/service:latest

# Then include header:
curl -H "X-Api-Key: my-key-123" ...
```

Supported modes: `ApiKey`, `Jwt`, `Basic`. See [BLUEPRINT.md](https://github.com/eggspot/EggPdf/blob/main/BLUEPRINT.md) for details.

## Response Headers

All render endpoints return these headers:
- `X-EggPdf-Pages` -- page count
- `X-EggPdf-Warnings` -- warning count (details in body if Accept: application/json)
- `X-EggPdf-Duration-Ms` -- render time in milliseconds

## Python Example

```python
import requests

response = requests.post("http://localhost:8080/api/render", json={
    "html": "<h1>Hello from Python</h1>",
    "options": {"pageSize": "A4", "title": "Python PDF"}
})

with open("output.pdf", "wb") as f:
    f.write(response.content)
```

## Node.js Example

```javascript
const response = await fetch("http://localhost:8080/api/render", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    html: "<h1>Hello from Node.js</h1>",
    options: { pageSize: "A4" }
  })
});

const buffer = await response.arrayBuffer();
fs.writeFileSync("output.pdf", Buffer.from(buffer));
```

## Health Check

```bash
curl http://localhost:8080/health
# {"status":"healthy","version":"1.0.0","uptime":"2h 15m","activeRenders":0}
```
