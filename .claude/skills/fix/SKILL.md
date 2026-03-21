---
name: fix
description: Fix a bug following the test-first approach -- reproduce with test, then fix, then verify
disable-model-invocation: true
user-invocable: true
allowed-tools: Bash, Read, Write, Edit, Grep, Glob, Agent, TodoWrite
---

# Bug Fix -- Test-First Approach

Fix a bug by first reproducing it with a failing test, then fixing the code, then verifying.

## Arguments

- `$ARGUMENTS` -- description of the bug (e.g., "margin collapse not working for empty divs", "PNG transparency renders as black", "CSS shorthand font not parsing correctly")

## Workflow

### Step 1: Reproduce

1. **Understand the bug** -- read the description, search for related issues:
   ```bash
   gh issue list --search "$ARGUMENTS"
   ```

2. **Write a failing test that demonstrates the bug**:
   - Name it: `BugFix_Description_ShouldBehavior`
   - The test MUST fail before your fix (proves the bug exists)
   - Keep the test minimal -- smallest HTML/CSS that reproduces the issue

3. **Run the test -- confirm it FAILS**:
   ```bash
   dotnet test --configuration Release --filter "FullyQualifiedName~BugFix_Description"
   ```
   If it passes, the test doesn't reproduce the bug. Try again.

### Step 2: Fix

4. **Fix the bug** -- make the minimal change needed:
   - Don't refactor unrelated code
   - Don't "improve" surrounding code
   - Just fix the bug

5. **Run the failing test -- confirm it PASSES**:
   ```bash
   dotnet test --configuration Release --filter "FullyQualifiedName~BugFix_Description"
   ```

### Step 3: Verify

6. **Run ALL tests** -- make sure the fix didn't break anything:
   ```bash
   dotnet test --configuration Release
   ```

7. **If the fix touches a hot path**, run performance check:
   ```bash
   cd benchmarks/EggPdf.Benchmarks && dotnet run -c Release -- --filter *{Scenario}* --job short
   ```

### Step 4: Commit

8. **Commit with `fix:` prefix**:
   ```bash
   git add -A && git commit -m "fix: description of what was wrong and why"
   ```

## Key Rules

- ALWAYS write the failing test FIRST. No test = no fix.
- NEVER commit a fix without a regression test.
- Keep the fix minimal. One bug per commit.
