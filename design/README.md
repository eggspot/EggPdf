# Design & Technical Documentation

Internal technical documentation for EggPdf contributors and maintainers. **This folder is NOT synced to the public wiki.**

For user-facing documentation, see [`docs/`](../docs/) (synced to [GitHub Wiki](https://github.com/eggspot/EggPdf/wiki)).

## Structure

```
design/
|-- webui/                   # Web UI design sketches and mockups
|   |-- webui-sketch.html    # Interactive responsive mockup (open in browser)
|-- architecture/            # Architecture Decision Records (ADRs)
|-- specs/                   # Detailed technical specs for each component
```

## What Goes Where

| Content | Location | Audience |
|---------|----------|----------|
| How to use EggPdf (install, configure, API) | `docs/` (wiki) | Users / developers |
| UI mockups, wireframes, design sketches | `design/webui/` | Contributors |
| Architecture decisions (why we chose X over Y) | `design/architecture/` | Contributors |
| Component technical specs (parser internals, layout algorithm details) | `design/specs/` | Contributors |
| Project blueprint (features, phases, CSS coverage) | `BLUEPRINT.md` (root) | Everyone |
| Claude Code instructions | `CLAUDE.md` (root) | AI assistants |
