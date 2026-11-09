---
name: pr
description: Take the current branch from finished code to a merged pull request into main, automatically and end to end — review the plan, review all the code in two rounds (Lead Dev then Dev), fix the clear findings and commit, run the test suites (and benchmarks for hot-path changes), open (or update) the PR with the standard description, do an independent pre-merge review pass in the session (no subagents) to a MERGE verdict, wait for every CI job to pass, squash-merge, check the release the merge starts, then give the user one report. Use EVERY time a PR is about to be created or updated — when the user says "make/open/create a PR", "send it", "ship it", "send for review", or when Claude has finished coding a task and is about to open a PR on its own (required by the "Every pull request goes through the `pr` skill" rule in `CLAUDE.md`).
---

Take the current branch to a pull request into `main`: $ARGUMENTS

**Run everything in THIS session, with no subagents** (the "One agent" rule in `CLAUDE.md`), so the user sees every finding and every edit. The independent pre-merge review in step 7 and any research question are separate passes you do yourself, not subagents. Follow `CLAUDE.md` and the user's global rules; this skill adds the PR routine on top.

**Runs end to end without stopping.** Invoking this skill (by `/pr`, by asking for a PR, or because the work is done and a PR is next) is the user's go-ahead to fix the clear review findings, commit this branch's work, run the tests, push, open the PR and merge it once the independent review says MERGE and every CI job is green. Don't pause for confirmation between steps; the user gets one report at the end (step 8). The only stops are the ones written below (nothing to PR, a blocker that can't be fixed safely, red tests or a benchmark regression that can't be fixed, a BLOCK verdict that can't be fixed).

**Every merge into `main` is a release.** The Publish workflow derives the version from the merged commit's conventional prefix (`feat:` → minor, `fix:`/`perf:`/`chore:`/`docs:`/`refactor:`/`test:` → patch, `!:` or `BREAKING CHANGE` → major), pushes to NuGet, tags, creates the GitHub Release, starts the binaries, Docker, Hugging Face and Pages deploys, then commits the version bump. Never bump `<Version>`, tag or run `gh release create` by hand.

**This repo is public.** Never mention Jira, tickets or internal systems in branch names, commits, the PR title or body. No secret values anywhere.

**Options** (in `$ARGUMENTS`; only when the user passes them — never pick one on your own):
- `--review-only` — do steps 1–3 (branch check, plan review, two code review rounds), present the findings and stop. No fixes, no commits, no tests, no PR.
- `--skip-review` — skip steps 2–4. Only for docs-only or few-line changes; say in the report and the PR that the review was skipped. Step 7's independent review still runs before a merge.
- `--no-merge` — stop after step 6 with the PR open and ready for review; say so in the report and in the PR.
- Anything else in `$ARGUMENTS` is extra context (where the plan is, what to focus on, a title override).

## 1. Check the branch

