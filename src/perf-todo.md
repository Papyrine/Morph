# Performance TODO

## Status (2026-07-04 implementation pass)

Implemented and verified (scenario suite green, zero baseline changes): **P1a–P1e, P2 (+all
satellites), P3, P4a/P4b/P4c/P4d/P4f, P5a/P5b/P5c/P5d, P5e (ImageSharp space-width/TextOptions
memo), P6a–P6g** (P6g safe subset). Render benchmarks, PDF benchmarks, export benchmarks and the
`smoke` harness were added under `src/Benchmarks/` per the Measurement section.

Deferred, in scope for a follow-up pass:
- **P4c (optional part)** — folding the remaining single-purpose extractor walks into one
  dispatch walk.
- **P5e (Tight/Through outline wrap)** — rectangular extent approximation stands; a true
  outline wrap is a layout feature, tracked in the feature matrix.

Completed in the 2026-07-05 follow-up passes (baselines regenerated where output changed):
- **P4e** — ParseRunProperties/ParseParagraphProperties collect their children in one
  dispatch pass into locals (first-wins preserved with `??=`); ~35/~20 sibling rescans gone.
  Byte-identical output (suite-verified).
- **P5d (width-alignment part)** — numbered cell paragraphs are now measured at exactly the
  width the cell render lays out at; the phantom 12pt marker inset inflated row heights and
  split the layout cache (affected: agendas-minutes/07+17, resumes/13, business-plans/02+15,
  cover-letters/09).
- **P5e** — vMerge span/height lookups now walk a per-table occupancy map (one O(R·C) pass,
  weakly keyed) instead of rescanning rows per Restart cell; the autofit minimum-width probe
  bypasses the layout caches instead of retaining a line per word;
  `AdvanceToBackgroundsTargetPage` reads a precomputed suffix of next-element height
  estimates instead of rescanning the document tail per background (provably identical —
  the estimate is position-independent).
- **P6g** — `QuerySelectorAll("tr")` replaced with direct structural-row traversal, so nested
  tables' rows are no longer parsed into the outer table as well.
- **HTML headings clear floats** — `h1-h6 { clear: both }` in the exporter stylesheet;
  every html snapshot regenerated (the embedded stylesheet is part of each).

Discovered while validating (not perf): the Skia color matrix passed biases in 0..255 scale
where SkiaSharp expects 0..1 (saturating channels to white); the Washout recolor preset kept a
corrected 0..1 matrix. Separately, Word's own reference exports render picture watermarks as
nothing at all (zero pixel trace on business-plans/04/06/07/08, even over coloured pages), so
both raster backends now skip drawing picture watermarks entirely — ImageSharp previously drew
a visible wash and its four baselines were regenerated, moving those scenarios sharply closer
to the Word reference (e.g. 04 page 1: 0.9965 → 0.1382 error). Skia's committed baselines
already matched the no-watermark output. Text watermarks still render. The PDF backend renders
no watermarks at all.

---

Findings from a full perf review (2026-07-04) of the parser, shared layout core, Skia/ImageSharp
backends, PDF backend, and text exporters. Every HIGH/MEDIUM item was verified by reading the code
at the cited location, including at least one call chain from a public converter entry point.
Line numbers are from commit `23b3f9370` — they will drift, so each item also names the
method/member to search for.

None of these change rendered output. The Verify scenario suite (`./scripts/test.sh`) is the
correctness gate: after each item, baselines must pass **unchanged** (a pure perf change that
shifts a pixel is a bug).

## Measurement

`src/Benchmarks/` (BenchmarkDotNet, MemoryDiagnoser enabled) has `ParseBenchmarks`
(`Parse_Small/ComplexTables/Watermark/Large`), `HtmlBenchmarks`, `InkBenchmarks` — parser items
below are measurable there today.

**There is no render benchmark.** Before starting the render-side items, add
`RenderBenchmarks`: multi-page DOCX with a header logo (raster + SVG variants), a picture
watermark, and a few autofit tables; benchmark `SkiaDocumentConverter`, `ImageSharpDocumentConverter`,
and the PDF converter end-to-end. Items P1/P2/P3 should each show up as an integer-factor or
double-digit-% change on it.

---

