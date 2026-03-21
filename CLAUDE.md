# EggPdf -- Claude Code Instructions

## Project Overview

EggPdf is a pure C#, zero-dependency HTML/CSS-to-PDF rendering engine targeting Chrome Print parity.
Open source under MIT license: https://github.com/eggspot/EggPdf

## Architecture

8-stage pipeline: HTML Parse -> CSS Parse -> Style Resolve -> Box Generate -> Layout -> Fragment -> Paint -> PDF Write

See `BLUEPRINT.md` for full architecture, CSS coverage, component specs, and phase plan.

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

## Project Structure

```
src/
  EggPdf/              -- main library (public API)
  EggPdf.Core/         -- shared primitives (units, colors, rect, point)
  EggPdf.Html/         -- HTML5 parser (WHATWG spec)
  EggPdf.Css/          -- CSS parser + cascade + selectors
  EggPdf.Style/        -- style resolution
  EggPdf.Layout/       -- layout engine (block, inline, flex, grid, table, float)
  EggPdf.Text/         -- font resolution, metrics, shaping, line breaking
  EggPdf.Svg/          -- SVG rendering engine
  EggPdf.Fragmentation/ -- pagination, @page, page breaks
  EggPdf.Paint/        -- paint commands (abstract rendering)
  EggPdf.Pdf/          -- PDF 1.7 writer
  EggPdf.Razor/        -- optional: Razor template integration (has NuGet deps)
  EggPdf.AspNetCore/   -- optional: ASP.NET Core integration
  EggPdf.Cli/          -- CLI tool: dotnet-eggpdf
  EggPdf.Service/      -- standalone HTTP microservice

tests/
  EggPdf.Tests.Unit/        -- fast isolated unit tests (< 30s)
  EggPdf.Tests.Layout/      -- layout assertion tests (< 60s)
  EggPdf.Tests.Visual/      -- visual regression tests (< 5min)
  EggPdf.Tests.Integration/ -- E2E tests
  EggPdf.Tests.Fuzz/        -- fuzz testing
  testdata/
    html5lib-tests/          -- git submodule

benchmarks/
  EggPdf.Benchmarks/        -- BenchmarkDotNet suite
  baselines/                 -- stored baseline results

tools/
  EggPdf.WptRunner/         -- WPT conformance test runner
  EggPdf.ChromeRef/         -- Chrome reference PDF generator (uses Playwright)
```

## Test Commands

```bash
# All tests
dotnet test --configuration Release

# Specific project
dotnet test tests/EggPdf.Tests.Unit/ --configuration Release

# Filtered
dotnet test --configuration Release --filter "FullyQualifiedName~HtmlTokenizer"

# Benchmarks
cd benchmarks/EggPdf.Benchmarks && dotnet run -c Release -- --filter * --exporters json markdown

# Quick benchmark smoke test
cd benchmarks/EggPdf.Benchmarks && dotnet run -c Release -- --filter * --job short
```

## Performance Targets

| Scenario | Time | Memory |
|----------|------|--------|
| Simple page (1 page) | < 50ms | < 10MB |
| Invoice (1 page, table + logo) | < 100ms | < 20MB |
| Report (10 pages) | < 1s | < 50MB |
| Large table (100 pages) | < 5s | < 200MB |

## Code Style

- C# 12+ features OK, but must compile on netstandard2.0 via `#if`
- Use `Span<T>` / `ReadOnlySpan<T>` on netstandard2.1+ for parsing hot paths
- Use `ArrayPool<T>` for temporary buffers
- No LINQ in hot paths -- use `for` loops
- XML documentation on all public types and members
- Test naming: `Feature_Condition_ExpectedBehavior`
- One assertion concept per test (multiple Assert calls OK if testing one thing)

## Skills (invoke with /slash commands)

- `/feat` -- implement a new feature (test-first workflow)
- `/test` -- run tests (all or filtered)
- `/bench` -- run benchmarks and analyze
- `/perf-check` -- verify no performance regression
- `/ship` -- create PR with test + perf evidence
- `/fix` -- fix a bug (reproduce with test first)
- `/render-debug` -- trace a rendering issue through the pipeline

## Key Files

- `BLUEPRINT.md` -- comprehensive project blueprint (3000+ lines)
- `CLAUDE.md` -- this file (Claude Code instructions)
- `.claude/skills/` -- skill definitions for slash commands
