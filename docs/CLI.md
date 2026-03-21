# CLI Tool

EggPdf ships as a standalone command-line tool. No .NET SDK or runtime needed -- download one file and run it.

## Download

Pre-built binaries for every platform are attached to each [GitHub Release](https://github.com/eggspot/EggPdf/releases/latest):

| Platform | Binary |
|----------|--------|
| Windows x64 | `eggpdf-win-x64.exe` |
| Windows ARM64 | `eggpdf-win-arm64.exe` |
| Linux x64 | `eggpdf-linux-x64` |
| Linux ARM64 | `eggpdf-linux-arm64` |
| macOS x64 (Intel) | `eggpdf-osx-x64` |
| macOS ARM64 (Apple Silicon) | `eggpdf-osx-arm64` |

## Or install as dotnet tool

```bash
dotnet tool install -g dotnet-eggpdf
```

## Or use Docker

```bash
docker run -v $(pwd):/work eggpdf/cli /work/input.html -o /work/output.pdf
```

## Usage

```bash
# HTML to PDF
eggpdf input.html -o output.pdf

# HTML to PDF with options
eggpdf input.html -o output.pdf --page-size A4 --margin 2cm --title "My Report"

# HTML to PNG image
eggpdf input.html -o output.png --format png --dpi 150

# Batch convert directory
eggpdf *.html -o output/ --batch

# Watch mode (re-render on file change, auto-refresh)
eggpdf input.html --watch

# Start REST API server (same as Docker service)
eggpdf --serve --port 8080

# Pipe from stdin
echo "<h1>Hello</h1>" | eggpdf - -o output.pdf
cat report.html | eggpdf - -o - > report.pdf
```

## Options

```
eggpdf [input] [options]

Arguments:
  input                  HTML file path, URL, or - for stdin

Options:
  -o, --output <path>    Output file path (- for stdout)
  --format <format>      Output format: pdf (default), png, jpeg
  --page-size <size>     A4, Letter, Legal, A3, A5, or WxH (e.g., 210x297mm)
  --orientation <dir>    portrait (default), landscape
  --margin <value>       Page margins (e.g., 2cm, "20mm 15mm 20mm 15mm")
  --title <title>        PDF document title
  --header <text>        Header text (supports {{page}}, {{pages}}, {{title}}, {{date}})
  --footer <text>        Footer text
  --dpi <number>         Image output DPI (default: 150)
  --batch                Batch convert multiple files
  --watch                Watch mode: re-render on file change
  --serve                Start REST API server
  --port <port>          Server port (default: 8080)
  --pdf-a <level>        PDF/A conformance (1b, 2b, 3b)
  --tagged               Generate tagged PDF (PDF/UA)
  --encrypt              Encrypt with password (prompted interactively)
  --shrink-to-fit        Scale content to fit page width
  --debug-layout         Draw box boundaries
  --verbose              Show render warnings and timing
  --version              Show version
  --help                 Show help
```

## Examples

```bash
# Invoice with headers/footers
eggpdf invoice.html -o invoice.pdf \
  --page-size A4 \
  --footer "Page {{page}} of {{pages}}" \
  --title "Invoice #1234"

# Accessible PDF for government submission
eggpdf report.html -o report.pdf --pdf-a 2b --tagged

# High-DPI image for email embedding
eggpdf card.html -o card.png --format png --dpi 300

# CI/CD pipeline
eggpdf templates/report.html -o artifacts/report.pdf --verbose
```