## P1 — Decoded images are not cached: re-decoded per draw, per page (all 3 backends)

The single biggest win. No backend caches decoded images, so a header/footer logo or picture
watermark is decoded from `byte[]` on **every page**, and each repeated body image on every
occurrence. Cost is O(pages × decode+resize) and should be O(1).

Common hot chain (all backends): `StartNewPage`/`FinishCurrentPage` →
`PageRendererBase.RenderHeader` (src/Morph/Rendering/PageRendererBase.cs:393) →
`RenderHeaderFooterElements` → `RenderFloatingImage` / header paragraph → inline image draw.
Runs once per page for every image in a header/footer/watermark.

### P1a — Skia: `DecodeBitmap` per draw

- `src/Morph.Skia/SkiaPageRenderer.cs:15-20` — `DecodeBitmap(byte[])` =
  `SKData.CreateCopy(data)` (full byte copy) + `SKCodec.Create` + `SKBitmap.Decode`, per call.
  Callers: `DrawBlockImage` (:531), `RenderImageInCell` (:1702), `DrawPictureWatermark` (:2089),
  `RenderBackgroundShape` (:2278).
- `src/Morph.Skia/TextRenderer.cs:1529-1533` — `RenderInlineImage` repeats the same pipeline per
  occurrence.
- Watermark extra: `DrawPictureWatermark` also does `SKImage.FromBitmap(bitmap)` (:2156) — a second
  full-pixel copy — every page.

**Fix:** cache decoded `SKBitmap` (and for the watermark, the final `SKImage`) on
`SkiaRenderContext`, keyed by `byte[]` reference identity; dispose with the context.
Reference identity is sufficient for the per-page case: header/watermark elements are the same
parsed object every page, holding the same array (verified: parser materializes one `byte[]` per
element, `DocumentParser.cs:455-466`). See P4f for making identity work across elements too.

### P1b — Skia: SVG re-preprocessed (regex), re-parsed, re-rasterized per occurrence/page

- `src/Morph.Skia/SkiaPageRenderer.cs:593-649` — `RenderSvgImage`:
  `SvgPreprocessor.StripStyleAndClass` (UTF8 decode → two regex passes over the whole SVG text →
  re-encode; src/Morph.Skia/SvgPreprocessor.cs:14-20), then `new SKSvg()` + `svg.Load` (XML parse +
  scene build), then `new SKBitmap(destSize)` + `DrawPicture` rasterization. `DrawBlockImage` routes
  SVG here (:525-528), so an SVG header logo pays all of it per page.
- `src/Morph.Skia/TextRenderer.cs:1498-1524` — inline SVG, identical pipeline per occurrence.

**Fix:** cache the loaded `SKPicture` keyed by `byte[]` identity (preprocess + parse once);
optionally also the rasterized `SKBitmap` keyed by `(bytes, destW, destH)` — dest size is stable
per element.

### P1c — ImageSharp: `Image.Load` + `Resize` per draw, decoded image disposed at page end

- `src/Morph.ImageSharp/ImageSharpPageRenderer.cs:451-463` — `DrawBlockImage`:
  `Image.Load<Rgba32>(imageData)` + `Mutate(Crop)` + `Mutate(Resize)` (full bicubic resample) per
  call. Also `RenderImageInCell` (:1471-1474), `RenderBackgroundShape` (:1835-1838).
- `src/Morph.ImageSharp/TextRenderer.cs:1440-1465` — `RenderInlineImage`, same per occurrence.
- `src/Morph.ImageSharp/ImageSharpRenderContext.cs:295-307` — `RetainForPage` /
  `DisposePendingPageDisposables`: decoded images are disposed when the page finishes, which
  *guarantees* re-decode on the next page. (The retain mechanism exists because `DrawingCanvas`
  queues an `ImageBrush` holding the image until canvas dispose — the cache must respect that:
  retained images move to document lifetime, disposed in context `Dispose()`.)

**Fix:** document-lifetime cache on `ImageSharpRenderContext` keyed
`(byte[] identity, targetW, targetH, crop, colorEffect)` → processed `Image<Rgba32>`. Cache the
*post-resize* image — the resize is the expensive part and dest size is stable per element.

### P1d — ImageSharp: picture watermark re-processed per page with a managed per-pixel loop

