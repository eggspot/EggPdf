---
name: render-compare
description: Render HTML through EggPdf and analyze PDF output for visual correctness issues
allowed-tools: Bash(dotnet *), Bash(curl *), Bash(cat *), Bash(grep *), Read, Grep, Glob, Agent
---

# Render Compare

Render HTML through the EggPdf pipeline and analyze the PDF content stream for rendering issues.

## Usage

`/render-compare <html or template-name>`

If argument is a template name (invoice, report, letter, etc.), use that template.
If argument is HTML, render it directly.

## Steps

1. **Ensure service is running**
   ```bash
   curl -s http://localhost:55727/health || (dotnet run --project src/EggPdf.Service -c Release -- --urls http://localhost:55727 & sleep 8)
   ```

2. **Render the HTML** via the API:
   ```bash
   curl -s -X POST http://localhost:55727/api/render \
     -H "Content-Type: application/json" \
     -d '{"html":"THE_HTML"}' -o /tmp/render_compare.pdf
   ```

3. **Extract and analyze the content stream**:
   ```bash
   cat /tmp/render_compare.pdf | sed -n '/^stream$/,/^endstream$/p'
   ```

4. **Verify paint order** (CRITICAL):
   - For each element with a background, the fill operation (`re f` or `h f`) MUST appear BEFORE the text (`Tj`)
   - If text appears before its background, it will be invisible (painted under the background)

5. **Verify all text is present**:
   - Extract all `Tj` operations and check that expected text strings are in the PDF
   - Check for `?` characters replacing special chars

6. **Verify layout**:
   - Check X/Y coordinates of text match expected positions
   - Check that no content extends beyond page boundaries (MediaBox)
   - Check flex item positioning (items should be side-by-side, not stacked)

7. **Report findings** with specific issues and suggested fixes
