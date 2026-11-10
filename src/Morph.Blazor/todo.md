# Editor backlog

Open findings from the 2026-11-02 review of `MorphEditor` (browser session plus code reviews of the
typing box, the viewer script, the ribbon and command table, and the .NET edit flow). A finding is
deleted when it lands. Paths are under `src/Morph.Blazor/` unless they say otherwise.

Every bug of that review, and the in-reach missing features, landed on 2026-11-02. What is left is
what those fixes stopped short of.

## Residues

- **A click on another paragraph in F8 extend mode ends the mode.** Word extends the selection to
  the click and keeps the mode on (WordProbe S-10d), which the editor now does within the open
  paragraph; a click elsewhere closes that paragraph, and a selection across paragraphs is the pages'.
- Touch pinch-zoom is untested on a real touch device.

## Missing features

Absent from the core: shapes, text boxes, equations, cover page, captions, cross-references, column
breaks, hyphenation, line numbers, picture position, sort, text effects, citations, spelling (browser
spellcheck only), show-markup options, compare, read mode, web layout, split and one/two-page views.

## Workbooks (SheetEditor)

Landed 2026-11-10: the grid, typing and formulas, the formula engine, formatting, clipboard, undo,
column and row sizes. Open:

- **Rows, columns and sheets**: insert and delete rows and columns, which moves every reference to the
  cells after them (formulas on every sheet, defined names, merges, tables, conditional formats,
  validations, drawings' anchors); add, rename, delete and reorder sheets.
- **Merge and unmerge**, **freeze panes** (a frozen pane is read but scrolls with the rest), **sort and
  filter**, the **fill handle**, conditional formatting and data validation edited.
- **Drawings over the cells**: a chart or picture anchored to a sheet is not drawn in the grid; a chart
  sheet says to open it in the viewer.
- **The engine**: dynamic arrays (a result is its first value; nothing spills), R1C1 INDIRECT, 3D and
  external references, iterative calculation. A value is recalculated whole on every edit, which is fine
  for the corpus's workbooks and slow for a workbook of tens of thousands of formulas.
- **Text widths are estimated** from the digit's width, for spilling text and for #### and AutoFit; the
  grid has no font to measure with.
