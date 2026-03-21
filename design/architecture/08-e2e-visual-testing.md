# 08 - E2E Visual Testing Architecture

## Overview

EggPdf uses its own WebUI as the E2E visual testing platform. The core idea:
compare **browser's native print rendering** against **EggPdf's PDF rendering**
for the same HTML input. If they match, EggPdf renders like Chrome.

## Architecture

```
                    Same HTML Input
                         |
            +------------+------------+
            |                         |
            v                         v
   Browser Print Preview         EggPdf PDF Render
   (iframe with @media print)    (POST /api/render)
            |                         |
            v                         v
      Screenshot A               Screenshot B
            |                         |
            +------------+------------+
                         |
                    Pixel Diff
                    (tolerance)
                         |
                    PASS / FAIL
```

## Components

### 1. Service Endpoints

| Endpoint | Purpose |
|---|---|
| `POST /api/render` | Render HTML to PDF via EggPdf engine |
| `POST /api/render/print-preview` | Wrap HTML in a print-simulation page for browser rendering |
| `GET /e2e` | E2E comparison page: side-by-side browser vs PDF |

### 2. E2E Comparison Page (`/e2e`)

A built-in test page at `http://localhost:8080/e2e` that:
- Shows two iframes side-by-side: **Browser Print Preview** and **EggPdf PDF**
- Dropdown to select test cases (heading, table, invoice, styles, lists)
- "Run Test" renders both and displays them for visual comparison
- "Run All" cycles through all test cases

### 3. Test Cases

Pre-defined HTML snippets covering core rendering features:

| Test Case | What It Tests |
|---|---|
| `heading` | h1, p, strong, em -- basic text |
| `table` | Table with headers, borders, rows |
| `invoice` | Full invoice: heading + table + total |
| `styles` | Background colors, border-radius, colors |
| `list` | Ordered and unordered lists |

### 4. Automated Testing (Playwright)

For CI, Playwright runs headless Chrome:

```
1. Start EggPdf.Service on port 8080
2. Navigate to http://localhost:8080/e2e
3. For each test case:
   a. Select from dropdown
   b. Click "Run Test"
   c. Wait for both frames to render
   d. Screenshot left iframe (browser print preview)
   e. Screenshot right iframe (EggPdf PDF)
   f. Pixel-diff the two screenshots
   g. Assert diff < threshold (e.g., 5% pixels differ)
4. Report PASS/FAIL per test case
```

## Testing Flow

### Manual Testing
1. Run: `dotnet run --project src/EggPdf.Service -- --urls http://localhost:8080`
2. Open: `http://localhost:8080/e2e`
3. Select test case from dropdown
4. Click "Run Test"
5. Visually compare left (browser) vs right (EggPdf PDF)
6. Click "Run All" to cycle through all tests

### Automated Testing (CI)
```bash
# Install Playwright
npx playwright install chromium

# Run E2E visual tests
dotnet test tests/EggPdf.Tests.E2E --configuration Release
```

## Why This Approach

| Advantage | Details |
|---|---|
| **Uses our own product** | The WebUI IS the test tool -- no separate test infrastructure needed |
| **Browser is the reference** | We compare against Chrome's rendering, which is our target |
| **Easy to add tests** | Just add HTML snippet to the test cases object |
| **Manual + automated** | Developers can visually compare, CI can pixel-diff |
| **Catches regressions** | Any rendering change shows up as a visual diff |
| **Tests real pipeline** | Full HTML -> Parse -> Style -> Layout -> Paint -> PDF path |

## Future Enhancements

- Screenshot capture via Playwright for CI
- Pixel diff with configurable tolerance
- Golden image storage for regression tracking
- Coverage tracking: which CSS properties are tested
- Automated test case generation from WPT reftests
