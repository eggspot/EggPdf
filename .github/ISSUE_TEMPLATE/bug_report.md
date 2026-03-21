---
name: Bug Report
about: Report a rendering issue or unexpected behavior
title: ''
labels: bug
assignees: ''
---

## Describe the bug
A clear description of what the bug is.

## To Reproduce

HTML input:
```html
<html>
<body>
  <!-- Minimal HTML that reproduces the issue -->
</body>
</html>
```

CSS (if separate):
```css
/* CSS that triggers the issue */
```

Code:
```csharp
var converter = new HtmlToPdfConverter(new PdfOptions { /* options */ });
byte[] pdf = await converter.RenderAsync(html);
```

## Expected behavior
What should the PDF look like? (attach screenshot from Chrome Print if possible)

## Actual behavior
What does the PDF actually look like? (attach screenshot or describe)

## Environment
- EggPdf version:
- .NET version:
- OS:
- PDF reader used to view output:
