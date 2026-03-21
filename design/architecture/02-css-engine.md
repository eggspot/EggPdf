# 02 - CSS Engine Architecture

## Overview

The CSS engine covers three subsystems: **parsing**, **selector matching**, and **cascade/style resolution**. Together they transform raw CSS text + a DOM tree into a `ComputedStyle` per element.

```
CSS text (from <style>, <link>, inline style="...")
    |
    v
CssTokenizer (CSS Syntax Level 3)
    |
    v
CssParser (rules, declarations, at-rules)
    |
    v
CssStyleSheet[] (structured rules)
    +
    | + DOM tree
    v
SelectorMatcher (match rules to elements)
    |
    v
CascadeResolver (specificity, origin, !important, @layer)
    |
    v
ComputedStyle per element (all properties resolved to absolute values)
```

## Part 1: CSS Tokenizer

### Responsibility
Implements CSS Syntax Module Level 3 tokenization. Converts CSS text to a flat token stream.

### Token Types

```csharp
enum CssTokenType
{
    Ident,          // color, margin-top, --my-var
    Function,       // rgb(, calc(, var(
    AtKeyword,      // @media, @page, @font-face
    Hash,           // #id, #fff
    String,         // "hello", 'world'
    BadString,      // unterminated string
    Url,            // url(image.png)
    BadUrl,         // malformed url()
    Number,         // 42, 3.14, -1
    Percentage,     // 50%
    Dimension,      // 10px, 2em, 1.5rem
    Whitespace,
    Colon,          // :
    Semicolon,      // ;
    Comma,          // ,
    LeftBracket,    // [
    RightBracket,   // ]
    LeftParen,      // (
    RightParen,     // )
    LeftCurly,      // {
    RightCurly,     // }
    Delim,          // any single character not covered above (>, +, ~, *, etc.)
    EOF
}

struct CssToken
{
    CssTokenType Type;
    ReadOnlyMemory<char> Value;     // raw text
    double NumericValue;            // for Number, Percentage, Dimension
    string? Unit;                   // for Dimension (px, em, rem, etc.)
    HashType HashType;              // Id or Unrestricted (for Hash tokens)
}
```

### Key Parsing Rules
- **Escape sequences**: `\` followed by hex digits or any character
- **Comments**: `/* ... */` consumed during tokenization (not emitted as tokens)
- **Strings**: single or double quoted, with escape support
- **Numbers**: integer and float, with optional sign
- **Dimensions**: number immediately followed by ident (no whitespace)
- **URLs**: `url(...)` with or without quotes

## Part 2: CSS Parser

### Responsibility
Converts token stream into structured stylesheets (rules, declarations, at-rules).

### Output Structure

```csharp
class CssStyleSheet
{
    List<CssRule> Rules { get; }
}

abstract class CssRule;

class CssStyleRule : CssRule
{
    SelectorList Selectors { get; }         // parsed selector
    DeclarationBlock Declarations { get; }  // property: value pairs
}

class CssAtRule : CssRule;

class CssMediaRule : CssAtRule
{
    MediaQueryList Queries { get; }         // @media print, @media (min-width: 768px)
    List<CssRule> Rules { get; }            // nested rules
}

class CssPageRule : CssAtRule
{
    PageSelectorList Selectors { get; }     // :first, :left, :right, :blank
    DeclarationBlock Declarations { get; }
    List<MarginAtRule> MarginRules { get; } // @top-center, @bottom-right, etc.
}

class CssFontFaceRule : CssAtRule
{
    DeclarationBlock Declarations { get; }  // font-family, src, unicode-range, etc.
}

class CssLayerRule : CssAtRule
{
    string? Name { get; }
    List<CssRule> Rules { get; }
}

class CssSupportsRule : CssAtRule
{
    SupportsCondition Condition { get; }    // evaluated against our supported properties
    List<CssRule> Rules { get; }
}

class CssImportRule : CssAtRule
{
    string Url { get; }
    MediaQueryList? MediaQueries { get; }
}

class CssCounterStyleRule : CssAtRule
{
    string Name { get; }
    DeclarationBlock Declarations { get; }  // system, symbols, prefix, suffix, range, etc.
}

class CssNestingRule : CssStyleRule { }     // CSS nesting: & selector
class CssScopeRule : CssAtRule { }          // @scope
class CssContainerRule : CssAtRule { }      // @container
class CssPropertyRule : CssAtRule { }       // @property
```

### Declaration Block

```csharp
class DeclarationBlock
{
    List<CssDeclaration> Declarations { get; }
}

struct CssDeclaration
{
    string Property { get; }        // e.g., "margin-top"
    CssValue Value { get; }         // parsed value
    bool Important { get; }         // !important flag
}
```

### CSS Value Types

```csharp
abstract class CssValue;