- `src/Morph.ImageSharp/ImageSharpPageRenderer.cs:1651-1714` — `DrawPictureWatermark`, called from
  `DrawWatermarks` (:1631) ← `StartNewPage` (:1574). Per page: decode + `ProcessPixelRows` scalar
  loop (4 × `Math.Clamp` + `Rgba32` reconstruct per pixel, :1668-1683) + `Mutate(Resize)` to page
  size. Gain/bias/alphaScale are constant for the document — the result is byte-identical every
  page.

**Fix:** compute the processed+resized watermark image once, keep it on the renderer/context for
document lifetime, `DrawImage` per page. (That makes the per-pixel loop a one-time cost; converting
it to ImageSharp bulk ops is optional after that.)

### P1e — PDF: `XImage.FromStream` per draw → same image re-embedded per page, output grows O(pages)

- `src/Morph.Pdf/PdfPageRenderer.cs:755-772` — `DrawRaster`:
  `XImage.FromStream(new MemoryStream(data))` per call. PdfSharp dedupes embedded image XObjects
  per `XImage` *instance* only — a fresh instance from the same bytes is decoded again **and
  embedded again in the output PDF**. Header logo on a 200-page doc = 200 decodes + 200 embedded
  copies (file size, not only CPU). The `XImage` is also never disposed.
- `src/Morph.Pdf/PdfTextEngine.cs:296-313` — `DrawImage` (inline images), same pattern.

**Fix:** cache `XImage` per `byte[]` reference on `PdfRenderContext`; dispose all at end of render.
Expect output PDFs with repeated images to shrink — PDF snapshot baselines (`pdf_result.verified.pdf`
etc.) **will** legitimately change for this one item; regenerate via
`./scripts/regenerate-baselines.sh` in its own commit.

---

## P2 — PDF backend has no paragraph-layout cache (table cells laid out ~5×)

- `src/Morph.Pdf/PdfTextEngine.cs` — implements `IParagraphMeasurer` with **no memoization**.
  Every public entry re-runs `Layout(paragraph, maxWidth)` (:414) from scratch:
  `LayoutParagraphForMeasurement` (:16), `MeasureParagraphNaturalWidth` (:33),
  `MeasureParagraphHeightWithWidth`/`MeasureHeight` (:44-47), `Draw` (:116-118).
  `Layout` tokenizes and calls `measure.MeasureString()` per word token (:612) and per whitespace
  gap (:606), allocating a `LineItem` per word.
- A table-cell paragraph is laid out ~5×: autofit natural width at `float.MaxValue/4`
  (src/Morph/Rendering/TableLayout.cs:425), autofit min width at `1f` (:428 — worst case, breaks at
  every word), row height (src/Morph/Rendering/TableHeightCalculator.cs:265), vertical-align
  measure (src/Morph/Rendering/PageRendererBase.cs:1209/1219), then the draw
  (src/Morph.Pdf/PdfPageRenderer.cs:359 → `RenderInBounds`). Positioned frames get 3×
  (PageRendererBase.cs:500-501 + render).
- The Skia and ImageSharp backends solved exactly this with `pagedLayoutCache` +
  `boundedLayoutCache` keyed `(ParagraphElement, float width)` — see
  `src/Morph.Skia/TextRenderer.cs:7-14` (the comment documents the rationale) and
  `src/Morph.ImageSharp/TextRenderer.cs:8-9`. Copy that design.

**Fix:** `Dictionary<(ParagraphElement, double), List<Line>>` on `PdfTextEngine`, consulted at the
top of `Layout`. Lifetime = the engine instance = one document render, same as Skia.

Satellite PDF items (fix alongside, same file):
- **Space width re-measured per whitespace token** — `PdfTextEngine.cs:606`
  `measure.MeasureString(" ", font).Width`: constant per font; cache per `XFont` (e.g. in the
  existing font cache entry).
- **Brush/pen churn** — `new XSolidBrush(ParseColor(...))` per drawn word (:235), `new XPen` per
  underline/strike item (:240-249), `EdgePen` allocates an `XPen` per border edge per cell
  (src/Morph.Pdf/PdfPageRenderer.cs:326-327). Cache by color key on `PdfRenderContext`.
