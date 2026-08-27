# HTML input

How HTML becomes a `ParsedDocument`, and the rules that were established by measuring Word rather than by reading a spec.

Two entry points share one parser:

- **AltChunk** — a DOCX embeds an HTML part via `w:altChunk`. `DocumentParser.ParseAltChunk` resolves the part by relationship id and hands the markup to `HtmlParser`.
- **HTML input converters** — `HtmlConverter` / `SkiaHtmlConverter` / `ImageSharpHtmlConverter` / `PdfHtmlConverter` parse the same way.

Everything below therefore applies to both. Parsing is AngleSharp-based and async; the output is the ordinary `DocumentElement` tree, so all four exporters and both raster backends see HTML-sourced content as though it came from OOXML.

The reference for "correct" is Word's own AltChunk import, captured as `expected_*.png` for the `html_*` scenarios in `src/Tests/Inputs/word/` and compared the same way as everything else (`fidelity-audit.md`).

## Block elements carry character CSS

`<p>`, `<div>` and `<h1>`–`<h6>` are block elements, so their inline `style` never reaches the character path — that path (`ParseSpanStyle` → `ApplyStyleToRunProps`) originally fired only for `<span>` and `<font>`. A block's own `font-size`, `font-family`, `font-weight`, `font-style`, `text-decoration` and `color` were silently dropped.

`CreateParagraph` now runs the block element through `ParseSpanStyle` to build its base run properties, so `<p style="font-size:18pt">` sizes its text. Two rules ride along:

- **`background-color` becomes full-width paragraph shading**, not a run highlight — Word paints the band across the content width. The base run properties clear `BackgroundColorHex` afterwards so a glyph-tight highlight is not painted on top of the band. `ParseContainer` pushes a `<div>`'s background down onto its child paragraphs.
- **`font-family` takes only the first family.** CSS gives a comma-separated fallback list with optionally-quoted names; handing the whole string to the font loader throws, because `Times New Roman', serif` reads as one missing family. `FirstFontFamily` splits, trims and unquotes.

Named colours resolve through the full CSS Color Level 4 set (147 entries in `namedColors`, up from an initial 10). `transparent` is deliberately absent — it must mean "no fill", not a colour.

## Which font unstyled HTML gets

`HtmlParser.Parse(html)` defaults to **Times New Roman**, and for body text that is right: Word renders an AltChunk's paragraphs and headings in the browser-default serif, not in the destination document's Normal. Verified by rendering `html_basic_formatting` — which declares no CSS at all — through Word, where every line comes back serif. Repointing the AltChunk path at the host document's default font measured **+0.2790 AE / −0.2595 SSIM across 55 scenario/backend pairs**, so the hardcoded serif is load-bearing, not an oversight.

**But it does not hold inside tables and lists.** A probe carrying one word in a paragraph, a table cell, a `ul`, an `ol` and a heading renders the paragraph and heading in Times while the cell and both list items come back in the host document's default — Aptos where the package declares no styles part. `html_complex` and `html_lists` confirm it in production references: headings and lead-in paragraphs are serif, list items and cell text sans.

`ParseAltChunk` passes both fonts — `Parse(html, "Times New Roman", effectiveDefaultFont)` — and `HtmlParser` applies `containerFontFamily` (the host default) to `ListItemParagraph` runs and table-cell runs through `ContainerRunProps()`, while body paragraphs and headings keep `DefaultRunProps()` (the serif). Standalone HTML input has no host document, so its `Parse` overloads leave container equal to body and nothing changes there. Landing this measured −0.0136 AE / +0.0082 SSIM over 28 pairs, the tables gaining most (`html_table_styled` −0.0074 AE / +0.009 SSIM per backend); the list scenarios tick up a hair on AE from the new-ink offset as their glyphs move to the correct narrower font, which the crops confirm is closer to Word.

## Measured constants

