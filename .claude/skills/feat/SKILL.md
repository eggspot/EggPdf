---
name: feat
description: Implement a new feature following EggPdf's test-first, performance-aware development loop
disable-model-invocation: true
user-invocable: true
allowed-tools: Bash, Read, Write, Edit, Grep, Glob, Agent, TodoWrite
---

# Feature Implementation -- Test-First Workflow

Implement a feature for EggPdf following the strict loop: write test -> write code -> verify test -> fix -> benchmark -> repeat until all green.

## Arguments

- `$ARGUMENTS` -- description of the feature to implement (e.g., "HTML tokenizer data state", "CSS margin shorthand expansion", "block formatting context layout")

## Workflow

### Phase 1: Understand

1. **Read the blueprint** for implementation details:
   - `BLUEPRINT.md` -- find the relevant component and phase
   - Search GitHub issues for context: `gh issue list --search "$ARGUMENTS"`

2. **Read the affected files** before writing any code. Understand existing patterns:
   - Check the project structure to find where the code belongs
   - Read neighboring files to match coding conventions
   - Check existing tests to understand test patterns

3. **Create a feature branch**:
   ```bash
   git checkout -b feat/{feature-name}
   ```

### Phase 2: Test First (RED)

4. **Write tests FIRST** -- this is non-negotiable:
   - Follow naming: `Feature_Condition_ExpectedBehavior`
   - Use xUnit + FluentAssertions, AAA pattern (Arrange, Act, Assert)
   - Cover: happy path, edge cases, null/empty input, error recovery
   - For parsers: use html5lib-tests data where applicable
   - For layout: assert exact positions and sizes numerically
   - For PDF: validate binary structure

5. **Run tests -- they MUST fail** (proves the test is meaningful):
   ```bash
   dotnet test --configuration Release --filter "FullyQualifiedName~{TestClass}"
   ```
   If tests pass before implementation, the tests are wrong. Fix them.

### Phase 3: Implement (GREEN)

6. **Implement the feature** following these constraints:
   - **Zero external dependencies** -- pure C#, BCL only
   - **Multi-target aware** -- code must compile on netstandard2.0 through net10.0
   - **Performance-conscious** -- no unnecessary allocations, use `Span<T>` on netstandard2.1+, `ArrayPool` where appropriate
   - **Infallible parsers** -- HTML/CSS parsers never throw, produce error nodes instead
   - **Graceful degradation** -- unknown CSS properties silently ignored, missing resources produce warnings not exceptions
   - Follow existing patterns in the codebase

7. **Run tests -- they MUST pass**:
   ```bash
   dotnet test --configuration Release
   ```
   Fix failures. Repeat until 100% green. Do NOT move forward with failing tests.

### Phase 4: Refine (REFACTOR)

8. **Review your own code**:
   - No dead code or commented-out blocks
   - No over-engineering -- simplest approach that passes all tests
   - Public API has XML documentation
   - Internal code has comments only where logic isn't self-evident

9. **Run all tests again** -- full suite, not just your new ones:
   ```bash
   dotnet test --configuration Release
   ```

### Phase 5: Performance Verify

10. **If the change is in a hot path** (parser, layout engine, paint, PDF writer):
    ```bash
    cd benchmarks/EggPdf.Benchmarks && dotnet run -c Release -- --filter *{Scenario}* --exporters json markdown
    ```

11. **Check for regressions** -- compare against previous results:
    - No benchmark should regress more than 10%
    - Memory allocations should not increase
    - If regression found: optimize before committing

### Phase 6: Commit

12. **Commit with conventional prefix**:
    - `feat:` for new features
    - `fix:` for bug fixes
    - `perf:` for performance improvements
    - `test:` for test-only changes
    - `refactor:` for restructuring without behavior change

13. **Push the branch** (but do NOT create PR -- use `/ship` for that):
    ```bash
    git push -u origin feat/{feature-name}
    ```

## Key Reminders

- NEVER push directly to `main` -- always feature branch + PR
- NEVER skip tests. Write test -> Write code -> Verify. Always.
- NEVER commit with failing tests
- Performance matters from day 1 -- don't write slow code expecting to optimize later
- Keep changes minimal and focused. One feature per branch.
- Every public type needs XML documentation
