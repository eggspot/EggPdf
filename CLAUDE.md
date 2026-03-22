# EggPdf -- Claude Code Instructions

## Project Overview

EggPdf is a pure C#, zero-dependency HTML/CSS-to-PDF rendering engine targeting Chrome Print parity.
Open source under MIT license: https://github.com/eggspot/EggPdf

**PRs auto-merge.** Be extra careful -- all tests must pass before any PR.

## Architecture

8-stage pipeline: HTML Parse -> CSS Parse -> Style Resolve -> Box Generate -> Layout -> Fragment -> Paint -> PDF Write

See `BLUEPRINT.md` for full architecture, CSS coverage, component specs, and phase plan.
See `design/architecture/` for detailed component specs (8 docs, ~3,000 lines).

## Core Principles

1. **Zero external dependencies** -- pure managed C#, BCL only. No NuGet packages in the core library.
2. **Multi-target** -- must compile on netstandard2.0, netstandard2.1, net6.0, net8.0, net9.0.
3. **Performance matters** -- benchmark every hot-path change. No regressions allowed.
4. **Test first, always** -- write failing test -> implement -> verify -> fix -> repeat.
5. **Infallible parsers** -- HTML/CSS parsers never throw. Produce error nodes / silently ignore.
6. **Graceful degradation** -- unknown CSS ignored, missing resources produce warnings not crashes.

## Development Workflow (STRICT)

Every code change follows this loop:

```
1. Write test (must FAIL)
2. Write code (minimal implementation)
3. Run test (must PASS)
4. Run ALL tests (no regressions)
5. Benchmark if hot path (no regressions)
6. Commit with conventional prefix
```

**Never commit with failing tests. Never skip tests. Never skip benchmarks for hot-path changes.**

## Conventional Commits

- `feat:` -- new feature
- `fix:` -- bug fix
- `perf:` -- performance improvement
- `test:` -- test-only change
- `refactor:` -- restructure without behavior change
- `docs:` -- documentation only
- `chore:` -- build, CI, tooling

## Project Structure (actual, as implemented)

```
src/
  EggPdf/              -- main library (public API: HtmlToPdf.RenderAsync)
  EggPdf.Core/         -- shared primitives (Color, geometry, warnings, resource resolver)
  EggPdf.Html/         -- HTML5 parser (tokenizer, tree builder, DOM types)
  EggPdf.Css/          -- CSS parser + cascade + selectors + inline parser
  EggPdf.Layout/       -- layout engine (block, inline, flex, table cells horizontal)
  EggPdf.Text/         -- TrueType parser, system font discovery, line breaking, font resolver
  EggPdf.Pdf/          -- PDF 1.7 writer (text, rectangles, rounded rects, links, multi-page, PNG/JPEG images)
  EggPdf.Cli/          -- CLI tool: eggpdf input.html -o output.pdf
  EggPdf.Service/      -- REST API + WebUI (POST /api/render, GET /e2e, GET /)
  EggPdf.Style/        -- (placeholder for future style resolution module)
  EggPdf.Svg/          -- (placeholder for future SVG engine)
  EggPdf.Paint/        -- (placeholder for future paint layer)
  EggPdf.Fragmentation/ -- (placeholder for future fragmentation)

tests/
  EggPdf.Tests.Unit/   -- 541 unit tests (parsers, CSS, var/calc, selectors, colors, PDF, PNG, bookmarks, E2E)
  EggPdf.Tests.Layout/ -- 118 layout tests (block, inline, flex, float, table, grid, margins, lists)
  EggPdf.Tests.E2E/    -- 20 Playwright tests (WebUI, API endpoints)

benchmarks/
  EggPdf.Benchmarks/   -- BenchmarkDotNet suite (3 scenarios)

design/
  architecture/        -- 8 detailed component design docs + E2E testing doc
  specs/               -- 5 technical specs (primitives, CSS properties, UA stylesheet, PDF operators, colors)
  webui/               -- WebUI design sketch (synced from implementation)

docker/
  Dockerfile.service   -- REST API + WebUI Docker image
  Dockerfile.cli       -- CLI Docker image
  docker-compose.yml

docs/                  -- Wiki pages (14 pages, auto-synced to GitHub Wiki)
```

## Test Commands

```bash
# All unit + layout tests
dotnet test tests/EggPdf.Tests.Unit -c Release
dotnet test tests/EggPdf.Tests.Layout -c Release

# Playwright E2E tests (starts service automatically)
PLAYWRIGHT_BROWSERS_PATH=0 dotnet test tests/EggPdf.Tests.E2E -c Release

# Filtered
dotnet test -c Release --filter "FullyQualifiedName~TableCell"

# Benchmarks
dotnet run --project benchmarks/EggPdf.Benchmarks -c Release -- --filter "*Render*" --job short
```

## Current Benchmark Results

| Scenario | Time | Memory | Target |
|----------|------|--------|--------|
| Simple page (h1 + p) | **13 us** | 20 KB | < 50ms |
| Invoice (table + styles) | **86 us** | 85 KB | < 100ms |
| Large table (100 rows) | **1.2 ms** | 939 KB | < 5s |

## WebUI & Service

```bash
# Start the service with WebUI
dotnet run --project src/EggPdf.Service -c Release -- --urls http://localhost:8080

# WebUI: http://localhost:8080 (HTML editor + live preview + PDF download)
# E2E comparison: http://localhost:8080/e2e (browser vs PDF side-by-side)
# API: POST http://localhost:8080/api/render (HTML -> PDF)
# Health: GET http://localhost:8080/health
```

## Code Style

- C# 12+ features OK, but must compile on netstandard2.0 via `#if`
- Use `IndexOf(string, StringComparison)` instead of `Contains(string, StringComparison)` for netstandard2.0
- No `record` types (not available on netstandard2.0 without polyfill)
- No `Span<T>.Contains` on netstandard2.0
- Use `ArrayPool<T>` for temporary buffers
- No LINQ in hot paths -- use `for` loops
- Test naming: `Feature_Condition_ExpectedBehavior`

## Skills (invoke with /slash commands)

- `/feat` -- implement a new feature (test-first workflow)
- `/test` -- run tests (all or filtered)
- `/bench` -- run benchmarks and analyze
- `/perf-check` -- verify no performance regression
- `/ship` -- create PR with test + perf evidence
- `/fix` -- fix a bug (reproduce with test first)
- `/render-debug` -- trace a rendering issue through the pipeline

## Key Files

- `BLUEPRINT.md` -- comprehensive project blueprint (3,400+ lines)
- `CLAUDE.md` -- this file
- `.claude/skills/` -- 7 skill definitions
- `design/architecture/` -- 9 architecture docs
- `design/specs/` -- 5 technical specs
