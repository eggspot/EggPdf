---
name: perf-check
description: Verify no performance regression after code changes -- gate before shipping
disable-model-invocation: true
user-invocable: true
allowed-tools: Bash, Read, Grep, Glob
---

# Performance Regression Check

Verify that code changes haven't introduced performance regressions. This MUST pass before any PR is shipped.

## Steps

1. **Check what changed** -- run `git status` and `git diff --stat` to understand modified files.

2. **Identify affected benchmarks** by analyzing which files changed:
   - `EggPdf.Html/` changes -> run Parse benchmarks
   - `EggPdf.Css/` changes -> run Parse + Style benchmarks
   - `EggPdf.Layout/` changes -> run Layout benchmarks (ALL)
   - `EggPdf.Text/` changes -> run Font + Layout benchmarks
   - `EggPdf.Paint/` changes -> run Render benchmarks
   - `EggPdf.Pdf/` changes -> run Render + PDF write benchmarks
   - `EggPdf.Core/` changes -> run ALL benchmarks
   - Test-only changes -> skip benchmarks (report PASS)

3. **Run affected benchmarks**:
   ```bash
   cd benchmarks/EggPdf.Benchmarks && dotnet run -c Release -- --filter *{scenario}* --exporters json markdown
   ```

4. **Analyze results against targets** (from BLUEPRINT.md):
   - Simple page: < 50ms
   - Invoice: < 100ms
   - Report (10 pages): < 1s
   - Large table (100 pages): < 5s
   - No benchmark should regress > 15% vs baseline

5. **Report verdict**:

   ### Perf Check: PASS / FAIL

   **Changed files**: list modified files
   **Benchmarks run**: list scenarios
   **Results**:

   | Scenario | Current | Baseline | Delta | Status |
   |----------|---------|----------|-------|--------|

   If FAIL:
   - Which scenario regressed and by how much
   - Which code change likely caused it
   - Suggested optimization approach

6. **IMPORTANT**: Do NOT approve changes that regress performance beyond 15%. Performance is a core value for EggPdf.
