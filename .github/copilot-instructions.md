# EggPdf -- Copilot Instructions

## Project
EggPdf is a pure C#, zero-dependency HTML/CSS-to-PDF rendering engine.
MIT license. Open source at https://github.com/eggspot/EggPdf

## Architecture
8-stage pipeline: HTML Parse -> CSS Parse -> Style Resolve -> Box Generate -> Layout -> Fragment -> Paint -> PDF Write.
See BLUEPRINT.md for full details.

## Rules
1. Zero external NuGet dependencies in the core library
2. Must compile on netstandard2.0 through net9.0
3. Write tests FIRST -- test must fail before implementation
4. Parsers never throw -- produce error nodes instead
5. Unknown CSS properties silently ignored (graceful degradation)
6. Use Span<T> on netstandard2.1+ via #if, ArraySegment on netstandard2.0
7. No LINQ in hot paths -- use for loops
8. Conventional commits: feat:, fix:, perf:, test:, refactor:, docs:, chore:

## Test Style
- xUnit + FluentAssertions
- Naming: Feature_Condition_ExpectedBehavior
- AAA pattern (Arrange, Act, Assert)
- Run: dotnet test --configuration Release
