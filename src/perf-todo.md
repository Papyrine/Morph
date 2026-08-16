# Performance TODO

**Open items only** — the rule `src/todo.md` follows. A landed item is deleted from this file; whatever is
durable about it moves into "Verified fine" below, or into the relevant doc.

None of the items here change rendered output. The Verify scenario suite (`./scripts/test.sh`) is the
correctness gate: after each one, baselines must pass **unchanged** — a pure perf change that shifts a pixel
is a bug.

Line numbers are from commit `09ed5de53` and will drift, so each item also names the method to search for.

## Rewritten 2026-08-15 against the layout engine

The previous contents were a full review (items P1–P6, 2026-07-04) of the Parse → Render architecture. Its
production renderers — `SkiaPageRenderer`, both `TextRenderer`s, `PdfTextEngine`, `PdfPageRenderer` and
`PageRendererBase` — were deleted in 2026-08 when the layout engine became the only path to a rendered page
(`docs/layout-engine.md`), so nearly every citation in that review pointed at a file that no longer exists.

Every item of it had landed except the optional part of P4c and the PDF page-range item, restated below as
L5 and L4. The caches the landed items added live on the render contexts rather than on the deleted
renderers, so they survived the deletion intact — they are listed under "Verified fine" and must not be
re-chased.

What the engine did reintroduce, in one shared place, is the measure-then-place duplication that the raster
backends had solved with `pagedLayoutCache` / `boundedLayoutCache`. Neither name existed in the tree any
more, and `CanonicalParagraphMeasurer` had replaced them with nothing — for all three backends at once
rather than one at a time. All three answers to it are landed — the wrap memo, the widths-only longest-token
measure, and the table-geometry memo (see "Verified fine"). What remains open is unrelated to layout.

## Measurement

`src/Benchmarks/` (BenchmarkDotNet, MemoryDiagnoser enabled) has `ParseBenchmarks`, `RenderBenchmarks`,
`PdfBenchmarks`, `ExportBenchmarks`, `HtmlBenchmarks` and `InkBenchmarks`, plus the `-- smoke` sanity
harness. BenchmarkDotNet needs the working directory to be `src/Benchmarks` and an explicit `--artifacts`
path.

**A stopwatch over a corpus slice cannot see a layout change — count the work instead.** Measured here:
end-to-end conversion of the same ten documents by the same build varied 3368/3460/3671ms run to run, a
~9% spread that swallows any plausible layout saving whole (a first attempt at sizing the wrap memo read
8% off that noise and was wrong). Temporary counters on the entry point under test, run over all 330
scenarios, size the same change deterministically and reproducibly — that is how the three landed layout
items are quantified, and it is the method to reuse. A wall-clock number would need a layout-dominated
benchmark, measured through PDF export rather than raster: both paginate through the same `Fragmenter`,
but raster spends most of its time in rasterization and PNG encode.

**Counting first also sizes the prize before the work.** `BenchmarkDocs` has no table-heavy document and no
header/footer table, which is why the corpus counters, not the benchmark set, settled all three. The same
counters showed the table-geometry memo touching 6 of 330 scenarios — worth knowing before, not after. L4
needs a page-range case; L5 is measurable on `ParseBenchmarks` as it stands.

---

## L4 — PDF `options.Pages` renders the whole document, then deletes the unwanted pages

`PdfRenderer.Render` (src/Morph.Pdf/PdfRenderer.cs:15-46): the fragmenter lays out the whole document and
`PdfPainter.Paint` draws every page — including font subsetting and image embedding — after which
`TrimPages` (:53) removes everything outside the range. Requesting page 1 of a 500-page DOCX pays for 500.

Layout genuinely has to reach the end, because NUMPAGES resolves against the document total (the engine takes
it from `LaidOutDocument.Pages.Count`, which is why the second counting pass the deleted path ran is gone —
see the comment at :8-14). Painting does not.

**Fix:** thread the range into `PdfPainter.Paint` and skip the draw for out-of-range pages.

## L5 — parser: ~9 independent whole-body walks in `ParseDocument`

`DocumentParser.ParseDocument` (src/Morph/OpenXml/Parsing/DocumentParser.cs:276) still walks the entire body
once per feature: `LastRenderedPageBreak` count (:284), `SectionProperties` (:314), `ExtractEmbeddedObjects`
(:530), `ExtractFieldCodes` over every run (:869) and its `SimpleField` pass (:920), `ExtractTrackedChanges`
(:976 and :986), `CommentRangeStart` (:1027), `BookmarkStart` (:1083), and `DetectAdvancedFeatures` (:457) —
whose walk is an untyped `body.Descendants()` and so the most expensive of the set.

The cheap halves of the original P4c landed: the paragraph-ordinal map is built at most once and only when
bookmarks or comments are present (:401-416), and `DetectAdvancedFeatures` already folded two walks into one.

**Fix:** fold the type-filtered extractors into the untyped walk `DetectAdvancedFeatures` already makes,
dispatching on `LocalName` — the consolidation that method's own comment describes, applied across the rest.

---

## Verified fine — do not re-chase

