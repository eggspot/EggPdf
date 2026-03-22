# EggPdf -- Copilot Instructions

## Project
EggPdf is a pure C#, zero-dependency HTML/CSS-to-PDF rendering engine.
MIT license. Open source at https://github.com/eggspot/EggPdf

## Architecture
8-stage pipeline: HTML Parse -> CSS Parse -> Style Resolve -> Box Generate -> Layout -> Fragment -> Paint -> PDF Write.
See BLUEPRINT.md for full details, CLAUDE.md for dev workflow, design/architecture/ for component specs.

## Rules
1. Zero external NuGet dependencies in the core library
2. Must compile on netstandard2.0 through net9.0
3. Write tests FIRST -- test must fail before implementation
4. Parsers never throw -- produce error nodes instead
5. Unknown CSS properties silently ignored (graceful degradation)
6. No `string.Contains(string, StringComparison)` on netstandard2.0 -- use `IndexOf`
7. No `record` types without polyfill on netstandard2.0
8. No LINQ in hot paths -- use for loops
9. Conventional commits: feat:, fix:, perf:, test:, refactor:, docs:, chore:
10. PRs auto-merge -- be extra careful, all tests must pass

## Current State
- 396 tests (310 unit + 66 layout + 20 Playwright E2E), 0 failures
- CLI tool: `eggpdf input.html -o output.pdf`
- REST API: `POST /api/render` returns PDF
- WebUI: http://localhost:8080 with HTML editor + live preview
- E2E comparison: http://localhost:8080/e2e (browser vs PDF)
- Benchmarks: simple=13us, invoice=86us, 100-row table=1.2ms

## Test Commands
```bash
dotnet test tests/EggPdf.Tests.Unit -c Release
dotnet test tests/EggPdf.Tests.Layout -c Release
PLAYWRIGHT_BROWSERS_PATH=0 dotnet test tests/EggPdf.Tests.E2E -c Release
dotnet run --project benchmarks/EggPdf.Benchmarks -c Release -- --filter "*Render*" --job short
```

## Test Style
- xUnit + FluentAssertions
- Naming: Feature_Condition_ExpectedBehavior
- AAA pattern (Arrange, Act, Assert)