These are Word-derived numbers. They are not defaults anyone would guess, and changing one shifts every `html_*` baseline.

| rule | value | how it was established |
|---|---|---|
| Block paragraph pitch | `spacing-after` **14pt** (12pt when the base font is over 14pt, so headings keep their own) | Word's AltChunk `<p>` pitch measures ≈57px at 150 DPI against a ~29px line box. The obvious 8pt packed paragraphs ~6pt too tight and every band and line drifted up the page. |
| `<img>` dimensions | CSS px **× 0.75** → points | `width`/`height` attributes are CSS pixels. Treated as points they render ~33% oversized — a 312×234 image where Word draws 234×175 — and each caption pushes further down the page (`ParseDimensionAttribute`). |
| `margin-left` indent | CSS px **× 0.75** → `LeftIndentPoints` | Word's staircase for `html_css_margin_padding` measures 78px and 156px at 150 DPI for `margin-left: 50px`/`100px` — exactly 37.5pt/75pt. The `margin` shorthand's left component carries the same meaning (a `margin: 20px` paragraph starts 31px ≈ 15pt in). |
| Empty `<p>` | dropped | Word renders a normal single paragraph gap between the neighbours of `<p></p>`, not a blank line (`html_paragraphs` bands match Word only with the element gone). Whitespace-only counts as empty; `&nbsp;`, images and `<br>` are content (`HtmlParser.IsEmptyParagraph`). |

## Tables

`HtmlParser.ParseTable` handles row fills, zebra shading, alignment, colours and widths from CSS.

### Geometry: Word aligns cell TEXT to the margin

Word does not put an imported table's frame on the text margin — it puts the cell **text** there, and outdents the table by everything that sits to the left of that text (the cell padding and the border). Probed by rendering one document through Word with four tables differing only in those values, measured at 150 DPI:

| table | frame x | first glyph x |
|---|---|---|
| `border=1`, no cellpadding | 143 | **152** |
| `border=1 cellpadding=0` | 144 | **152** |
| `border=1 cellpadding=15` | 125 | **152** |
| `border=3 cellpadding=5` | 137 | **152** |

The glyph never moves; the frame moves to suit. `ParseTable` reproduces this with a negative `IndentPoints` of `cellPadding.Left + borderWidth`. Anchoring the frame at the margin instead — what Morph did until 2026-07-24 — pushed cell text up to 30px right of Word's and made `cellpadding=15` wrap a line that Word fits.

`cellpadding` counts CSS pixels, so it converts at 0.75 like the image attributes. Read as points it inset text by a third too much (33px against Word's 27px).

### Declared table widths

A CSS `width` in px on a table or cell is a genuine CSS length, so it converts to points at 0.75 like the image and cellpadding attributes — `TryParseCssLengthToPoints`, which honours the unit, rather than `TryParseCssDimension`, which reads px as pt. Word sizes `html_table_styled`'s `width: 400px` table at 300pt: measured 622px against the 625px that 400 CSS px predicts at 150 DPI.

A declared width also bounds the **flexible** columns. `TableLayout.CalculateColumnWidths` shares the leftover space among zero-width columns, and that leftover is measured against the table's `PreferredWidthPoints` when it has one, not against the whole text column — otherwise a `width:400px` table with two fixed columns stretches its third to the page edge (976px against Word's 622px). Only a table with no declared width fills the available column. The cap is `min(declared, available)`, so an over-wide declaration still falls through to the existing autofit squeeze. This path is shared with DOCX `w:tblW dxa` tables; no DOCX scenario moved when it landed.

**Both the padding and outdent rules are applied only to auto-width tables.** Word widens a `width:100%` table by the inset at each end so its cell text still spans the text column exactly, while Morph's fill-container path resolves columns against the container width alone. With the total width fixed, cellpadding drives the column distribution, so correcting it alone moved every column away from Word — `html_complex` measured +0.015 AE per backend. The pixel rule is right and the column distribution is the compensating error; they have to land together.

