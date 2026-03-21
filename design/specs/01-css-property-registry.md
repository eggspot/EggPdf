# Spec: CSS Property Registry

Complete registry of CSS properties supported by EggPdf. Each entry defines: initial value, whether it's inherited, the value type, and which shorthand(s) expand to it.

## Registry Format

```csharp
class PropertyDefinition
{
    PropertyId Id { get; }
    string Name { get; }                // e.g., "margin-top"
    CssValue InitialValue { get; }      // e.g., CssLength.Zero
    bool Inherited { get; }             // e.g., false
    ValueType ValueType { get; }        // e.g., ValueType.Length
    string? Shorthand { get; }          // e.g., "margin"
    bool AppliesToAllElements { get; }  // most do; some only apply to specific boxes
}
```

## Box Model Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `margin-top` | 0 | No | `margin` |
| `margin-right` | 0 | No | `margin` |
| `margin-bottom` | 0 | No | `margin` |
| `margin-left` | 0 | No | `margin` |
| `margin-block-start` | 0 | No | `margin-block` |
| `margin-block-end` | 0 | No | `margin-block` |
| `margin-inline-start` | 0 | No | `margin-inline` |
| `margin-inline-end` | 0 | No | `margin-inline` |
| `padding-top` | 0 | No | `padding` |
| `padding-right` | 0 | No | `padding` |
| `padding-bottom` | 0 | No | `padding` |
| `padding-left` | 0 | No | `padding` |
| `padding-block-start` | 0 | No | `padding-block` |
| `padding-block-end` | 0 | No | `padding-block` |
| `padding-inline-start` | 0 | No | `padding-inline` |
| `padding-inline-end` | 0 | No | `padding-inline` |
| `border-top-width` | medium (3px) | No | `border-top`, `border-width`, `border` |
| `border-right-width` | medium | No | `border-right`, `border-width`, `border` |
| `border-bottom-width` | medium | No | `border-bottom`, `border-width`, `border` |
| `border-left-width` | medium | No | `border-left`, `border-width`, `border` |
| `border-top-style` | none | No | `border-top`, `border-style`, `border` |
| `border-right-style` | none | No | `border-right`, `border-style`, `border` |
| `border-bottom-style` | none | No | `border-bottom`, `border-style`, `border` |
| `border-left-style` | none | No | `border-left`, `border-style`, `border` |
| `border-top-color` | currentColor | No | `border-top`, `border-color`, `border` |
| `border-right-color` | currentColor | No | `border-right`, `border-color`, `border` |
| `border-bottom-color` | currentColor | No | `border-bottom`, `border-color`, `border` |
| `border-left-color` | currentColor | No | `border-left`, `border-color`, `border` |
| `border-top-left-radius` | 0 | No | `border-radius` |
| `border-top-right-radius` | 0 | No | `border-radius` |
| `border-bottom-right-radius` | 0 | No | `border-radius` |
| `border-bottom-left-radius` | 0 | No | `border-radius` |
| `width` | auto | No | - |
| `height` | auto | No | - |
| `min-width` | auto | No | - |
| `max-width` | none | No | - |
| `min-height` | auto | No | - |
| `max-height` | none | No | - |
| `block-size` | auto | No | - |
| `inline-size` | auto | No | - |
| `box-sizing` | content-box | No | - |
| `box-decoration-break` | slice | No | - |

## Display and Positioning

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `display` | inline | No | - |
| `position` | static | No | - |
| `top` | auto | No | `inset` |
| `right` | auto | No | `inset` |
| `bottom` | auto | No | `inset` |
| `left` | auto | No | `inset` |
| `inset-block-start` | auto | No | `inset-block`, `inset` |
| `inset-block-end` | auto | No | `inset-block`, `inset` |
| `inset-inline-start` | auto | No | `inset-inline`, `inset` |
| `inset-inline-end` | auto | No | `inset-inline`, `inset` |
| `float` | none | No | - |
| `clear` | none | No | - |
| `z-index` | auto | No | - |
| `overflow-x` | visible | No | `overflow` |
| `overflow-y` | visible | No | `overflow` |
| `visibility` | visible | **Yes** | - |
| `opacity` | 1 | No | - |
| `isolation` | auto | No | - |
| `order` | 0 | No | - |

## Flexbox Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `flex-direction` | row | No | `flex-flow` |
| `flex-wrap` | nowrap | No | `flex-flow` |
| `flex-grow` | 0 | No | `flex` |
| `flex-shrink` | 1 | No | `flex` |
| `flex-basis` | auto | No | `flex` |
| `justify-content` | normal | No | `place-content` |
| `align-items` | normal | No | `place-items` |
| `align-self` | auto | No | `place-self` |
| `align-content` | normal | No | `place-content` |
| `gap` | 0 | No | - |
| `row-gap` | normal | No | `gap` |
| `column-gap` | normal | No | `gap` |

