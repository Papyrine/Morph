# Layout engine proposal — separate layout from painting

**Status: proposal; step 1 (the canonical measurer) landed, steps 2–7 open.** This is the architecture to reach for when Word-fidelity
pagination matters more than incremental effort. It is the "if time and effort are no object"
answer to the columns/height-model work tracked in `src/page_counts.md` and `src/todo.md` (#2, #5,
the columns item under image_wrap_square). Nothing here has landed; the current renderers are
described in `docs/word-features.md`.

## The problem this solves

Morph paginates **three times**, independently:

| engine | paragraph split | section breaks | metrics |
|---|---|---|---|
| `Morph.Skia` (`SkiaPageRenderer` + `TextRenderer`) | **no** — whole-paragraph only (`SkiaPageRenderer` ~line 486: "can't break a paragraph in the middle") | via `SectionBreakHandler` | SkiaSharp font metrics |
| `Morph.ImageSharp` (`ImageSharpPageRenderer` + `TextRenderer`) | **no** (duplicate of Skia) | via `SectionBreakHandler` | SixLabors.Fonts metrics |
| `Morph.Pdf` (`PdfPageRenderer` + `PdfTextEngine`) | **yes** — line-level, across pages *and* columns (`PdfTextEngine.Draw` + `RequestNewPage` + `CountLinesThatFit`/`PlanWidowBreak`) | inline, bypasses `SectionBreakHandler` | PdfSharp `GetHeight()` |

Each engine measures content and decides page breaks in its own render loop, with its own font
metrics. They diverge, and that divergence is the root cause of the remaining fidelity gaps:

- **Backend page counts straddle Word.** `resumes/13` is raster-4 / PDF-6 / Word-5 — one document,
  three answers, because three engines fragment independently. These "knife-edges" (`src/page_counts.md`)
  are not bugs to patch; they are the *structural symptom* of triple pagination.
- **Every rule is written up to three times.** All 21 height-model experiments in `src/page_counts.md`
  had to land per-backend and be kept in sync. Widow/orphan is *real* in PDF and *approximated* in
  raster because raster's loop cannot fragment a paragraph.
- **Columns are stuck.** Raster cannot flow a paragraph from column 1 into column 2 (it moves the
  whole paragraph); PDF can. That asymmetry is why the columns work (image_wrap_square) was built and
  reverted (experiment 11).

Patching raster to split like PDF yields three *agreeing* engines instead of three *diverging* ones —
but they must be kept agreeing forever, and the knife-edges remain the tax of that arrangement.

## The proposal: one layout pass, three painters