### Borders, fills and cell spacing

Probed against Word with one filled table per variant, measured across the header row at 150 DPI:

| table | fills | rules |
|---|---|---|
| `border=1 cellspacing=0` | touch, separated by a 1px rule | thin light rule, `(204,210,223)` over the fill |
| `border=1` (default cellspacing) | **~3px of page white between them** | grey rule per cell |
| **no `border` attribute** | ~3px of page white between them | **none at all** |

Two rules fall out. **A table with no `border` attribute is borderless** — Word draws not one rule, so `ParseTable` starts from an empty `CellBorders` rather than `CellBorders.All`, which had been drawing an outer box around every borderless table. And **cell spacing is independent of the border attribute**: the ~3px gap is there even with no borders at all, so the fills are per-cell boxes separated by the HTML default `cellspacing` of 2px.

### Borders are detached, not collapsed — the landed model (2026-08-20)

A second probe amplified the variables (`_probe_htmlborders`: `border=8`, cellspacing absent/0/12, a colored header row per table, measured at 150 DPI) and settled the full law:

- Word renders an attribute-bordered table DETACHED: every cell is its own box carrying a 1-CSS-px grey rule on **all four edges**, the fills split by a white gap of exactly the cellspacing (3 device px at the default 2, 18 at cellspacing=12), and the `border` attribute's width draws only on the table **frame** (as 3-D grey chrome; Morph draws a flat grey band at the declared width).
- `cellspacing=0` (or `border-collapse: collapse`) collapses the boxes: fills abut and the rules become shared inside edges (Word draws the two abutting cell rules, ~3 device px; Morph's single 0.75pt rule is the close form).
- **The colored-fill fear that sank the fourth landing attempt is refuted**: Word itself splits a colored header row into per-cell boxes with seams whenever the spacing is non-zero. Four attempts (2026-07-24) had been reverted — the first drew a collapsed grid (wrong model), the second and fourth measured +0.047/+0.049 AE because the autofit columns paid the spacing out of the content area and re-wrapped text, and the crops were mis-read as Word wanting one continuous colour bar.

The mapping (`ParseTable`): `CellSpacingPoints = cellspacing_px × 0.75 / 2` — the model's detached law draws every gap at 2 × spacing — applied to EVERY html table, borderless included; per-cell `Borders` of 0.75pt `B2B2B2` when detached; `InsideHorizontalBorder`/`InsideVerticalBorder` when collapsed; the frame stays on `DefaultBorders` at the attribute width. The spacing insets join the column measure (`TableLayout.CalculateContentBasedColumnWidths` adds `CellSpacingInsets(...).Horizontal` per cell) so the gaps are not paid out of the text measure — the re-wrap that sank the earlier attempts — and the table outdent carries the outer cell's 2 × spacing inset so cell text still lands at the text margin. CSS-shorthand borders (`style="border: ..."`) keep the frame-only footing; the probe covered the attribute form. Landing measured +0.045 AE over 26 pages — the new-ink offset penalty of many thin rules — while the probe and the `html_table_cellpadding`/`html_complex` crops show the structure matching Word box for box.

## CSS box borders, padding and margins — the landed model (2026-08-21)

The 2026-07-21 attempt at this was reverted on three named blockers; all three dissolved before
the retry, which landed against Word's own measured geometry (`html_css_borders`,
`html_css_margin_padding`, `html_complex`'s Info/Warning/Error boxes — every box within a few px
of the reference).

- **Border → `ParagraphProperties.Borders`, style token kept.** `ParseCssBorderEdge` maps
  solid/dashed/dotted/double/groove/ridge/inset/outset onto `BorderLineStyle`, and the engine's
  `BorderStroke` (landed 2026-08 for DOCX border styles) strokes them in all three backends —
  the first revert's "every style draws solid" is gone. Per-edge longhands
  (`border-top`/`-right`/`-bottom`/`-left`) override the shorthand's edge. CSS declares a double
  border's TOTAL width where `w:sz` declares EACH line, so `double` converts at a third.