- **Leader glyph measured per tab-leader draw** — `PdfTextEngine.cs:281`.

---

## P3 — `FontResolver.Resolve` does ~50–70 string suffix scans before its cache lookup, per fragment

- `src/Morph/Fonts/FontResolver.cs:124-133` — `Resolve(fontFamily, bold, italic)` computes
  `FontHelpers.GetCandidateNames(...)` + `FontHelpers.ResolveTargetWeight(...)` *before* probing its
  `(candidates.Effective, targetWeight, italic)` cache — so a 100% hit rate still pays full price.
- The cost lives in `src/Morph/Fonts/FontHelpers.cs`: `GetCandidateNames` (:192) →
  `StripWeightSuffixes` (:150) do-while over 29 suffixes (≥29 `EndsWith(OrdinalIgnoreCase)`);
  `ResolveTargetWeight` (:92) → `InferWeightFromName` (:75) up to 18 more.
- Hot chain: `TextRenderer.RenderFragment` → `context.CreateFont` per **fragment** (a single
  word/whitespace token) — src/Morph.Skia/TextRenderer.cs:1065 and :1699,
  src/Morph.ImageSharp/TextRenderer.cs:1028 and :1617. Fragment draws are not layout-cached and
  headers redraw every page → 10⁵–10⁶ calls on a text-dense doc.
- Backends stack more pre-cache work on top: Skia `GetTypeface`/`CreateFont`
  (src/Morph.Skia/SkiaRenderContext.cs:100-122) calls `ShouldSyntheticallyEmbolden` →
  `ResolveTargetWeight` again (:153-157); ImageSharp `GetFont`
  (src/Morph.ImageSharp/ImageSharpRenderContext.cs:122-151) runs `FontHelpers.ImpliesBold`
  (6 × `Contains(OrdinalIgnoreCase)`) + `PickAvailableStyle` (:170-197, `TryGetMetrics` probes)
  before its `fontCache` lookup at :153.

**Fix:** first-level memo in `Resolve` keyed on the *raw* request `(fontFamily, bold, italic)` →
`TFont`; compute candidates/weight only on miss. Then key the backends' derived per-call decisions
(embolden flag, style pick) off the same raw tuple so the per-fragment path is one dictionary hit.

---

## P4 — Parser hot loops (`src/Morph/OpenXml/Parsing/DocumentParser.cs`)

Measurable with the existing `ParseBenchmarks`.

### P4a — Per-run linear scan of styles.xml (HIGH)

`ParseRunProperties`, :8113-8120: for every run carrying `w:rStyle` (hyperlinks, TOC entries,
footnote/endnote refs — ubiquitous), does
`stylesPart?.Styles?.Elements<Style>().FirstOrDefault(_ => _.StyleId?.Value == runStyleId)` — a
linear scan of every style in styles.xml (100–400 in real templates) to find which properties the
character style *explicitly* defines, followed by ~12 `GetFirstChild<X>()` pair-probes. O(runs ×
styles). Note the resolved properties themselves ARE cached (`styleRunProperties`) — only this
explicitness check goes back to raw XML.
**Fix:** during `ExtractStyleRunProperties` (one-time pass, :950) also build
`Dictionary<string, Style>` by styleId — or better, a per-style bitmask/flags struct of
explicitly-defined properties — and look that up.

### P4b — Hyperlink relationship linear scan (O(links²))

`ResolveHyperlinkUrl`, :7774-7789: `foreach` over `mainPart.HyperlinkRelationships` per
`w:hyperlink` (called from `ParseParagraph` :4369). `HyperlinkRelationships` is an `OfType<>`
filter over the part's full relationship list, re-enumerated per call → quadratic on link-heavy
docs (TOCs, bibliographies).
**Fix:** lazily build `Dictionary<string relId, string url>` per parse instance on first use.

### P4c — ~12–15 separate whole-body `Descendants` walks in `ParseDocument` (:137-247)

Each an independent full-DOM walk:
- :145 `Descendants<LastRenderedPageBreak>().Count()`
- :173 `Descendants<SectionProperties>().ToList()` → `sectionPropsList`
- :217-234 `ExtractHeaderFooter` called 2–6×, and **each call re-runs
  `body.Descendants<SectionProperties>()` from scratch** (:3022) even though `sectionPropsList`
  already exists → pass it in (low-effort first step)