Compute the paginated layout **once**, backend-independently, into a retained **layout tree**; make
every backend a dumb painter that walks the tree and draws placed boxes at their computed positions.
This is the standard document-engine architecture (Word's `SwFrame` tree, LibreOffice, browsers).
Morph is already half-way there: `RenderContextBase` carries `CurrentColumn`, a column-shifted
`ContentLeft`, and `PushContentContainer` (a proto-region for float bands) — but the placement is
recomputed inline during each paint instead of being computed once and retained.

```
ParsedDocument ──▶ DocumentLayoutEngine ──▶ LaidOutDocument ──▶ { SkiaPainter, ImageSharpPainter, PdfPainter }
  (parse, today)      (NEW: the one              (retained tree,        (thin: draw placed boxes,
                       pagination)                absolute positions)     no measurement, no breaks)
```

### The layout tree (output — backend-agnostic, absolute positions in points)

```csharp
sealed record LaidOutDocument(IReadOnlyList<LaidOutPage> Pages);

sealed record LaidOutPage(
    int Number,
    PageSettings Settings,                  // geometry in force for this page (section-aware)
    IReadOnlyList<PlacedItem> Background,    // page fill, behind-text floats — painted first
    IReadOnlyList<PlacedItem> Body,          // flow content, in paint order
    IReadOnlyList<PlacedItem> Foreground);   // in-front floats, headers/footers

// Everything below is positioned absolutely from the page top-left. No backend touches metrics.
abstract record PlacedItem(float X, float Y);

sealed record PlacedLine(                    // one wrapped line of a paragraph
    float X, float Y, float Baseline,
    IReadOnlyList<PlacedGlyphRun> Runs) : PlacedItem(X, Y);

sealed record PlacedGlyphRun(                // shaping + advances already resolved
    float X, float Width, ResolvedFont Font, string Text,
    IReadOnlyList<float> Advances,           // canonical per-glyph advances (the metric model)
    RunDecoration Decoration) : PlacedItem(X, 0);  // color, underline, strike, highlight, tracking

sealed record PlacedImage(float X, float Y, float Width, float Height, ImagePayload Source, PictureEffects Effects) : PlacedItem(X, Y);
sealed record PlacedShape(float X, float Y, ShapeGeometry Geometry, Transform Transform) : PlacedItem(X, Y);  // vector / WordArt, pre-transformed
sealed record PlacedRule(float X, float Y, float Width, BorderEdge Edge) : PlacedItem(X, Y);      // HR, paragraph/cell border segment
sealed record PlacedFill(float X, float Y, float Width, float Height, string ColorHex) : PlacedItem(X, Y);  // shading, page bg
```

Key property: a `PlacedGlyphRun` carries **fixed advances**. The painter positions glyphs at those
advances; the rasterizer's own metrics affect only anti-aliasing/hinting, never positions or wrap
points. This is `DefaultFontSettings.DeterministicRendering` extended from glyph rasterization all
the way up to line breaking.

### The layout pass (the one pagination engine)

```csharp
sealed class DocumentLayoutEngine
{
    public LaidOutDocument Layout(ParsedDocument document, LayoutOptions options)
    {
        var measurer  = new CanonicalTextMeasurer(options.Fonts);  // THE single metric source
        var fragmenter = new Fragmenter(measurer);
        // For each section: build the page's region chain (columns / continuous band),
        // fill the flow through it, spilling to the next region/page on overflow, and
        // emit PlacedItems. Floats register exclusions; tables recurse as sub-layouts.
        ...
    }
}
```

### Regions unify columns, cells, text boxes and float bands

The single most clarifying move: a page's columns, a continuous section's mid-page column band, a
**table cell**, a **text box**, a **float-exclusion band**, and a **header/footer band** are all the
*same thing* — a constrained box that holds flow with a fill order. Today these are four separate
mechanisms (`CurrentColumn` shifting, `TableHeightCalculator`, text-box rendering, and
`PushContentContainer`/`ResolveFlowBand`). Unify them:

```csharp
sealed class Region                          // a constrained box that holds flow
{
    public float Left, Top, Width, Bottom;   // points, page-absolute
    public IReadOnlyList<Exclusion> Exclusions;   // floats carving this region (wrapSquare/Tight)
    public Region? Next;                     // fill order: spill target when this region is full
}
```

Columns stop being special: a 2-column section is a region whose `Next` is the second column whose
`Next` is the first column of the following page. `image_wrap_square` then works because the
continuous section builds a 2-column region chain anchored at the break Y and the fragmenter
fills it.

### The fragmenter (the heart — the thing raster lacks)

```csharp
sealed class Fragmenter
{
    // Places a flow of block elements into a region chain, spilling on overflow, applying
    // widow/orphan/keep-next/keep-lines/table-row-split as PLACEMENT rules — computed once.
    public void Place(IReadOnlyList<DocumentElement> flow, Region start, PlacementSink sink);
}
```

Its rules are exactly the 21 measured experiments from `src/page_counts.md`, moved here and applied
once instead of 3×: the hhea line-pitch model, `max(after, before)` spacing collapse, space-before
dropped at page tops, empty-mark heights, end-of-cell collapse, the docDefaults cascades, widow/orphan,
keep abandonment, the exact-fit rule. **The investment in those experiments is preserved — they become
the fragmenter's rule set.**

### Painters (thin — no measurement, no pagination)

```csharp
interface ILayoutPainter
{
    void BeginPage(LaidOutPage page);
    void DrawGlyphRun(PlacedGlyphRun run);   // draw glyphs at run.X + advances, at run baseline
    void DrawImage(PlacedImage image);
    void DrawShape(PlacedShape shape);
    void DrawRule(PlacedRule rule); void DrawFill(PlacedFill fill);
    void EndPage();
}
```

`SkiaPainter` / `ImageSharpPainter` / `PdfPainter` each become a `foreach placed item: draw it`.
`EnsureSpaceFor`, `AdvanceToNextColumnOrPage`, `MoveToNextColumn`, `RenderParagraph`'s widow logic,
`SectionBreakHandler`, `TableHeightCalculator` — all delete from the backends and move into the layout
pass. The HTML/Markdown exporters stay on their own path (they deliberately reflow and are not
paginated), though they could consume the tree's logical structure later.

## The crux: one canonical metric model

Divergence comes from metrics, so the layout pass must own **both**:

- **Line metrics** — the XPS-validated `pitch = (ascent + descent + line gap) × multiplier`, no
  1.20×size floor and no ×1.035 leading boost. Already measured (`src/page_counts.md`, "Height model").
- **Glyph advances / line breaking** — the measured advance model: integer-pixel advances at 120 dpi,
  `ppem = round(size × 120/72)`, `letter ≈ round(linearEm × ppem)`, per-font space elasticity. Already
  measured (`src/page_counts.md`, "Advance model") and already implemented once in PdfSharp — here it
  becomes *the* shared basis, not a per-backend detail.

This is the entire risk surface, even with unlimited effort: the model must reproduce **Word's actual
wrap points and line heights** (Word lays out with GDI/DirectWrite) across every bundled font. The
ledger has measured this to ±1px and validated the height model against XPS, so it is achievable — but
it is *the* work. Get it right and every backend inherits Word-fidelity for free; get it wrong and
every backend inherits the *same* error (still strictly better — one error to calibrate against one
target, versus three that cancel differently and mask the model).

## Migration checklist (sequence matters even unbounded)

Build alongside the existing renderers; do not delete anything until all three backends consume the tree.

- [x] **1. `CanonicalTextMeasurer`** — landed (`src/Morph/Fonts/FontMetrics.cs`,
      `FontMetricsReader.cs`, `src/Morph/Layout/CanonicalTextMeasurer.cs`, `CanonicalMetricsTests`).
      A backend-independent reader pulls `head`/`hhea`/`hmtx`/`cmap` from the font file; the measurer
      computes line heights (Auto/Exactly/AtLeast), the pixel-quantized advance width, and greedy
      wrap. Line pitch is pinned to the XPS numbers (Aptos 12pt = 14.65pt, Calibri 10.8pt = 13.18pt)
      and advances to an independent parse. **Wrap validated** (`CanonicalWrapAgreementTests`): the
      canonical wrap agrees with the raster backend's own font engine on **~99.3%** of a corpus
      paragraph sample. The advance model uses **pen-position rounding** — the whole-line total tracks
      the linear ideal to within half a device pixel, so per-glyph error can't accumulate and
      over-wrap; this is the "inter-word spaces are elastic upward" behaviour, spread across the run.
      The lone residual is one Calibri 10pt paragraph a sub-pixel from its boundary. The
      **`IParagraphMeasurer` surface adapter** landed too (`CanonicalParagraphMeasurer`,
      `CanonicalParagraphMeasurerTests`): multi-run greedy wrap (a mid-word format change never splits
      the word), per-line height = the tallest run's hhea box under the spacing rule, and Word's
      before/after spacing — font resolution injected as a delegate so a caller wires a
      `FontResolver<FontMetrics>`. Not yet consumed by the render pipeline (that is step 6, and carries
      baseline regeneration). Remaining as refinements: tabs and first-line/hanging indents in the
      adapter, and an optional per-font upward space factor (Aptos 1.0125×, Times 1.0213×) for the last
      fraction of a percent.
- [x] **2. Layout-tree types** — landed (`PlacedItem` base carrying the bounding box, with
      `LaidOutDocument`, `LaidOutPage`, `PlacedLine`, `PlacedTableRow`, `PlacedCell`, `PlacedImage`,
      `MeasuredLine`). `PlacedLine` carries its baseline and the `PlacedRun`s to paint (text, width and
      `RunProperties` for font/colour/decoration) — one per contiguous source run on the line, so a
      mixed-format line ("plain **bold** plain") is several runs at their own X and a uniform line is one,
      and a painter draws without re-measuring — plus its inline `PlacedImage`s (bottom on the baseline).
      `PlacedTableRow` carries its `PlacedCell`s (box, shading, borders, and the cell's laid-out content).
      Each run's X is the canonical pen position at its start, its width the pen distance to the next
      boundary; per-glyph advances (exact intra-run boundaries) and the rule/shape and floating placed-item
      kinds attach as later slices.
- [x] **3. `Fragmenter` — block-flow + table + column slices landed** (`Fragmenter`,
      `CanonicalFragmenterTests`, `FragmenterPageCountTests`). Multi-column flow with **line-level
      page/column breaks** (a paragraph too tall for the space left splits at a line boundary and
      continues in the next column or page — the thing the raster backends cannot do) and **row-level
      table breaks** (a table taller than a column flows row by row, re-emitting `w:tblHeader` rows and
      absorbing a trailing run of empty rows), reusing the shared `TableLayout`/`TableHeightCalculator`
      with the canonical measurer so the two paginate a table identically. Content fills column 0 to the
      bottom, then column 1, and the last column overflowing starts a page; `w:br` column breaks advance
      the column. Plus the measured rules: max-collapse paragraph spacing, space-before dropped at a broken
      region (column or page) top, empty-paragraph mark line, explicit-break blank pages (experiment 18),
      an exact bottom-of-region fit for paragraph flow, and the backend's rounding tolerances mirrored for
      table fit. **Validated: 96/96 = 100% on the corpus's pure-block documents, 150/153 = 98.0% once plain
      text tables are added, 154/157 = 98.1% with multi-column flow, 156/157 = 99.4% once the
      w:contextualSpacing collapse lands, 182/183 = 99.5% once inline images join the set (the measurer
      sizes each line to its tallest inline image, so they paginate), 238/239 = 99.6% once non-wrapping
      body floats join too (they take no flow space, so pagination is unchanged), 260/261 = 99.6% once
      flow-neutral section breaks join (NextPage as a page break, Continuous as a no-op, same geometry), and
      274/276 = 99.3% once per-section geometry lands (a NextPage or even/odd break switching page size,
      margins or column count, plus even/odd parity filler pages) — all four corpus column documents match**
      — one backend-independent pass reproducing Word's pagination. Two misses remain: resumes/13 (a sub-line
      knife-edge Word's own backends straddle, 6 vs 5) and business-plans/15 (18 vs 19, a one-page knife-edge
      across a nineteen-page multi-geometry document). Columns are equal-width within a section.
      Remaining slices: per-section geometry (a section break switching column count or page size,
      including the continuous mid-page kind — the newsletter masthead → body case); widow/orphan and
      keep-next/keep-lines; float exclusions (reuse `ResolveFlowBand`); images and nested tables inside a
      cell; floating tables; header/footer band height.
- [ ] **4. `DocumentLayoutEngine`** — section walk, per-page region chains, header/footer bands.
      Fix `ParseSectionBreak` (`DocumentParser` ~9318) to read the *following* section's `w:type`
      (ECMA-376 §17.6.22) so continuous multi-column sections are recognised.
- [~] **5. `PdfPainter` — first slice landed** (`PdfPainter`, `PdfPainterTests`). A pure draw pass: it
      takes a `Fragmenter`-produced `LaidOutDocument` and emits a PDF with the tree's pages, page sizes
      and line/run positions — no measurement, no pagination. It reuses `PdfRenderContext` for font and
      brush resolution, so a `PlacedLine`'s runs draw with `XGraphics.DrawString` at the tree's baseline.
      **Proven end-to-end**: a synthetic multi-paragraph flow measured by the canonical measurer,
      fragmented, painted, and rasterised by PDFium renders the text correctly at the canonical positions —
      the tree drives real PDF output. **Per-run fidelity landed**: a mixed-format line paints each run in
      its own font and colour (bold, italic, red, italic-blue all confirmed in a render), and a mid-word
      format change never splits the word at a wrap. **Table cell sub-layout landed**: the fragmenter
      places each cell's paragraphs into the tree (column geometry, padding, vertical-merge heights, `w:jc`
      table alignment, mirroring `RenderTableRow`) and the painter draws each cell's shading, content and
      borders — a shaded bold header, a full border grid and wrapped multi-line cells all confirmed in a
      render. **List markers landed**: a list paragraph's first line carries its marker as a run in the
      hanging-indent gutter (bullet in the embedded "Morph Bullets" font or number in the paragraph font,
      `numbering.Text` and colour mirroring `PdfTextEngine`), positioned a hanging indent left of the text
      — bullets and numbers with correct hanging-indent continuation confirmed in a render. **Run decorations
      landed**: each run paints its highlight (behind the glyphs, over the line box), underline (below the
      baseline) and strikethrough (through the x-height), coloured and sized from `RunProperties` with the
      geometry from `PdfTextEngine` — underline, strike, yellow/green highlight, combinations, and a
      wrapped underlined run underlined on both lines all confirmed in a render. **Inline images landed**: the
      wrap treats an image as an unbreakable box (its width counts toward the line, its height grows the
      line), the fragmenter places it with its bottom on the baseline, and the painter decodes the bytes
      (an SVG's raster fallback) and draws them — an inline icon flowing mid-sentence and a figure growing
      its own line both confirmed in a render. **Alignment (centre/right/justify), page background,
      intra-paragraph line breaks and all-caps landed**: the fragmenter shifts each line by its alignment
      offset within the available width; justify distributes the leftover width evenly across a naturally
      wrapped line's inter-word gaps (the last line and break-ended lines stay natural); the painter fills
      the page's `w:background`; a soft line break (parsed as `"\n"`) forces a line break instead of a
      missing-glyph box; a `w:caps` run is upper-cased; a table cell's first paragraph keeps its
      space-before (which `TableHeightCalculator` already sizes the cell with, so the content must be
      positioned with it or float to the top); a cell's content shifts down for centre/bottom vertical
      alignment within the space its row leaves; a **header's behind-text floating images** are
      resolved to page positions and painted behind every page's body — the full-page decorative frames of
      letter templates live here (letters/02 0.62 → 0.78, letters/03 0.73 → 0.79); and **behind-text
      cell-float shapes** land — a cell's `Floats` are resolved to cell-relative boxes and painted before its
      content, so a label template's coloured background panel (a preset rect) and freeform blobs (unit-square
      subpaths scaled into the box via the reused `PdfPageRenderer.BuildShapePath`) fill each cell behind the
      white recipient text (labels/14 blank → 0.86; solid fills only — gradient/image fills stay deferred).
      **Empty-paragraph after-spacing and a last-line bottom-margin tolerance landed**: an empty paragraph
      carries its after-spacing into the collapse with the next paragraph like any other (measured against
      Word — two_columns' title/blank/body gap is line + after + line + after, not one dropped after), and a
      line is placed while its *baseline* clears the bottom margin, letting the last line's descent and
      trailing gap encroach as Word does (self-limiting: the next line's baseline then falls below the
      margin and breaks). Together they fix two_columns' column-break point (it broke after paragraph 11
      instead of 10; 0.63 → 0.74) while holding the page-count match at 98.1% (154/157) — the tolerance
      exactly offsets the extra empty-paragraph height on the borderline documents.
      **Character spacing (w:spacing tracking) landed**: the measurer adds the per-character points to every
      token's advance so a letter-spaced run widens the wrap and alignment maths, and the painter spreads
      the glyphs to match (reusing `PdfTextEngine`'s per-glyph logic). A tracked all-caps subtitle
      (cover-letters/16's `ACCOUNTANT`) now renders at Word's width; the SSIM is unchanged because that page
      is white-on-dark, where host-vs-container text AA dominates the score — a case where the *visible*
      render is the honest check, not the number.
      **Paragraph shading (w:shd) landed**: a paragraph with a background colour emits a `PlacedShading`
      band per line spanning its column box (indent to right margin), painted behind the text — so a centred
      title's band still spans the full column, not only the glyphs' width. resumes/15's `Janna Gardner`
      header band renders (0.75 → 0.80); run-level highlight (on the run, text-width) is separate and already
      painted.
      **Paragraph borders (w:pBdr) landed**: a paragraph with any visible edge emits one `PlacedBorder` box
      around its column box, expanded by each edge's space, which the painter strokes edge by edge (the same
      geometry as a table cell) — a box, a block-quote left bar, a right rule and a heading's bottom rule all
      render (cover-letters/02's rule under `DEAR ROWAN MURPHY`). Paint-only, so no page-count effect. It does
      not move the aggregate SSIM: a border is a thin line, so where the paragraph sits even a few points off
      Word's position (cover-letters/02's header block is compressed by a display/script-font metric gap
      upstream) the stroke lands beside Word's rather than over it. Deferred: the between-border collapse of
      consecutive same-bordered paragraphs (currently each box tiles, which reads correctly), and reserving a
      large border space in the layout (a 16pt box overlaps its neighbours — the reservation conflicts with
      the collapse case, so the two land together).
      **Header and footer text landed**: header and footer paragraphs lay out per page as self-contained
      bands (the reusable `LayoutBand` — wrap, alignment and shading, no page breaks). The header band sits at
      the header distance in front of the background image; the footer band is anchored so its bottom is the
      footer distance above the page edge, with each `PAGE` field resolved to that page's number (`Page 1`,
      `Page 2`, …). Page 1 honours the `w:titlePg` "different first page": with a title page it takes the
      first-page header/footer, which is often null — so Word (and now the engine) shows no footer on a title
      page (agendas-minutes/01's `PAGE 1` correctly disappears). A shared `SelectVariant` also gives an
      even-numbered page its even header/footer when the document opts into `w:evenAndOddHeaders`, else the
      default. `NUMPAGES` resolves too: the bands are assembled in a post-pass once the flow ends and the
      total page count is known, so a `Page N of M` footer reads correctly (`page_numbers`' `Page 1 of 2`).
      Paint-only, so the body and page count are untouched; `Inputs/header`'s centred `Document Header`
      renders at Word's position. The behind-text header background image follows the same variant as the
      text, so a title page's frame comes from its first-page header. A header/footer *table* lays out in the
      band too, reusing the nested-table layout — business/01's `CANEIRO GROUP` footer grid renders at the
      page bottom. *Foreground* header images (a title-page illustration that is not a behind-text watermark)
      are the remaining band piece.
      **Tabs landed**: a tab run advances the pen to its resolved stop during line building, reusing the
      production `TabStopResolver` (left / centre / right / decimal, with a stop past the column clamped to
      the column edge and right/centre/decimal measuring the text up to the next tab). `Inputs/tab_stops`'
      right-aligned TOC numbers, left-aligned columns, default 0.5" stops and left/centre/right line all land
      at Word's positions, and `decimal_tabs/01` aligns on the decimal point (0.99). The advance is
      position-dependent, so it is resolved in `BuildLineItems` rather than baked into a fixed piece width.
      **Tab leaders landed**: a leadered stop leaves a filler run (empty text, a non-`None` `Leader`) across
      the gap, and the painter fills it — a baseline rule for underscore, or the leader glyph tiled at ~2×
      its advance for dots/hyphens (Word spaces the dots roughly a glyph apart, not a dense line). A TOC's
      dot leaders and a `Signature` underline both render. The dots are a thin horizontal feature, so the
      *visible* render is the honest check: SSIM barely moves because a row a point off Word's leaves the
      dots on a different pixel row, but the leaders read correctly.
      **Empty-paragraph mark font landed**: a blank paragraph has no runs, so its spacer line's height comes
      from the paragraph mark's own run properties (`w:rPr` on `w:pPr`) — Word sizes the blank line by the
      mark, not a bare default. Sizing it from a fresh `RunProperties` (default font, 11pt) shrank spacer
      lines, and in a multi-column flow that under-tall gap let a column hold an extra item: three_columns'
      title column packed 15 items where Word fits 14. Using the mark font fixed the column break (0.84 →
      0.89) and lifted the corpus mean (0.9464 → 0.9474) as every empty-spacer document tightened toward Word.
      **Widow/orphan control landed**: the line loop became a fit-count loop — it takes as many of a
      paragraph's remaining lines as clear the region, and when a break falls it never strands a single line
      (Word's default `WidowControl`): one line alone at the bottom (orphan) moves the pair to the next
      region, one line alone at the top (widow) carries a second line with it. three_columns' two-line
      `Item 30` now stays whole in the third column instead of splitting across two (0.89 → 0.90), and
      two_columns' break tightened (0.74 → 0.78); the page-count match held at 98.1%, since Word paginates
      with the same rule. **Keep-lines (w:keepLines)** rides on the same fit-count loop: a paragraph so
      marked moves to the next region intact rather than splitting when it will not all fit.
      **Nested tables landed**: a table inside a cell lays out inline at the cell cursor with no page breaks
      (`BuildRow` was generalised to take an explicit row Y, so the same row builder serves body and nested
      tables). `Inputs/complex_tables`' quarterly grid — a two-column Apr/May sub-table inside its Q2 cell —
      renders at Word's position, colours and values. A cell holding a nested table stays top-aligned, since
      the vertical-alignment shift only moves text lines; nested-table pages are excluded from the harness,
      so this is a render-verified capability rather than a metric move.
      **Body floating images and shapes landed**: a top-level floating image or image-filled shape resolves
      to a page position — page-anchored offsets from the sheet edge, margin-anchored from the content box,
      paragraph-anchored from the flow cursor — and paints behind or in front of the body by its `BehindText`
      flag, in the post-pass that assembles the header/footer bands (so the page count is known). SVG artwork
      paints its raster fallback, since PdfSharp (like ImageSharp) cannot rasterise SVG; an image-fill shape
      becomes a plain image, since the shape painter draws only solid and outline fills. agendas-minutes/01's
      people-at-a-table illustration lands at Word's position and size, and a single full-bleed background
      photo fills its page. The float is anchored to the page the flow has reached, so a document with one
      full-page background per page across a page break stacks both on the first page (brochures/01) — tying a
      body float to its anchor paragraph's resolved page is a later slice, as is float wrap (text flowing
      around a square/tight float). Body-float pages are excluded from the harness, so this is render-verified.
      **Contextual spacing (w:contextualSpacing) landed**: two same-style contextual paragraphs collapse the
      gap between them (the first's space-after and the second's space-before), matching Word's tight memo
      To/From/CC blocks and list runs — mirroring `PageRendererBase`'s rule (both contextual, equal `StyleId`;
      a table breaks the run). The Fragmenter previously added the full inter-paragraph spacing, so a memo's
      three heading lines sat ~48pt too low and pushed the body table onto a second page. Applying the
      collapse tightened business/05 and resumes/07 to Word's single page, lifting the page-count match from
      98.1% to **99.4% (156/157)** with no document regressing — the most direct progress yet toward the
      engine's reason for existing, one pagination answer instead of three.
      **Inline images entered the validated set**: the measurer already sized a line to its tallest inline
      image and the painter already drew `PlacedLine.Images`, so admitting inline-image documents to the
      page-count and fidelity harnesses (they had been excluded out of caution) added 26 documents at
      **99.5% page-count match (182/183)** with no new miss — business-plans/03's Contoso logo and
      left-margin arrow land at Word's positions. The residual SSIM on those Aptos-heavy pages is the
      display-font width gap (a title wrapping to two lines in Word, one in the engine), not the image.
      **Non-wrapping body floats joined the page-count set, with trailing-blank-page absorption**: a
      non-wrapping float (every floating shape, and a floating image with no square/tight wrap) takes no flow
      space, so admitting it leaves pagination untouched — the page-count harness widened by 55 documents to
      **238/239 = 99.6%**. Two of the newcomers first missed by a page because a document-final empty
      paragraph, pushed off a full page, landed alone on a new one; `FinishPage` now drops a page carrying
      only blank spacer lines (a table row, an image, a shape, or a line with real text keeps it), matching
      Word, which does not render a page for a trailing empty paragraph. The floats are admitted to the
      page-count harness only — their rendering is verified separately, and the multi-page background
      limitation would otherwise depress an image-AA-heavy fidelity page for a known reason.
      **Flow-neutral section breaks joined the page-count set**: the Fragmenter already treats a NextPage
      section break as a page break and a Continuous one as a no-op, so a document whose sections keep the
      same geometry (no new column count, page size, or margins) paginates like Word — a corpus census found
      section breaks in 44 documents, most single-column NextPage template dividers. Admitting the
      same-geometry ones added 22 documents to the harness at **260/261 = 99.6%** with no new miss, across
      newsletters, menus, cards, weddings and multi-page business plans.
      **Per-section geometry landed (NextPage and even/odd)**: the section geometry — page size, margins and
      column count — is no longer fixed for the whole document. A NextPage or even/odd section break finishes
      the current page and adopts the new section's `PageSettings`, so each page carries its own geometry:
      the derived content box and column metrics recompute, and every emitted page records the settings it
      was laid out at (its background, header and footer bands resolve against those). An even/odd break
      inserts a blank filler page when the next page's parity is wrong, as Word does. business-plans/12 now
      paginates with pages flipping between portrait and landscape and per-section margins; admitting the
      geometry-changing and even/odd documents added 15 to the harness at **274/276 = 99.3%**.
      **The Continuous mid-page column switch landed too** — the newsletter masthead → multi-column body,
      where the column count changes without a page break. It flows the new columns from the break point: a
      `columnTop` cursor records where the columns begin (the break Y, below a full-width masthead, rather
      than the page top), so `AdvanceColumnOrPage` tops each later column out there and an overflow to the
      next page resets it to the page top. A corpus census found zero documents exercise it (the three
      multi-column documents are multi-column from their first section), so it is validated on a synthetic
      fixture instead: two unit tests assert both columns begin at the break and the overflow page resets to
      its top, and a rendered three-column newsletter confirms the shape.
      **Column balancing landed**: a multi-column section that ends the document is newspaper-flowed — column
      0 fills to the bottom, then column 1 — while a section a *section break* terminates has its last page's
      columns balanced to equal heights. Which of the two Word applies was settled by rendering documents
      through Word itself: three_columns (a final section) lays its thirty items out 14 / 15 / 1 across the
      columns, the last column holding a single item, and the engine reproduces that split exactly (two_columns
      likewise). For the balanced case the corpus has no example, so a synthetic fixture — a three-column
      section closed by a continuous break — was authored and rendered through Word: Word balances its six
      items two / two / two, and the engine now reproduces that, the single-column footer flowing full-width
      below. `BalanceCurrentColumns` redistributes the last page's column lines in reading order, filling each
      column to the average height (total / columns), triggered at the section break (a section that ends the
      document never reaches it, so it stays newspaper-flowed). Uneven-height balancing is approximate — the
      greedy fill targets the average rather than searching for the minimal tallest column — and a region
      carrying a table, shading or a border box is left newspaper-flowed (those move as coupled groups); both
      are later refinements with no corpus demand.
      **Measured end-to-end**
      (`PdfPainterFidelityTests`): parse → fragment → paint → rasterise a real corpus DOCX and SSIM the
      pages against Word's own render (`expected_*.png`). Across 180 block/table/column documents the
      painter scores **mean 0.941, median 0.975 SSIM** — plain text and tables are near pixel-identical
      (0.997–1.000). Two lessons from rendering the low scorers next to Word (which the harness makes
      cheap): the fixes come from *seeing* the gap, not guessing it — the two worst were dark-themed cover
      letters rendering as blank pages for want of the page background (0.246/0.322 → 0.712/0.789), which no
      amount of alignment work would have touched; and the score is a **lower bound** — it runs on the host,
      so a text-dense page carries sub-pixel glyph/line-metric drift and host-vs-container rasterisation AA
      that depress its SSIM (e.g. long_paragraph 0.78) even where the wrap and alignment match Word exactly.
      Still to land before it can replace the production `PdfRenderer`:
      shapes/WordArt and float wrap (behind-text *cell* floats, and now body floating images, image-fill and
      gradient-fill shapes, render — a gradient shape reuses the production linear-gradient brush and paints
      as Word does, the vertical bars of labels/04 and the banners of cover-letters/06 among them; text
      flowing around a square/tight float does not, nor does tying a multi-page
      background to its anchor paragraph's page), image recolour/duotone effects
      (letters/02's frame is drawn but blue where Word recolours it
      brown — needs a pixel path Morph.Pdf lacks); a floating or inline image now applies its DrawingML
      rotation, flip, source-rectangle crop, and ellipse/freeform clip about the box centre (reusing the
      production geometry — letters/13's rotated letterhead banners and brochures/03's round photos match
      Word, and the dedicated image_rotation/01 and image_cropping/01 rise to 0.989 and 0.991 SSIM); a rotated
      *inline* image still tops out a touch higher than Word, which reserves the rotated bounding box's height
      in the line; first/even-page header/footer *images* and header/footer
      tables (default, first-page and even-page header/footer *text* plus behind-text header images render;
      band *tables*, per-variant images and 3-way tab alignment do not), nested tables; a partial `w:tcMar`
      cell-margin override now inherits its absent sides from the table's `w:tblCellMar` per side instead of
      collapsing them to zero (`DocumentParser.ParseCellMargin`), which is what actually spaces
      business-plans/04's vAlign=bottom section headings from their bodies — Word's XPS puts that heading row
      at 31.8pt where the dropped 14.4pt top margin had left 16pt, and the shared parser fix lifted 11 corpus
      scenarios toward Word across all three backends; label grids, and per-glyph advances (exact intra-run
      boundaries — the painter currently anchors each run at its canonical start and lets the font library
      fill the run). Then repoint `Morph.Pdf` at `LaidOutDocument`, delete `PdfTextEngine`'s pagination, and
      run the harness in the container (matching Word's rasteriser) to separate real gaps from AA, then
      validate the full container suite (PDF page-count scoreboard unchanged or better, AE/SSIM neutral).
      **Apply the section-break-type parser fallback at this cutover, not before.** `DocumentParser` reads a
      break's `w:type` from the ending section's sectPr; Word also honours it on the following section's, and
      one corpus document (image_wrap_square) authors a continuous column switch that way, so the parser
      mis-types it NextPage. Reading the ending section first and falling back to the following section fixes
      the type, and the layout engine then renders it as Word does (columns mid-page, two pages). But the
      shared parser also feeds today's production path, which cannot flow continuous mid-page columns and
      would overlap that document's columns onto the text above — so the change regresses production until the
      painters own pagination, and is deliberately held for this step.
- [ ] **6. `SkiaPainter` + `ImageSharpPainter`** — the payoff step. Both become thin painters of the
      same tree; the whole-paragraph pagination and the duplicated `TextRenderer` layout code delete.
      This is where the raster knife-edges collapse (raster now paginates identically to PDF — one
      answer, not a straddle). Regenerate all raster + PDF baselines; validate the scoreboard.
- [ ] **7. Delete** `PageRendererBase` pagination, `SectionBreakHandler` (subsumed), the per-backend
      `EnsureSpaceFor`/`AdvanceToNextColumnOrPage`, `TableHeightCalculator` (folded into the table
      sub-layout). Keep the backends' primitive draw ops only.

## The PDF cutover (step 5), in detail

Scoped against the current tree. The map below fixes the seam, the one wiring gap, the strategy, and the
ordered backlog so the cutover can proceed as a run of small validated slices rather than one large flip.

### The seam

Every PDF entry point — `PdfDocumentConverter.ConvertToPdf`, `PdfHtmlConverter.ConvertToPdf`, and the
`WordDocument`/`HtmlDocument` `ExportToPdf` extensions — funnels into one method:
`PdfRenderer.Render(ParsedDocument, PdfExportOptions?)` (`src/Morph.Pdf/PdfRenderer.cs:8`). Its body has two
halves. Lines 12–30 paginate and draw (`CountPagesIfRequired`, then `new PdfRenderContext(...)`,
`new PdfPageRenderer(...)`, `renderer.RenderDocument(document)`). Lines 32–42 post-process the bytes for
reproducibility (`MakeDeterministic`, optional `TrimPages`, `Save`, `Normalize`). **Only the first half is
replaced.**

The replacement already exists and is exercised by `PdfPainterFidelityTests`:

    var pdf = PdfPainter.Paint(
        Fragmenter.Layout(
            document.Elements, document.PageSettings,
            document.Header, document.Footer,
            document.FirstPageHeader, document.FirstPageFooter,
            document.EvenPageHeader, document.EvenPageFooter),
        options.FontDirectory);

`PdfPainter.Paint` builds its own `PdfDocument`, so `MakeDeterministic` / `TrimPages` / `Save` / `Normalize`
run over it unchanged — the byte-reproducibility that makes the snapshots stable is untouched. The throwaway
NUMPAGES pre-count (`CountPagesIfRequired`) becomes unnecessary on the engine path: `LaidOutDocument.Pages.Count`
IS the total, so the page-number post-pass already has it. Swapping the seam retires **both** `PdfTextEngine`
(paragraph-internal line breaks) **and** the `PdfPageRenderer` driver (`RenderDocument`/`RenderElement` +
the page lifecycle). `PageRendererBase` stays — the Skia/ImageSharp backends still use it until step 6.

### The one wiring gap

`Fragmenter` needs a `CanonicalParagraphMeasurer` whose `resolveFont(family, bold, italic) -> FontMetrics?`
delegate honours the conversion's `PdfExportOptions` (`FontDirectory`, `FontFallback`, `FontWidthScale`).
The tests build it from `LayoutTestFonts.Resolve` (a `FontFileCache` over `src/Fonts` + `FontMetricsReader`);
production has `PdfFontResolver` as the analogue, but its per-conversion `FontFallback` delegate is not yet
wired (see `pdf-font-resolver-divergence`). The measurer's metrics drive the wrap and the painter's font
resolution drives the draw, so both MUST resolve a given run to the same face or the paint drifts off the
measured line. Closing this gap — one delegate, threaded from options into both the measurer and
`PdfPainter`'s `PdfRenderContext` — is the whole of what makes the seam physically work.

### Strategy: a capability-gated hybrid, not a single flip

A corpus census (325 docs; regexes approximate) puts a drawing on ~49%, a text-box/shape on ~38%, a floating
anchor on ~36%, and WordArt warp on ~28%. `PdfPainter` is already structurally complete over the `PlacedItem`
set (all six kinds paint), so those docs are not blocked by the painter — they are blocked by the `Fragmenter`
not yet EMITTING the art, or by a handful of painter-side fidelity items. The engine handles 180 of 325 docs
today at 0.942 mean SSIM; production covers all 325 at 0.880.

Because the unhandled 145 are concentrated in the hardest features and each tends to fail on a single emission
gap, feature-completing everything before any flip would ship no benefit for a long time. The lower-risk path
is a **capability predicate** — a generalisation of `PdfPainterFidelityTests.IsBlockTableOrColumnFlow` — that
decides, per document, whether the engine covers it. `PdfRenderer.Render` routes covered documents through the
engine and falls back to `PdfTextEngine` for the rest. Each emission slice that lands tightens the predicate,
moving documents from the fallback onto the engine. `PdfTextEngine` is deleted only once the predicate admits
everything the corpus contains (the fallback goes cold).

### Phases

- **A — Wiring spike.** Thread `resolveFont` from `PdfExportOptions` (via `PdfFontResolver` + `FontFallback`)
  into a production `CanonicalParagraphMeasurer`. Add the engine path to `PdfRenderer.Render` behind an
  internal gate, both paths coexisting. Gate: engine PDFs render and stay byte-deterministic.
- **B — Capability predicate + fallback.** Add the per-document predicate; route covered docs to the engine,
  the rest to `PdfTextEngine`. Gate: the container suite stays green (fallback preserves current output for
  uncovered docs; the covered subset regenerates its `pdf_result*` baselines once, reviewed against Word).
- **C — Close emission gaps (a run of slices).** Each lands a `Fragmenter` emission, tightens the predicate,
  and regenerates the newly-covered `pdf_result*` baselines. Ordered by corpus impact below.
- **D — Flip and delete.** When the predicate admits ~everything, make the engine the default, regenerate all
  `pdf_result*` baselines, confirm `compare-all-pdf.md` holds or beats 0.880 mean and the PDF page-count
  scoreboard holds or improves, then delete `PdfTextEngine` + the `PdfPageRenderer` driver.

### Emission backlog (Fragmenter — unblocks whole documents, ordered by corpus reach)

1. WordArt / inline shape groups (~28% carry a warp; the test filter keys off `run.InlineShapeGroup`).
2. Floating shapes/images with text-wrap exclusions (square/tight) and multi-page float anchor-page
   resolution (a body float currently binds to the page the flow has reached, not its anchor's page).
3. Floating tables.
4. Form fields and content controls.
5. Footnote/endnote appendix at document end (`RenderNotesAppendix`).
6. Per-variant (first/even) header/footer images, header/footer band tables, and 3-way footer tab alignment.
7. Label grids.
8. Line numbers, bar tabs, per-fragment paragraph borders across a break, per-section NUMPAGES restart.

### Painter-fidelity backlog (does not block a document from rendering)

Per-glyph advances (exact intra-run boundaries); image recolour/duotone (needs a pixel path `Morph.Pdf`
lacks); super/subscript vertical offset (the font shrinks but is not raised — a self-contained fix);
`w:position` baseline shift; small-caps; run borders and text effects; RTL. These depress SSIM where present
but do not gate the predicate — a covered document still renders without them.

### Validation gates (every slice)

`PdfPainterFidelityTests` (SSIM vs Word, the standing gate); regenerate the touched `pdf_result*.verified.*`
and diff `compare-all-pdf.md` for direction against Word; the PDF page-count scoreboard; the full container
suite green. The HTML→PDF path rides the same seam (`PdfHtmlConverter` builds a `ParsedDocument` and calls the
same `PdfRenderer.Render`), so it is covered by the same predicate and gates.

### Risks

Measure/paint font divergence (the wiring gap above) is the primary one — a mismatch paints text off its
measured line. Determinism must survive the swap (verified by the unchanged `pdf_result.verified.pdf`
snapshots). The engine's OpenType metrics differ subtly from PdfSharp's, so covered docs re-wrap and
re-paginate on cutover — that is the intended new truth, but every covered baseline regenerates and must be
reviewed against Word, not against the old production bytes.

## Testing strategy (a large secondary win)

The current suite infers layout from rasterized PNGs (the entire struggle behind `src/page_counts.md`).
With a retained tree the **layout tree can be snapshotted as text** — page count, region rectangles, line
Y-positions, break points — and diff it *directly* against Word's XPS box geometry, decoupled from
rasterization. That turns "why is this page 2px off" into a structural diff. Keep the existing
Verify-PNG scenario tests as the painter-fidelity gate; add layout-tree snapshots as the pagination gate.

## What is preserved vs deleted

- **Preserved:** the parser and `ParsedDocument` model (unchanged input); the 21 measured rules (become
  fragmenter rules); the float pipeline knowledge (`docs/floating-art-pipeline.md`, becomes exclusions);
  the shape/WordArt geometry (becomes `PlacedShape`); the Verify-PNG suite (becomes the painter gate).
- **Deleted:** three copies of pagination; `TableHeightCalculator` as a separate measure pass; the
  whole-paragraph widow approximation; the raster/PDF metric divergence; and the knife-edge category.

## Open questions

- **Column balancing** — Word equalizes a continuous section's last-page column heights
  (`PropertyMap.cxx:900`). Cosmetic, not page-count; the fragmenter can add it as a second pass, but it
  is not required for correctness. Defer.
- **`image_wrap_square` residual** — it also exercises the mode ≤14 `UseFormerTextWrapping` flag
  (`src/page_counts.md`, LO rule index), which is orthogonal to columns; columns are necessary but may
  not be sufficient there.
- **Incremental relayout** — not needed (Morph is batch, not interactive), so the tree can be
  immutable and recomputed wholesale. This removes the hardest part of real layout engines.