1. `git status`, `git branch --show-current`, `git fetch origin`.
2. Take the project lock unless this session already holds it: `project-lock status .`, then `project-lock acquire . "<session title>"` (script in `vibe-coding-playbook/scripts/project-lock`). If another session holds it, stop and say who.
3. On `main`, or on a branch whose PR is already merged (`gh pr list --head {branch} --state merged`): create a new branch from `origin/main`, carrying the uncommitted changes (`git switch -c {type}/{kebab-slug} origin/main`). If `main` has local commits not on `origin/main` (`git log origin/main..HEAD`), branch from `HEAD` instead so they come along, then `git branch -f main origin/main`. `{type}` is the conventional prefix of the work (`feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `chore`), as in the repo's history (`feat/pdf-stamper`, `perf/lazy-pdf-reader`).
4. **Uncommitted changes:** list them. They are part of the review scope; step 4 commits them.
5. `git log origin/main..HEAD --oneline` plus the uncommitted changes are what the PR carries. If both are empty, stop and say so.
6. `gh pr list --head {branch} --state open` — if this branch already has an open PR (a draft opened to claim the work counts), step 6 updates that PR instead of opening a new one.
7. `gh pr list --state open` — if another open PR does the same work, stop and say so (global "Parallel sessions" rule 6).

Don't merge `origin/main` into the branch here — only step 7 does that, when GitHub reports the branch out of date or conflicting.

## 2. Review the plan

**Find the plan**, in this order: the plan agreed earlier in this conversation; `.git/progress-{branch}.md` (`/` → `-`); a plan file the conversation or `$ARGUMENTS` points to; a GitHub issue the work closes. If there's no plan at all, rebuild the intent from the request and the commit messages, say plainly that no written plan existed, and review against that.

Read the changed files in full (`git diff origin/main...HEAD` + uncommitted) before judging. Then check two things:

**A. Is the plan right?**
- It does what was asked, and the user-visible result is checked in the actual PDF bytes, not just "doesn't throw".
- Right place in the 8-stage pipeline (HTML Parse → CSS Parse → Style → Box → Layout → Fragment → Paint → PDF Write) and the right project (`EggPdf.Layout` vs `EggPdf.Paint` vs `EggPdf.Pdf`, ...); no duplicate of an existing helper in `EggPdf.Core` or elsewhere.
- Zero external dependencies; compiles on every target (netstandard2.0 through net10.0).
- Performance First: memory grows with what the caller uses, not with input size; lazy over eager; bounded caches; a scale test with an allocation ceiling for reader/writer/layout paths.
- Entry-point parity: a new PDF-writer capability is wired into `PdfRenderOptions`, the CLI, the REST API and `EggPdf.Fluent` in the same change, or the gap is tracked in `BLUEPRINT.md`.
- HTML/CSS ↔ Fluent parity: a new or fixed capability has typed Fluent sugar, or is confirmed reachable through `RawStyle`/`RawAttribute`/`Raw`/`Css`, or is tracked as a gap.
- Names are honest and use the CSS/PDF spec's words (global "Naming" rule). Public API names are a breaking change once released.
- Edge cases: empty/zero/negative values, unknown CSS (ignored, never throws), missing resources (warning, not crash), pagination across page breaks, RTL/complex scripts where text is involved.

**B. Does the code match the plan?**
- Every planned step is done.
- Anything the code does that the plan didn't say — list it; fine if justified, but it must be named in the PR.
- The tests the plan promised exist and assert the behaviour.

## 3. Review the code — two rounds

Follow the two perspectives of `.claude/skills/review/SKILL.md` — Round 1 = its 🔵 Lead Dev review, Round 2 = its 🟢 Dev review — with the scope = every file changed on this branch vs `origin/main`, plus uncommitted changes. Read each changed file in full. Skip that skill's "Run the test suite" step (step 5 here runs it) and its verdict ending (step 8 reports).

In Round 1 also check these PR-level items:
- **netstandard2.0:** no `record`, no `Span<T>.Contains`, `IndexOf(string, StringComparison)` not `Contains(string, StringComparison)`, newer APIs behind `#if`.
- **Hot paths:** no LINQ, `ArrayPool<T>` for temporary buffers, no per-call allocations that scale with input. A hot-path change needs a benchmark in step 5.
- **Infallible parsers:** HTML/CSS parsing never throws.
- **Breaking changes:** a removed or changed public member or behaviour needs `!` in the PR title (major release). Say so in the PR.
- **No comments** outside the global rule's allowed list (XML doc comments on public API, analyzer directives), including old comments left in touched files.
- **Fluent:** every new public `EggPdf.Fluent` member has an XML `<summary>`; typed values only, no magic strings outside the `Raw*` methods; `site/fluent-api.html` regenerated with `dotnet run --project tools/EggPdf.DocGen` when the public surface changed.
- **Docs in sync:** `README.md`, `llms.txt`, `site/llms.txt`, the relevant `site/*.html` page and `CLAUDE.md`, updated for any user-facing capability change; nothing documented as working that isn't verified in the PDF bytes.
- **File size:** a substantial new feature added to an existing class lives in its own `ClassName.Feature.cs` partial.
- **Tests:** named `Feature_Condition_ExpectedBehavior`; they would fail without the change.
- No dead code, unused usings, leftover debug output, `Console.WriteLine` outside `EggPdf.Cli`, or secret values.
- Invisible or line-breaking characters written as `(char)0x....`, never as raw characters in source.

Don't edit anything during the rounds. Write down every finding from step 2 and both rounds — step 8 reports each one.

With `--review-only`: present the findings (Plan review, Round 1 in the Lead Dev format, Round 2 in the Dev format, Top priorities) and end here.

## 4. Fix and commit

Don't stop for the user.

- **Fix** every CRITICAL, MODERATE and ISSUE finding, and every MINOR or SUGGESTION whose fix is clear and inside this change's scope, plus anything ❌ missing from the plan that the request clearly asks for. Re-read the exact lines before each fix and show each edit. Write all the fixes first; step 5 tests them once.
- **A bug found outside this change's scope** (in code the branch doesn't touch) still gets fixed, per the "Fix every bug you find" rule in `CLAUDE.md`: list it in the report, and queue it for the next batch PR (see "Batch bug fixes" in `CLAUDE.md`) right after this one merges.
- **Don't fix, research it instead:** a finding that needs a design decision, would change a public API name beyond this change, or that you're not sure about gets researched by you in this session (global "Questions: research them" rule, no subagent): read the repo, its history and, where useful, the web, and decide. Follow that recommendation and record it in the PR under **Decisions** and in the report under "Decided by research".
- **Stop before the PR** if a CRITICAL finding can't be fixed safely (the plan itself is wrong, a requirement is missing or unclear): give the step 8 report without a PR link and say what blocks it. Never open a PR that is known to be broken.
- **Commit** with a conventional prefix: the uncommitted work from step 1 as `{type}: {what changed}`; review fixes as `fix: {what changed}` or `refactor: {what changed}`; comment removals in their own commit `refactor: remove comments in {File}`. No `Co-Authored-By` or other AI attribution trailer — branch commits stay visible on this public repo. Stage the files by name (never `git add -A` blind — check `git status` for stray files like local settings, scratch output, `bin/`/`obj/` or rendered PDFs and leave those out; say so in the report).
- Keep a list of what each fix changed — it goes in the PR's **Plan** section ("Changes during the code review rounds").

