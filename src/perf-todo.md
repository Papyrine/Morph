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

Every item of it had landed except the optional part of P4c and the PDF page-range item, restated as L5
(since landed) and L4. The caches the landed items added live on the render contexts rather than on the deleted
renderers, so they survived the deletion intact — they are listed under "Verified fine" and must not be
re-chased.

What the engine did reintroduce, in one shared place, is the measure-then-place duplication that the raster
backends had solved with `pagedLayoutCache` / `boundedLayoutCache`. Neither name existed in the tree any
more, and `CanonicalParagraphMeasurer` had replaced them with nothing — for all three backends at once
rather than one at a time. All three answers to it are landed — the wrap memo, the widths-only longest-token
measure, and the table-geometry memo (see "Verified fine"). What remains open is unrelated to layout.

## Measurement

`src/Benchmarks/` (BenchmarkDotNet, MemoryDiagnoser enabled) has `ParseBenchmarks`, `RenderBenchmarks`,
`PdfBenchmarks`, `ExportBenchmarks`, `HtmlBenchmarks`, `InkBenchmarks` and `EditBenchmarks` (the
`MorphEditor` round trip, stage by stage), plus the `-- smoke` sanity
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
needs a page-range case.

---

## L4 — PDF `options.Pages` renders the whole document, then deletes the unwanted pages

`PdfRenderer.Render` (src/Morph.Pdf/PdfRenderer.cs:15-46): the fragmenter lays out the whole document and
`PdfPainter.Paint` draws every page — including font subsetting and image embedding — after which
`TrimPages` (:53) removes everything outside the range. Requesting page 1 of a 500-page DOCX pays for 500.

Layout genuinely has to reach the end, because NUMPAGES resolves against the document total (the engine takes
it from `LaidOutDocument.Pages.Count`, which is why the second counting pass the deleted path ran is gone —
see the comment at :8-14). Painting does not.

**Fix:** thread the range into `PdfPainter.Paint` and skip the draw for out-of-range pages.

---

## Editor round trip (`EditBenchmarks`, 2026-10-20)

Every edit that changes the pages goes file → parse → layout → reload (`MorphViewer.ShowAsync` →
`MorphViewer.Open`), so committing one typed character pays for the whole chain. `EditBenchmarks` times
each stage on three documents; desktop CoreCLR, `--job short` — the WebAssembly runtime is several times
slower, so read these as proportions:

| stage | letter (1p) | twelve-pages | newsletter (2.8MB) |
| --- | ---: | ---: | ---: |
| `Rewrite` (edit + save) | 2.5ms | 3.7ms | **71ms** |
| `Format` (bold a word) | 3.1ms | 3.6ms | 78ms |
| `SetTracking` (no body edit) | 3.1 → 0.6ms | 3.0 → 0.1ms | **69 → 3.0ms** |
| `Open` (parse + layout) | 12.5ms | 9.4ms | 49ms |
| `Outline` | 6.6ms | 4.2ms | 22ms |
| `Review` | 4.4ms | 3.5ms | 21ms |
| `ReviewAndOutline` (both, one open — what the viewer now runs) | 3.9ms | 2.8ms | 22ms |
| `TextLayers` (over `Open`) | ~1ms | small | small |
| `EditMap` | 5µs | 36µs | 11µs |
| `Diff` | <1µs | <1µs | <1µs |
| **`RoundTrip`** | **13ms** | **20ms** | **236ms** |

`EditMap`, `SourceIndex`, the text layers and `TextDiff` are not worth touching. What is:

### E3 — paragraph lookup is linear per paragraph

`Editing.Find` is `AllParagraphs().ElementAtOrDefault(ordinal)`, and `Align`/`SetStyle`/`Indent`/…
call it per selected paragraph, so a whole-document command is O(k·n). `Between` and `Atoms` re-index
every run of every story (`SourceRuns.IndexAll`) per call. Invisible at a one-word selection; worth an
ordinal list built once per `Editing` if select-all commands on long documents show up.

---

## Verified fine — do not re-chase

- **Review and outline read from one package open** (`DocumentReading.Read`, landed 2026-10-20). The viewer
  read `DocumentReview` and `DocumentOutline` after every edit, each opening the package, normalizing Strict
  markup and loading every part. One open now serves both and costs what `Outline` alone did, so the
  `Review` read is gone: letter 6.5→3.9ms, twelve-pages 4.7→2.8ms, newsletter 45.5→22ms, and newsletter
  allocation 26.6→14.1MB (`EditBenchmarks`, default job). If the shared read throws, each is read on its
  own as before, so a file readable for one still gets it.
