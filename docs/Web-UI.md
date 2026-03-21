# Web UI

EggPdf includes a simple browser-based interface for converting HTML to PDF. No coding required -- just open the URL, paste HTML, and download your PDF.

## Getting Started

```bash
# Start the service with Web UI
docker run -p 8080:8080 eggpdf/service:latest

# Open in browser
open http://localhost:8080
```

Or use the CLI:
```bash
eggpdf --serve --port 8080
# Open http://localhost:8080
```

## Features

- **HTML Editor**: syntax-highlighted editor for writing HTML
- **CSS Editor**: separate panel for CSS (or include `<style>` in HTML)
- **Live Preview**: see the PDF rendering update as you type
- **Options Panel**: configure page size, orientation, margins, headers/footers
- **Download**: download as PDF or PNG
- **Upload**: upload an existing HTML file
- **Templates**: pre-built templates (invoice, report, letter, certificate) to customize
- **Responsive**: works on desktop and tablet

## Configuration

```bash
# Enable Web UI (default: true)
docker run -e WEBUI_ENABLED=true -p 8080:8080 eggpdf/service:latest

# Disable Web UI (API only)
docker run -e WEBUI_ENABLED=false -p 8080:8080 eggpdf/service:latest
```

## How It Works

The Web UI is a single-page application (HTML/CSS/JavaScript) served by the EggPdf.Service at the root URL (`/`). It calls the same REST API endpoints that any external client would use:

1. User types HTML in the editor
2. On each change (debounced), the UI calls `POST /api/render/image` for preview
3. When user clicks "Download PDF", the UI calls `POST /api/render` and triggers a file download

No separate build system, no Node.js, no npm -- the Web UI is a few static files bundled with the service.

## Use Cases

- **Quick testing**: paste HTML and see how EggPdf renders it
- **Template design**: iterate on invoice/report templates visually
- **Non-developer users**: marketing, HR, finance teams can create simple PDFs
- **Demo**: show EggPdf capabilities without writing code
