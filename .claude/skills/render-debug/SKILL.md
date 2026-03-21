---
name: render-debug
description: Debug rendering issues by tracing HTML through the full pipeline -- parse, style, layout, paint, PDF
disable-model-invocation: true
user-invocable: true
allowed-tools: Bash, Read, Grep, Glob
---

# Render Pipeline Debugger

Trace an HTML/CSS rendering issue through the full EggPdf pipeline to find where it goes wrong.

## Arguments

- `$ARGUMENTS` -- the symptom to debug (e.g., "div not centering", "table wider than page", "font looks wrong", "page break in wrong place", "image missing")

## Steps

1. **Identify the pipeline stage** most likely causing the issue:

   | Symptom | Likely Stage |
   |---------|-------------|
   | Element missing / wrong structure | HTML Parser |
   | Style not applied / wrong style | CSS Parser, Cascade, Selector Engine |
   | Wrong position / size | Layout Engine |
   | Content on wrong page / bad page break | Fragmentation Engine |
   | Missing visual (shadow, border-radius, gradient) | Paint Layer |
   | Corrupt PDF / can't open / text not selectable | PDF Backend |
   | Image missing / wrong size | Image handling + Resource Resolver |
   | Font wrong / tofu characters | Text Engine + Font Resolution |

2. **Read the relevant source code** for that pipeline stage.

3. **Trace the input through each stage**:
   - **Parse**: Does the HTML parse into the expected DOM tree? Check element nesting, attributes
   - **Style**: Does the element get the expected computed style? Check selector matching, specificity, inheritance, shorthand expansion
   - **Box Gen**: Is the correct box type generated? Check display value, anonymous boxes
   - **Layout**: Are position and size correct? Check containing block, margin collapse, intrinsic sizing
   - **Fragment**: Does the content break at the right page boundary? Check break properties, orphans/widows
   - **Paint**: Are all visual effects applied? Check paint order, stacking context, clipping
   - **PDF**: Is the content stream correct? Check operators, font references, image XObjects

4. **Report findings**:

   ### Render Debug: {symptom}

   **Pipeline trace**:
   - HTML Parse: OK / Issue found
   - CSS Resolve: OK / Issue found
   - Layout: OK / Issue found
   - Fragment: OK / Issue found
   - Paint: OK / Issue found
   - PDF Write: OK / Issue found

   **Root cause**: {which stage and why}
   **Suggested fix**: {what code to change}
   **Test to add**: {regression test for this case}

5. **If the issue is in layout**, provide the box tree with positions:
   ```
   BlockBox (0, 0) 595x842
     BlockBox (20, 20) 555x30  <-- expected 555x50, height wrong
       InlineBox (20, 20) "Hello World"
   ```
