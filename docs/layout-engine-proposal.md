# Layout engine proposal — separate layout from painting

**Status: landed for raster — the Skia and ImageSharp backends paginate covered documents (98.8% of the
corpus) through this one engine by default (step 6); the PDF painter is built and capability-gated but held
on `PdfTextEngine` (step 5); the old per-backend pagination is not yet deleted (step 7).** Steps 1–3 — the
canonical measurer, the layout tree, and the Fragmenter — are done, and step 4's section walk folded into
the Fragmenter. This was the "if time and effort are no object" answer to the columns/height-model work
tracked in `src/page_counts.md` and `src/todo.md` (#2, #5, the columns item under image_wrap_square); the
running log of landed slices is below, and `docs/word-features.md` describes the per-backend draw code the
thin painters now front. `MORPH_SKIA_ENGINE=off` / `MORPH_IMAGESHARP_ENGINE=off` are the kill switches.

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

*This section is the original motivation, stated as it stood before any of the work below landed. The raster
half is now solved: as of step 6, Skia and ImageSharp no longer paginate independently — both run the single
engine below for covered documents (98.8% of the corpus), so their counts agree by construction rather than by
maintenance. PDF (`PdfTextEngine`) is the last independent pagination. What follows is the design that got
there and the running log of how.*

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

**A note on names.** The type and method sketches in the proposal above are the design as first drafted; the
implementation refined several, and the checklist and cutover logs below use the landed names. `PlacedGlyphRun`
became `PlacedRun` (per-glyph advances deferred — see step 2); the sketched `PlacedRule`/`PlacedFill` became
`PlacedBorder`/`PlacedShading`, with `PlacedCell`/`PlacedTableRow` added for tables; the `DocumentLayoutEngine`
section walk and the `Region` chain folded into the `Fragmenter`, whose entry point is `Fragmenter.Layout`
returning a `LaidOutDocument` (not the sketched `Place(flow, region, sink)`); and each backend's painter draws
through the render context's own primitives rather than a common `ILayoutPainter` interface.

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
- [~] **4. `DocumentLayoutEngine`** — the section walk, per-page region chains and header/footer bands all
      landed, folded into the `Fragmenter` rather than a separate class (per-section geometry, even/odd parity
      pages, the Continuous mid-page column switch and header/footer band layout are in the raster-cutover log
      below). One piece is held for the PDF cutover: fixing `ParseSectionBreak` (`DocumentParser` ~9362) to read
      the *following* section's `w:type` (ECMA-376 §17.6.22) — it regresses production until the painters own
      pagination, so it lands with step 5's flip.
- [~] **5. `PdfPainter` — built and capability-gated; flip held on `PdfTextEngine`** (`PdfPainter`,
      `PdfPainterTests`). A pure draw pass over a `Fragmenter`-produced `LaidOutDocument` — no measurement,
      no pagination — reusing `PdfRenderContext` for font and brush resolution. The painter is structurally
      complete over the covered set; what remains is the flip, held on the font/image tail. The feature log
      (the ~30 slices it renders, in landing order) is "The PDF painter — what landed" below; the cutover
      plan and what is left are the rest of "The PDF cutover (step 5)".
- [x] **6. `SkiaPainter` + `ImageSharpPainter`** — the payoff step, LANDED as the DEFAULT raster path for
      covered documents (98.8% of the corpus). Both are thin painters of the same tree; the raster knife-edges
      collapse (raster now paginates identically across backends — one answer, not a straddle). The gate is
      `MORPH_SKIA_ENGINE` / `MORPH_IMAGESHARP_ENGINE != "off"` (the env var became a kill switch, not an opt-in);
      an uncovered document falls through to the production `<Backend>PageRenderer`. The flip landed at
      covered-doc parity (aggregate −0.0001), decoupled from step 5 — PDF stays on `PdfTextEngine`, so a covered
      document's PNG and PDF page counts can still diverge until the PDF flip. All covered raster baselines
      regenerated. The whole-paragraph pagination and duplicated `TextRenderer` layout code still exist as the
      fallback; deleting them is step 7. See "The raster cutover (step 6), in detail".
- [ ] **7. Delete** `PageRendererBase` pagination, `SectionBreakHandler` (subsumed), the per-backend
      `EnsureSpaceFor`/`AdvanceToNextColumnOrPage`, `TableHeightCalculator` (folded into the table
      sub-layout). Keep the backends' primitive draw ops only.

### Remaining work, in one place

The checklist is landed through step 6; what is left, most-blocking first:

1. **The PDF flip** (step 5 D) — painter and predicate are done; the flip is held on the font/image tail
   (−0.0054, dominated by Aptos display-font wrap under PDFium's AA plus compound-image and full-page-gradient
   rasterization). See "The PDF cutover (step 5)".
2. **The four coverage hold-outs** of 325 — warp WordArt (`wordart`, `wordart-envelope`; the 16 presets), float
   wrap (`image_wrap_square`; text flowing around a square/tight float, an exclusion the Fragmenter does not
   emit), and `agendas-minutes/14` (a positioned frame). These plus the flip take the `PdfTextEngine` fallback
   cold.
3. **Step 7 — delete** the old pagination (`PageRendererBase`, `PdfTextEngine`, `SectionBreakHandler`,
   `TableHeightCalculator`), once PDF flips and coverage is total — the fallback still serves uncovered
   documents and the kill switch until then.
4. **Painter-fidelity backlog** — score-improving, not gating a document: per-glyph advances, image
   recolour/duotone, super/subscript raise, `w:position`, small-caps, run borders/effects, RTL, per-variant
   header/footer images and band tables (detailed under the two cutover sections).

## The PDF cutover (step 5), in detail

Scoped against the current tree. The map below fixes the seam, the one wiring gap, the strategy, and the
ordered backlog so the cutover can proceed as a run of small validated slices rather than one large flip.

### The PDF painter — what landed, in order

The step-5 `PdfPainter` feature log, in landing order (pulled out of the migration checklist so it scans). A
pure draw pass: it takes a `Fragmenter`-produced `LaidOutDocument` and emits a PDF with the tree's pages, page
sizes and line/run positions — no measurement, no pagination. It reuses `PdfRenderContext` for font and brush
resolution, so a `PlacedLine`'s runs draw with `XGraphics.DrawString` at the tree's baseline. **Proven
end-to-end**: a synthetic multi-paragraph flow measured by the canonical measurer, fragmented, painted, and
rasterised by PDFium renders the text correctly at the canonical positions — the tree drives real PDF output.

- **Per-run fidelity landed**: a mixed-format line paints each run in its own font and colour (bold, italic,
  red, italic-blue all confirmed in a render), and a mid-word format change never splits the word at a wrap.
- **Table cell sub-layout landed**: the fragmenter places each cell's paragraphs into the tree (column geometry,
  padding, vertical-merge heights, `w:jc` table alignment, mirroring `RenderTableRow`) and the painter draws
  each cell's shading, content and borders — a shaded bold header, a full border grid and wrapped multi-line
  cells all confirmed in a render.
- **List markers landed**: a list paragraph's first line carries its marker as a run in the hanging-indent
  gutter (bullet in the embedded "Morph Bullets" font or number in the paragraph font, `numbering.Text` and
  colour mirroring `PdfTextEngine`), positioned a hanging indent left of the text — bullets and numbers with
  correct hanging-indent continuation confirmed in a render.
- **Run decorations landed**: each run paints its highlight (behind the glyphs, over the line box), underline
  (below the baseline) and strikethrough (through the x-height), coloured and sized from `RunProperties` with
  the geometry from `PdfTextEngine` — underline, strike, yellow/green highlight, combinations, and a wrapped
  underlined run underlined on both lines all confirmed in a render.
