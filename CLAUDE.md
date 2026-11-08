# EggPdf -- Claude Code Instructions

## Project Overview

EggPdf is a pure C#, zero-dependency HTML/CSS-to-PDF rendering engine targeting Chrome Print parity.
Open source under MIT OR Apache-2.0: https://github.com/eggspot/EggPdf

**PRs auto-merge.** Be extra careful -- all tests must pass before any PR.

## Architecture

8-stage pipeline: HTML Parse -> CSS Parse -> Style Resolve -> Box Generate -> Layout -> Fragment -> Paint -> PDF Write

See `BLUEPRINT.md` for full architecture, CSS coverage, component specs, and phase plan.
See `design/architecture/` for detailed component specs (8 docs, ~3,000 lines).

## Core Principles

1. **Zero external dependencies** -- pure managed C#, BCL only. No NuGet packages in the core library.
2. **Multi-target** -- must compile on netstandard2.0, netstandard2.1, net6.0, net8.0, net9.0, net10.0.
3. **Performance is the top priority** -- see "Performance First" below. Benchmark every hot-path change. No regressions allowed.
4. **Test first, always** -- write failing test -> implement -> verify -> fix -> repeat.
5. **Infallible parsers** -- HTML/CSS parsers never throw. Produce error nodes / silently ignore.
6. **Graceful degradation** -- unknown CSS ignored, missing resources produce warnings not crashes.

## Performance First (owner's top priority)

Design every feature for the largest realistic input, not the test fixture: a 5 GB PDF, 150,000 pages, a
100,000-row table. Memory must grow with what the caller actually uses, never with the size of the input.
An earlier `PdfReader` walked the whole page tree and held the whole file at `Open`, costing 500 MB for a
150k-page PDF; don't write code like that again.

- **No whole-input copies.** Read from a `Stream`/file through bounded buffers; offer `Stream` (and path)
  overloads beside `byte[]`, and write output to a `Stream`. Use `long` for file offsets.
- **Lazy, not eager.** Don't walk or materialize a whole structure (page tree, xref, object graph, table rows)
  up front when an indexed or on-demand lookup works. Prefer O(1)/O(log n) access to an O(n) pre-pass.
- **Bounded caches.** Every cache has a size cap (LRU or similar); nothing grows per page/object without limit.
- **Compact representations.** Store large indexes as packed arrays of primitives, not objects or dictionaries
  per entry.
- **Prove it at scale.** A reader/writer/layout path gets a test that measures allocated bytes on a large
  input (e.g. `GC.GetAllocatedBytesForCurrentThread()` around open + one lookup on 150k pages) with a hard
  ceiling, plus a benchmark. Reviewers block a PR whose memory or time scales with input size where it needn't.

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

**Every pull request goes through the `pr` skill.** Before creating a PR (`gh pr create` or any other
way) — whether the user asks for one or the coding task is done and a PR is the next step — invoke the
`pr` skill (`.claude/skills/pr/SKILL.md`) and follow it, unless all of its steps have already been done
in this session for the current commit. It reviews the plan, reviews the code in two rounds, fixes and
commits, runs the tests (and benchmarks for hot paths), opens the PR to `main`, does an independent
pre-merge review pass (in this session, no subagent) to a MERGE verdict, waits for every CI job, squash-merges and checks the release the merge starts, then
reports to the user. Running it is the go-ahead for its own fixes, commits, one test run and the merge.

**One agent: no subagents in this project.** The owner's rule (2026-11-08): the main session does all
the work itself and never launches a subagent (the Agent tool, workflows or forks), even where a
global rule asks for one. That covers the plan review rounds, the two code review rounds, research
questions and the independent pre-merge review: do each of them in the session, as a separate pass
that rereads the plan or diff from scratch against the rules, and write down its findings before
fixing anything. The usual sequence for a batch is: plan (with its two review passes), write all the
code for every batch, phase and step, build and test once, review the code in two rounds and fix,
then open the PR, do the pre-merge review pass, wait for CI and merge.

