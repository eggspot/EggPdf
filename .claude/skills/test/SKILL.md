---
name: test
description: Run EggPdf tests -- all tests or filtered by project/class/method name
disable-model-invocation: true
user-invocable: true
allowed-tools: Bash, Read, Grep
---

# Test Runner

Run EggPdf tests with optional filtering. Always run in Release mode.

## Arguments

- `$ARGUMENTS` -- optional test filter. Examples:
  - Empty -> run ALL tests across all test projects
  - `Unit` -> run unit tests only
  - `Layout` -> run layout tests only
  - `Visual` -> run visual regression tests only
  - `Integration` -> run integration/E2E tests only
  - `HtmlTokenizer` -> run tests matching "HtmlTokenizer"
  - `MarginCollapse` -> run a specific test class
  - `BlockLayout_ChildrenStack` -> run a specific test method

## Steps

1. **Determine which tests to run**:
   - If `$ARGUMENTS` is empty -> run all tests:
     ```bash
     dotnet test --configuration Release
     ```
   - If `$ARGUMENTS` is `Unit` / `Layout` / `Visual` / `Integration`:
     ```bash
     dotnet test tests/EggPdf.Tests.{$ARGUMENTS}/ --configuration Release
     ```
   - Otherwise use as filter:
     ```bash
     dotnet test --configuration Release --filter "FullyQualifiedName~$ARGUMENTS"
     ```

2. **On failure**:
   - Read the failing test to understand the expected vs actual
   - Read the relevant source code
   - Explain clearly what's wrong: which assertion failed, expected vs got
   - Suggest a fix but do NOT auto-fix unless the user asks

3. **On success**: Report pass count concisely. Example: "42 tests passed (Unit: 30, Layout: 12)"
