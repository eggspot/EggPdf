# Tables

## Basic Table

```html
<table>
    <thead>
        <tr><th>Item</th><th>Qty</th><th>Price</th></tr>
    </thead>
    <tbody>
        <tr><td>Widget</td><td>10</td><td>$5.00</td></tr>
        <tr><td>Gadget</td><td>5</td><td>$12.00</td></tr>
    </tbody>
    <tfoot>
        <tr><td colspan="2">Total</td><td>$110.00</td></tr>
    </tfoot>
</table>
```

## Repeating Headers

When a table spans multiple pages, `<thead>` automatically repeats at the top of each page. `<tfoot>` repeats at the bottom. No CSS needed -- this works out of the box.

## Table Styling

```css
table {
    width: 100%;
    border-collapse: collapse;
}

th, td {
    border: 1px solid #ddd;
    padding: 8px;
    text-align: left;
}

th {
    background-color: #f5f5f5;
    font-weight: bold;
}

/* Zebra stripes */
tr:nth-child(even) { background-color: #fafafa; }

/* Prevent row splitting across pages */
tr { break-inside: avoid; }
```

## Wide Tables

If a table is wider than the page:
- Default: content is clipped
- With `PdfOptions.ShrinkToFit = true`: content scales down to fit

## Large Tables (5,000+ rows)

EggPdf uses streaming table layout for very large tables. Rows are laid out incrementally without holding the entire table in memory. This handles tables with tens of thousands of rows.