- :237 `ExtractBookmarks` builds a paragraph-ordinal map over all `Descendants<Paragraph>`
  **even when there are zero bookmarks** (:872-880) → enumerate `BookmarkStart` first, bail if none
- :238 `ExtractComments` rebuilds the *same* paragraph-ordinal map (:816-821) → share it
- :239 `ExtractTrackedChanges` (2 walks), :241 `ExtractFieldCodes` — a complete second pass over
  every run in the document (:661-708), :244 `ExtractEmbeddedObjects`, :246
  `DetectAdvancedFeatures` (:289 — its comment shows two walks were already merged once; the same
  consolidation applies across all these extractors).

**Fix order:** (a) thread `sectionPropsList` into `ExtractHeaderFooter`; (b) gate/share the
ordinal maps; (c) optionally fold the remaining extractors into one dispatch walk.

### P4d — 3 redundant subtree scans per run in `ParseParagraph` (the hottest loop)

`case OoxmlRun run:` block: `foreach (var drawing in run.Descendants<Drawing>())` (:4424), then
`if (!run.Descendants<Drawing>().Any())` (:4649) — same subtree again — then
`run.Descendants<Break>().All(...)` (:4654-4656) for every text-bearing run. Same
double-`Descendants<Drawing>` copy-pasted in the SdtRun path (:4212, :4233). Each walk allocates an
iterator and traverses the whole run subtree even when empty (the common case).
**Fix:** one up-front pass over `run.ChildElements` collecting `hasDrawing`,
`hasPageOrColumnBreak`, and the drawings (drawings sit directly under `w:r` or under
`mc:AlternateContent`); branch on flags.

### P4e — ~35 sequential `GetFirstChild<T>()` scans per `rPr` (and ~20 per `pPr`)

`ParseRunProperties` :7945-8240 and `ParseParagraphProperties` :7418-7658. Each `GetFirstChild<T>`
restarts at `FirstChild` and walks siblings; a typical `rPr` has 2–6 children, so this is ~10–30×
more type tests than one pass needs, per run/paragraph with inline props.
**Fix:** single `foreach (child in props.ChildElements)` + type `switch` into locals — the same
shape `ParseRun`'s child loop and `DetectAdvancedFeatures` already use.

### P4f — Image part bytes duplicated per reference

`TryParseInlineImageRun` (:4975-5006), `ParseDrawingElements` (:5527-5573), `ParsePictureWatermark`
(:441-471), `ShapeParser.ExtractBlipFillImage` (src/Morph/OpenXml/Parsing/ShapeParser.cs:793-824):
every drawing referencing an `ImagePart` does `GetStream()` → `CopyTo(MemoryStream)` → `ToArray()`
— decompressed and buffered per *reference*. Repeated logos/icons get N copies retained in the
model.
**Fix:** per-parse `Dictionary<OpenXmlPart, byte[]>` shared with `ShapeParser`. Bonus: elements
sharing an image then hold the *same* array instance, which makes the P1 byte-identity caches
dedupe across elements for free.

---

## P5 — Shared layout core

### P5a — Post-tab text measured unconditionally (then re-measured by the layout loop)

Every backend measures the entire text after a tab before calling `TabStopResolver.Resolve`, but
`followingWidth` is only read in the Center/Right/Decimal arms
(src/Morph/Rendering/TabStopResolver.cs:82-87). Default/Left tabs — the most common — never use it,
and the same text is then measured again word-by-word by the main loop. TOC/form paragraphs pay
~2× layout; 3× with decimal stops (PDF has no layout cache, so it pays this fully).
Call sites: src/Morph.Skia/TextRenderer.cs:1601 (`MeasureFollowingWidthScaled`) and :589;
src/Morph.ImageSharp/TextRenderer.cs:1519 and :563; src/Morph.Pdf/PdfTextEngine.cs:536.
The template for the fix is two lines up at each site: `decimalPrefix` is already gated on
`props.HasDecimalTabStop()`.
**Fix:** gate on `tabStops` containing a Right/Center/Decimal stop, or make the parameter a
`Func<double>` evaluated lazily inside `Resolve`.

