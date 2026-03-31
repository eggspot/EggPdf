---
name: test-templates
description: Render all WebUI templates through the PDF pipeline and verify output correctness
allowed-tools: Bash(dotnet *), Bash(curl *), Bash(cat *), Bash(grep *), Bash(wc *), Read, Grep, Glob, Agent
---

# Test All Templates

Render every WebUI template through the EggPdf API and verify PDF output quality.

## Steps

1. **Ensure service is running**
   ```bash
   curl -s http://localhost:55727/health || (dotnet run --project src/EggPdf.Service -c Release -- --urls http://localhost:55727 & sleep 8)
   ```

2. **Extract template HTML** from `src/EggPdf.Service/wwwroot/index.html`
   - Read the `templates` JavaScript object
   - Extract each template's HTML string

3. **For each template**, render via API and verify:
   ```bash
   curl -s -X POST http://localhost:55727/api/render \
     -H "Content-Type: application/json" \
     -d '{"html":"TEMPLATE_HTML"}' -o /tmp/test_TEMPLATE.pdf
   ```

4. **Verify each PDF**:
   - File size > 500 bytes (not empty/error)
   - Contains expected text strings (grep for key text in content stream)
   - Paint order is correct (backgrounds `re f` or `h f` appear BEFORE their text `Tj`)
   - No `?` characters replacing special chars (bullet, em-dash etc.)
   - Font resources include `/Encoding /WinAnsiEncoding`
   - All list bullets have correct Y positions (matching their item text Y)
   - Border strokes present where CSS specifies borders
   - Rounded rect backgrounds use Bezier curves (`c` operator)

5. **Report results** as a table:

   | Template | Size | Text OK | Backgrounds | Borders | Paint Order | Issues |
   |----------|------|---------|-------------|---------|-------------|--------|
   | blank    | ...  | ...     | ...         | ...     | ...         | ...    |
   | invoice  | ...  | ...     | ...         | ...     | ...         | ...    |
   | ...      | ...  | ...     | ...         | ...     | ...         | ...    |

6. **Flag any template** that has:
   - Missing text content
   - Backgrounds painted after text (paint order bug)
   - Text appearing as `?` instead of special characters
   - Content extending beyond page boundaries
   - Missing borders or backgrounds
