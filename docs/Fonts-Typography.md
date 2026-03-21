# Fonts & Typography

## Default Fonts

EggPdf uses system fonts by default. The fallback chain:
1. Fonts added via `PdfOptions.Fonts`
2. `@font-face` fonts from CSS
3. System fonts (Windows/macOS/Linux)
4. Built-in PDF fonts (Helvetica, Times, Courier)

## Custom Fonts

```csharp
var options = new PdfOptions();
options.Fonts.AddFile("./fonts/MyFont.ttf");
options.Fonts.AddDirectory("/usr/share/fonts/truetype");
options.Fonts.EnableSubsetting = true;  // Only embed used glyphs
```

## @font-face

```css
@font-face {
    font-family: 'CustomFont';
    src: url('CustomFont.woff2') format('woff2'),
         url('CustomFont.woff') format('woff');
    font-weight: normal;
    font-style: normal;
}

body { font-family: 'CustomFont', Arial, sans-serif; }
```

WOFF and WOFF2 formats are supported.

## Variable Fonts

```css
@font-face {
    font-family: 'Inter';
    src: url('Inter-Variable.ttf') format('truetype');
}

h1 { font-variation-settings: "wght" 700; }
p { font-variation-settings: "wght" 400; }
```

## CJK (Chinese, Japanese, Korean)

CJK fonts are auto-detected when CJK characters appear and the current font lacks them. System CJK fonts (Noto Sans CJK, MS Gothic, PingFang) are used as fallback.

Font subsetting efficiently handles large CJK fonts (10-20MB) by extracting only used glyphs.

## Emoji

Color emoji are supported. EggPdf uses the system color emoji font or an optionally embedded Noto Color Emoji:

```csharp
options.Fonts.EmbedEmojiFont = true;  // Bundle Noto Color Emoji
```

## Hyphenation

```css
p {
    hyphens: auto;
    text-align: justify;
}
```

Requires the `lang` attribute on the HTML element. Hyphenation dictionaries for major languages are built in.