- **The wrap memo** (`CanonicalParagraphMeasurer.wrapCache`, landed 2026-08-15). `Wrap` is a pure function
  of `(ParagraphElement, maxWidth)` and was being asked the same question repeatedly — the row-height and
  placement passes ask at the identical cell width, and a header/footer paragraph is asked once per page.
  Memoized for the measurer's lifetime, which is one conversion. Counted over the whole 330-scenario
  corpus: **29,573 wrap requests resolve to 16,448 builds — 44% answered from the memo.**
  **The cache must stay a `ConcurrentDictionary`** — a plain `Dictionary` corrupted under the layout spec
  tests, which drive a static `LayoutTestFonts.Measurer` from parallel TUnit tests.
- **The table-geometry memo** (`Fragmenter.Flow.tableGeometryCache`, landed 2026-08-15). A table's column
  widths and row heights derive only from the table, the measure and the measurer, and every caller reads
  them without mutating, so `TableGeometry` computes each `(table, width)` pair once. It removes a per-PAGE
  repeat: a header or footer table has its height measured to reserve the band and is then laid out again,
  and both halves ran the full autofit and row-height math on every page. **The corpus barely exercises
  this** — 19 of 542 computations saved (3.5%), in 6 of 330 scenarios — because few documents put a table
  in a header or footer. Where one does the effect is not marginal: `business-plans/10` goes from 8
  computations to 1. `HeaderReservedTop` was hoisted out of three `Flow` field initializers into `Run` in
  the same pass, which is what let it (and `NestedTableHeight`) stop being static and reach the cache.
- **The longest-token measure and the widths-only wrap** (`MeasureLongestTokenWidth`, `BuildWrap`'s
  `buildItems` flag, landed 2026-08-15). The autofit minimum probe asks for the longest unbreakable token
  by laying the paragraph out at a 1pt measure, which puts every word on its own line; `BuildLineItems` ran
  once per word to produce segments and images that the caller discards. It now runs widths-only and caches
  the single float. Counted over the corpus: **1,105 of 15,871 `BuildLineItems` calls removed (7.0%)**,
  concentrated in the **33 of 330 scenarios** that autofit a table. `IParagraphMeasurer` carries the 1pt
  definition as a default implementation, so the test stubs need no change and the semantics stay
  documented where the interface is.
- **Decoded-image caches, all three backends.** Skia keys bitmap / `SKImage` / `SKSvg` / rasterized-SVG
  caches on `byte[]` reference identity (src/Morph.Skia/SkiaRenderContext.cs:292-295); ImageSharp caches the
  post-resize image against the full processing recipe (src/Morph.ImageSharp/ImageSharpRenderContext.cs:424+),
  at document lifetime rather than page lifetime; PDF caches `XImage` per source array
  (src/Morph.Pdf/PdfRenderContext.cs:138), so a repeated logo is embedded once instead of once per page.
  These sit on the render contexts and were untouched by the renderer deletion.
- **Font resolution.** `FontResolver.Resolve` memoizes on the **raw** request tuple in front of the canonical
  cache (src/Morph/Fonts/FontResolver.cs:125), so the ~50–70 suffix scans that build the canonical key no
  longer run per fragment. Each backend also caches per requested triple (SkiaRenderContext.cs:112,
  ImageSharpRenderContext.cs:125, PdfRenderContext.cs:27), and PdfSharp memoizes embedding process-wide.
- **Per-draw object churn.** Skia reuses text/fill/rule paints (SkiaRenderContext.cs:204-235); ImageSharp
  caches brushes, pens, `TextOptions` and space widths (ImageSharpRenderContext.cs:329-330, :398-399); PDF
  caches brushes and pens by value (PdfRenderContext.cs:107-108). The remaining `new SKPaint` sites in
  `SkiaPainter` (:305-366) are per **shape**, not per fragment — low value.
- **Skia PNG encode** goes straight from the page pixmap (src/Morph.Skia/SkiaPainter.cs:33-34); no full-page
  copy.
- **PDF determinism post-processing** patches the byte buffer in place (PdfRenderer.cs:88-101) instead of
  round-tripping the file through a Latin1 string and two regex passes.
- **Exporters.** `CoalesceRuns` absorbs each mergeable group in one forward scan
  (src/Morph/Export/DocumentExportHelpers.cs:229); the Markdown image path skips `EscapeUrl` for
  self-generated data URIs (src/Morph/Export/MarkdownExporter.cs:728-732).
- **`HtmlParser`** holds one static AngleSharp parser (src/Morph/Html/HtmlParser.cs:38) and traverses
  structural rows directly rather than via `QuerySelectorAll("tr")` (:1338, which also fixed nested-table
  rows being parsed into the outer table).
- **Parser resolution caches.** Styles, numbering, theme colours/fonts and table-style borders resolve once
  per document; image part bytes are shared per part, so elements referencing the same image hold the same
  array — which is what makes the identity-keyed image caches above dedupe across elements.
- **Single-pass drivers.** Top-level converters parse once and lay out once; `PdfRenderer` no longer runs a
  NUMPAGES pre-count pass (the engine reports its own page total).
- **Deliberate one-time costs.** `FontResolver`'s static host-font index (built once per generic
  instantiation, even in `FontDirectory` mode) and `StrictToTransitional.Normalize`'s extra ZipArchive open
  per parse.

## Suggested order

**L4** and **L5** are independent of each other and of everything landed. Both are self-contained; neither
needs the layout work that preceded them.
