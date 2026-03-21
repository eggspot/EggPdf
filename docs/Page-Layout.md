# Page Layout

## Page Size and Margins

Via PdfOptions:
```csharp
var options = new PdfOptions
{
    PageSize = PageSize.A4,            // or Letter, Legal, A3, A5
    Orientation = PageOrientation.Portrait,
    Margins = new PageMargins(20, 15, 20, 15, Unit.Mm)  // top, right, bottom, left
};
```

Via CSS:
```css
@page {
    size: A4 portrait;
    margin: 2cm;
}
```

## Mixed Orientations

Different pages can have different orientations:

```css
@page { size: A4 portrait; }
@page landscape { size: A4 landscape; }

.chart-section { page: landscape; }
```

## Page Breaks

```css
/* Force break before an element */
.chapter { break-before: page; }

/* Prevent break inside an element */
.keep-together { break-inside: avoid; }

/* Legacy (also supported) */
.chapter { page-break-before: always; }
```

## Orphans and Widows

```css
p {
    orphans: 3;  /* Min lines at bottom of page */
    widows: 3;   /* Min lines at top of page */
}
```

## CSS Flexbox and Grid

Both work exactly as in Chrome:

```css
.container { display: flex; gap: 20px; }
.grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; }
```
