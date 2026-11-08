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