- **Inline images landed**: the wrap treats an image as an unbreakable box (its width counts toward the line,
  its height grows the line), the fragmenter places it with its bottom on the baseline, and the painter decodes
  the bytes (an SVG's raster fallback) and draws them — an inline icon flowing mid-sentence and a figure growing
  its own line both confirmed in a render.
- **Alignment (centre/right/justify), page background, intra-paragraph line breaks and all-caps landed**: the
  fragmenter shifts each line by its alignment offset within the available width; justify distributes the
  leftover width evenly across a naturally wrapped line's inter-word gaps (the last line and break-ended lines
  stay natural); the painter fills the page's `w:background`; a soft line break (parsed as `"\n"`) forces a line
  break instead of a missing-glyph box; a `w:caps` run is upper-cased; a table cell's first paragraph keeps its
  space-before (which `TableHeightCalculator` already sizes the cell with, so the content must be positioned
  with it or float to the top); a cell's content shifts down for centre/bottom vertical alignment within the
  space its row leaves; a **header's behind-text floating images** are resolved to page positions and painted
  behind every page's body — the full-page decorative frames of letter templates live here (letters/02 0.62 →
  0.78, letters/03 0.73 → 0.79); and **behind-text cell-float shapes** land — a cell's `Floats` are resolved to
  cell-relative boxes and painted before its content, so a label template's coloured background panel (a preset
  rect) and freeform blobs (unit-square subpaths scaled into the box via the reused
  `PdfPageRenderer.BuildShapePath`) fill each cell behind the white recipient text (labels/14 blank → 0.86;
  solid fills only — gradient/image fills stay deferred).