### P5b — Top-aligned cells measure content height then discard it

`PageRendererBase.RenderTableCell`, src/Morph/Rendering/PageRendererBase.cs:1196-1258: the
`contentHeight` loop (:1203-1234) measures every paragraph/content-control/image in the cell, but
the value feeds only the Center/Bottom arms (:1244-1248); `Top` — the default for the vast majority
of cells — discards it. Runs per cell per render, including header rows re-emitted every page
(:1083-1093). With the raster layout caches each call is cheapish; for content controls (:1213-1221)
it's a full cold layout (see P5d).
**Fix:** skip the loop when `cell.Properties.VerticalAlignment == Top` (the vMerge-Restart cap at
:1254 only applies to Center, so it stays covered).

### P5c — Table layout runs twice for "KeepNext heading + table"

All built-in Word heading styles set KeepNext. `RenderParagraph` → `MeasureElementHeight` →
`MeasureTableHeight` (src/Morph.Skia/SkiaPageRenderer.cs:1602-1615 called at :371;
src/Morph.ImageSharp/ImageSharpPageRenderer.cs:1252-1263 called at :320) runs the full
`GetColumnCount` + `CalculateColumnWidths` (incl. autofit content measurement) + `HasVerticalMerge`
+ `CalculateRowHeights`; `RenderTable` (src/Morph/Rendering/PageRendererBase.cs:819-823) then
recomputes all four. The paragraph layout caches absorb re-shaping, so the duplicate cost is the
O(cells) walk itself + vMerge scans + border resolution allocs.
**Fix:** per-render memo `Dictionary<(TableElement, float availableWidth), (float[] colWidths,
float[] rowHeights)>` shared by `MeasureTableHeight` and `RenderTable`.

### P5d — Synthetic `ParagraphElement` wrappers defeat the identity-keyed layout caches

The raster layout caches key on `ParagraphElement` reference. Each pipeline stage wraps the same
`ContentControlElement.Runs` in a **new** `ParagraphElement`:
src/Morph/Rendering/TableHeightCalculator.cs:226-254, PageRendererBase.cs:1213-1221,
PageRendererBase.cs:767-796 (`RenderContentControlInCell`), src/Morph/Rendering/TableLayout.cs:410-417
(`MeasureCellContentWidth`). A content control in an autofit cell → 4–6 full cold layouts, zero
hits.
Related width mismatch: numbered/bulleted cell paragraphs are *measured* at `contentWidth - 12`
(TableHeightCalculator.cs:221/265, PageRendererBase.cs:1208-1209) but *rendered* at `contentWidth`
(PageRendererBase.cs:1266) → two cache keys → two layouts each.
**Fix:** cache one measurement wrapper per `ContentControlElement` (single shared instance across
stages, e.g. lazily-created field or `ConditionalWeakTable`); align measure width with render width
for numbered cell paragraphs.

### P5e — Smaller (do opportunistically)

- **vMerge span resolution is O(rows² × cells)** — `CalculateVerticalMergeHeight` /
  `CalculateVerticalMergeRowSpan`, src/Morph/Rendering/TableLayout.cs:444-508; each Restart cell
  forward-scans subsequent rows, each row re-walked from cell 0. Callers:
  TableHeightCalculator.cs:110, PageRendererBase.cs:1037/1133. Fix: one O(R·C) grid-occupancy pass
  per table, then O(1) lookups. Only matters for large tables with heavy vMerge.
- **Autofit min-width measurement materializes a `TextLine` per word** —
  TableLayout.cs:424-428 calls `MeasureParagraphNaturalWidth(para, 1f)`, which greedy-wraps at every
  word (a `TextLine` + fragment list per word) and the result is retained in `boundedLayoutCache`
  for the rest of the render. Only a single max float is needed. Fix: dedicated
  longest-unbreakable-token measurement on `IParagraphMeasurer` that doesn't materialize lines or
  pollute the cache.
