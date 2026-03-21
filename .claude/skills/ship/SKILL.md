---
name: ship
description: Create a PR with test evidence and performance verification
disable-model-invocation: true
user-invocable: true
allowed-tools: Bash, Read, Grep, Glob
---

# Ship -- Create PR with Quality Evidence

Create a pull request for the current feature branch, including test results and performance evidence.

## Arguments

- `$ARGUMENTS` -- optional PR title override. If empty, auto-generate from branch name and commits.

## Steps

1. **Pre-flight checks** (ALL must pass):
   ```bash
   dotnet build --configuration Release    # No warnings
   dotnet test --configuration Release     # All green
   ```
   If either fails, STOP. Fix first.

2. **Verify we are NOT on `main`**:
   ```bash
   git branch --show-current
   ```
   If on main, STOP and tell the user to create a feature branch.

3. **Run performance check** if any non-test code changed:
   ```bash
   cd benchmarks/EggPdf.Benchmarks && dotnet run -c Release -- --filter *Render* --job short --exporters markdown
   ```

4. **Analyze commits** on this branch vs main:
   ```bash
   git log main..HEAD --oneline
   git diff main...HEAD --stat
   ```

5. **Count test results**:
   ```bash
   dotnet test --configuration Release --verbosity minimal 2>&1 | tail -5
   ```

6. **Create PR** using `gh pr create`:

   ```markdown
   ## Summary
   - What was added/changed (1-3 bullets)

   ## Test Evidence
   - X tests pass (Y new, Z existing)
   - Test types: unit / layout / visual / E2E

   ## Performance
   | Scenario | Time | Memory | Target | Status |
   |----------|------|--------|--------|--------|
   (include benchmark results if available)

   No performance regressions detected.

   ## Checklist
   - [ ] Tests written FIRST, then implementation
   - [ ] All tests pass (new + existing)
   - [ ] No performance regression
   - [ ] Public API has XML documentation
   - [ ] Multi-target compatible (netstandard2.0+)

   Generated with [Claude Code](https://claude.com/claude-code)
   ```

7. **Return the PR URL** to the user.

## Important

- NEVER push to `main` directly
- NEVER create PR with failing tests
- NEVER ship without running tests
- Performance evidence required for any non-test change