- **…and from the edit's own package** (`DocumentReading.Edit` / `Capture`, landed 2026-10-20). Every
  editor and `ReviewEditor` opens through `DocumentReading.Edit`; under `Capture` (only `ShowAsync` asks)
  it saves explicitly, reads review and outline off the open DOM with autosave off — so the reading cannot
  write back the parts it loaded — and remembers them against the returned array, so the reload's read is a
  lookup. Capture costs +1.9 / +0.6 / +4.8ms (letter / twelve-pages / newsletter) against the 3.5 / 2.8 /
  20.5ms open it replaces. Part contents are identical with and without capture (only the zip container
  bytes differ); `EditCorpusTests.EveryDocument_ReadsTheSameCapturedAsAfresh` checks that and the reading
  over the whole corpus. Round trip now (default job): letter 8.1ms, twelve-pages 10.0ms, newsletter 94ms.
- **`SetTracking` leaves the body unloaded** (landed 2026-10-20). It checked the main part through
  `MainPart`, whose `Document` check loads the body DOM — which the save then re-serialized and
  re-deflated. Newsletter 69 → 3.0ms, letter 3.1 → 0.6ms, twelve-pages 3.0 → 0.1ms. Under the viewer's
  `Capture` the body is still read for the outline, but no longer written.
- **An edit writes the parts it changed at Fastest** (`FastDeflate`, landed 2026-10-20, was E2). Probed on
  newsletters/03, a 2.8MB file that is mostly its body: 2.6MB of XML, ~2.5MB of it in two VML shapes'
  attributes. Media is irrelevant — untouched entries are copied raw, so a save with no DOM loaded is ~4ms.
  A body edit is ~20ms loading the DOM and ~57ms saving it, and of the save ~4ms is serialization and the
  rest Optimal deflate. The SDK gives an existing part no compression option (`CompressionOption` reaches
  only new parts), but `ZipArchive` since .NET 9 rewrites an entry at the level its general-purpose flags
  declare, so `DocumentReading.Edit` marks its copy's entries "fast" before opening it — two bytes per
  entry, and the SDK's own save does the rest. `EditBenchmarks`, default job: newsletter `Rewrite`
  70.6→39.0ms, `Format` 66.5→37.8ms, `RoundTrip` 94.2→65.6ms; letter `RoundTrip` 8.25→7.9ms; twelve-pages
  flat. The newsletter's file grows 2.7→3.3MB (its body deflates to 2.4MB rather than 1.85MB); ordinary XML
  barely does (the letter's file 29→30KB). `EditCorpusTests` checks part contents are unchanged by the
  marking over the corpus, and `AnEdit_WritesTheBodyAtFastest` that the runtime still honours the flags.
  Deflate per level and strategy, newsletter body / complex_tables body: Optimal 73ms 1851KB / 2KB,
  Fastest 27ms 2423KB / 3KB, Huffman-only 14ms 1982KB / 29KB, stored 1.5ms 2607KB / 47KB — Huffman-only
  wins only on this body's high-entropy attributes, and is ten times worse on ordinary XML.
  **What is left** is the ~25ms Fastest still costs on such a body. The flags cannot ask for stored (the
  "super fast" value maps to Fastest too, "maximum" to SmallestSize), so it means writing the changed parts
  into the zip beneath the SDK (`OpenXmlPart.IsRootElementLoaded` / `UnloadRootElement` are public, so
  serialize, unload, let the SDK save structure, then replace those entries), and either a file a third
  larger or a repack on the way out (download, `OnSave`). Not worth it unless bodies like this are common.
- **One walk of the body for what the parser reads beside its blocks** (`BodyScan`, landed 2026-10-20, was
  L5). Section breaks, fields, tracked changes, comment and bookmark anchors, embedded objects, anchors and
  the feature flags were a dozen whole-body walks; one untyped walk switching on type gathers them all. The
  prize was small and is what was measured: `ParseBenchmarks`, default job, Small 3.31→3.07ms,
  ComplexTables 3.62→3.25ms, RepeatedImage 2.79→2.66ms, Watermark and Toc within noise, and Large (the
  newsletter) flat at ~21ms across three back-to-back runs — its parse is the 2.7MB DOM load, not the walks.
  Do not chase walks further: on a loaded body one walk is 0.03–0.2ms.

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

**L4** is independent of everything landed and self-contained. **E3** waits on select-all commands over long
documents showing up.