**Fix every bug you find, and merge the fix.** A bug that turns up during any task (writing code, a
review round, a test run, investigating something else) gets fixed, even a minor one: reproduce it with
a failing test, fix it, and land it through the `pr` skill until it is merged. Noting it, leaving it
for later or only opening an issue is not enough; an issue is fine as the tracker, but the fix still
follows. Batch bug fixes: when several bugs are queued, write all their fixes (and tests) on one
branch, one commit per fix, then build and test once at the end and land them as one PR. Every
build and test run loads the owner's PC, so don't open a PR (and rerun the suites) per bug. Keep a
fix out of a batch only when it must ship on its own, and don't fold bug fixes into an unrelated
feature change.

- When the right behaviour is a design question (a public API change, an ambiguous spec), research it
  yourself first (the global "Questions: research them" rule, done in this session, see "One agent"
  below), then fix it.
- A bug that a deterministic test can't pin (a flaky or timing-dependent one) is still fixed: fix the
  cause and say in the PR how the fix was verified.
- A bug in code another session is working on (its open PR or its project lock) is reported to that
  work (a comment on its PR) instead of fixed in parallel, and fixed after that work merges if it is
  still there ("Stay in your lane").
- The decisions the global rules leave to the owner (money, licenses, deleting or force-pushing shared
  branches and data, deploys beyond the merges this repo allows) are reported, not acted on.

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
  EggPdf/              -- main library (public API: HtmlToPdf.RenderAsync; Fluent/ = the fluent C# document builder,
                          namespace EggPdf.Fluent, compiled into this assembly/package -- no separate NuGet package --
                          builds the DOM directly and renders through HtmlToPdf.RenderDocument, no HTML string involved)
  EggPdf.Core/         -- shared primitives (Color, geometry, warnings, resource resolver)
  EggPdf.Html/         -- HTML5 parser (tokenizer, tree builder, DOM types)
  EggPdf.Css/          -- CSS parser + cascade + selectors + inline parser
  EggPdf.Layout/       -- layout engine (block, inline, flex, table cells horizontal)
  EggPdf.Text/         -- TrueType parser, system font discovery, line breaking, font resolver,
                          OpenType GSUB/GPOS shaping for complex scripts (OpenType/), Arabic joining,
                          full UAX #9 bidi (BidiAlgorithm*.cs), variable-font instancing (TrueType/VariableFontInstancer*.cs), CFF/CFF2 -> glyf conversion (TrueType/CffFont.cs),
                          syllable line breaking for Thai/Lao/Khmer/Myanmar (SpacelessLineBreaker),
                          per-character font fallback for every script (Fallback/: UnicodeScripts, FontFallbackCatalog
                          per-platform candidate lists, FontFallbackResolver coverage-driven choice incl. .ttc faces --
                          never hard-code a language; extend the catalog instead)
  EggPdf.Pdf/          -- PDF 1.7 writer (text, CID fonts, images, links, merging, RC4 encryption, CMS/PKCS#7 signing),
                          image decoders incl. WebP lossy VP8 (Vp8Decoder*.cs) + lossless VP8L
  EggPdf.Cli/          -- CLI tool: eggpdf input.html -o output.pdf
  EggPdf.Service/      -- REST API + WebUI (POST /api/render, GET /e2e, GET /)
  EggPdf.AspNetCore/   -- ASP.NET Core middleware/DI integration (API key auth, service registration)
  EggPdf.Razor/         -- Razor component/view -> PDF conversion
  EggPdf.Style/        -- (placeholder for future style resolution module)
  EggPdf.Svg/          -- SVG parser + renderer (inline <svg>, and <img> with an .svg file or data:image/svg+xml, both painted as vectors), filter graphs (SvgFilter*, FilterPixels)
  EggPdf.Paint/        -- paints a laid-out box tree onto a PdfPage (text, backgrounds, borders, shadows, transforms)
  EggPdf.Fragmentation/ -- pure pagination computation (page breaks, orphans/widows, fixed-position collection)

tests/
  EggPdf.Tests.Unit/   -- ~1700 unit tests (parsers, CSS, fonts/webfonts/fallback, signing, PDF, image decoders, SVG filters, E2E render checks)
  EggPdf.Tests.Layout/ -- ~662 layout tests (block, inline, flex, float, table, grid, margins, lists)
  EggPdf.Tests.E2E/    -- ~221 Playwright tests (WebUI, API endpoints; needs `playwright.ps1 install chromium` once)
  EggPdf.Tests.Fluent/ -- EggPdf.Fluent document-builder tests

benchmarks/
  EggPdf.Benchmarks/   -- BenchmarkDotNet suite (3 scenarios)

tools/
  EggPdf.DocGen/       -- generates the Fluent API reference block in site/fluent-api.html from the assembly + XML doc comments
                          (`dotnet run --project tools/EggPdf.DocGen`; add `-- --check` in CI)

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
- Never format or parse a number or date with the machine's culture: pass `CultureInfo.InvariantCulture`
  (and `NumberStyles`) to every `ToString`, `Parse`/`TryParse` and string `Format`, and format PDF/XMP/ASN.1
  timestamps through `PdfDates`. A decimal comma, a U+2212 minus sign (sv-SE) or a Buddhist-calendar year
  (th-TH) otherwise lands in the PDF. Interpolating or concatenating a *signed* integer, `float`, `double` or
  date has the same problem (non-negative integers are safe). All test projects run under a deliberately hostile
  culture (`tests/HostileCulture.cs`, linked into each test csproj) so a violation fails on every machine and in
  CI; `CultureInvarianceTests` renders a broad document under several cultures and diffs it against the invariant render.
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
(the C#-properties convenience wrapper over CSS), the CLI (`EggPdf.Cli`), the REST API
(`EggPdf.Service`), and the fluent C# document-builder (`EggPdf.Fluent`). When a new PDF-writer-level
capability is added -- a new `HtmlToPdf.Render`/`RenderAsync` overload, a new `PdfDocument` property
(e.g. `Conformance`, `Invoice`, `Encryption`) -- wire it into the other entry points in the SAME
change, not as a follow-up:

- `PdfRenderOptions` -- add the corresponding property, if it's a per-render setting
- CLI (`EggPdf.Cli`) -- add the corresponding flag/argument
- REST API (`EggPdf.Service`) -- add the corresponding request field
- `EggPdf.Fluent` -- add the corresponding `DocumentDescriptor`/`PageDescriptor` method (see below)
- Update the "Docs Stay in Sync" targets above for every surface actually wired, not just the core API

If wiring every entry point in the same change is genuinely too large (e.g. it needs its own
request/response schema design), say so explicitly and track the gap in BLUEPRINT.md/the relevant
doc -- don't silently ship a feature reachable from only one of the four surfaces.

### HTML/CSS and EggPdf.Fluent stay at parity

No gap is acceptable between what HTML/CSS can express and what `EggPdf.Fluent` can express. Three
explicitly-unchecked escape hatches guarantee this at the capability level even before a feature has
typed sugar: `Container.RawStyle(property, value)` (any CSS declaration), `Container.RawAttribute(name,
value)` and `Container.Raw(html)` (any HTML markup/element, parsed and spliced into the DOM being
built), plus `DocumentDescriptor.Css(css)` for head-level stylesheets. Together they mean nothing is
ever literally unreachable from the fluent API -- treat that as the floor, not the goal.

**No magic strings in the typed API.** A typo in a CSS keyword/color/unit is silently ignored by the
CSS engine, so every typed `EggPdf.Fluent` method takes a typed value, never a string: keyword enums
(`OverflowMode`, `PositionMode`, `FlexJustify`, `BorderLineStyle`, ... in `CssKeywords.cs`, each
mapped by an exhaustive `ToCss()` switch), `Color`/`Colors` (validated hex, no implicit string
conversion), `Length` (unit-explicit; a bare number is px), `CssTransform`, `GridTrack`, `HeadingLevel`.
Internal CSS property names live in `CssProp` -- never type a property name string twice. When adding a
method: add a new enum/struct for any new keyword set or unit (with a test that every member maps and
bad input throws, see `TypedValueTests`), validate numeric ranges, and only take `string` where the
value is genuinely free text (content, URLs, paths, ids, class names, font family names). The `Raw*`
escape hatches are the ONLY place raw CSS/HTML strings are accepted; keep the `Raw` prefix so their
unchecked nature is visible at every call site.

When a change adds or fixes a user-facing HTML/CSS/PDF capability (the same trigger as "Docs Stay in
Sync" above), also do one of, in the SAME change:

- Add a typed `EggPdf.Fluent` method for it (e.g. `Container.Border(...)`, `PageDescriptor.Watermark(...)`),
  mirroring the naming/shape of nearby methods in `Container`/`PageDescriptor`/`ColumnDescriptor`/
  `RowDescriptor`/`TableDescriptor`
- Or confirm it's already reachable via `.RawStyle()`/`.RawAttribute()`/`.Raw()`/`.Css()` and say so explicitly in the PR
  description -- don't leave it silently undiscoverable
- Or, if typed sugar is genuinely out of scope for this change, say so explicitly and track it as a
  known gap (e.g. in BLUEPRINT.md) rather than letting the two APIs silently drift apart

**The `EggPdf.Fluent` API reference is generated, not hand-written.** Write an XML `<summary>` on
every public type and member (it is also the IntelliSense text); the "API reference" block in
`site/fluent-api.html` (between the `BEGIN/END GENERATED API REFERENCE` markers) is produced from the
code by `tools/EggPdf.DocGen`. After adding/renaming/re-documenting any public `EggPdf.Fluent` member,
run `dotnet run --project tools/EggPdf.DocGen` and commit the regenerated page -- never edit inside the
markers by hand. `ApiReferenceUpToDateTests` fails when a public member has no `<summary>` (generation
throws, listing them) or when the committed page differs from the code, so the reference can't go
stale or incomplete. (`dotnet run --project tools/EggPdf.DocGen -- --check` does the staleness check
without writing.) The rest of `fluent-api.html` (guide prose) is still hand-written.

As with docs, never claim an `EggPdf.Fluent` method supports something until it's verified
end-to-end (rendered, inspected in the actual PDF bytes via `tests/EggPdf.Tests.Fluent`) -- code
that merely doesn't throw is not "supported". `Container`, `ColumnDescriptor`, `RowDescriptor`,
`TableDescriptor`/`TableRowDescriptor`, `PageDescriptor` and `DocumentDescriptor` are the extension
points; keep new fluent surface in those files (or a new `ClassName.Feature.cs` partial per the
file-size rule above) rather than inventing parallel builder types for the same concept.

## Skills (invoke with /slash commands)

- `/feat` -- implement a new feature (test-first workflow)
- `/test` -- run tests (all or filtered)
- `/bench` -- run benchmarks and analyze
- `/perf-check` -- verify no performance regression
- `/pr` -- review, fix, test, open the PR to `main`, merge once the independent review and every CI job pass, check the release
- `/fix` -- fix a bug (reproduce with test first)
- `/render-debug` -- trace a rendering issue through the pipeline
- `/review` -- dual-perspective code review: Lead Dev (architecture/correctness) + Dev (implementation/tests), runs in main agent for easy follow-up
- `/docs` -- bring README, site/, llms.txt and CLAUDE.md in line with the implementation
- `/render-compare` -- render HTML through EggPdf and analyze the PDF for visual issues
- `/test-templates` -- render every WebUI template through the pipeline and verify the output

## Key Files

- `BLUEPRINT.md` -- comprehensive project blueprint (3,400+ lines)
- `CLAUDE.md` -- this file
- `.claude/skills/` -- 11 skill definitions
- `design/architecture/` -- 9 architecture docs
- `design/specs/` -- 5 technical specs
