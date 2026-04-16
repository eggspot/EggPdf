---
name: review
description: Dual-perspective code review — Lead Dev (architecture/correctness) + Dev (implementation/tests). Runs entirely in the main agent for easy follow-up.
disable-model-invocation: true
user-invocable: true
allowed-tools: Bash, Read, Grep, Glob, TodoWrite
---

# Code Review — Lead Dev + Dev Perspectives

Review the current branch diff from two perspectives in sequence. Everything runs in the main agent — no subagents — so you can ask follow-up questions about any finding.

## Arguments

- `$ARGUMENTS` — optional: specific commit range or file to focus on (default: HEAD~1..HEAD, or all commits ahead of main)

## Step 1: Gather context

```bash
# What commits are on this branch vs main?
git log main..HEAD --oneline

# Full diff for the branch
git diff main..HEAD

# Which files changed?
git diff --stat main..HEAD
```

If `$ARGUMENTS` specifies a range or file, use that instead.

## Step 2: Run the test suite

```bash
dotnet test tests/EggPdf.Tests.Unit -c Release
dotnet test tests/EggPdf.Tests.Layout -c Release
```

Report pass/fail counts. Stop and surface any failures before continuing.

## Step 3: 🔵 LEAD DEV REVIEW

Think like a senior engineer who owns the architecture. Read every changed file carefully.

Check for:

**Correctness & Spec compliance**
- Does the logic match the CSS/PDF spec? (cite spec if relevant)
- Off-by-one errors, wrong comparisons, missed edge cases
- Incorrect coordinate system assumptions (px vs pt, top-origin vs bottom-origin)
- PDF operator semantics (persistent state, BT/ET scope, graphics state stack)

**Architecture & design**
- Does the change fit the 8-stage pipeline model (Parse → Style → Layout → Paint → PDF)?
- Does it belong in the right project (`EggPdf.Layout` vs `EggPdf.Pdf` vs `EggPdf` etc.)?
- Does it introduce unnecessary coupling or violate zero-dependency rule?
- Is it the right abstraction, or is there a simpler/more robust approach?

**Regressions**
- Could this change silently break other layout paths (flex, table, block, inline)?
- Does it affect hot paths? (check for LINQ, allocations, repeated work)

**netstandard2.0 compatibility**
- No `record` types, no `Span<T>.Contains`, no `Contains(string, StringComparison)` → use `IndexOf`
- No C# features unavailable on netstandard2.0 without `#if`

Present findings as:
```
🔵 LEAD DEV
─────────────────────────────
CRITICAL  — [file:line] description
MODERATE  — [file:line] description
MINOR     — [file:line] description
APPROVED  — areas that look solid
```

## Step 4: 🟢 DEV REVIEW

Think like the developer who will maintain this code day-to-day.

Check for:

**Test quality**
- Are new behaviors covered by tests?
- Do tests prove the bug existed before the fix (failing test first)?
- Are test names descriptive (`Feature_Condition_ExpectedBehavior`)?
- Any tests that could pass vacuously (assert nothing meaningful)?

**Code clarity**
- Is the logic easy to follow without comments?
- Are variable names clear?
- Is there dead code, unused imports, or orphaned helpers?
- Are comments added only where logic is non-obvious?

**Implementation details**
- Does the fix address root cause or just symptom?
- Any magic numbers that should be named constants?
- Edge cases handled (null, empty, zero, negative)?

**Consistency**
- Does the style match surrounding code?
- Same patterns used for similar problems elsewhere in the codebase?

Present findings as:
```
🟢 DEV
─────────────────────────────
ISSUE     — [file:line] description
SUGGESTION — [file:line] description
LOOKS GOOD — areas that are well done
```

## Step 5: Summary verdict

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
REVIEW VERDICT
──────────────
Tests:      ✅ N passed / ❌ N failed
Lead Dev:   ✅ APPROVED / ⚠️ CHANGES REQUESTED / ❌ BLOCKED
Dev:        ✅ APPROVED / ⚠️ CHANGES REQUESTED

Blockers (must fix before merge):
  1. ...

Suggestions (nice to have):
  1. ...
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

Stay in the main agent throughout. After the review, the user can ask follow-up questions or request fixes on any specific finding.