## Grid Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `grid-template-columns` | none | No | `grid-template`, `grid` |
| `grid-template-rows` | none | No | `grid-template`, `grid` |
| `grid-template-areas` | none | No | `grid-template`, `grid` |
| `grid-auto-columns` | auto | No | `grid` |
| `grid-auto-rows` | auto | No | `grid` |
| `grid-auto-flow` | row | No | `grid` |
| `grid-column-start` | auto | No | `grid-column`, `grid-area` |
| `grid-column-end` | auto | No | `grid-column`, `grid-area` |
| `grid-row-start` | auto | No | `grid-row`, `grid-area` |
| `grid-row-end` | auto | No | `grid-row`, `grid-area` |
| `justify-items` | legacy | No | `place-items` |
| `justify-self` | auto | No | `place-self` |

## Typography Properties (all inherited)

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `font-family` | depends on UA | **Yes** | `font` |
| `font-size` | medium (16px) | **Yes** | `font` |
| `font-weight` | normal (400) | **Yes** | `font` |
| `font-style` | normal | **Yes** | `font` |
| `font-variant-caps` | normal | **Yes** | `font-variant` |
| `font-variant-ligatures` | normal | **Yes** | `font-variant` |
| `font-variant-numeric` | normal | **Yes** | `font-variant` |
| `font-variant-east-asian` | normal | **Yes** | `font-variant` |
| `font-variant-position` | normal | **Yes** | `font-variant` |
| `font-stretch` | normal | **Yes** | `font` |
| `font-size-adjust` | none | **Yes** | - |
| `font-kerning` | auto | **Yes** | - |
| `font-feature-settings` | normal | **Yes** | - |
| `font-variation-settings` | normal | **Yes** | - |
| `font-optical-sizing` | auto | **Yes** | - |
| `font-synthesis-weight` | auto | **Yes** | `font-synthesis` |
| `font-synthesis-style` | auto | **Yes** | `font-synthesis` |
| `font-synthesis-small-caps` | auto | **Yes** | `font-synthesis` |
| `line-height` | normal | **Yes** | `font` |
| `letter-spacing` | normal | **Yes** | - |
| `word-spacing` | normal | **Yes** | - |
| `text-align` | start | **Yes** | - |
| `text-align-last` | auto | **Yes** | - |
| `text-indent` | 0 | **Yes** | - |
| `text-transform` | none | **Yes** | - |
| `text-decoration-line` | none | No | `text-decoration` |
| `text-decoration-style` | solid | No | `text-decoration` |
| `text-decoration-color` | currentColor | No | `text-decoration` |
| `text-decoration-thickness` | auto | No | `text-decoration` |
| `text-decoration-skip-ink` | auto | **Yes** | - |
| `text-underline-offset` | auto | **Yes** | - |
| `text-underline-position` | auto | **Yes** | - |
| `text-shadow` | none | **Yes** | - |
| `text-emphasis-style` | none | **Yes** | `text-emphasis` |
| `text-emphasis-color` | currentColor | **Yes** | `text-emphasis` |
| `text-emphasis-position` | over right | **Yes** | - |
| `text-overflow` | clip | No | - |
| `text-wrap` | wrap | **Yes** | - |
| `white-space` | normal | **Yes** | - |
| `white-space-collapse` | collapse | **Yes** | - |
| `tab-size` | 8 | **Yes** | - |
| `word-break` | normal | **Yes** | - |
| `overflow-wrap` | normal | **Yes** | - |
| `hyphens` | manual | **Yes** | - |
| `hyphenate-character` | auto | **Yes** | - |
| `hyphenate-limit-chars` | auto | **Yes** | - |
| `hanging-punctuation` | none | **Yes** | - |
| `text-justify` | auto | **Yes** | - |
| `text-rendering` | auto | **Yes** | - |
| `direction` | ltr | **Yes** | - |
| `unicode-bidi` | normal | No | - |
| `writing-mode` | horizontal-tb | **Yes** | - |
| `text-orientation` | mixed | **Yes** | - |
| `text-combine-upright` | none | **Yes** | - |
| `quotes` | auto | **Yes** | - |

## Color and Background Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `color` | (UA-dependent, typically black) | **Yes** | - |
| `background-color` | transparent | No | `background` |
| `background-image` | none | No | `background` |
| `background-repeat` | repeat | No | `background` |
| `background-position-x` | 0% | No | `background-position`, `background` |
| `background-position-y` | 0% | No | `background-position`, `background` |
| `background-size` | auto | No | `background` |
| `background-origin` | padding-box | No | `background` |
| `background-clip` | border-box | No | `background` |
| `background-attachment` | scroll | No | `background` |
| `opacity` | 1 | No | - |
| `color-scheme` | normal | **Yes** | - |
| `print-color-adjust` | economy | **Yes** | - |
| `accent-color` | auto | **Yes** | - |