- **Padding → the `w:pBdr` `w:space` gap, applied only through a border — Word's own gate.**
  Word keeps the text at the margin and outdents the rule by border + padding (the 15px-padded
  `#CCE5FF` box strokes at x=123 against the 150px text margin at 150 DPI, box height 76px =
  rule + 23.4px space + 26px line each way), while the padded but border-LESS `#DDD` div right
  above it renders as a plain 28px one-line band at the margin. So padding maps to the four
  `Border*SpacePoints` and nothing else — no indent change (the reverted attempt's
  padding→indent mapping was wrong), and no reservation-gate change (the reverted attempt's
  blast-radius worry disappears with it).
- **Vertical margins → paragraph spacing; the engine's max-collapse IS the CSS margin
  collapse.** A `margin-top: 30px` paragraph after a 14pt-after paragraph measures a 22.5pt gap
  in Word (max), not 36.5 (sum) — and the fragmenter already collapses
  `max(lastAfter, SpacingBefore)`. `margin-bottom` replaces the 14pt block pitch;
  `margin-right` becomes a right indent (Word shortens the `#EEE` band's right edge by exactly
  the 20px margin).
- **A `<div>`'s box pushes onto its child paragraphs** (`ParseContainer`): borders + padding +
  background propagate to each child, and identical borders on consecutive children merge into
  ONE box under the paragraph border-group law — which is exactly the CSS box around the div.
  The div's own margins are the block's pitch: margin-top joins at the first child,
  margin-bottom (or the ordinary 14pt block gap when none is declared — measured: Word spaces
  the next block 14pt below a `margin: 0` child) leaves at the last.
- **Shading fills the border box.** `Fragmenter.FlushBorderRun` inserts one `PlacedShading`
  covering the whole box rect (rule to rule, across the space gap) behind the member lines;
  Word's boxes are solid from rule to rule. This applies to DOCX `w:pBdr` + `w:shd` paragraphs
  too, which is Word's DOCX behaviour as well.
- **Edge whitespace sheds.** Source indentation such as `<p style="…">\n    text\n</p>` neither
  renders in a browser nor in Word, but the literal newlines became soft breaks and each took a
  line box — the `#CCE5FF` box grew two blank lines taller than Word's until
  `TrimEdgeWhitespace` dropped them. INTERNAL whitespace stays untouched: the full CSS collapse
  was tried 2026-07-21 and reverted (the newline hard-breaks compensate the narrow measure in
  `html_complex`'s intro — see that scenario's entry in `src/todo.md`).
- **A `td`'s own `border` style overrides the table model for that cell** — Word draws
  `html_css_borders`' "2px solid black" cell heavy and its "1px solid gray" neighbour light
  where the shared grid drew both uniform.

## Lists: bare line boxes, one block gap

Word's imported list rows measure the bare line box — 30.5px/row at 150 DPI against the line's
own ~30px, so items carry NO after-spacing (a 4pt item spacing overshot to 38.5px/row) — and the
pitch runs uniform straight through every nesting level (`html_nested_lists`' six items land on
one 30.5px grid, sub-list boundaries included). Only the LAST item of the outermost list block
carries the ordinary 14pt block pitch, giving the ~42px gap the references show before whatever
follows (`HtmlParser.EndListBlock`).

## Known gaps

Tracked as issue **#31** in `src/todo.md`, listed here because they shape what the parser can express:

- **Cell content is flattened to one run.** `cell.TextContent` builds a single run, so `<b>` or `<span style>` inside a `<td>` loses its formatting.
- **`vertical-align` on cells is unmodelled**, which is what `html_css_alignment` actually means to demonstrate.
- Cell padding composes slightly tighter than Word's.
