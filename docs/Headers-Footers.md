# Headers, Footers & Page Numbers

## Programmatic (Simple)

```csharp
var options = new PdfOptions
{
    Header = new PageHeaderFooter
    {
        Left = "My Company",
        Center = "{{title}}",
        Right = "{{date:yyyy-MM-dd}}",
        FontSize = 9
    },
    Footer = new PageHeaderFooter
    {
        Center = "Page {{page}} of {{pages}}",
        FontSize = 8,
        LineAbove = true
    }
};
```

Template variables: `{{page}}`, `{{pages}}`, `{{title}}`, `{{date}}`, `{{date:format}}`.

## CSS @page Margin Boxes (Advanced)

```css
@page {
    margin: 2cm;

    @top-center {
        content: "My Document";
        font-size: 9pt;
    }

    @bottom-center {
        content: "Page " counter(page) " of " counter(pages);
        font-size: 8pt;
    }

    @bottom-right {
        content: "Confidential";
        font-size: 7pt;
        color: #999;
    }
}

/* Different first page */
@page :first {
    @top-center { content: none; }
}

/* Different left/right pages */
@page :left { @bottom-left { content: counter(page); } }
@page :right { @bottom-right { content: counter(page); } }
```

## Running Headers (change per section)

```css
h1 { string-set: chapter-title content(); }

@page {
    @top-left {
        content: string(chapter-title);
    }
}
```

## Page Labels (different numbering per section)

```csharp
var merger = new PdfMerger();
merger.Add(coverPdf, label: null);                    // no number
merger.Add(tocPdf, label: new PageLabel(Roman));       // i, ii, iii
merger.Add(bodyPdf, label: new PageLabel(Decimal));    // 1, 2, 3
merger.Add(appendixPdf, label: new PageLabel("A-"));   // A-1, A-2
```