## 5. Test once

Invoking this skill counts as the request for one test run (the batch is complete). No standalone `dotnet build` — `dotnet test` compiles what it needs.

If the branch changes no file the build or tests read (only Markdown, `.claude/`, `design/`), skip the tests and say so in the PR and the report.

If the public `EggPdf.Fluent` surface changed, regenerate the API reference first: `dotnet run --project tools/EggPdf.DocGen`, and commit `site/fluent-api.html` if it changed.

```bash
export MSBuildEnableWorkloadResolver=false
dotnet test tests/EggPdf.Tests.Unit -c Release
dotnet test tests/EggPdf.Tests.Layout -c Release
dotnet test tests/EggPdf.Tests.Fluent -c Release
```

Also run the Playwright suite when `src/EggPdf.Service` or `tests/EggPdf.Tests.E2E` changed: `PLAYWRIGHT_BROWSERS_PATH=0 dotnet test tests/EggPdf.Tests.E2E -c Release`.

**Benchmarks** when a hot path changed (parsing, style, layout, fragmentation, paint, PDF writing, readers): `dotnet run --project benchmarks/EggPdf.Benchmarks -c Release -- --filter "*Render*" --job short` and compare with "Current Benchmark Results" in `CLAUDE.md`. A regression in time or memory is a failure: fix it like a red test.

If something fails, fix it (show each fix), commit the fixes (`fix: ...`) and re-run until green. If it can't be made green safely, stop before the PR and report the failure. Record the totals per suite (`N passed, N failed, N skipped`) and the benchmark table for the PR. When done: `cd ~ && dotnet build-server shutdown --vbcscompiler`.

## 6. Push and open the PR

1. `git status` must be clean apart from the stray files step 4 deliberately left out. If branch work is still uncommitted, commit it as in step 4 before going on.
2. `git push -u origin HEAD`. Never force-push.
3. No open PR for the branch (step 1.6): `gh pr create --base main --title "{title}" --body-file <scratchpad file>`. An open PR already exists: `gh pr edit {n} --title "{title}" --body-file <scratchpad file>`, and `gh pr ready {n}` if it is a draft.

**Title:** `{type}: {what the user gets}` in plain words, lowercase after the prefix, no trailing period, `!` after the type for a breaking change — the same style as the repo's history (`feat: stamp images onto pages of an existing PDF -- PdfStamper`). The title becomes the squash commit's subject, so its prefix decides the release: pick it for the whole PR, not the last commit. The title is also copied into the release notes: write every CSS at-rule and code name in backticks (`` `@page` ``, `` `@media` ``), because a bare `@word` mentions and notifies the GitHub account of that name.

**Body** — write from the actual diff, the plan and the review report. Omit sections that don't apply; keep sentences short and concrete:

````markdown
## Summary
One-line summary, then bullets: what the user gets, the public API added or changed, the projects touched.

## Plan
How it was planned and reviewed, and the decisions from the plan review.

Changes during the code review rounds (not in the reviewed plan):
- Round 1 found ... ; changed to ...
- Round 2 ...

## Decisions
Choices made and why (naming, defaults, behaviour on bad input). Mark the ones decided by research.

## Entry points
Which of `HtmlToPdf`, `PdfRenderOptions`, CLI, REST API and `EggPdf.Fluent` expose the change, and how (typed method or `RawStyle`/`Raw`/`Css`). Name any gap and where it is tracked.

