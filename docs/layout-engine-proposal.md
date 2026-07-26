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
      `LaidOutDocument`, `LaidOutPage`, `PlacedLine`, `PlacedTableRow`, `PlacedCell`, `MeasuredLine`).
      `PlacedLine` carries its baseline and the `PlacedRun`s to paint (text + `RunProperties` for
      font/colour) — one per contiguous source run on the line, so a mixed-format line ("plain **bold**
      plain") is several runs at their own X and a uniform line is one, and a painter draws without
      re-measuring. `PlacedTableRow` carries its `PlacedCell`s (box, shading, borders, and the cell's
      laid-out content). Each run's X is the canonical pen position at its start; per-glyph advances (exact
      intra-run boundaries) and the image/rule/shape placed-item kinds attach as later slices.
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
      text tables are added, 154/157 = 98.1% with multi-column flow — all four corpus column documents
      match** — one backend-independent pass reproducing Word's pagination. The three misses are sub-line
      knife-edges (a table tipping onto an extra page, a trailing line spilling — resumes/13 is one Word's
      own backends straddle) plus the document-dependent question of how much paragraph-after-spacing
      precedes a table (a probe would settle it). Columns are equal-width from one `PageSettings`.
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
      render. Still to land before it can replace the production `PdfRenderer`: paragraph/run decorations
      (underline, strike, highlight, paragraph borders, shading), list markers, tabs, images/shapes,
      in-cell vertical alignment and nested tables, and per-glyph advances (exact intra-run boundaries —
      the painter currently anchors each run at its canonical start and lets the font library fill the
      run). Then repoint `Morph.Pdf` at `LaidOutDocument`, delete `PdfTextEngine`'s pagination, and
      validate the full container suite (PDF page-count scoreboard unchanged or better, AE/SSIM neutral).
- [ ] **6. `SkiaPainter` + `ImageSharpPainter`** — the payoff step. Both become thin painters of the
      same tree; the whole-paragraph pagination and the duplicated `TextRenderer` layout code delete.
      This is where the raster knife-edges collapse (raster now paginates identically to PDF — one
      answer, not a straddle). Regenerate all raster + PDF baselines; validate the scoreboard.
- [ ] **7. Delete** `PageRendererBase` pagination, `SectionBreakHandler` (subsumed), the per-backend
      `EnsureSpaceFor`/`AdvanceToNextColumnOrPage`, `TableHeightCalculator` (folded into the table
      sub-layout). Keep the backends' primitive draw ops only.

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
