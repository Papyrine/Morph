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

## Known gaps

Tracked as issue **#31** in `src/todo.md`, listed here because they shape what the parser can express:

- **Cell content is flattened to one run.** `cell.TextContent` builds a single run, so `<b>` or `<span style>` inside a `<td>` loses its formatting.
- **Shaded blocks have no box.** A background renders as a thin full-width band rather than a padded, bordered box. See the attempt below before trying again.
- **`vertical-align` on cells is unmodelled**, which is what `html_css_alignment` actually means to demonstrate.
- Cell padding composes slightly tighter than Word's.

## Attempted and reverted: CSS box borders and padding

2026-07-21, affecting `html_css_borders`, `html_css_margin_padding`, `html_complex` and `html_css_colors`. Recorded because the approach was sound and the blockers are specific — a retry should start from here rather than from scratch.

The model already carries what is needed: `ParagraphProperties.Borders`, the four `Border{Top,Bottom,Left,Right}SpacePoints`, and `ParseCssBorderShorthand` for `1px solid #rrggbb`. The implementation read `border`/`padding`/`margin` in `ParseInlineStyle` (with a new `FirstCssLengthPoints` converting CSS px→pt at 0.75); mapped border → `Borders`, padding → all four border-spaces *and* Left/RightIndent, and margin → spacing-after; and propagated the whole box to child paragraphs from `ParseContainer`.

Mapping padding to indents as well as border-spaces is the load-bearing part: border-space alone puts the border *outside* the text, which is the DOCX `w:pBdr` model, not the CSS box model. In all three renderers the shading band also had to move after `paragraphStartY` and expand by the border-spaces, because it was drawn tight to the text before the top-space reservation.

**Borders and padded boxes did render** — crops confirmed the Info/Warning/Error boxes and the `#CCE5FF` padded paragraph matching Word closely — but the metric regressed everywhere: `html_complex` p2 +0.053 AE, `html_css_margin_padding` +0.034, `html_css_borders` +0.021 AE and **−0.048 SSIM**, `html_css_colors` +0.010. Three causes, all still true:

1. **Every border style draws solid.** `ParseCssBorderShorthand` discards the style token, and the paragraph-border renderers ignore `BorderEdge.Style` outright (Skia's `CreatePaint` sets colour, width and Stroke, with no dash effect). Word draws dashed, dotted and double.
2. **Per-edge longhands are unparsed** — `border-top`/`-bottom`/`-left` produce nothing, so "Top red, bottom blue borders" and "Left border only" stay blank.
3. **Cumulative vertical drift** — padded box heights don't match Word's, so every border below the first progressively misaligns. This dominates the AE.

Blast radius to watch on a retry: broadening the space-reservation gate from "has border" to "has border OR border-space > 0" (so padding without a border still reserves room) shifted two DOCX scenarios' PDF output — `brochures/06` and `newsletters/14`, paragraphs with border-space and no top border. Keep the gate keyed off a border, or accept and re-judge those.

To land it: map the CSS style token to `BorderEdge.Style` and teach all three paragraph-border renderers to stroke dashed/dotted/double; parse the per-edge `border-*` longhands; then tune padding until box heights match Word, and only then re-judge.
