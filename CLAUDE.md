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
2. **Multi-target** -- must compile on netstandard2.0, netstandard2.1, net6.0, net8.0, net9.0, net10.0.
3. **Performance matters** -- benchmark every hot-path change. No regressions allowed.
4. **Test first, always** -- write failing test -> implement -> verify -> fix -> repeat.
5. **Infallible parsers** -- HTML/CSS parsers never throw. Produce error nodes / silently ignore.
6. **Graceful degradation** -- unknown CSS ignored, missing resources produce warnings not crashes.

## Development Workflow (STRICT — batched TDD)

Tests are still written before the code they cover, but execution is batched:
write ALL the code for the task first, then run the test suite and fix.
**Do not run tests or builds between individual edits mid-batch.**

```
1. Write failing tests for every change in the batch
2. Write all the code for the batch
3. Run ALL tests (this also compiles — no separate build step)
   - FAIL → fix code, run again; iterate until green
   - PASS → continue
4. Benchmark if hot path (no regressions allowed)
5. Commit with conventional prefix
```

**Rules:**
- Never write code before writing the test that covers it.
- Never commit with failing tests.
- Never skip benchmarks for hot-path changes.
- Keep iterating fix → run → fix until all tests pass — do not give up early.
- Write the whole batch of code first, THEN run tests once and fix failures — no test runs after every small edit.
- No standalone `dotnet build` — `dotnet test` compiles everything it needs. Only build separately when diagnosing a build-system issue itself.

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
  EggPdf.Text/         -- TrueType parser, system font discovery, line breaking, font resolver,
                          OpenType GSUB/GPOS shaping for complex scripts (OpenType/), Arabic joining,
                          full UAX #9 bidi (BidiAlgorithm*.cs), variable-font instancing (TrueType/VariableFontInstancer*.cs), CFF/CFF2 -> glyf conversion (TrueType/CffFont.cs),
                          syllable line breaking for Thai/Lao/Khmer/Myanmar (SpacelessLineBreaker)
  EggPdf.Pdf/          -- PDF 1.7 writer (text, CID fonts, images, links, merging, RC4 encryption, CMS/PKCS#7 signing),
                          image decoders incl. WebP lossy VP8 (Vp8Decoder*.cs) + lossless VP8L
  EggPdf.Cli/          -- CLI tool: eggpdf input.html -o output.pdf
  EggPdf.Service/      -- REST API + WebUI (POST /api/render, GET /e2e, GET /)
  EggPdf.AspNetCore/   -- ASP.NET Core middleware/DI integration (API key auth, service registration)
  EggPdf.Razor/         -- Razor component/view -> PDF conversion
  EggPdf.Style/        -- (placeholder for future style resolution module)
  EggPdf.Svg/          -- SVG parser + renderer (inline <svg>, external .svg images), filter graphs (SvgFilter*, FilterPixels)
  EggPdf.Paint/        -- paints a laid-out box tree onto a PdfPage (text, backgrounds, borders, shadows, transforms)
  EggPdf.Fragmentation/ -- pure pagination computation (page breaks, orphans/widows, fixed-position collection)

tests/
  EggPdf.Tests.Unit/   -- ~1440 unit tests (parsers, CSS, fonts/webfonts, signing, PDF, image decoders, SVG filters, E2E render checks)
  EggPdf.Tests.Layout/ -- ~659 layout tests (block, inline, flex, float, table, grid, margins, lists)
  EggPdf.Tests.E2E/    -- ~221 Playwright tests (WebUI, API endpoints; needs `playwright.ps1 install chromium` once)

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

site/                  -- GitHub Pages static site (getting-started, fonts, rest-api, ... + llms.txt)
llms.txt               -- AI/LLM discovery file (repo root)
```

## Test Commands

```bash
# All unit + layout tests
dotnet test tests/EggPdf.Tests.Unit -c Release
dotnet test tests/EggPdf.Tests.Layout -c Release

# Playwright E2E tests (starts service automatically)
PLAYWRIGHT_BROWSERS_PATH=0 dotnet test tests/EggPdf.Tests.E2E -c Release
# Note: Playwright script path uses net10.0 (see ci.yml)

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
# Visual Studio: F5 launches http://localhost:55727 (configured in launchSettings.json)

# CLI: start the service with WebUI
dotnet run --project src/EggPdf.Service -c Release -- --urls http://localhost:55727

# WebUI: http://localhost:55727 (HTML editor + live preview + PDF download)
# E2E comparison: http://localhost:55727/e2e (browser vs PDF side-by-side)
# API: POST http://localhost:55727/api/render (HTML -> PDF)
# Health: GET http://localhost:55727/health

# Docker uses port 8080 (see docker/Dockerfile.service)
```

## Code Editing

- Always edit source files directly using the Edit or Write tools -- never create helper scripts (PowerShell, Python, bash, etc.) to modify code files.
- Read the file first, understand the context, then apply targeted edits.

## Code Style

- C# 12+ features OK, but must compile on netstandard2.0 via `#if`
- Use `IndexOf(string, StringComparison)` instead of `Contains(string, StringComparison)` for netstandard2.0
- No `record` types (not available on netstandard2.0 without polyfill)
- No `Span<T>.Contains` on netstandard2.0
- Use `ArrayPool<T>` for temporary buffers
- No LINQ in hot paths -- use `for` loops
- Remove unused code -- no dead methods, unused usings, or orphaned helpers. Keep the codebase clean.
- Test naming: `Feature_Condition_ExpectedBehavior`
- Keep files focused. Don't grow an existing file back into a monolith by tacking a new
  self-contained feature onto the end of it -- when adding a substantial new chunk of
  logic (roughly 150+ lines, or a clearly separate concern) to a class that already
  exists, split it into a new `ClassName.Feature.cs` file using `partial class`/`partial
  static class`, the same way `BlockLayout.*.cs`, `PdfRenderer` (-> `PageFragmenter`/
  `BoxPainter`), and `SvgRenderer.Filter.cs` are split. Do this as you write the feature,
  not as a later cleanup pass.

## Docs Stay in Sync

When a change adds, fixes, or removes a user-facing CSS/HTML/PDF capability (a new supported
property, a bug fix that makes something documented-but-broken actually work, a documented
limitation that no longer applies), update the relevant docs in the SAME change, not as a
follow-up:

- `README.md` -- feature bullet lists (`### HTML & CSS`, `### PDF`, etc.)
- `llms.txt` / `site/llms.txt` -- AI/LLM discovery files (`## Key features`)
- `site/*.html` -- the relevant GitHub Pages doc (e.g. `headers-footers.html`, `page-layout.html`,
  `tables.html`) -- correct any "(planned)" / "not yet supported" notes that the change resolves
- `CLAUDE.md` -- if the change alters this file's own project-structure/workflow claims

Never document a capability as working until it's verified end-to-end (rendered, inspected in the
actual PDF bytes) -- a feature that merely doesn't crash is not "supported". Conversely, if you
discover docs claiming something works that doesn't (verify before trusting the doc), fix the
underlying gap or correct the doc -- don't leave the mismatch. The `/docs` skill runs a fuller
audit pass (versions, benchmarks, Docker image names, wiki pages) -- use it periodically, but
per-change doc updates above shouldn't wait for that pass.