- **ImageSharp: every whitespace token goes through full shaping** — `SplitIntoWords` emits each
  space as its own word (src/Morph.ImageSharp/TextRenderer.cs:1946-1992), each measured via
  `TextMeasurer.MeasureAdvance` with a **fresh `TextOptions` per call**
  (src/Morph.ImageSharp/ImageSharpRenderContext.cs:228-238, call sites TextRenderer.cs:1669/:706).
  ~30–50% of measure calls are single spaces with a handful of distinct fonts. Fix: memoize space
  width per `(Font, KerningMode)`; cache the `TextOptions` per `(Font, KerningMode)`. (Skia's
  equivalent is a cheap native call — low value there.)
- **`AdvanceToBackgroundsTargetPage` scans to end-of-document per background element** —
  PageRendererBase.cs:689-753; paragraphs return estimated height 0 (:752) so in table-free docs
  every behind-text shape scans all remaining elements. Cap the lookahead.

---

## P6 — Backends & exporters, smaller

### P6a — Skia: full-page pixel copy per page at PNG encode

`FlushPendingPage`, src/Morph.Skia/SkiaPageRenderer.cs:1993-2004: `SKImage.FromBitmap(page)`
snapshots a copy of the page (~33 MB at Letter/300 DPI) before `Encode`, plus an intermediate
`SKData`. Fix: encode from the bitmap's pixels directly —
`using var pixmap = page.PeekPixels(); pixmap.Encode(...Png...)` into the callback stream (or the
`SKBitmap.Encode(SKWStream, ...)` extension). One large copy per page saved.

### P6b — Skia: per-fragment native `SKPaint` churn for underline/strike/background

src/Morph.Skia/TextRenderer.cs:1094-1112 (run background fill), :1195-1206 (underline),
:1208-1220 (strikethrough) — allocate+dispose a native `SKPaint` per decorated fragment (hyperlink-
and TOC-heavy docs). Also `DrawCellBackground` per cell (SkiaPageRenderer.cs:1757-1770) and
paragraph border `CreatePaint` per edge (TextRenderer.cs:376-415). The codebase already has the
pattern to copy: `SkiaRenderContext.GetReusableTextPaint` (src/Morph.Skia/SkiaRenderContext.cs:166-177)
— add reusable stroke/fill paints alongside it.

### P6c — `CoalesceRuns` quadratic merge

src/Morph/Export/DocumentExportHelpers.cs:196-223: per mergeable fragment does
`merged[^1] = new Run { Text = previous.Text + run.Text, ... }` — re-copies accumulated text and
allocates a new `Run` per fragment → O(k²·len) per merge group. Word fragments runs at
proofing/rsid boundaries (the method's own doc comment cites "Sep"+"tember"), 10–50 fragments per
paragraph is routine. Called per paragraph from both exporters (HtmlExporter.cs:982,
MarkdownExporter.cs:415).
**Fix:** scan forward to find the mergeable range, build the text once (`string.Concat` of the
slice / StringBuilder), emit one `Run` per group.

### P6d — `PdfRenderer.Normalize` round-trips the whole PDF through a Latin1 string

src/Morph.Pdf/PdfRenderer.cs:75-95, run unconditionally on every export (:30):
`stream.ToArray()` → `Encoding.Latin1.GetString` (2× bytes as UTF-16) → two compiled-regex
`Replace` passes over the entire text (subset tags + XMP UUIDs) → `GetBytes`. ~5× the file size in
transient allocations; the subset-tag pattern anchors on `/` so it probes constantly, including
inside compressed streams. Exists for deterministic snapshot output — behavior must be preserved.
**Fix:** both replacements are same-length (6-char tag → 6-char tag; UUID → fixed UUID), so patch
the byte buffer in place: search `stream.GetBuffer()` for the ASCII literals `/BaseFont/`,
`/FontName/`, `uuid:` and overwrite. Zero reallocation, single pass.

### P6e — `options.Pages` trims after rendering the entire document

src/Morph.Pdf/PdfRenderer.cs:22-30 + `TrimPages` (:37-50): requesting page 1 of a 500-page DOCX
pays full layout + drawing + font/image embedding for all 500, then deletes 499. Layout is strictly
forward, so rendering can stop once `pagesAdded > range.End` (pages before `range.Start` still need
layout, but can skip drawing). Thread the range into `PdfPageRenderer`.

### P6f — Exporter string churn (GC pressure, no algorithmic change)

- **Markdown base64 images copied ~7×** — src/Morph/Export/MarkdownExporter.cs:620-636 (`Image()`),
  :873-876 (`EscapeUrl` — 3 full scans of the data URI; base64 never contains ` ( )`), :430-445
  (inline builder), :27 (`Finish`). A 5 MB image ⇒ ~90 MB of UTF-16 churn. Fix: append the
  `![](data:...)` pieces directly into the destination StringBuilder; skip `EscapeUrl` for
  self-generated data URIs.
- **Whole output duplicated ~3× at the end** — HtmlExporter.cs:58/65/105-107 (`writer.ToString()` →
  append into second builder → `doc.ToString()`; `DominantFont` is computed *before* writing at :54,
  so the prelude can be emitted into the same builder up front). MarkdownExporter.cs:27
  (`builder.ToString().TrimEnd('\n') + "\n"` — trim the StringBuilder in place instead).
- **Per-run tag assembly** — HtmlExporter.cs:1061-1114 (`FormattingTags`: 2 StringBuilders +
  `Insert(0, ...)` per tag + 2 `ToString()` per visible run), :1047-1059 (`AppendEncodedWithBreaks`
  always calls `text.Split('\n')` — guard with `IndexOf('\n') < 0` fast path);
  MarkdownExporter.cs:499-537 (`Decorate`: same pattern, and :535 copies the escaped text even with
  no formatting). Fix: append open tags directly, track close tags in a small stack; append
  prefix/text/suffix into the caller's builder.

### P6g — HtmlParser small items

src/Morph/Html/HtmlParser.cs — low impact (HTML inputs are typically small AltChunks/fragments):
- :936-946 each cell's `style` attribute parsed 3× (`ParseCssSpacing` padding, margin, then
  explicit `ParseStyleAttribute`); table style 2× (:881-896). Parse once, pass the dictionary.
