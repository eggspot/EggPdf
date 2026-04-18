# EggPdf — HTML to PDF Service

[![CI](https://github.com/eggspot/EggPdf/actions/workflows/ci.yml/badge.svg)](https://github.com/eggspot/EggPdf/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Pure C# HTML/CSS to PDF rendering engine packaged as a ready-to-run REST API service. No WebKit, no Chromium, no native dependencies.

## Quick Start

```bash
docker run -p 8080:8080 eggspot/eggpdf:latest
```

Open **http://localhost:8080** for the Web UI, or call the REST API from any language.

## REST API

### Convert HTML to PDF

```bash
curl -X POST http://localhost:8080/api/render \
  -H "Content-Type: application/json" \
  -d '{"html": "<h1>Hello World</h1>"}' \
  --output output.pdf
```

### With options

```bash
curl -X POST http://localhost:8080/api/render \
  -H "Content-Type: application/json" \
  -d '{
    "html": "<h1>Invoice</h1>",
    "pageSize": "A4",
    "orientation": "portrait",
    "margins": { "top": 20, "right": 15, "bottom": 20, "left": 15 }
  }' \
  --output invoice.pdf
```

### Health check

```bash
curl http://localhost:8080/health
```

## docker-compose

```yaml
services:
  eggpdf:
    image: eggspot/eggpdf:latest
    ports:
      - "8080:8080"
    environment:
      - AUTH_ENABLED=false
      - WEBUI_ENABLED=true
    volumes:
      - ./fonts:/app/fonts:ro       # mount custom fonts
    restart: unless-stopped
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_URLS` | `http://+:8080` | Listening address |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Runtime environment |
| `WEBUI_ENABLED` | `true` | Enable the browser-based Web UI |
| `AUTH_ENABLED` | `false` | Enable API key authentication |
| `AUTH_KEY` | — | API key (required when `AUTH_ENABLED=true`) |
| `MAX_REQUEST_SIZE_MB` | `10` | Maximum request body size |

## Ports

| Port | Protocol | Description |
|------|----------|-------------|
| `8080` | HTTP | REST API + Web UI |

## Volumes

| Path | Description |
|------|-------------|
| `/app/fonts` | Custom font files (TTF/OTF/WOFF) |
| `/app/templates` | Razor view templates |

## Supported Platforms

`linux/amd64` · `linux/arm64`

Compatible with Docker, Kubernetes, Fly.io, Railway, Render, and Hugging Face Spaces.

## Calling from Any Language

**Python**
```python
import requests
resp = requests.post("http://localhost:8080/api/render",
    json={"html": "<h1>Hello</h1>"})
open("output.pdf", "wb").write(resp.content)
```

**Node.js**
```js
const res = await fetch("http://localhost:8080/api/render", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ html: "<h1>Hello</h1>" }),
});
fs.writeFileSync("output.pdf", Buffer.from(await res.arrayBuffer()));
```

**Go**
```go
resp, _ := http.Post("http://localhost:8080/api/render",
    "application/json",
    strings.NewReader(`{"html":"<h1>Hello</h1>"}`))
io.Copy(file, resp.Body)
```

## Using as a .NET Library

If you're building a .NET app, use the NuGet package instead — no Docker needed:

```bash
dotnet add package EggPdf
```

See the [GitHub repository](https://github.com/eggspot/EggPdf) for full C# usage docs.

## License

MIT — [github.com/eggspot/EggPdf](https://github.com/eggspot/EggPdf)