class CssKeyword : CssValue           // auto, inherit, initial, none, bold, flex, etc.
class CssNumber : CssValue            // 42, 3.14
class CssPercentage : CssValue        // 50%
class CssDimension : CssValue         // 10px, 2em, 1.5rem, 90deg
class CssColor : CssValue             // #fff, rgb(255,0,0), hsl(0,100%,50%), oklch(...)
class CssString : CssValue            // "hello"
class CssUrl : CssValue               // url(image.png)
class CssFunction : CssValue          // calc(100% - 20px), var(--x), min(), max(), clamp()
class CssValueList : CssValue         // space-separated: 10px 20px 30px 40px
class CssCommaSeparatedList : CssValue // comma-separated: Arial, Helvetica, sans-serif
```

### Shorthand Expansion

The parser expands all CSS shorthands into longhands before storing in the declaration block:

```csharp
static class ShorthandExpander
{
    // margin: 10px 20px ->
    //   margin-top: 10px, margin-right: 20px, margin-bottom: 10px, margin-left: 20px
    static IEnumerable<CssDeclaration> Expand(string property, CssValue value);
}
```

50+ shorthands, each with unique expansion rules. The expander is a registry mapping property names to expansion functions.

### Graceful Degradation
- Unknown properties: silently ignored (per CSS error recovery spec)
- Invalid values: property skipped, fallback to initial value
- Malformed rules: rule skipped, parser continues
- Parser NEVER throws

## Part 3: Selector Engine

### Responsibility
Given an element and a selector, determine if the selector matches.

### Selector Representation

```csharp
abstract class Selector;

class TypeSelector : Selector               // div, p, *
class ClassSelector : Selector              // .foo
class IdSelector : Selector                 // #bar
class AttributeSelector : Selector          // [href], [type="text"], [class~="foo"]
class PseudoClassSelector : Selector        // :first-child, :hover, :nth-child(2n+1)
class PseudoElementSelector : Selector      // ::before, ::after, ::first-line
class CombinatorSelector : Selector         // A B (descendant), A > B (child), A + B (adjacent), A ~ B (sibling)
class CompoundSelector : Selector           // div.foo#bar (all must match)
class SelectorList : Selector               // div, p, span (any must match)
class NegationSelector : Selector           // :not(...)
class IsSelector : Selector                 // :is(...)
class WhereSelector : Selector              // :where(...) (zero specificity)
class HasSelector : Selector                // :has(...)
```

### Matching Algorithm

```csharp
static class SelectorMatcher
{
    // Core: does this selector match this element?
    static bool Matches(Selector selector, HtmlElement element);

    // For the cascade: find ALL rules that match this element
    static List<MatchedRule> FindMatchingRules(
        HtmlElement element,
        CssStyleSheet[] stylesheets);
}

struct MatchedRule
{
    CssStyleRule Rule;
    Specificity Specificity;
    int SourceOrder;
    CascadeOrigin Origin;       // UserAgent, Author, AuthorImportant
    int LayerOrder;             // @layer position
}
```

### Performance: Bloom Filter for Fast Rejection

For large stylesheets (Bootstrap: 5000+ rules), checking every rule against every element is O(elements * rules). We use two optimizations from Servo:

1. **Rule indexing by rightmost selector**: index rules by ID, class, tag name, and universal. When matching an element, only check rules whose rightmost selector could match.

2. **Ancestor Bloom filter**: for descendant/child selectors, maintain a Bloom filter of ancestor tag names/classes/IDs. If the Bloom filter says "no ancestor has class .container", skip all `.container .item` rules without walking the DOM tree.

### Specificity Calculation

```csharp
struct Specificity : IComparable<Specificity>
{
    int A;  // ID selectors
    int B;  // class selectors, attribute selectors, pseudo-classes
    int C;  // type selectors, pseudo-elements

    // :where() contributes zero specificity
    // :is() and :not() use the most specific argument
    // :has() uses the most specific argument
}
```

## Part 4: Cascade and Style Resolution

### Cascade Algorithm (CSS Cascade Level 5)

For each property on each element, the winning value is determined by:

```
1. Cascade origin + importance:
   Transition declarations              (highest)
   User-agent !important
   Author !important
   Animation declarations
   Author normal                        (most rules are here)
   User-agent normal                    (lowest)

2. Within same origin: @layer order (later layers win)

3. Within same layer: specificity (higher wins)

4. Same specificity: source order (later wins)

5. Inline styles (style="...") have no specificity -- they always beat stylesheet rules
   (unless the stylesheet rule is !important)