- :89/:377/:1182 `TagName.ToLowerInvariant()` allocates per node — `LocalName` is already lowercase.
- :24 `new AngleSharp.Html.Parser.HtmlParser()` per `Parse` call (also per AltChunk,
  DocumentParser.cs:3150) — could be a shared static.
- :901 `QuerySelectorAll("tr")` per table also matches nested tables' rows (perf + correctness
  quirk); iterate `Children` of table/thead/tbody instead.

---

## Verified fine — do not re-chase

- Parser: style/numbering/theme resolution all cached once per document (`styleRunProperties`,
  `styleParagraphProperties`, `numberingDefinitions`, `styleNumbering`, `tableStyleBorders`,
  theme colors/fonts). P4a is the only per-element leak back to raw XML.
- Raster backends: paragraph measure-then-render double work is already memoized
  (`pagedLayoutCache`/`boundedLayoutCache`); table height-calc, autofit, and cell vertical-align
  measures hit those caches when `cellSpacing == 0` (the common case). Font/typeface lookups cached
  per context; ImageSharp brush/pen caches used on hot paths; Skia dominant text fill paint reused.
- `OpenTypeReader` parses each font file once at index build; PdfSharp memoizes font
  resolution/embedding process-wide + `PdfRenderContext.fontCache` per (family,bold,italic,size) —
  fonts are NOT re-read per run/page.
- Top-level drivers parse once and render in a single forward pass — no per-page re-parse.
- PNG encode settings are fine in both backends (zlib level 6 equivalents).
- `ConvertToImageData`'s per-page `ms.ToArray()` is required by the `byte[]` API contract.
- One-time costs deliberately excluded: `FontResolver`'s static `allFontsCache` eagerly indexes
  host fonts even in `FontDirectory` mode (once per generic instantiation — Skia and ImageSharp
  each build one); `StrictToTransitional.Normalize`'s extra ZipArchive open per parse.

## Suggested order

1. **P1** image caches (per backend, independently landable; P1e changes PDF snapshots — own
   commit + baseline regen)
2. **P2** PDF layout cache (+ satellites)
3. **P3** font-resolver memo
4. **P4a/P4b** parser dictionary fixes (small, high value), then P4c/P4d/P4e/P4f
5. **P5a/P5b** (both are near-one-line gates), then P5c/P5d
6. **P6** as cleanup passes

Add the render benchmark before 1–3; run `./scripts/test.sh` after each item and expect zero
baseline changes (except P1e, noted above).
