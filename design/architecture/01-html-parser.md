# 01 - HTML Parser Architecture

## Overview

Implements the WHATWG HTML5 parsing algorithm. Converts an HTML string into a DOM tree. The parser is **infallible** -- every possible input produces a valid DOM.

## Components

```
string (HTML)
    |
    v
HtmlTokenizer (state machine, ~80 states)
    |
    v
Token stream (StartTag, EndTag, Character, Comment, Doctype, EOF)
    |
    v
HtmlTreeBuilder (insertion modes, ~20 modes)
    |
    v
HtmlDocument (DOM tree)
```

## HtmlTokenizer

### Responsibility
Converts a character stream into HTML tokens. Implements the WHATWG tokenizer spec.

### State Machine
The tokenizer is a state machine with ~80 states. Key states:

| State | Entered When | Produces |
|-------|-------------|----------|
| Data | Default / after closing tag | Character tokens |
| TagOpen | After `<` | Transitions to StartTag/EndTag/Comment/Doctype |
| TagName | After `<a` | StartTag token with tag name |
| BeforeAttributeName | After tag name + space | Transitions to attribute parsing |
| AttributeName | After space in tag | Attribute name |
| AttributeValue (quoted/unquoted) | After `=` | Attribute value |
| RawText / RCDATA | After `<script>`, `<style>`, `<textarea>`, `<title>` | Raw text until matching close tag |
| MarkupDeclaration | After `<!` | Comment or Doctype |
| BogusComment | Invalid markup | Comment token (error recovery) |

### Bidirectional Coupling with TreeBuilder
The HTML5 spec **requires** the tree builder to switch tokenizer states. This is not optional:
- `<script>` -> tokenizer enters Script Data state
- `<style>` -> tokenizer enters RawText state
- `<textarea>` -> tokenizer enters RCDATA state
- `<title>` -> tokenizer enters RCDATA state
- `<plaintext>` -> tokenizer enters Plaintext state

The tokenizer exposes `SetState(TokenizerState state)` called by the tree builder.

### Character Reference (Entity) Decoding
Entities are decoded during tokenization:
- Named: `&amp;` -> `&`, `&hearts;` -> `♥` (~2,231 named entities)
- Numeric decimal: `&#65;` -> `A`
- Numeric hex: `&#x41;` -> `A`
- Invalid references: produce U+FFFD replacement character

Entity lookup uses a **trie** (prefix tree) for efficient matching, since entity names vary in length and some are prefixes of others (`&not` vs `&notin`).

### Token Types

```csharp
abstract record HtmlToken;
record StartTagToken(string TagName, List<Attribute> Attributes, bool SelfClosing) : HtmlToken;
record EndTagToken(string TagName) : HtmlToken;
record CharacterToken(char Character) : HtmlToken;
record CommentToken(string Data) : HtmlToken;
record DoctypeToken(string? Name, string? PublicId, string? SystemId, bool ForceQuirks) : HtmlToken;
record EndOfFileToken() : HtmlToken;

record Attribute(string Name, string Value);
```

### Performance Considerations
- Use `ReadOnlySpan<char>` on netstandard2.1+ for zero-allocation tokenization
- On netstandard2.0: use `string` with index tracking (no Span)
- Emit character tokens in batches (collect consecutive characters into a single string) to reduce token count
- Pre-allocate attribute list (most elements have < 5 attributes)
- Entity trie is a static readonly structure built at class load time

## HtmlTreeBuilder

### Responsibility
Consumes tokens from the tokenizer and builds a DOM tree. Implements the WHATWG tree construction spec.

### Insertion Modes
The tree builder is a state machine with ~20 insertion modes:

| Mode | Active During | Key Behavior |
|------|-------------|--------------|
| Initial | Before `<!DOCTYPE>` | Sets quirks mode |
| BeforeHtml | Before `<html>` | Creates `<html>` if missing |
| BeforeHead | Before `<head>` | Creates `<head>` if missing |
| InHead | Inside `<head>` | Handles `<title>`, `<style>`, `<link>`, `<meta>`, `<script>` |
| AfterHead | Between `</head>` and `<body>` | Creates `<body>` if missing |
| **InBody** | Inside `<body>` | **Handles most elements** -- the largest and most complex mode |
| InTable | Inside `<table>` | Special table parsing rules |
| InTableBody | Inside `<tbody>`, `<thead>`, `<tfoot>` | Row group handling |
| InRow | Inside `<tr>` | Cell handling |
| InCell | Inside `<td>`, `<th>` | Cell content |
| InSelect | Inside `<select>` | Option handling |
| AfterBody | After `</body>` | Only `</html>` expected |
| AfterAfterBody | After `</html>` | Only whitespace/comments expected |
| Text | After `<script>`, `<title>`, `<style>` etc. | Raw text collection |