```

### Style Resolution Pipeline

```csharp
class StyleResolver
{
    // For each element in the DOM tree:
    ComputedStyle Resolve(HtmlElement element, ComputedStyle? parentStyle)
    {
        // 1. Collect all matching rules
        var matched = SelectorMatcher.FindMatchingRules(element, stylesheets);

        // 2. Sort by cascade order
        matched.Sort(CascadeComparer);

        // 3. Build cascaded values (winning value per property)
        var cascaded = new CascadedValues();
        foreach (var rule in matched)
            foreach (var decl in rule.Declarations)
                cascaded.Set(decl.Property, decl.Value, decl.Important);

        // 4. Add inline styles (highest priority in non-important)
        if (element.HasAttribute("style"))
            ApplyInlineStyles(element.GetAttribute("style"), cascaded);

        // 5. Inherit from parent
        foreach (var prop in InheritableProperties)
            if (!cascaded.Has(prop))
                cascaded.Set(prop, parentStyle?.Get(prop) ?? InitialValues[prop]);

        // 6. Apply defaults for non-inherited properties
        foreach (var prop in AllProperties)
            if (!cascaded.Has(prop))
                cascaded.Set(prop, InitialValues[prop]);

        // 7. Resolve relative values to absolute
        return ComputeAbsoluteValues(cascaded, parentStyle);
    }
}
```

### Value Resolution (relative -> absolute)

```csharp
static class ValueResolver
{
    // em -> px (relative to parent font-size)
    // rem -> px (relative to root font-size)
    // % -> px (relative to containing block width/height)
    // calc() -> evaluated to a number
    // var() -> substituted with custom property value
    // currentColor -> resolved to computed color
    // min(), max(), clamp() -> evaluated
    // env() -> resolved (for print: all zero)

    static CssValue Resolve(CssValue value, ResolveContext ctx);
}
```

### ComputedStyle

```csharp
class ComputedStyle
{
    // ~200+ properties, all resolved to absolute values
    // Stored as a flat array indexed by PropertyId (enum) for O(1) access

    CssValue Get(PropertyId property);
    void Set(PropertyId property, CssValue value);

    // Frequently accessed shortcuts:
    DisplayType Display { get; }
    float FontSize { get; }             // in px
    Color Color { get; }
    float Width { get; }                // in px, or Auto
    float MarginTop { get; }            // in px
    // ... etc.
}

enum PropertyId : ushort
{
    Display = 0,
    Position = 1,
    Float = 2,
    // ... ~200+ properties
    // Contiguous enum for array indexing
}
```

### User-Agent Stylesheet

A built-in default stylesheet matching Chrome's print defaults. Parsed once at converter creation and cached.

```css
/* Excerpt from the UA stylesheet */
html { display: block; }
body { display: block; margin: 8px; }
h1 { display: block; font-size: 2em; font-weight: bold; margin: 0.67em 0; }
h2 { display: block; font-size: 1.5em; font-weight: bold; margin: 0.83em 0; }
p { display: block; margin: 1em 0; }
a { color: blue; text-decoration: underline; }
code, pre, kbd, samp { font-family: monospace; }
ul, ol { padding-left: 40px; }
table { border-collapse: separate; border-spacing: 2px; }
[hidden] { display: none !important; }
/* ... ~200 more rules */
```

### @media Evaluation

```csharp
static class MediaEvaluator
{
    // Our engine default: @media print = true, @media screen = false
    static bool Evaluate(MediaQueryList queries, MediaContext ctx);
}

class MediaContext
{
    MediaType Type { get; }           // Print (default) or Screen
    float Width { get; }              // page content width in px
    float Height { get; }             // page content height in px
    bool Color { get; }               // true
    int ColorIndex { get; }           // 0
    bool Monochrome { get; }          // false
    string PrefersColorScheme { get; } // "light"
    float Resolution { get; }         // 96 dpi (standard)
}
```

### @supports Evaluation

```csharp
static class SupportsEvaluator
{
    // Evaluate against OUR supported properties
    static bool Evaluate(SupportsCondition condition)
    {
        // @supports (display: flex) -> true (we support flexbox)
        // @supports (display: subgrid) -> depends on implementation phase
        // Supports AND, OR, NOT combinators
    }
}
```

## Testing

| Test Area | Approach |
|-----------|----------|
| CSS Tokenizer | Token output for known inputs (edge cases: escape sequences, URLs, numbers, strings) |
| CSS Parser | Parse input, verify AST structure (shorthands expanded, at-rules structured) |
| Shorthand Expansion | Every shorthand with every value pattern |
| Selector Matching | Match selectors against known DOM trees, verify matches/non-matches |
| Specificity | Calculate specificity for complex selectors, verify ordering |
| Cascade | Full cascade resolution with multiple origins, layers, !important |
| @media | Evaluate media queries against known contexts |
| @supports | Feature query evaluation against our property set |
| ComputedStyle | Full style resolution for known HTML+CSS inputs |
