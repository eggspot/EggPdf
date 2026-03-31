---
name: review
description: Review code changes for bugs, regressions, and quality. Run tests and analyze diffs.
allowed-tools: Bash(dotnet *), Bash(git *), Read, Grep, Glob, Agent
---

# Code Review

Review recent code changes for correctness, quality, and regressions.

## Steps

1. **Check what changed**
   ```bash
   git diff --stat HEAD~1
   git diff HEAD~1
   ```

2. **Run the full test suite**
   ```bash
   dotnet test tests/EggPdf.Tests.Unit -c Release
   dotnet test tests/EggPdf.Tests.Layout -c Release
   ```

3. **Review each changed file** for:
   - Logic bugs (off-by-one, null refs, wrong comparisons)
   - Performance regressions (LINQ in hot paths, unnecessary allocations)
   - Missing error handling at system boundaries
   - Broken netstandard2.0 compatibility (no `record`, no `Span.Contains`, use `IndexOf` not `Contains(string, StringComparison)`)
   - Dead code or unused imports
   - Missing test coverage for new behavior

4. **Check PDF output** for any template that uses the changed code paths:
   ```bash
   # Start service if not running
   curl -s http://localhost:55727/health || dotnet run --project src/EggPdf.Service -c Release -- --urls http://localhost:55727 &
   sleep 5
   # Render a test PDF and verify content stream
   curl -s -X POST http://localhost:55727/api/render -H "Content-Type: application/json" \
     -d '{"html":"<html><body><h1>Test</h1><p>Hello</p></body></html>"}' | sed -n '/^stream$/,/^endstream$/p'
   ```

5. **Report findings** with:
   - List of issues found (severity: critical/moderate/minor)
   - Suggested fixes
   - Overall approval status (approve/request-changes)
