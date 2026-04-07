---
name: docs
description: Update EggPdf documentation — README, docs/ wiki pages, site/, llms.txt, CLAUDE.md — to reflect current implementation
user-invocable: true
allowed-tools: Read, Edit, Write, Bash, Grep, Glob
---

# Docs Updater

Audit and update all EggPdf documentation to stay in sync with the implementation.

## Scope

| File/Dir | Purpose |
|----------|---------|
| `README.md` | GitHub repo front page + NuGet description |
| `docs/*.md` | GitHub Wiki pages (14 files) |
| `site/` | GitHub Pages static site |
| `site/llms.txt` | AI/LLM discovery (site-scoped) |
| `llms.txt` | AI/LLM discovery (repo root) |
| `CLAUDE.md` | Claude Code development rules |
| `spaces/README.md` | HuggingFace Space metadata |

## Steps

### 1. Audit current state

Check for common staleness signals:

```bash
# Version mismatches
grep -r "version\|Version" src/EggPdf/EggPdf.csproj src/EggPdf.Service/Program.cs site/index.html README.md | grep -E "\"[0-9]+\.[0-9]+\.[0-9]+\""

# Wrong Docker image name (should be eggspot/eggpdf)
grep -r "eggpdf/service\|eggpdf/cli" docs/ README.md site/

# Outdated benchmark placeholder
grep -n "Phase 1\|will be added" README.md

# HuggingFace short_description length (must be ≤ 60 chars)
python3 -c "
import re
content = open('spaces/README.md').read()
m = re.search(r'short_description: (.+)', content)
if m: print(len(m.group(1)), 'chars:', m.group(1))
"
```

### 2. Fix version numbers

- `site/index.html`: Update `softwareVersion` in JSON-LD and `v{x.y.z}` in hero badge
- `src/EggPdf.Service/Program.cs`: Use `typeof(EggPdf.HtmlToPdf).Assembly.GetName().Version` instead of hardcoded string

### 3. Fix Docker image names

Correct name is `eggspot/eggpdf:latest` (check `.github/workflows/docker.yml` to confirm).
Replace any `eggpdf/service:latest` or `eggpdf/cli` references in `docs/` and `README.md`.

### 4. Update benchmarks

If `<!-- BENCHMARK_START -->` in README.md has a placeholder, replace with current numbers from `CLAUDE.md`:

```
| Simple page (h1 + p) | **13 µs** | 20 KB |
| Invoice (table + styles) | **86 µs** | 85 KB |
| Large table (100 rows) | **1.2 ms** | 939 KB |
```

### 5. Check llms.txt files

Both `llms.txt` (root) and `site/llms.txt` should have:
- Current install command
- Working code examples
- Performance numbers
- Link to full AI guide: `https://raw.githubusercontent.com/eggspot/EggPdf/main/llms.txt`

### 6. Check spaces/README.md

- `short_description` must be ≤ 60 characters
- `app_port` must be `8080`
- API example must use the correct HF Space URL

### 7. Check docs/ wiki pages

For each changed feature, verify the corresponding wiki page is accurate:
- Endpoint names, request/response shapes, option names
- Code examples actually compile (no removed APIs)
- Version requirements (e.g., "requires EggPdf 1.0+")

### 8. CLAUDE.md

Verify:
- Project structure section matches actual `src/` and `tests/` layout
- Test count comment is approximately correct (`dotnet test --list-tests | wc -l`)
- Benchmark table reflects current numbers
- Key files list is accurate

## What NOT to change

- Don't document stub/unimplemented features as if they work (e.g. image rendering, full signing)
- Don't add fictional API surface that doesn't exist in code
- Don't change `docs/*.md` content without verifying against actual implementation in `src/`

## After making changes

Run a quick sanity check:
```bash
# Build still passes
dotnet build src/EggPdf/EggPdf.csproj -c Release --no-restore

# No broken links in README (basic check)
grep -o 'https://[^)\"]*' README.md | sort -u
```
