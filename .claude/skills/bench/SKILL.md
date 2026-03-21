---
name: bench
description: Run EggPdf benchmarks and analyze performance for specific rendering scenarios
disable-model-invocation: true
user-invocable: true
allowed-tools: Bash, Read, Grep, Glob
---

# Benchmark Runner

Run benchmarks and analyze EggPdf's rendering performance. Performance is a core value -- every change must be measured.

## Arguments

- `$ARGUMENTS` -- optional benchmark filter. Examples:
  - Empty -> run all benchmarks
  - `Parse` -> run HTML/CSS parsing benchmarks
  - `Layout` -> run layout benchmarks
  - `Render` -> run end-to-end rendering benchmarks
  - `Memory` -> run memory allocation benchmarks
  - `Font` -> run font loading/subsetting benchmarks
  - `short` -> run all benchmarks with `--job short` for quick smoke test

## Steps

1. **Run the benchmark**:
   - If `$ARGUMENTS` is `short`:
     ```bash
     cd benchmarks/EggPdf.Benchmarks && dotnet run -c Release -- --filter * --job short --exporters json markdown
     ```
   - If `$ARGUMENTS` is provided:
     ```bash
     cd benchmarks/EggPdf.Benchmarks && dotnet run -c Release -- --filter *{$ARGUMENTS}* --exporters json markdown
     ```
   - If empty:
     ```bash
     cd benchmarks/EggPdf.Benchmarks && dotnet run -c Release -- --filter * --exporters json markdown
     ```

2. **Read and analyze results**:
   - Find results in `BenchmarkDotNet.Artifacts/results/`
   - Read the markdown results file

3. **Report with this format**:

   ### Benchmark Results: {filter}

   | Scenario | Time | Memory | Allocations | Pages |
   |----------|------|--------|-------------|-------|

   **Performance targets** (from BLUEPRINT.md):
   - Simple page (1 page, text only): < 50ms, < 10MB
   - Invoice (1 page, table + logo): < 100ms, < 20MB
   - Report (10 pages, mixed content): < 1s, < 50MB
   - Large table (100 pages): < 5s, < 200MB

   **Per-stage breakdown** (if available):
   - Parse HTML: Xms
   - Parse CSS: Xms
   - Style resolution: Xms
   - Layout: Xms
   - Fragmentation: Xms
   - Paint: Xms
   - PDF write: Xms

4. **Compare against baselines** if `benchmarks/baselines/latest.json` exists:
   - Flag any regression > 15% in time
   - Flag any regression > 25% in memory
   - Flag any new allocations in hot paths

5. **CRITICAL**: If any target is missed, flag it and suggest optimization areas.
