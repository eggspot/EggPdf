# Spec: User-Agent Stylesheet

Complete default stylesheet matching Chrome's print defaults. Applied at lowest cascade priority.

## Source

This is based on Chromium's `html.css` user-agent stylesheet, filtered for print-relevant rules.

## The Stylesheet

```css
/* ============================================
   EggPdf User-Agent Stylesheet
   Based on Chromium defaults for print rendering
   ============================================ */

/* === Document === */
html {
    display: block;
}

body {
    display: block;
    margin: 8px;
}

/* === Headings === */
h1 { display: block; font-size: 2em; font-weight: bold; margin-block-start: 0.67em; margin-block-end: 0.67em; }
h2 { display: block; font-size: 1.5em; font-weight: bold; margin-block-start: 0.83em; margin-block-end: 0.83em; }
h3 { display: block; font-size: 1.17em; font-weight: bold; margin-block-start: 1em; margin-block-end: 1em; }
h4 { display: block; font-weight: bold; margin-block-start: 1.33em; margin-block-end: 1.33em; }
h5 { display: block; font-size: 0.83em; font-weight: bold; margin-block-start: 1.67em; margin-block-end: 1.67em; }
h6 { display: block; font-size: 0.67em; font-weight: bold; margin-block-start: 2.33em; margin-block-end: 2.33em; }

/* Heading inside hgroup */
hgroup > h1 ~ h2, hgroup > h1 ~ h3, hgroup > h1 ~ h4, hgroup > h1 ~ h5, hgroup > h1 ~ h6 {
    font-size: revert;
    margin-block-start: revert;
    margin-block-end: revert;
}

/* === Paragraphs and Block Elements === */
p { display: block; margin-block-start: 1em; margin-block-end: 1em; }
div { display: block; }
section { display: block; }
article { display: block; }
aside { display: block; }
header { display: block; }
footer { display: block; }
nav { display: block; }
main { display: block; }
hgroup { display: block; }
search { display: block; }
figure { display: block; margin-block-start: 1em; margin-block-end: 1em; margin-inline-start: 40px; margin-inline-end: 40px; }
figcaption { display: block; }
address { display: block; font-style: italic; }

/* === Horizontal Rule === */
hr {
    display: block;
    margin-block-start: 0.5em;
    margin-block-end: 0.5em;
    margin-inline-start: auto;
    margin-inline-end: auto;
    border-style: inset;
    border-width: 1px;
    overflow: hidden;
    color: gray;
}

/* === Lists === */
ul, menu { display: block; list-style-type: disc; margin-block-start: 1em; margin-block-end: 1em; padding-inline-start: 40px; }
ol { display: block; list-style-type: decimal; margin-block-start: 1em; margin-block-end: 1em; padding-inline-start: 40px; }
li { display: list-item; }
ul ul, ol ul { list-style-type: circle; }
ul ul ul, ol ul ul, ul ol ul, ol ol ul { list-style-type: square; }

/* Nested list margins */
ul ul, ul ol, ul menu, ol ul, ol ol, ol menu, menu ul, menu ol, menu menu {
    margin-block-start: 0;
    margin-block-end: 0;
}

dl { display: block; margin-block-start: 1em; margin-block-end: 1em; }
dt { display: block; font-weight: bold; }
dd { display: block; margin-inline-start: 40px; }

/* === Blockquote and Pre === */
blockquote {
    display: block;
    margin-block-start: 1em;
    margin-block-end: 1em;
    margin-inline-start: 40px;
    margin-inline-end: 40px;
}

pre {
    display: block;
    font-family: monospace;
    white-space: pre;
    margin-block-start: 1em;
    margin-block-end: 1em;
    font-size: 0.83em;
}

/* === Inline Text === */
a { color: blue; text-decoration: underline; }
/* Note: :visited is NEVER matched in our engine (privacy + static doc) */

strong, b { font-weight: bold; }
em, i { font-style: italic; }
u { text-decoration: underline; }
s, del, strike { text-decoration: line-through; }
ins { text-decoration: underline; }
small { font-size: smaller; }
big { font-size: larger; }
sub { vertical-align: sub; font-size: smaller; }
sup { vertical-align: super; font-size: smaller; }
mark { background-color: yellow; color: black; }
code, kbd, samp, tt { font-family: monospace; font-size: 0.83em; }
var { font-style: italic; }
dfn { font-style: italic; }
abbr[title] { text-decoration: underline dotted; }
q::before { content: open-quote; }
q::after { content: close-quote; }
cite { font-style: italic; }

/* Bidirectional text */
bdi { unicode-bidi: isolate; }
bdo { unicode-bidi: bidi-override; }

/* === Tables === */
table { display: table; border-collapse: separate; border-spacing: 2px; border-color: gray; box-sizing: border-box; text-indent: 0; }
thead { display: table-header-group; vertical-align: middle; }
tbody { display: table-row-group; vertical-align: middle; }
tfoot { display: table-footer-group; vertical-align: middle; }
tr { display: table-row; vertical-align: inherit; }
td { display: table-cell; vertical-align: inherit; padding: 1px; }
th { display: table-cell; vertical-align: inherit; padding: 1px; font-weight: bold; text-align: center; }
caption { display: table-caption; text-align: center; }
col { display: table-column; }
colgroup { display: table-column-group; }

/* === Forms (rendered as static visual elements) === */
input { font-family: inherit; font-size: inherit; }
textarea { font-family: monospace; font-size: inherit; white-space: pre-wrap; }
select { font-family: inherit; font-size: inherit; }
button { font-family: inherit; font-size: inherit; text-align: center; }
fieldset { display: block; margin-inline-start: 2px; margin-inline-end: 2px; padding-block-start: 0.35em; padding-inline-start: 0.75em; padding-inline-end: 0.75em; padding-block-end: 0.625em; border: 2px groove #ddd; min-inline-size: min-content; }
legend { display: block; padding-inline-start: 2px; padding-inline-end: 2px; }
label { cursor: default; }

/* === Placeholder Text === */
::placeholder { color: #757575; opacity: 1; }

/* === Interactive Elements === */
details { display: block; }
summary { display: list-item; list-style-type: disclosure-closed; }
details[open] > summary { list-style-type: disclosure-open; }
dialog { display: none; }
dialog[open] { display: block; }

/* === Replaced Elements === */
img, svg { display: inline; }
img { object-fit: contain; }

/* === Deprecated Elements (Chrome still renders these) === */
center { display: block; text-align: center; }
nobr { white-space: nowrap; }
marquee { display: block; }
acronym { font-variant: small-caps; }

/* === Hidden === */
[hidden] { display: none !important; }
area, base, head, link, meta, param, script, style, title,
template, datalist { display: none; }
noscript { display: inline; }  /* We show noscript content (no JS engine) */

/* === Ruby (CJK) === */
ruby { display: ruby; }
rt { display: ruby-text; font-size: 0.5em; }
rp { display: none; }

/* === Meter and Progress === */
meter { display: inline-block; width: 5em; height: 1em; vertical-align: -0.2em; }
progress { display: inline-block; width: 10em; height: 1em; vertical-align: -0.2em; }

/* === Print-Specific === */
@media print {
    /* Chrome's default: suppress backgrounds unless print-color-adjust: exact */
    /* Our engine default: PRESERVE backgrounds (users explicitly want PDF output) */
    /* Users can opt out with print-color-adjust: economy */
}
```

## Notes

1. **Font sizes are relative** to the parent's font-size (em-based). The root default is 16px.
2. **Margins use `margin-block-start/end`** (logical properties) but for horizontal-tb writing mode these resolve to margin-top/bottom.
3. **`a` color is always blue** (we don't support `:visited` since there's no browsing history in a PDF renderer).
4. **`noscript` displays its content** because we don't execute JavaScript.
5. **`datalist` is hidden** by default (only shown as part of `<select>` rendering).
6. **Backgrounds are preserved by default** in our engine, unlike Chrome which suppresses them in print. Users explicitly want PDF output, so background preservation is the expected behavior. They can disable with `print-color-adjust: economy`.