## Docs
Which of README, llms.txt, site pages and CLAUDE.md were updated.

## Breaking changes
What breaks for existing callers and how to migrate (only with a `!` title).

## Not in this PR
What's deliberately left out, and gaps in test coverage with the reason.

## Comment cleanup
The `remove comments` commit: what was removed and where its information went.

## Tests
New/changed test classes and what each covers. Per suite: N passed, N failed, N skipped.

## Performance
Benchmark table before/after for hot-path changes, or "No hot-path change".
````

Rules for the body (and for any PR comment this skill posts): no AI attribution line of any kind, no Jira or ticket mentions, no secret values, no `@` mentions. With `--skip-review`, the Plan section says the review was skipped.

With `--no-merge`: end here and go to step 8.

## 7. Independent review, CI, merge

1. **Independent review pass (no subagent).** Reread `gh pr diff {n}` from the top as a reviewer who hasn't seen the work, against `CLAUDE.md` and the same rules as step 3, without relying on your earlier conclusions. Write down a verdict, MERGE or BLOCK, with each blocking issue's file, line and reason before changing anything. For each blocking issue: fix it, commit, re-test the affected suites, push, and do the pass again over the new commits. If a blocker can't be fixed safely, leave the PR open, say why in a PR comment, and go to step 8.
2. **Up to date.** If GitHub reports the branch behind or conflicting (`gh pr view {n} --json mergeStateStatus,mergeable`): `git merge origin/main`, resolve conflicts, re-run step 5's suites, push. This replaces the global "rebase late" rule here: the PR is squash-merged, so a merge commit on the branch never reaches `main`, while rebasing a pushed branch would need a force-push. Never force-push.
3. **CI, per job.** `gh pr checks {n} --watch`, then check every matrix job, because `gh pr checks` deduplicates by name and can say pass while one OS leg failed:
   `gh run list --branch {branch} --workflow CI --limit 1 --json databaseId --jq '.[0].databaseId'` gives `{runId}`, then
   `gh run view {runId} --json jobs --jq '.jobs[] | .name + " => " + .conclusion'`
   Every job must be `success`. On a failure, read the log (`gh run view {runId} --log-failed`; `MSB4181` with `0 Error(s)` is a test failure, grep for `Failed EggPdf`), fix, commit, push and watch again.
4. **Merge.** `gh pr merge {n} --squash --delete-branch --subject "{PR title} (#{n})" --body "{short summary}"` — write the subject and body yourself so branch commit trailers don't carry over.
5. **After the merge.** Confirm it merged (`gh pr view {n} --json state,mergeCommit`). Then `gh run list --commit {merge sha}` and watch CI and Publish to the end; Publish pushes a `chore: bump version to X.Y.Z [skip ci]` commit and starts Release Binaries, Docker, Pages and the Hugging Face deploy (`gh run list --limit 10`); check the GitHub Release exists (`gh release view vX.Y.Z`). Every run must succeed. A failed release means that version never shipped: fixing it comes before any new work in this repo.
6. **Clean up.** `git switch main && git pull --ff-only`; `gh pr merge --delete-branch` usually deletes the local branch too — if `git branch --list {branch}` still shows it, `git branch -D {branch}` (squash-merged branches look unmerged to `-d`); delete `.git/progress-{branch}.md` if it exists (its exact path, in its own command), and `project-lock release . "<session title>"`.

## 8. Report to the user

Once the PR is merged (or once a step above stopped), give one report:

```
## PR
{link} — {merged as vX.Y.Z | open, ready for review | open, blocked} · CI: {per-job summary}

## Plan review
✅ Done as planned · ❌ Was missing (fixed / not fixed) · ➕ Not in the plan · ⚠️ Plan problems

## Review findings
Round 1 (Lead Dev), Round 2 (Dev) and the independent review, each finding with: file:line — what — ✔ Fixed (commit) / ✖ Not fixed (why)

## Decided by research
Each question, the answer followed and why, so the user can overrule it.

## Tests & benchmarks
Per suite: N passed, N failed, N skipped · benchmark result or "no hot-path change"

## Release
Publish run, version, NuGet / binaries / Docker / Hugging Face runs and their result.

## Left over
Stray files left uncommitted, follow-ups, gaps tracked in BLUEPRINT.md.
```

- When the PR was left open, say exactly why, in the report and in the PR.
- Leave the working tree clean on `main` after a merge, or on the feature branch when the PR stays open.