## Table Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `border-collapse` | separate | **Yes** | - |
| `border-spacing` | 0 | **Yes** | - |
| `caption-side` | top | **Yes** | - |
| `empty-cells` | show | **Yes** | - |
| `table-layout` | auto | No | - |
| `vertical-align` | baseline | No | - |

## List Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `list-style-type` | disc | **Yes** | `list-style` |
| `list-style-position` | outside | **Yes** | `list-style` |
| `list-style-image` | none | **Yes** | `list-style` |
| `counter-increment` | none | No | - |
| `counter-reset` | none | No | - |
| `counter-set` | none | No | - |

## Paged Media Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `break-before` | auto | No | - |
| `break-after` | auto | No | - |
| `break-inside` | auto | No | - |
| `page` | auto | No | - |
| `orphans` | 2 | **Yes** | - |
| `widows` | 2 | **Yes** | - |

## Transform Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `transform` | none | No | - |
| `transform-origin` | 50% 50% | No | - |
| `transform-box` | view-box | No | - |

## Effect Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `box-shadow` | none | No | - |
| `outline-width` | medium | No | `outline` |
| `outline-style` | none | No | `outline` |
| `outline-color` | invert | No | `outline` |
| `outline-offset` | 0 | No | - |
| `filter` | none | No | - |
| `backdrop-filter` | none | No | - |
| `mix-blend-mode` | normal | No | - |
| `clip-path` | none | No | - |
| `mask-image` | none | No | `mask` |
| `image-rendering` | auto | No | - |
| `image-orientation` | from-image | No | - |

## Object Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `object-fit` | fill | No | - |
| `object-position` | 50% 50% | No | - |
| `aspect-ratio` | auto | No | - |

## Multi-Column Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `column-count` | auto | No | `columns` |
| `column-width` | auto | No | `columns` |
| `column-gap` | normal | No | `gap` |
| `column-rule-width` | medium | No | `column-rule` |
| `column-rule-style` | none | No | `column-rule` |
| `column-rule-color` | currentColor | No | `column-rule` |
| `column-span` | none | No | - |
| `column-fill` | balance | No | - |

## Content and Generated Properties

| Property | Initial | Inherited | Shorthand |
|----------|---------|-----------|-----------|
| `content` | normal | No | - |
| `string-set` | none | No | - |
| `bookmark-level` | none | No | - |
| `bookmark-label` | content() | No | - |

## Custom Properties

| Property | Initial | Inherited |
|----------|---------|-----------|
| `--*` (any custom property) | (guaranteed-invalid) | **Yes** |

Custom properties are always inherited. They are resolved via `var()` during value computation.

## Shorthand Expansion Rules

### 1-4 Value Pattern (margin, padding, border-width, border-style, border-color, border-radius)

```
1 value:  all four sides
2 values: vertical horizontal
3 values: top horizontal bottom
4 values: top right bottom left
```

### font Shorthand

```
font: [font-style] [font-variant] [font-weight] [font-stretch] font-size[/line-height] font-family

// Examples:
font: bold 14px/1.5 Arial, sans-serif
  -> font-style: normal
  -> font-variant: normal
  -> font-weight: bold
  -> font-stretch: normal
  -> font-size: 14px
  -> line-height: 1.5
  -> font-family: Arial, sans-serif

font: italic small-caps 700 condensed 16px "Times New Roman"
```

### background Shorthand

```
background: [color] [image] [repeat] [attachment] [position] [/ size] [origin] [clip]

// Can have multiple layers (comma-separated), color only on last layer
```

### border Shorthand

```
border: [width] [style] [color]
// Applies to all 4 sides
// Each side can also be set individually: border-top, border-right, etc.
```

### flex Shorthand

```
flex: [grow] [shrink] [basis]

flex: none        -> 0 0 auto
flex: auto        -> 1 1 auto
flex: 1           -> 1 1 0%
flex: 1 0 auto    -> 1 0 auto
```

### grid Shorthand

```
grid: [grid-template-rows] / [grid-template-columns]
grid-template: "header header" 60px "main sidebar" 1fr / 2fr 1fr
grid-area: row-start / column-start / row-end / column-end
```

### Total Property Count

| Category | Count |
|----------|-------|
| Box model | 32 |
| Display/position | 20 |
| Flexbox | 12 |
| Grid | 12 |
| Typography | 50 |
| Color/background | 14 |
| Table | 6 |
| List | 6 |
| Paged media | 6 |
| Transform | 3 |
| Effects | 12 |
| Object | 3 |
| Multi-column | 8 |
| Content | 4 |
| **Total** | **~188 longhands** |

Plus ~50 shorthand properties that expand to the longhands above.