### Key Algorithms

**Adoption Agency Algorithm** (most complex part of the spec):
Handles misnested formatting elements like `<b>text<i>more</b>still italic</i>`.
The algorithm reparents nodes to produce the correct DOM.

**Foster Parenting**:
When a block element appears inside `<table>` without being in a cell, the element is "foster parented" -- placed before the table in the DOM.

**Active Formatting Elements List**:
Tracks open formatting elements (`<b>`, `<i>`, `<a>`, etc.) to reconstruct them after certain elements are closed.

**Implied End Tags**:
Some elements are implicitly closed when another element opens. E.g., `<p>text<p>more` creates two separate `<p>` elements.

### Open Elements Stack
The tree builder maintains a stack of open elements. The current node is the top of the stack. Elements are pushed when opened and popped when closed.

```csharp
class OpenElementsStack
{
    void Push(HtmlElement element);
    HtmlElement Pop();
    HtmlElement Current { get; }
    bool Contains(string tagName);
    void PopUntil(string tagName);
    bool IsInScope(string tagName);          // "in scope" per spec
    bool IsInButtonScope(string tagName);
    bool IsInTableScope(string tagName);
    bool IsInSelectScope(string tagName);
}
```

## DOM Types

```csharp
abstract class HtmlNode
{
    HtmlNode? Parent { get; internal set; }
    NodeList ChildNodes { get; }
    void AppendChild(HtmlNode child);
    void InsertBefore(HtmlNode child, HtmlNode? reference);
    void RemoveChild(HtmlNode child);
}

class HtmlDocument : HtmlNode
{
    QuirksMode QuirksMode { get; set; }
    HtmlElement? DocumentElement { get; }  // <html>
    HtmlElement? Head { get; }             // <head>
    HtmlElement? Body { get; }             // <body>
    string? Title { get; }
}

class HtmlElement : HtmlNode
{
    string TagName { get; }                // lowercase
    string? NamespaceUri { get; }          // null for HTML, SVG/MathML URI for foreign elements
    AttributeMap Attributes { get; }
    string? Id { get; }                    // shortcut for Attributes["id"]
    string[] ClassList { get; }            // parsed from Attributes["class"]
    string? GetAttribute(string name);
    bool HasAttribute(string name);
    string InnerText { get; }
    string InnerHtml { get; }
    string OuterHtml { get; }
}

class HtmlTextNode : HtmlNode
{
    string Data { get; set; }              // mutable: tree builder may merge adjacent text nodes
}

class HtmlComment : HtmlNode
{
    string Data { get; }
}

class HtmlDocumentType : HtmlNode
{
    string Name { get; }
    string PublicId { get; }
    string SystemId { get; }
}

class NodeList : IReadOnlyList<HtmlNode>
{
    // Backed by List<HtmlNode>
}

class AttributeMap
{
    string? this[string name] { get; }
    bool Contains(string name);
    int Count { get; }
    IEnumerable<(string Name, string Value)> All { get; }
}
```

## Encoding Detection

Order of precedence (per WHATWG spec):
1. BOM (Byte Order Mark) at start of input
2. `<meta charset="...">` in the first 1024 bytes
3. `<meta http-equiv="Content-Type" content="...; charset=...">` in the first 1024 bytes
4. Default: UTF-8

The parser pre-scans the first 1024 bytes looking for charset declarations before full parsing begins.

```csharp
static class EncodingDetector
{
    static Encoding Detect(ReadOnlySpan<byte> input)
    {
        if (TryDetectBom(input, out var bomEncoding)) return bomEncoding;
        if (TryDetectMeta(input, out var metaEncoding)) return metaEncoding;
        return Encoding.UTF8;
    }
}
```

## Error Recovery

The parser NEVER throws. Invalid input produces a valid DOM through error recovery:

| Invalid Input | Recovery |
|--------------|----------|
| `<p>text` (no close tag) | Implicitly close `<p>` at appropriate point |
| `</br>` (close tag for void element) | Treat as `<br>` |
| `<b><i></b></i>` (misnested) | Adoption agency algorithm reparents nodes |
| `<table><p>text</table>` | Foster parent `<p>` before `<table>` |
| Unknown element `<custom-thing>` | Create element, treat as inline (per spec) |
| Duplicate attributes | Keep first, ignore second |
| Missing `<html>`, `<head>`, `<body>` | Implicitly created |
| `&#0;` (null character ref) | Replace with U+FFFD |

## Testing

Primary test data: **html5lib-tests** (git submodule)
- `tree-construction/*.dat` -- input HTML -> expected DOM tree (500+ test cases)
- `tokenizer/*.test` -- input -> expected tokens (JSON format)

Our test runner parses these files and validates our output matches expected results.