- **Empty-paragraph after-spacing and a last-line bottom-margin tolerance landed**: an empty paragraph carries
  its after-spacing into the collapse with the next paragraph like any other (measured against Word —
  two_columns' title/blank/body gap is line + after + line + after, not one dropped after), and a line is placed
  while its *baseline* clears the bottom margin, letting the last line's descent and trailing gap encroach as
  Word does (self-limiting: the next line's baseline then falls below the margin and breaks). Together they fix
  two_columns' column-break point (it broke after paragraph 11 instead of 10; 0.63 → 0.74) while holding the
  page-count match at 98.1% (154/157) — the tolerance exactly offsets the extra empty-paragraph height on the
  borderline documents.
- **Character spacing (w:spacing tracking) landed**: the measurer adds the per-character points to every token's
  advance so a letter-spaced run widens the wrap and alignment maths, and the painter spreads the glyphs to
  match (reusing `PdfTextEngine`'s per-glyph logic). A tracked all-caps subtitle (cover-letters/16's
  `ACCOUNTANT`) now renders at Word's width; the SSIM is unchanged because that page is white-on-dark, where
  host-vs-container text AA dominates the score — a case where the *visible* render is the honest check, not the
  number.
- **Paragraph shading (w:shd) landed**: a paragraph with a background colour emits a `PlacedShading` band per
  line spanning its column box (indent to right margin), painted behind the text — so a centred title's band
  still spans the full column, not only the glyphs' width. resumes/15's `Janna Gardner` header band renders
  (0.75 → 0.80); run-level highlight (on the run, text-width) is separate and already painted.
- **Paragraph borders (w:pBdr) landed**: a paragraph with any visible edge emits one `PlacedBorder` box around
  its column box, expanded by each edge's space, which the painter strokes edge by edge (the same geometry as a
  table cell) — a box, a block-quote left bar, a right rule and a heading's bottom rule all render
  (cover-letters/02's rule under `DEAR ROWAN MURPHY`). Paint-only, so no page-count effect. It does not move the
  aggregate SSIM: a border is a thin line, so where the paragraph sits even a few points off Word's position
  (cover-letters/02's header block is compressed by a display/script-font metric gap upstream) the stroke lands
  beside Word's rather than over it. Deferred: the between-border collapse of consecutive same-bordered
  paragraphs (currently each box tiles, which reads correctly), and reserving a large border space in the layout
  (a 16pt box overlaps its neighbours — the reservation conflicts with the collapse case, so the two land
  together).
- **Header and footer text landed**: header and footer paragraphs lay out per page as self-contained bands (the
  reusable `LayoutBand` — wrap, alignment and shading, no page breaks). The header band sits at the header
  distance in front of the background image; the footer band is anchored so its bottom is the footer distance
  above the page edge, with each `PAGE` field resolved to that page's number (`Page 1`, `Page 2`, …). Page 1
  honours the `w:titlePg` "different first page": with a title page it takes the first-page header/footer, which
  is often null — so Word (and now the engine) shows no footer on a title page (agendas-minutes/01's `PAGE 1`
  correctly disappears). A shared `SelectVariant` also gives an even-numbered page its even header/footer when
  the document opts into `w:evenAndOddHeaders`, else the default. `NUMPAGES` resolves too: the bands are
  assembled in a post-pass once the flow ends and the total page count is known, so a `Page N of M` footer reads
  correctly (`page_numbers`' `Page 1 of 2`). Paint-only, so the body and page count are untouched;
  `Inputs/header`'s centred `Document Header` renders at Word's position. The behind-text header background
  image follows the same variant as the text, so a title page's frame comes from its first-page header. A
  header/footer *table* lays out in the band too, reusing the nested-table layout — business/01's `CANEIRO
  GROUP` footer grid renders at the page bottom. *Foreground* header images (a title-page illustration that is
  not a behind-text watermark) are the remaining band piece.
- **Tabs landed**: a tab run advances the pen to its resolved stop during line building, reusing the production
  `TabStopResolver` (left / centre / right / decimal, with a stop past the column clamped to the column edge and
  right/centre/decimal measuring the text up to the next tab). `Inputs/tab_stops`' right-aligned TOC numbers,
  left-aligned columns, default 0.5" stops and left/centre/right line all land at Word's positions, and
  `decimal_tabs/01` aligns on the decimal point (0.99). The advance is position-dependent, so it is resolved in
  `BuildLineItems` rather than baked into a fixed piece width.
- **Tab leaders landed**: a leadered stop leaves a filler run (empty text, a non-`None` `Leader`) across the
  gap, and the painter fills it — a baseline rule for underscore, or the leader glyph tiled at ~2× its advance
  for dots/hyphens (Word spaces the dots roughly a glyph apart, not a dense line). A TOC's dot leaders and a
  `Signature` underline both render. The dots are a thin horizontal feature, so the *visible* render is the
  honest check: SSIM barely moves because a row a point off Word's leaves the dots on a different pixel row, but
  the leaders read correctly.
- **Empty-paragraph mark font landed**: a blank paragraph has no runs, so its spacer line's height comes from
  the paragraph mark's own run properties (`w:rPr` on `w:pPr`) — Word sizes the blank line by the mark, not a
  bare default. Sizing it from a fresh `RunProperties` (default font, 11pt) shrank spacer lines, and in a
  multi-column flow that under-tall gap let a column hold an extra item: three_columns' title column packed 15
  items where Word fits 14. Using the mark font fixed the column break (0.84 → 0.89) and lifted the corpus mean
  (0.9464 → 0.9474) as every empty-spacer document tightened toward Word.
- **Widow/orphan control landed**: the line loop became a fit-count loop — it takes as many of a paragraph's
  remaining lines as clear the region, and when a break falls it never strands a single line (Word's default
  `WidowControl`): one line alone at the bottom (orphan) moves the pair to the next region, one line alone at
  the top (widow) carries a second line with it. three_columns' two-line `Item 30` now stays whole in the third
  column instead of splitting across two (0.89 → 0.90), and two_columns' break tightened (0.74 → 0.78); the
  page-count match held at 98.1%, since Word paginates with the same rule. **Keep-lines (w:keepLines)** rides on
  the same fit-count loop: a paragraph so marked moves to the next region intact rather than splitting when it
  will not all fit.
- **Nested tables landed**: a table inside a cell lays out inline at the cell cursor with no page breaks
  (`BuildRow` was generalised to take an explicit row Y, so the same row builder serves body and nested tables).
  `Inputs/complex_tables`' quarterly grid — a two-column Apr/May sub-table inside its Q2 cell — renders at
  Word's position, colours and values. A cell holding a nested table stays top-aligned, since the
  vertical-alignment shift only moves text lines; nested-table pages are excluded from the harness, so this is a
  render-verified capability rather than a metric move.
- **Body floating images and shapes landed**: a top-level floating image or image-filled shape resolves to a
  page position — page-anchored offsets from the sheet edge, margin-anchored from the content box,
  paragraph-anchored from the flow cursor — and paints behind or in front of the body by its `BehindText` flag,
  in the post-pass that assembles the header/footer bands (so the page count is known). SVG artwork paints its
  raster fallback, since PdfSharp (like ImageSharp) cannot rasterise SVG; an image-fill shape becomes a plain
  image, since the shape painter draws only solid and outline fills. agendas-minutes/01's people-at-a-table
  illustration lands at Word's position and size, and a single full-bleed background photo fills its page. The
  float is anchored to the page the flow has reached, so a document with one full-page background per page
  across a page break stacks both on the first page (brochures/01) — tying a body float to its anchor
  paragraph's resolved page is a later slice, as is float wrap (text flowing around a square/tight float).
  Body-float pages are excluded from the harness, so this is render-verified.
- **Contextual spacing (w:contextualSpacing) landed**: two same-style contextual paragraphs collapse the gap
  between them (the first's space-after and the second's space-before), matching Word's tight memo To/From/CC
  blocks and list runs — mirroring `PageRendererBase`'s rule (both contextual, equal `StyleId`; a table breaks
  the run). The Fragmenter previously added the full inter-paragraph spacing, so a memo's three heading lines
  sat ~48pt too low and pushed the body table onto a second page. Applying the collapse tightened business/05
  and resumes/07 to Word's single page, lifting the page-count match from 98.1% to **99.4% (156/157)** with no
  document regressing — the most direct progress yet toward the engine's reason for existing, one pagination
  answer instead of three.
- **Inline images entered the validated set**: the measurer already sized a line to its tallest inline image and
  the painter already drew `PlacedLine.Images`, so admitting inline-image documents to the page-count and
  fidelity harnesses (they had been excluded out of caution) added 26 documents at **99.5% page-count match
  (182/183)** with no new miss — business-plans/03's Contoso logo and left-margin arrow land at Word's
  positions. The residual SSIM on those Aptos-heavy pages is the display-font width gap (a title wrapping to two
  lines in Word, one in the engine), not the image.
- **Non-wrapping body floats joined the page-count set, with trailing-blank-page absorption**: a non-wrapping
  float (every floating shape, and a floating image with no square/tight wrap) takes no flow space, so admitting
  it leaves pagination untouched — the page-count harness widened by 55 documents to **238/239 = 99.6%**. Two of
  the newcomers first missed by a page because a document-final empty paragraph, pushed off a full page, landed
  alone on a new one; `FinishPage` now drops a page carrying only blank spacer lines (a table row, an image, a
  shape, or a line with real text keeps it), matching Word, which does not render a page for a trailing empty
  paragraph. The floats are admitted to the page-count harness only — their rendering is verified separately,
  and the multi-page background limitation would otherwise depress an image-AA-heavy fidelity page for a known
  reason.
- **Flow-neutral section breaks joined the page-count set**: the Fragmenter already treats a NextPage section
  break as a page break and a Continuous one as a no-op, so a document whose sections keep the same geometry (no
  new column count, page size, or margins) paginates like Word — a corpus census found section breaks in 44
  documents, most single-column NextPage template dividers. Admitting the same-geometry ones added 22 documents
  to the harness at **260/261 = 99.6%** with no new miss, across newsletters, menus, cards, weddings and
  multi-page business plans.
- **Per-section geometry landed (NextPage and even/odd)**: the section geometry — page size, margins and column
  count — is no longer fixed for the whole document. A NextPage or even/odd section break finishes the current
  page and adopts the new section's `PageSettings`, so each page carries its own geometry: the derived content
  box and column metrics recompute, and every emitted page records the settings it was laid out at (its
  background, header and footer bands resolve against those). An even/odd break inserts a blank filler page when
  the next page's parity is wrong, as Word does. business-plans/12 now paginates with pages flipping between
  portrait and landscape and per-section margins; admitting the geometry-changing and even/odd documents added
  15 to the harness at **274/276 = 99.3%**.
- **The Continuous mid-page column switch landed too** — the newsletter masthead → multi-column body, where the
  column count changes without a page break. It flows the new columns from the break point: a `columnTop` cursor
  records where the columns begin (the break Y, below a full-width masthead, rather than the page top), so
  `AdvanceColumnOrPage` tops each later column out there and an overflow to the next page resets it to the page
  top. A corpus census found zero documents exercise it (the three multi-column documents are multi-column from
  their first section), so it is validated on a synthetic fixture instead: two unit tests assert both columns
  begin at the break and the overflow page resets to its top, and a rendered three-column newsletter confirms
  the shape.
- **Column balancing landed**: a multi-column section that ends the document is newspaper-flowed — column 0
  fills to the bottom, then column 1 — while a section a *section break* terminates has its last page's columns
  balanced to equal heights. Which of the two Word applies was settled by rendering documents through Word
  itself: three_columns (a final section) lays its thirty items out 14 / 15 / 1 across the columns, the last
  column holding a single item, and the engine reproduces that split exactly (two_columns likewise). For the
  balanced case the corpus has no example, so a synthetic fixture — a three-column section closed by a
  continuous break — was authored and rendered through Word: Word balances its six items two / two / two, and
  the engine now reproduces that, the single-column footer flowing full-width below. `BalanceCurrentColumns`
  redistributes the last page's column lines in reading order, filling each column to the average height (total
  / columns), triggered at the section break (a section that ends the document never reaches it, so it stays
  newspaper-flowed). Uneven-height balancing is approximate — the greedy fill targets the average rather than
  searching for the minimal tallest column — and a region carrying a table, shading or a border box is left
  newspaper-flowed (those move as coupled groups); both are later refinements with no corpus demand.
- **Measured end-to-end** (`PdfPainterFidelityTests`): parse → fragment → paint → rasterise a real corpus DOCX
  and SSIM the pages against Word's own render (`expected_*.png`). Across 180 block/table/column documents the
  painter scores **mean 0.941, median 0.975 SSIM** — plain text and tables are near pixel-identical
  (0.997–1.000). Two lessons from rendering the low scorers next to Word (which the harness makes cheap): the
  fixes come from *seeing* the gap, not guessing it — the two worst were dark-themed cover letters rendering as
  blank pages for want of the page background (0.246/0.322 → 0.712/0.789), which no amount of alignment work
  would have touched; and the score is a **lower bound** — it runs on the host, so a text-dense page carries
  sub-pixel glyph/line-metric drift and host-vs-container rasterisation AA that depress its SSIM (e.g.
  long_paragraph 0.78) even where the wrap and alignment match Word exactly.

Still to land before it can replace the production `PdfRenderer` — much of this list has since landed via the
shared raster cutover (nested tables, unwarped WordArt, floating tables and label grids all emit now; see the
post-flip update under "The PDF cutover (step 5)"), leaving the font/image tail, float wrap and warp WordArt as
the real blockers: shapes/WordArt and float wrap (behind-text *cell* floats, and now body floating images,
image-fill and gradient-fill shapes, render — a gradient shape reuses the production linear-gradient brush and
paints as Word does, the vertical bars of labels/04 and the banners of cover-letters/06 among them; text flowing
around a square/tight float does not, nor does tying a multi-page background to its anchor paragraph's page),
image recolour/duotone effects (letters/02's frame is drawn but blue where Word recolours it brown — needs a
pixel path Morph.Pdf lacks); a floating or inline image now applies its DrawingML rotation, flip,
source-rectangle crop, and ellipse/freeform clip about the box centre (reusing the production geometry —
letters/13's rotated letterhead banners and brochures/03's round photos match Word, and the dedicated
image_rotation/01 and image_cropping/01 rise to 0.989 and 0.991 SSIM); a rotated *inline* image still tops out a
touch higher than Word, which reserves the rotated bounding box's height in the line; first/even-page
header/footer *images* and header/footer tables (default, first-page and even-page header/footer *text* plus
behind-text header images render; band *tables*, per-variant images and 3-way tab alignment do not); a partial
`w:tcMar` cell-margin override now inherits its absent sides from the table's `w:tblCellMar` per side instead of
collapsing them to zero (`DocumentParser.ParseCellMargin`), which is what actually spaces business-plans/04's
vAlign=bottom section headings from their bodies — Word's XPS puts that heading row at 31.8pt where the dropped
14.4pt top margin had left 16pt, and the shared parser fix lifted 11 corpus scenarios toward Word across all
three backends; per-glyph advances (exact intra-run boundaries — the painter currently anchors each run at its
canonical start and lets the font library fill the run). Then repoint `Morph.Pdf` at `LaidOutDocument`, delete
`PdfTextEngine`'s pagination, and run the harness in the container (matching Word's rasteriser) to separate real
gaps from AA, then validate the full container suite (PDF page-count scoreboard unchanged or better, AE/SSIM
neutral). **Apply the section-break-type parser fallback at this cutover, not before.** `DocumentParser` reads a
break's `w:type` from the ending section's sectPr; Word also honours it on the following section's, and one
corpus document (image_wrap_square) authors a continuous column switch that way, so the parser mis-types it
NextPage. Reading the ending section first and falling back to the following section fixes the type, and the
layout engine then renders it as Word does (columns mid-page, two pages). But the shared parser also feeds
today's production path, which cannot flow continuous mid-page columns and would overlap that document's columns
onto the text above — so the change regresses production until the painters own pagination, and is deliberately
held for this step.

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
not yet EMITTING the art, or by a handful of painter-side fidelity items. When this was written the engine
covered 180 of 325 docs at 0.942 mean SSIM (production 0.880 over all 325); the raster cutover has since
widened the *shared* predicate far past that — see the post-flip update after the phases.

Because the unhandled 145 are concentrated in the hardest features and each tends to fail on a single emission
gap, feature-completing everything before any flip would ship no benefit for a long time. The lower-risk path
is a **capability predicate** — a generalisation of `PdfPainterFidelityTests.IsBlockTableOrColumnFlow` — that
decides, per document, whether the engine covers it. `PdfRenderer.Render` routes covered documents through the
engine and falls back to `PdfTextEngine` for the rest. Each emission slice that lands tightens the predicate,
moving documents from the fallback onto the engine. `PdfTextEngine` is deleted only once the predicate admits
everything the corpus contains (the fallback goes cold).

### Phases

- **A — Wiring spike. LANDED.** `LayoutFonts` builds a `FontResolver<FontMetrics>` from the conversion's
  `FontDirectory`/`FontFallback` (bundled seed + directory + fallback + DefaultFont; an OS metrics fallback
  is a later slice) and wraps it as the `CanonicalParagraphMeasurer`'s `resolveFont`. `PdfRenderer.RenderViaEngine`
  runs `Fragmenter.Layout` → `PdfPainter.Paint` and reuses the shared `MakeDeterministic`/`TrimPages`/`Normalize`
  post-processing; the `MORPH_PDF_ENGINE` env var routes `Render` to it, and the method is internal so a test
  drives it without the process-global toggle. Verified: the engine path through the public `ConvertToPdf`
  reproduces `PdfPainterFidelityTests`' SSIM verbatim (multiple_pages 0.776, even_odd_headers/01 0.998,
  dot_points 0.998), so `LayoutFonts` resolves the same faces as the tests' `LayoutTestFonts`; the default
  path stays byte-identical. Not yet wired: per-conversion `FontWidthScale` into the measurer.
- **B — Capability predicate + fallback. LANDED (predicate); flip DEFERRED.** `EngineCoverage.Covers`
  (`src/Morph/Layout`) routes only covered documents to the engine, the rest to `PdfTextEngine`; still behind
  `MORPH_PDF_ENGINE`, default path unchanged. A host measurement (engine vs production, both via `ConvertToPdf`,
  SSIM vs Word over the 180 page-count-matched covered docs) found the engine at **0.9420 vs production 0.9472
  — −0.0052**, production winning 45 to 15. The split is sharp: the engine WINS on tables (header_row_repeat
  +0.076, table_multipage +0.031, table_indent +0.028) and LOSES on paragraph decorations and header/footer
  (header_footer −0.074, cover-letters/02 `w:pBdr` −0.073, resumes/15 `w:shd` −0.060). So flipping the default
  now would regress fidelity — the flip is gated on covered-doc parity, not on emission alone.
- **C — Two tracks, run together.** (1) Covered-doc FIDELITY: close the decoration gaps the measurement named
  (header/footer band + per-variant, `w:pBdr` position/collapse, `w:shd`, per-glyph advances) so the engine's
  covered mean crosses production's. (2) Coverage EMISSION: land `Fragmenter` art so the predicate admits more
  (ordered below). Each slice regenerates the affected `pdf_result*` baselines and is reviewed against Word.
- **D — Flip and delete.** Once the engine's covered mean holds or beats production and the predicate admits
  ~everything, make the engine the default, regenerate all `pdf_result*` baselines, confirm `compare-all-pdf.md`
  holds or beats 0.880 mean and the PDF page-count scoreboard holds or improves, then delete `PdfTextEngine` +
  the `PdfPageRenderer` driver.

**Update — post-raster-flip.** Phases A–D were written before step 6. Because the `Fragmenter` and
`EngineCoverage.Covers` are SHARED with the raster path, every raster emission slice (non-wrapping floats,
section breaks, inline shape groups, nested-table cells, content controls, floating text boxes, floating
tables, unwarped WordArt) widened the PDF gate too, and `PdfPainter` paints each — so the shared predicate now
admits **321 / 325** documents, not 180. Phase C's coverage-EMISSION track is therefore essentially done: the
four hold-outs are the two warp-WordArt test documents, image_wrap_square (float wrap) and agendas-minutes/14
(a positioned frame). A post-flip re-measurement (engine vs `PdfTextEngine`, both via `ConvertToPdf`, SSIM vs
Word over 318 page-count-matched covered docs) put the engine at **−0.0054**, production winning 136 to 54 — a
far wider set than Phase B's 180, same verdict. And Phase C's covered-FIDELITY track has largely landed: the
header/footer band, `w:pBdr` and `w:shd` that Phase B named as the sharpest losses now render in `PdfPainter`
(the step-5 painter log above). What remains is the intractable font/image tail — Aptos display-font wrap width
amplified by PDFium's harsher AA, compound-image and full-page-gradient rasterization — the same tail the raster
measurement called "exhausted". **The flip is held on that tail, not on coverage**, its accepted cost the
PNG-vs-PDF page-count divergence the raster-only flip introduced, carried until the font tail is tractable.

### Emission backlog (Fragmenter — unblocks whole documents, ordered by corpus reach)

Most of this has landed via the shared raster cutover (below); the list is kept for the sequence and the
few genuinely-remaining items — **float wrap exclusions** (square/tight, `image_wrap_square`) and **warp
WordArt** (the 16 presets, only the two envelope test documents need them) are the last emission gaps, plus
`agendas-minutes/14`'s positioned frame.

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

## The raster cutover (step 6), in detail

The raster analogue of step 5, and structurally easier: the raster painters share the render context's own
drawing primitives (font creation, text layout, colour parsing, image processing), so an engine-drawn page and
a production-drawn page of the same covered document differ only where pagination differs — not in how a glyph
or a fill is rasterized. That is why the raster gap is far smaller than PDF's −0.005.

### The seams

Two entry points, one per backend, both funnelling through the same `RenderPages` override:
`SkiaDocumentConverter.RenderPages(ParsedDocument, ImageExportOptions, Action<Action<Stream>>) : int` and the
identically-shaped `ImageSharpDocumentConverter.RenderPages`. Each emits one PNG per page through a
`pageCallback` and returns the page count. As with PDF, only the paginate-and-draw half is replaced:
`RenderViaEngine` runs `Fragmenter.Layout` → `<Backend>Painter.Paint`, gated on `MORPH_SKIA_ENGINE` /
`MORPH_IMAGESHARP_ENGINE != "off"` and `EngineCoverage.Covers`. This section was written when the gate was an
opt-in (`!= null`) and the default path was byte-unchanged; since the flip landed the engine is the DEFAULT for
covered documents (the env var a kill switch), and it is the *uncovered* fallback to the production
`<Backend>PageRenderer` that stays byte-unchanged. `RenderViaEngine` is internal so `EngineSkiaPathTests` /
`EngineImageSharpPathTests` drive it without the process-global toggle.

### The unit gotcha

The `LaidOutDocument` tree is in POINTS. PdfSharp is point-native and `PdfPainter` draws directly, but Skia and
ImageSharp draw in PIXELS, so every coordinate scales by `context.PointsToPixels` (`points * dpi/72`). Text
anchoring differs between them: Skia's `DrawText` is baseline-anchored (draw at `line.Baseline`), while
ImageSharp's `RichTextOptions.Origin` is top-anchored, so a run's origin drops by the font ascent
(`Origin.Y = P(baseline) − ascent*Scale`). Both draw whole strings through the context's text path — the same
`canvas.DrawText` production uses — so covered-document text matches production advance-for-advance; only the
per-glyph `w:spacing` tracked path re-derives advances (`DrawTracked`, matching production's own tracked path).

### What landed

- **`SkiaPainter`** — full `PlacedItem` switch (line/tableRow/image/shape/shading/border), per-glyph tracked
  text, tab leaders, inline images (transforms deferred), and `PaintShape` (solid fill + outline over
  `SkiaPageRenderer.BuildPolygonPath` freeform subpaths or a preset rect/ellipse; gradient fill deferred).
- **`ImageSharpPainter`** — the same coverage over ImageSharp's deferred `DrawingCanvas`; images route through
  `GetProcessedImage` (crop/rotation/flip baked for free), and `PaintShape` reuses the production
  `ImageSharpPageRenderer.BuildPath` / `BuildPresetPath` / `BuildRotation` / `NonzeroFill` geometry.

### Validation

`labels/14` — a label sheet whose behind-text floating shapes tile a table grid — is the shape probe.
Before `PaintShape`, the engine dropped every shape (**−0.28** vs production, both backends). After, the
engine-vs-production diff shows **no shape ink at all** — the shapes match production exactly — and SSIM is
0.956, the residual being a per-cell line-pitch drift in the multi-line recipient-address cells (the sender
cells' 3 lines barely doubles, the recipient cells' 5 lines accumulates it; the shared measurer's default-
paragraph pitch differs from production's by a fraction of a pixel per line, small enough that page-count
parity holds at 99.3%). `EngineSkiaPathTests` / `EngineImageSharpPathTests` gain `labels/14` as the
shape-path guard.

### Characterization: the raster engine is at production parity

Both raster seams were measured the way PDF Phase B was — render every covered document twice (production
`<Backend>PageRenderer` vs the engine), SSIM each page against Word's `expected_*.png`, page-count-matched:

| Backend | covered docs | engine mean | production mean | delta | engine wins / loses / ties |
|---------|-------------:|------------:|---------------:|------:|---------------------------:|
| Skia | 182 | — | — | **−0.0023** | — |
| ImageSharp | 182 | 0.9395 | 0.9403 | **−0.0008** | 35 / 30 / 117 |

This is the decisive contrast with PDF (**−0.0042**, held because it clearly regressed): the raster engine is
at parity. Both raster paths share their render context's drawing primitives (font creation, text layout,
colour, image processing), so an engine-drawn covered page and a production-drawn one differ only where
pagination differs — and pagination already agrees (181/182 covered docs match production's page count, the
lone exception the standing resumes/13 knife-edge). The wins and losses roughly cancel and 117 of 182 are
ties. The split is the familiar one: the engine WINS on tables (header_row_repeat +0.093, table_multipage
+0.035, table_indent +0.027) and LOSES on the intractable tail — per-glyph advances / line-pitch (labels/14,
labels/16), unavailable commercial fonts (business-plans/03 Aptos), compound-image and full-page-gradient
rasterization AA (cover-letters/12 −0.058, cards/05), and `w:shd`/`w:pBdr` decoration sub-pixel offsets
(resumes/15). Spot-checking the three biggest losers confirmed each renders correctly against Word — the gap
is AA/pitch noise amplified by dense text or a full-bleed background, not a missing feature. The same set the
PDF measurement named "exhausted".

The consequence for the flip: unlike PDF, flipping the raster default is **not** a fidelity regression on
covered documents — it is a pagination-unification move at parity. The remaining lever is coverage EMISSION
(WordArt/inline shape groups, float wrap, floating tables, nested-cell content) to route MORE of the corpus
through the one engine, since the covered set already paginates identically across all three backends.

### Emission slice: non-wrapping top-level floats (coverage 183 → 236)

The first emission slice widens `EngineCoverage.Covers` to admit top-level `FloatingShapeElement` (behind or
in front of text — shapes carry no wrap) and `FloatingImageElement` with `WrapType.None`. Nothing new is
emitted: the `Fragmenter` already lays body floats out by anchor into each page's float items and every
painter already draws `PlacedShape`/`PlacedImage`, so the predicate was the only gate. Two small pieces
closed the seam first: the raster `PaintShape` gained the **linear-gradient fill** it had deferred (built the
same way as each production `PageRenderer`, so gradient-filled float shapes render), and image-fill shapes
need nothing because the `Fragmenter` routes those to `PlacedImage`. Wrapping images (Square/Tight/Through/
TopAndBottom) stay excluded — they need flow exclusions the engine does not emit.

Coverage jumped **183 → 236 documents** (89 still excluded, now dominated by section breaks and non-paragraph
cell content). Measured the newly-covered set the same way: the **53 float documents come in at −0.0026 vs
production** (23 wins / 21 losses / 9 ties), at parity — lower in absolute SSIM (0.842 vs 0.845) only because
art-heavy pages are harder, not because the engine diverges. The whole covered set is **−0.0012 over 235
docs**. The losers are the multi-**page** float documents: newsletters/12 (−0.23), brochures/05 (−0.07),
business-plans/06, brochures/02. Their failure is one known bug — a body float is tagged with the page the
flow has *reached* when the anchor element is encountered, not the page its anchor paragraph finally lands on,
so page-2 floats stack onto page 1 (newsletters/12's page-2 photo overlays page-1's header). Single-page float
documents render correctly. **Multi-page float anchor-page resolution is now the gate for those documents and
the next float slice** — it must be fixed before any raster flip that includes them. (pct_pos_offset, a
single-page doc, loses 0.043 for a different, smaller reason: percent-position float placement differs
slightly from production.) **Both were fixed in the fidelity slice below** — multi-page anchoring lifts
brochures/01 −0.375 → −0.021 and newsletters/12 to +0.018, and percent-position placement reaches exact parity.

### Emission slice: section breaks (coverage 236 → 273)

The second emission slice admits `SectionBreakElement`. Again nothing new is emitted at the `Fragmenter`: it
already paginates every break kind — NextPage/Even/Odd advance and re-lay at the new geometry (inserting an
even/odd parity filler page), Continuous switches columns at the break point — and records the `PageSettings`
each page was laid at. The one missing piece was on the paint side: `SkiaPainter`/`ImageSharpPainter` sized
every page bitmap from the context's initial page size, so a mid-document portrait→landscape switch would have
been clipped. A `RenderContextBase.PagePixels(PageSettings)` helper (the same double-precision points→pixels
the context uses, so no off-by-one) now sizes each page from its own recorded settings. On the uniform-geometry
covered set this changes nothing; it lights up the moment a section switches page size.

Coverage jumped **236 → 273 documents** (84% of the corpus; 52 still excluded — WordArt, wrapping floats,
non-paragraph cell content, floating tables/text-boxes). Validation: the newly-covered section documents match
production's page count everywhere except the standing business-plans/15 knife-edge (18 vs 19), and per-page
geometry renders exactly — business-plans/13's engine pages 13–16 and 19–20 come out landscape (1650×1275) and
the rest portrait, matching production page-for-page, with the landscape START-UP COSTS table, header, footer
and page number all in place. Measured the same way, the section set is at parity with production (**−0.0017**
over 36 documents, 13 wins / 16 losses / 7 ties; the whole 271-doc covered set is **−0.0012**). The worst
section losers (resumes/10 −0.069, wedding/04 −0.039) are the same intractable tail, not a section defect.
The remaining excluded buckets (WordArt/inline shape groups, wrapping floats, floating tables/text-boxes,
non-paragraph cell content) are the next emission slices; the multi-page float anchor-page bug still gates the
multi-page float documents among the now-covered set.

### Emission slice: inline shape groups (coverage 273 → 300)

The third and largest emission slice admits **inline shape groups** — grouped drawings embedded in a run
(arrow glyphs, icon bubbles on coloured circles, circle-cropped photos), distinct from the WordArt text-warp
element (a separate block/floating element still excluded). Unlike floats and section breaks, this one *does*
emit new geometry: the group had no representation in the laid-out tree. It measures exactly like an inline
image, though (all three production backends share the measure branch — width/height from the run's inline
extent, bottom on the baseline), so it rides the existing inline-image carrier: `LaidOutImage`/`PlacedImage`
gain an optional `ShapeGroup` and `Data` becomes nullable (a group carries no bytes), and the measurer's
`Flatten`, `MapImages`, and every line height/ascent/shift/content path are reused unchanged.

Each painter's `PaintImage` branches to a `PaintInlineGroup` that is a verbatim port of the production
`TextRenderer.RenderInlineShapeGroup` / `PdfTextEngine.DrawShapeGroup` — the group's child shapes scaled from
its child coordinate space into the inline box, painted back to front (lines, preset rect/ellipse, custom
subpaths, solid fills, strokes, drop shadows, picture members, per-shape flips, and a whole-group rotation).
The ports reuse the production `ParseColor`/`BuildGroupShapePath`/`DrawGeometry`/`RenderGroupPicture`/`StrokePen`
helpers (promoted to internal; the picture helpers made static with the context threaded in), so the engine
paints a group pixel-identically to production. `EngineCoverage` then drops the `HasInlineArt` rejection.

Coverage jumped **273 → 300 documents** (92% of the corpus; 25 still excluded — WordArt warp, wrapping floats,
floating tables/text-boxes, nested-table and other non-paragraph cell content). Validation: on
inline_shape_arrows (the canonical connector-arrow set) the engine-vs-production diff is **1.6% (anti-aliasing
only)** on both Skia and ImageSharp — the four coloured arrows and the thinner-stroke variant render exactly.

Across the corpus the inline-group set measures **−0.0180 vs production** (26 docs, 9 wins / 17 losses); the
whole 297-doc covered set is **−0.0027**. Unlike the float and section sets, this one does not sit at parity —
but the drag is *not* the inline-group paint. The worst losers are design-heavy templates whose **coloured
background panel the engine drops**: cover-letters/10 (−0.163) loses its charcoal header band and resumes/09
(−0.064) its red sidebar (white where Word fills), while their inline groups render correctly (cover-letters/10's
yellow logo, resumes/09's "JM" mark and sidebar text are all in place). cover-letters/10 has no top-level
floating elements at all, so these panels are floats anchored in a paragraph or cell — which the engine's
top-level-and-cell float handling does not place. (menus/07's full-page charcoal shape, by contrast, IS a
top-level float and renders correctly — engine and production both sample charcoal.) So this is a pre-existing
non-top-level-float defect these documents merely *exposed* on entering the covered set, and it is the next
slice. Setting it aside, the inline groups themselves match production (inline_shape_arrows 1.6% AA on both
backends).

### Fidelity slice: header shapes and float-shape opacity

The inline-group set's worst loser, cover-letters/10 (−0.163), was diagnosed to two small defects rather than
the header-shape *placement* the first attempt guessed at. First, `Fragmenter.ResolveHeaderImages` emitted only
header *images*, so the document's two behind-text header *shapes* — a charcoal banner and a companion accent
panel — dropped entirely; it now emits header `FloatingShapeElement`s too (mirroring the body-float shape
cases). Second, and the real subtlety: the accent panel is a full-page rectangle at **10% opacity**
(`a:alpha`), which Word renders as a near-white tint (252,251,248) — not a solid panel. The raster and PDF
`PaintShape` were ignoring `FillAlpha`/`LineAlpha` (a general float-shape bug, not header-specific), so the
engine painted it as an opaque tan slab over the body. Applying the alpha (the production `PageRenderer`
already did) makes the panel read as the same near-white tint. Together: the charcoal band renders and the
accent panel matches Word's body colour exactly. **cover-letters/10 lifts 0.703 → 0.882 (+0.18)**; the whole
covered set moves from −0.0027 to **−0.0011** over 297 documents (85 wins / 80 losses, up from 80/84). The
alpha fix is a correctness win for any float shape with a translucent fill, not just this document.

### Emission slice: nested-table cells (coverage 300 → 304)

`EngineCoverage.IsSimpleTable` rejected any cell holding a non-paragraph element, including a nested table —
even though the `Fragmenter` has laid nested tables out inline at the cell cursor since an earlier slice. It
now admits a cell containing a nested table when that table is *itself* simple (a recursive check). Coverage
rises **300 → 304**; the four newly-covered documents measure complex_tables +0.001, business/03 −0.002 and
resumes/06 +0.006 — at parity, the nested-table layout is sound — while brochures/01 drops −0.375. That drop
is not the nested table (its inner grid renders): it is the standing multi-page float-anchor bug — brochures/01
is a two-page document whose page-2 background float stacks onto page 1 — which the nested-table exclusion had
been hiding. brochures/01 joins newsletters/12 in the set that the multi-page float-anchor fix will lift.

### Emission slice: content controls (coverage 304 → 306)

A `ContentControlElement` (a checkbox, dropdown, date picker or plain-text placeholder) is a wrapper whose
`CellParagraph` property synthesizes a paragraph from its resolved value — the parser already writes every
control type's visible text into runs (a checkbox becomes its glyph, a dropdown its selection, a date its
formatted string). So the whole feature is: treat a control as that paragraph. `EngineCoverage` admits a
block-level control and a control-in-cell; the `Fragmenter`'s element loop and cell-content loop render its
`CellParagraph`. Coverage rises **304 → 306**: content_control_inline (five mixed control types) measures
**+0.0023** vs production (engine wins — the glyph, selection and date all render), and labels/07's eight
plain-text placeholders 0.912. Word lays inline content controls out on one line with their label where both
the engine and the production renderer stack them — a pre-existing production limitation the engine matches,
not an engine defect.

### Emission slice: floating text boxes (coverage 306 → 310)

A `FloatingTextBoxElement` is a floating box (background, outline, freeform geometry) with a mini-flow of
paragraphs inside. Production draws the box then lays the content out at the box's top-left, full box width,
with no internal inset. The engine emits both without new painter code: `Fragmenter.PlaceTextBox` adds the box
chrome as a `PlacedShape` (a synthetic `FloatingShapeElement` carrying the box's fill/outline/geometry) and the
content by reusing `LayoutCellContent` — wrapping the box's content in a synthetic cell — so paragraph spacing,
alignment, inline images and even a nested table inside a text box lay out for free. Both are tagged with the
page the flow has reached and paint behind or in front per `BehindText`. `EngineCoverage` admits a non-wrapping
text box (wrapping ones need flow exclusions). Coverage rises **306 → 310**; all four documents measure at
parity or better — brochures/03 +0.021 (a two-page document), cards/13 +0.0001, labels/10 −0.0006, labels/11
+0.0051 (three wins, one tie). A rotated text box currently rotates its box but not its content lines; none of
the corpus text boxes rotate.

### Emission slice: floating tables (coverage 310 → 314)

A floating table (`w:tblpPr`) positions by its own offsets from a page/margin/text anchor. `PlaceFloatingTable`
resolves the position (mirroring the production `ResolveFloatingTableY`/`ComputeTableX`), then reuses
`LayoutNestedTable` to lay the grid out at that position with no page breaks, emitting the rows as body floats
in front of the text. `EngineCoverage` splits its table check into an `IsSimpleTable` (non-floating) and a
shared `HasSimpleCells`, and admits a floating table whose cells are simple. Coverage rises **310 → 314**.
Three of the four documents are at parity — labels/04 −0.004, labels/09 −0.010, letters/01 +0.001 — where the
floating table *is* the content with nothing to overlap. agendas-minutes/11 drops −0.073: its text-anchored
float table sits over the body because a float takes no flow space, so the body flows into the same rows. That
overlap is fixed in the fidelity slice below (a text-anchored floating table flows inline, matching
production); a wrapping *image* like image_wrap_square is a separate case that still needs flow exclusions.
It is admitted with the overlap documented, as the multi-page-anchor floats were.

### Emission slice: unwarped WordArt (coverage 316 → 321, 98.8%)

WordArt looks like the hardest remaining bucket — text-on-path, per-glyph envelope warps, 16 presets — but the
warp math turns out to matter only to the two synthetic test documents. Every WordArt in a real corpus template
(business/06's LOGO frame, brochures/08's ring, menus/03's cell labels, wedding/08's ellipse badge, cards/02) is
`textNoShape` — no warp. And unwarped WordArt is just Word's inline text box: box chrome plus the text shrunk to
fit and centred. So the engine emits it with no new painter code — a `PlacedShape` for the box (fill, outline,
freeform or ellipse geometry) and the text through `LayoutWordArtText`, which lays a centre-aligned synthetic
paragraph at the fitted font size out via `LayoutCellContent`. Three contexts: a block WordArt takes flow space
at the aligned cursor, a floating one is absolutely positioned text with no box, and a cell one lays out at the
cell cursor. `EngineCoverage` admits an unwarped WordArt (a warped one still disqualifies). Coverage rises
**316 → 321 (98.8%)**, and all five documents *beat* production — business/06 +0.012, brochures/08 +0.004,
menus/03 +0.007, wedding/08 +0.004, cards/02 +0.029 — because the engine's box-and-centred-text matches Word's
*layout* more closely than the production path does. (The glyph colour and size resolved from the first run's
direct properties alone at this point, so a box driven entirely by its paragraph style — menus/03's
`EVENT INTRO`/`EVENT DATE` — still rendered black at the WordArt default size, a shared parse gap this slice
shared with production; the full run/style cascade landed later, see "inherited fills and text-box styles"
below.) Deferred: the glyph outline (fill only for now) and the 16 warp presets, which only the
wordart / wordart-envelope test documents need.

Coverage now stands at **321 / 325** — verified by counting `EngineCoverage.Covers` over every corpus document
with a Word reference render (`Inputs/**/input.docx` beside an `expected_*.png`). The four hold-outs are the two
warp test documents (`wordart`, `wordart-envelope`), `image_wrap_square` (float wrap), and `agendas-minutes/14`
(a positioned frame). The per-slice tallies above are point-in-time counts taken as each slice landed, so they
drift by a document or two between slices as the shared predicate shifted (the +2 between the floating-tables
and unwarped-WordArt slices is one such step) and do not chain exactly to this total — 321 / 325 is the
authoritative figure.

### Fidelity slice: multi-page float anchoring and inline floating tables

The worst covered documents shared three deferred root causes, all now fixed in the Fragmenter (no
parser change needed) ahead of the flip.

**Multi-page float anchor-page.** A body float was tagged with the page the flow cursor happened to be on
when its *element* was reached (`bodies.Count`). But a full-page background for page 2 is declared before
page-1's content finishes flowing, so both of a document's backgrounds landed on page 1 — brochures/01
stacked its page-2 blob over the page-1 text and lost its page-2 background entirely (−0.375); newsletters/12
did the same (−0.23). The fix defers an *absolutely*-positioned float (page/margin anchor — an absolute Y
independent of the cursor) into a `pendingFloats` queue and resolves it to the page where the next *visible*
line or row commits, skipping an empty spacer paragraph at a page boundary so a knife-edge does not
misassign it. A flow-anchored float keeps the emit-time cursor page, where its Y offset is measured.
brochures/01 lifts **−0.375 → −0.021**, and newsletters/12 **−0.23 → +0.018** (it now beats production).

**Inline text-anchored floating tables.** `PlaceFloatingTable` overlaid every floating table as a body float
taking no flow space, so agendas-minutes/11's text-anchored Date/Time block sat *over* the headings that
flowed into the same rows (−0.073). Production renders a text-anchored floating table inline (PageRendererBase
leaves the cursor at its bottom); the engine now mirrors that — a `Text`-anchored floating table lays out at
the cursor, consuming the preceding paragraph's after-spacing (a 30pt placeholder gap here) and advancing the
cursor past its bottom so the body clears it. Page/margin-anchored floating tables stay absolute overlays and
now defer like the other absolute floats. agendas-minutes/11 lifts **−0.073 → −0.028**: the overlap is gone
and the block is positioned correctly, leaving only minor table-height variance.

**Percentage-positioned floats.** `FloatX`/`FloatY` read only the point offset, so a `wp14:pctPosHOffset` /
`pctPosVOffset` float (its point offset zero) collapsed onto the anchor origin — pct_pos_offset's centre
shape rendered in the top-left corner (−0.043, the flip's single *visible* loser; every other loss is
sub-pixel AA). The fix resolves a percentage offset as that fraction of the anchor's reference dimension —
the page for a page anchor, else the content box — mirroring the production `FloatingPosition`. pct_pos_offset
lifts **−0.043 → 0.000** (exact parity); the point-offset path is byte-identical, so every other float is
untouched.

With all three levers pulled, the covered set is at production parity across all three raster backends
(aggregate **−0.0023 → −0.0001**, 98 wins / 85 losses over 318 page-count-matched documents) and every
visibly-regressing outlier is eliminated — the remaining losses are the intractable AA/font tail.

### The raster flip landed (step 6)

With coverage at 98.8% and the three fidelity fixes closing the last visible outliers, the raster default is
now the engine for covered documents. Both `SkiaDocumentConverter.RenderPages` and
`ImageSharpDocumentConverter.RenderPages` gate on `MORPH_<BACKEND>_ENGINE != "off"` (was `!= null`): a covered
document paginates through `Fragmenter.Layout` and paints through `<Backend>Painter.Paint` by default, and the
env var is now a kill switch (`=off` forces the production `<Backend>PageRenderer`) rather than an opt-in. An
uncovered document still falls through to production unchanged.

Every covered-document Skia + ImageSharp scenario baseline was regenerated to the engine render via
`regenerate-baselines.sh` — 1,780 baselines (the two backends' page PNGs, result JSON, and per-scenario
`compare.md`) in the 1,783-file flip commit (`aab47e002`, the balance being the two gate flips and this doc);
the export (HTML / Markdown / PDF) and spec snapshots are byte-unchanged, since the PDF path
still runs `PdfTextEngine`. The container suite is green on the regenerated baselines. This unifies raster
pagination on the one backend-independent engine at production parity — the wins land where the engine
paginates a document more like Word (business-plans/12's page count corrects 17 → 18), and the losses are the
AA/font tail that no pagination change touches.

The remaining divergence is deliberate: PDF stays on `PdfTextEngine`, so a covered document's PNG and PDF page
counts can differ until the PDF cutover (step 5) follows — the accepted cost of a raster-only flip, and the
next lever alongside the last emission gaps (warp WordArt, float wrap).

**Follow-on fix — image-only line height.** A PDF re-measurement (engine still −0.0054 below `PdfTextEngine`,
dominated by the font/image tail, so PDF stays held) surfaced one systematic loser that turned out to live in
the *Fragmenter*, so it also dinged the shipped raster flip: a paragraph whose only run is an inline drawing —
a resume template's section rule, a thin full-width line in an otherwise-empty `Line`-style paragraph —
collapsed its line to the 0.5pt image height instead of the paragraph's font line height, dropping the rule
onto the next line where the body text obscured its left half (it read as a partial-width rule). The empty-
mark branch of `CanonicalParagraphMeasurer` already restored the *ascent* from the mark font; it now restores
the *height* too (the mark font's line pitch), matching production's `max(font, image)`. Eleven documents'
baselines regenerated; resumes/18's rules render full-width below each heading again and its PDF SSIM vs Word
rose **−0.12 → +0.010**.

**Follow-on fix — Skia outline-only shapes flooded.** Checking the flipped covered set surfaced menus/09's
stacked menu card rendering solid green in Skia (ImageSharp correct): its top shape is a green outline-only
rectangle (`a:ln`, no fill) over a grey card body. `SkiaRenderContext`'s reusable rule paint set `IsAntialias`
but no `Style`, so SkiaSharp defaulted it to `Fill`; `SkiaPainter.PaintShape` strokes a shape's outline
through that paint via `DrawPath`/`DrawRect`, and with `Fill` those flooded the shape with the line colour
rather than stroking a thin frame. Setting `Style = SKPaintStyle.Stroke` fixes it — Skia only, since
ImageSharp's `PaintShape` already stroked and `DrawLine` (underline/border edges) ignores `Style`. menus/09
renders as a grey card with a thin green border, and every Skia fill+line or line-only shape that had been
flooding now strokes correctly (33 baselines regenerated).

**Follow-on fix — inherited fills and text-box styles.** Two shared-parser gaps surfaced the same way, each a
property Word inherits from an enclosing context that the shape/text-box parse had dropped — so both hurt
production as much as the engine, and closing them lifts all three backends. First, brochures/06's accent
stripes and hot-air-balloon line-art are inline `wpg:wgp` groups whose child rects carry `a:grpFill` (defer the
fill to the group's own `a:solidFill`); the inline-group parser read only each shape's direct fill, so every
grpFill child resolved to no colour and drew as nothing. Resolving `a:grpFill` against the ancestor group — the
shared `ResolveGroupFill` the floating-shape path already used — restores them: brochures/06's page-1 SSIM rises
**+0.030** on Skia, and the stripes and balloon render across raster, PDF and the HTML export alike. Second,
menus/03's "EVENT INTRO"/"EVENT DATE" labels are unwarped WordArt boxes with no run `rPr`; `ParseWordArt` read
their glyph colour and size from the first run's direct properties alone, so the labels drew 36pt black where
their Heading1→Normal style resolves 10pt white. Resolving the full run/style/docDefault cascade
(`ParseRunProperties`) for an unwarped box paints them white at Word's size. The `wps:style/a:fontRef` colour —
a lower DrawingML fallback below the style chain, and the actual source of wedding/08's white `&` badge — is a
distinct case left deferred, since a fontRef fallback must sit below an explicit run/style colour or it would
flip the very menus/03-style boxes it was added to fix.

## Testing strategy (a large secondary win)

The current suite infers layout from rasterized PNGs (the entire struggle behind `src/page_counts.md`).
With a retained tree the **layout tree can be snapshotted as text** — page count, region rectangles, line
Y-positions, break points — and diff it *directly* against Word's XPS box geometry, decoupled from
rasterization. That turns "why is this page 2px off" into a structural diff. Keep the existing
Verify-PNG scenario tests as the painter-fidelity gate; add layout-tree snapshots as the pagination gate.

*Partly realized.* The page-count half is a standing gate: `FragmenterPageCountTests` diffs the tree's
`Pages.Count` against Word at 99%+ agreement, and `CanonicalFragmenterTests` / `CanonicalWrapAgreementTests`
assert specific placements and wraps. The full-geometry structural diff the paragraph above envisions — every
region rectangle and line Y-position snapshotted as text and diffed against Word's XPS box tree — is the part
that remains unbuilt; today the painter-fidelity `Verify`-PNG suite is still the backstop for placement.

## What is preserved vs deleted

- **Preserved:** the parser and `ParsedDocument` model (unchanged input); the 21 measured rules (become
  fragmenter rules); the float pipeline knowledge (`docs/floating-art-pipeline.md`, becomes exclusions);
  the shape/WordArt geometry (becomes `PlacedShape`); the Verify-PNG suite (becomes the painter gate).
- **Deleted (step 7 — the remaining cleanup):** three copies of pagination; `TableHeightCalculator` as a
  separate measure pass; the whole-paragraph widow approximation; the raster/PDF metric divergence; and the
  knife-edge category. For covered raster documents these are already gone; the old code persists as the
  uncovered fallback and the kill switch until step 7, and PDF keeps its copy until its own flip.

## Open questions

- **`image_wrap_square` residual** — it also exercises the mode ≤14 `UseFormerTextWrapping` flag
  (`src/page_counts.md`, LO rule index), which is orthogonal to columns; columns are necessary but may
  not be sufficient there.
- **Incremental relayout** — not needed (Morph is batch, not interactive), so the tree can be
  immutable and recomputed wholesale. This removes the hardest part of real layout engines.