## Feature Parity Across Entry Points

EggPdf exposes rendering through multiple entry points: the core `HtmlToPdf` API, `PdfRenderOptions`
(the C#-properties convenience wrapper over CSS), the CLI (`EggPdf.Cli`), and the REST API
(`EggPdf.Service`). When a new PDF-writer-level capability is added -- a new `HtmlToPdf.Render`/
`RenderAsync` overload, a new `PdfDocument` property (e.g. `Conformance`, `Invoice`, `Encryption`) --
wire it into the other entry points in the SAME change, not as a follow-up:

- `PdfRenderOptions` -- add the corresponding property, if it's a per-render setting
- CLI (`EggPdf.Cli`) -- add the corresponding flag/argument
- REST API (`EggPdf.Service`) -- add the corresponding request field
- Update the "Docs Stay in Sync" targets above for every surface actually wired, not just the core API

If wiring every entry point in the same change is genuinely too large (e.g. it needs its own
request/response schema design), say so explicitly and track the gap in BLUEPRINT.md/the relevant
doc -- don't silently ship a feature reachable from only one of the four surfaces.

## Skills (invoke with /slash commands)

- `/feat` -- implement a new feature (test-first workflow)
- `/test` -- run tests (all or filtered)
- `/bench` -- run benchmarks and analyze
- `/perf-check` -- verify no performance regression
- `/ship` -- create PR with test + perf evidence
- `/fix` -- fix a bug (reproduce with test first)
- `/render-debug` -- trace a rendering issue through the pipeline
- `/review` -- dual-perspective code review: Lead Dev (architecture/correctness) + Dev (implementation/tests), runs in main agent for easy follow-up

## Key Files

- `BLUEPRINT.md` -- comprehensive project blueprint (3,400+ lines)
- `CLAUDE.md` -- this file
- `.claude/skills/` -- 7 skill definitions
- `design/architecture/` -- 9 architecture docs
- `design/specs/` -- 5 technical specs
