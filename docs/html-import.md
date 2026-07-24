# HTML input

How HTML becomes a `ParsedDocument`, and the rules that were established by measuring Word rather than by reading a spec.

Two entry points share one parser:

- **AltChunk** — a DOCX embeds an HTML part via `w:altChunk`. `DocumentParser.ParseAltChunk` resolves the part by relationship id and hands the markup to `HtmlParser`.
- **HTML input converters** — `HtmlConverter` / `SkiaHtmlConverter` / `ImageSharpHtmlConverter` / `PdfHtmlConverter` parse the same way.

Everything below therefore applies to both. Parsing is AngleSharp-based and async; the output is the ordinary `DocumentElement` tree, so all four exporters and both raster backends see HTML-sourced content as though it came from OOXML.

The reference for "correct" is Word's own AltChunk import, captured as `expected_*.png` for the `html_*` scenarios in `src/Tests/Inputs/` and compared the same way as everything else (`fidelity-audit.md`).

## Block elements carry character CSS

`<p>`, `<div>` and `<h1>`–`<h6>` are block elements, so their inline `style` never reaches the character path — that path (`ParseSpanStyle` → `ApplyStyleToRunProps`) originally fired only for `<span>` and `<font>`. A block's own `font-size`, `font-family`, `font-weight`, `font-style`, `text-decoration` and `color` were silently dropped.

`CreateParagraph` now runs the block element through `ParseSpanStyle` to build its base run properties, so `<p style="font-size:18pt">` sizes its text. Two rules ride along:

- **`background-color` becomes full-width paragraph shading**, not a run highlight — Word paints the band across the content width. The base run properties clear `BackgroundColorHex` afterwards so a glyph-tight highlight is not painted on top of the band. `ParseContainer` pushes a `<div>`'s background down onto its child paragraphs.
- **`font-family` takes only the first family.** CSS gives a comma-separated fallback list with optionally-quoted names; handing the whole string to the font loader throws, because `Times New Roman', serif` reads as one missing family. `FirstFontFamily` splits, trims and unquotes.

Named colours resolve through the full CSS Color Level 4 set (147 entries in `namedColors`, up from an initial 10). `transparent` is deliberately absent — it must mean "no fill", not a colour.

## Measured constants

These are Word-derived numbers. They are not defaults anyone would guess, and changing one shifts every `html_*` baseline.

| rule | value | how it was established |
|---|---|---|
| Block paragraph pitch | `spacing-after` **14pt** (12pt when the base font is over 14pt, so headings keep their own) | Word's AltChunk `<p>` pitch measures ≈57px at 150 DPI against a ~29px line box. The obvious 8pt packed paragraphs ~6pt too tight and every band and line drifted up the page. |
| `<img>` dimensions | CSS px **× 0.75** → points | `width`/`height` attributes are CSS pixels. Treated as points they render ~33% oversized — a 312×234 image where Word draws 234×175 — and each caption pushes further down the page (`ParseDimensionAttribute`). |

## Tables

`HtmlParser.ParseTable` handles row fills, zebra shading, alignment, colours and widths from CSS.

## Known gaps

Tracked as issue **#31** in `src/todo.md`, listed here because they shape what the parser can express:

- **Cell content is flattened to one run.** `cell.TextContent` builds a single run, so `<b>` or `<span style>` inside a `<td>` loses its formatting.
- **`margin-left` is ignored**, so CSS-indented paragraphs sit flush at the left margin.
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
