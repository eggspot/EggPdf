---
title: EggPdf Demo
emoji: 📄
colorFrom: yellow
colorTo: purple
sdk: docker
pinned: true
app_port: 8080
short_description: Pure C# HTML to PDF — zero-dependency rendering engine by Eggspot
---

# EggPdf Demo

Live demo of [EggPdf](https://github.com/eggspot/EggPdf) — a pure C#, zero-dependency HTML/CSS-to-PDF rendering engine by [Eggspot](https://eggspot.app).

## What's running here

- **WebUI**: Paste HTML, render PDF instantly in the browser
- **REST API**: `POST /api/render` — convert HTML to PDF from any language
- **Health**: `GET /health`

## Quick API test

```bash
curl -X POST https://eggspot-eggpdf-demo.hf.space/api/render \
  -H "Content-Type: application/json" \
  -d '{"html": "<h1>Hello from EggPdf!</h1>"}' \
  -o output.pdf
```

## Links

- 📦 [NuGet](https://www.nuget.org/packages/EggPdf)
- 🐙 [GitHub](https://github.com/eggspot/EggPdf)
- 📖 [Docs](https://eggspot.github.io/EggPdf/)
- 🏢 [Eggspot](https://eggspot.app)
