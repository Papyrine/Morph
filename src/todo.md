# Rendering fidelity todo

Deep comparison of every scenario in `src/Tests/Inputs/` (325 scenarios, 548 Word reference pages): `expected_*.png` (Word, 150 DPI, via RenderHelper) versus `skia_result#page_*.verified.png`, `imagesharp_result#page_*.verified.png`, `pdf_result#page_*.verified.png` (PDFium render), and `html_result.verified.png` (headless-browser screenshot of the HTML export).

Each finding: `severity | backends | pages | description`. `all` = skia+imagesharp+pdf. HTML findings ignore pagination/viewport-width reflow by design and only flag content/styling errors. Not reported: anti-aliasing texture, 1-2px subpixel shifts, ImageSharp's softer glyph rasterization. `[known]` = already documented as accepted in that scenario's notes.md. How a difference is judged — crops versus metrics, and what counts as noise — is `docs/fidelity-audit.md`.

**This file lists only what is still wrong.** A finding is deleted the moment it lands, and its durable knowledge moves to `docs/word-features.md` (feature behaviour and the evidence behind it), `docs/floating-art-pipeline.md` (anchored/floating art), `docs/html-import.md` (HTML and AltChunk input), `src/page_counts.md` (page-count experiment ledger) or `docs/fidelity-audit.md` (comparison method). Nothing that has shipped is described here — for how a fix was reached, read those docs and the git history. This is a temporary working document; it is expected to shrink to nothing.

**Open: 164 major, 335 medium, 255 minor = 754 findings across 253 scenarios.** Established by a full page-by-page re-audit on 2026-07-20 and maintained by deletion since; the 2026-07-21..23 fix batches were pruned as they landed. The tally is recounted from the `severity | backends | pages` lines below rather than hand-adjusted — it had read 807 against an actual 769 on 2026-08-08, having drifted as later batches were pruned without moving it, and 760 against an actual 753 on 2026-08-13 for the same reason. The 2026-08-13 recount also folds in the five `border_style_variants` findings added that day.

### Re-validation status (2026-08-08)

A partial re-measurement pass ran against the current baselines. **What it settled:**

- **Whole-page vertical alignment is essentially solved; block-local displacement is not.** Aligning each page's ink profile against Word's gives a median page offset of 2px. But a sliding-window local alignment (150px windows, ambiguous windows discarded) puts the median WORST local displacement at 11px and the p75 at 43px. So a finding describing the whole page drifting or compressing is usually stale, while one naming a specific block that sits N px off is usually still live. Findings are not yet individually pruned on this basis.

**What it did NOT settle — do not read an unpruned finding as re-validated.** The `missing`, `weight`, `wrap` and `colour` classes and all 267 HTML findings have no mechanical test here; a page-level chroma comparison was tried for `colour` and discarded as non-decisive (a duotone photo is too small a fraction of the page to move the page mean). Those need the crop-vetting loop in `docs/fidelity-audit.md`. Findings that predate the current baselines remain suspect where they describe vertical drift.

## Systemic issues (cross-scenario root causes)

These patterns repeat across many scenarios; fixing one clears whole families of the per-scenario findings below. **IDs are stable** — findings reference them by number — so a closed issue's number is retired rather than reused, and the list is deliberately gappy.

### All raster + PDF backends

- **#1 Page-number fields on long documents.** PAGE/NUMPAGES/SECTIONPAGES evaluate per page and section restarts work (`docs/word-features.md`). Still open: `business-plans/12`'s footer numbers need per-section headers/footers, which are unmodelled. HTML/Markdown keep the cached values by design.
- **#25 Word's row-split trigger — LANDED 2026-08-07 (third attempt), floor-fit CORRECTED the same day; measured residues below.** The full bundle: table-level fit routing (a table that does not fit the space left flows row by row, mirroring the whole-table move's slack exactly), the split-acceptance test (a first fragment offered only a region remainder must genuinely split — something continues AND the placed content fits — else the row moves whole), the vMerge tie rule (a continuation row never breaks from its predecessor; it stacks, overflowing and clipping, pinned by `CanonicalFragmenterTests.A_merge_continuation_row_stacks_rather_than_breaking`), the strict floor fit (below), and style-inherited `w:pageBreakBefore` with the inline `w:val="0"` override (the old parser comment claiming Word only honours it inline was refuted by probe). Probe evidence, mechanics and the failed attempts' history: `docs/word-features.md` (Multi-page Tables / Page Break Before) and git history. Corpus: page counts 325/325/325; the trigger landing measured 7.799 → 7.631 over 41 changed pages, and the floor-fit correction a further 12 better / 0 worse on `business-plans/13` (4.744 → 4.586, its split points now band-for-band Word's).

  **The floor-fit law flipped once within the day, both directions Word-measured.** A letters/04 in-situ reading ("Word keeps a floored row whose content fits the remainder, floor overflowing the margin") briefly landed a content-only fit; four controlled fixtures (`_probe_floorfit_single`/`_last`/`_mid`/`_enddoc`) then showed Word MOVING such a row in every structure, and the letters/04 keep dissolved into upstream height drift — Word's letter runs ~50pt more compact, so the floor simply fits its layout (the engine keeps the page count through the whole-table move's slack instead). Final law, pinned by `A_floored_row_whose_content_fits_but_floor_does_not_moves_whole`: **an atLeast floor participates in row break decisions and reserves STRICTLY against the hard content bottom** (business-plans/13's landscape pages break exactly where the 21.6pt-floored row's floor crosses the margin — box to 519.4, floor to 541.2, bottom 540 — a fact hidden until the pages were read at their true 612pt height; the "8-11pt short header block" recorded here was this extra packed row plus the landscape-scale inflation, and is RESOLVED); **content-driven rows keep HasSpaceFor's shared slack** (Word keeps business-plans/15's content-sized 79.6pt boundary table 13pt past the margin, drawn and clipped); **a row carrying any vertical merge — span head included — is exempt from the strict test** (a merge span is one drawn unit Word clips rather than moves; without the exemption resumes/06's span head at 1.1pt over re-split 3 pages into 6); and **repeated `w:tblHeader` rows do not cost the row beneath them its floor** (fixed 2026-08-12, pinned by `A_row_carried_under_repeated_headers_keeps_its_declared_height` — `PlaceSplitRow` read `atRegionTop` *after* the header loop had cleared it, so a row carried whole to a fresh region under re-emitted headers was sized by content: business-plans/13's first data row came out 11pt against its declared 21.6pt on pages 14, 16 and 20, shifting the eight rows below it 23px up the page at 150 DPI, and the fix took those pages 0.122/0.248/0.140 → 0.034/0.067/0.030 AE identically on all three backends). Still open, each measured:
  - The engine absorbs a trailing empty-PARAGRAPH row Word carries (`_probe_trail2_para`: AFTER ink at 75.36pt vs Word's 174.72) — and LibreOffice has no absorption rule at all, so `lastVisibleRow` may be scoped too wide. Unpicking it touches the rule that stops a letter template's spacer rows from starting a page; needs its own adjudication.
  - `FinishPage` drops the natural-overflow blank page Word renders (`_probe_trail2_flowblank`: 54 exact lines plus a trailing empty paragraph is 2 pages in Word, page 2 genuinely blank; the engine gives 1, and the code comment claiming Word does not render that page is refuted). LibreOffice corroborates: its `RemoveSuperfluous` only trims pages with no content frame, and an empty paragraph is one.
  - The whole-TABLE move still carries the 4% slack a floored single-row table should not get: `_probe_floorfit_enddoc` (30pt floor into a 24pt remainder, whole-table path) stays on the engine where Word moves it. **The letters/04 blocker on this is GONE (2026-08-08)** — the ~50pt of "upstream height drift" its one-page render depended on the slack to mask was the cell-measure contextual-spacing hole (`docs/word-features.md`, Contextual Spacing), now fixed, and that page is band-for-band Word. Making the whole-table path floor-strict can be retried on its own merits. resumes/06 carries a similar debt: its rows 0-9 accumulate ~13.6pt over Word (the two ~77.5pt content rows are the suspects), which is what pushed its span head onto the strict-fit knife edge in the first place.
  - Word's flow and cell genuinely differ under auto spacing (42 vs 41 lines against the same band, engine reproduces both) — a separate, uncharacterised difference in a cell's usable height.
  - Un-probed LibreOffice leads retained (hypotheses, not evidence): exact spacing hard-sets the ascent to 80% of the declared box (`itrform2.cxx:2403-2421`; the engine keeps the font's natural ratio — measurable); Writer charges a split row's trHeight floor once ACROSS fragments (follow minimum = declared − earlier fragments' heights, tabfrm.cxx:5123) where the engine floors only a whole row carried to a region top; Writer synthesizes split-edge rules by borrowing the neighbour row's border and suppresses them under repeated headlines (paintfrm.cxx:2858/2932) where the engine draws the fragment's own full box; `COLLAPSE_EMPTY_CELL_PARA` (a cell whose sole paragraph is empty collapses to 1 twip); the broader always-on flag inventory (`sw/inc/IDocumentSettingAccess.hxx:36-106`, DOCX defaults `WriterFilter.cxx:300-341`) — notably `TAB_OVER_SPACING`'s side effect (Word ≥2013 drops the body's upper spacing at the top of non-first pages), `HEADER_SPACING_BELOW_LAST_PARA`, `TREAT_SINGLE_COLUMN_BREAK_AS_PAGE_BREAK`, and `IGNORE_TABS_AND_BLANKS_FOR_LINE_CALCULATION`.
- **#42 Rules that were fitted to a single measurement and could be settled by an amplified probe** (opened 2026-08-13). Each of these is currently a constant or a shape that reproduces one observation without anyone knowing whether it is the right MODEL — the failure mode described under "Ad hoc Word probes" in CLAUDE.md, where a realistic-sized fixture cannot separate the hypotheses. All are cheap: one fixture per item, the variable declared large, and measured at two or more magnitudes. Ranked by how much rides on the answer:
  - **The `w:line` fallback multiplier of 1.04** (`agendas-minutes/13`'s "Notes:" box, and the systemic line-pitch family). An invented constant applied when a document declares no `w:spacing/@w:line` at all. Word grows 26.17px per break there against Morph's 27.50. The existing note already says "do not tune it against this one box" — the probe is N breaks x several font sizes x several faces, so a wrong CONSTANT separates from a wrong METRIC.
  - **#41's baseline ascent** already names the fixture it needs (one size per variant across several faces, because the usWinAscent/hhea gap is font-specific: 0.071 em Aptos, 0.202 em Calibri). Amplify the size as well as varying the face — the offset is a fraction of the em, so 48pt makes it 11px and unmistakable.
  - **The three-D bevel block** — Word's 6pt groove spans 19px split ~12px dark / ~3px light, modelled as 1.2/0.3 units at 0.41x from that ONE width. Re-probe at 2pt and 12pt.
  - **The thin/thick border family** — divides its declared width where the symmetric families repeat it, which lands within 3px of Word but is not understood. Probe `thinThick*`/`thickThin*` at several `w:sz`.
  - **Cell padding / row-height fudges** — `DocumentParser` (~line 4836) and `TableHeightCalculator` (~line 362) both carry comments about compensating fudges since removed or reworked; a probe sweeping `w:tblCellMar` and `w:trHeight` at exaggerated values would confirm what is left is right.
  - **Subscript/superscript at "approximately 58%"** (`SkiaRenderContext`) — a convention, never measured. One 96pt probe answers it.
  - **Footnote text size approximated at 10pt** (#13).
  - **#33's uncharacterised difference in a cell's usable height** (Word fits 42 lines in flow against 41 in a cell for the same band) — amplify by making the cell very tall so the discrepancy is many lines rather than one.

- **#43 Word's Calibri advance model is SOLVED and TOOLED (2026-08-18); activating it — and the Calibri-12 default-font flip it gates — is blocked on KERNING.** The 2026-08-13 characterisation ("Word departs from hmtx by grid-fitting… NOT cheaply fixable… entry point is hinted advances") is superseded: the model was measured from Word's own XPS (per-glyph `Indices` advances — see the probes section in `CLAUDE.md`) and no hinting execution is needed.
  - **The measured model.** Word rounds the em to whole pixels on its 120-dpi layout grid per size (its XPS declares em 7.8pt = 13px for 8pt text) and takes per-glyph GDI natural widths on that grid: most Calibri glyphs at text sizes snap to whole pixels (n at 12pt is 11px uniform, +4.7% over linear), the rest run linear at the rounded em plus a half-twip. The snap is per glyph AND per authored size — 10.5pt and 11pt both render on an 18px em with different n advances — so no closed ppem formula exists, and no public API reproduces the values (DirectWrite's GDI-compat mode rounds cells Word keeps fractional; GDI's integer APIs round everything). The 2026-08-13 sweep's "11pt exact" was wrong: Word's 11pt Calibri is 1.6% NARROWER than linear (em 18px vs 18.33). Aptos is em-rounded and hinted too, just within ~±2%, which is why the linear track survives there.
  - **The tooling, landed and inert.** `scripts/generate-word-advances.py` memoizes Word per (face, half-point size, codepoint) into `src/Fonts/*.wordadvances` sidecars, read by `FontMetricsReader` into `FontMetrics.WordAdvances` and consumed by `CanonicalTextMeasurer.LinearPixels`; unmeasured cells fall back to linear×(round(pt×5/3)+1/24). All five bundled Calibri faces are generated but ship PARKED as `.wordadvances.pending` — rename to activate. A reflection check reproduces Word's line totals exactly at 10/11/12pt, and a glyph-for-glyph XPS cross-check on `table_colors` matched every letter.
  - **Kerning LANDED (2026-08-19)** — measured, implemented and adjudicated in the layout engine (see `docs/word-features.md`, Kerning): GPOS pair values (`GposKernTable`), Word's probed quantization (kern snapped to 1/16 px, the pair's first-glyph advance then rounded to a whole pixel — `_probe_kern_*`, six pairs × three sizes), and the probed gating (no docDefaults → built-in Normal kerns; docDefaults without `w:kern` → off; `w:kern` → its threshold; inline overrides). Kerning-only adjudication: zero page-count changes, aggregate flat (+0.0001), `table_borders` −0.005×3 (its "Cell (1,1)" wrap — this entry's original symptom — now matches Word) and `complex_document` −0.002×3; the only regressors are `table_cell_margin_per_cell` (+0.009×3) and `table_default_cell_margin` (+0.004×3), whose autofit columns were ALREADY ~13px wide of Word before kerning (Word 292/293px, Morph 306/306 at 150 DPI) — kerned text merely moved inside the mismatched grid. A wrap-level check matches Word exactly on a kerned fixture (7 = 7 lines). Known gaps: per-STYLE `w:kern` does not cascade, and painter INK is unkerned (backends draw runs with their own advances) — kerning today moves wrap points and segment origins, which is what pagination and the metric see first.
  - **Why the SIDECARS stay parked: table/autofit interactions, not kerning.** With sidecars + kerning both active the corpus measured 13 better / 38 worse (AE +0.0007) and `resumes/16` still lost Word's page count (1→2) — its docDefaults disable kerning, so kerning was never its cause. The regressors are table-geometry scenarios: the autofit interplay under changed advances (the `table_colors` zero-slack knife-edge, the cell-margin fixtures' pre-existing 13px column divergence), plus two template mysteries (`resumes/16`'s overflow — its 10pt body advances match the sidecar glyph-for-glyph in Word's own XPS, so the extra page comes from some layout cascade, not run widths; `newsletters/03` p4, a page already at AE 0.56 where any reflow moves mass). Next: root the autofit slack model (Word's preferred-width rule under its real advances), then reactivate `.wordadvances.pending` and re-adjudicate.
  - **Space advances are context-dependent — do not re-add them to the sidecars.** A run of consecutive spaces measures Word's hinted space (uniform 5px at 12pt) but a single inter-word space gets its fractional linear width pen-rounded per context (4px in `table_colors`' "Header 1", 5px in its title, same document). The generator deliberately excludes the space; the first generated sidecars carried the run value and wrapped lines early corpus-wide (+10% per word gap at 12pt) until it was stripped.
  - **The family flip additionally exposes a zero-slack autofit knife-edge.** With the (narrower) Calibri default active, `table_colors`' autofit columns land exactly at the bold header's text width and the cell wraps its own content, where Word gives the column ~6px of slack (Word column 100px vs its own text 94px). Whether that slack is a default cell margin or autofit rule difference is unmeasured — probe before retrying the flip. The flip plumbing itself is in place (`DocumentParser.builtInDefaultFontFamily`, `DefaultFontSettings.CustomizedDefaultFont`); it is a one-line change once the gates clear (kerning has since landed; the autofit slack remains).
  - **Glyph RENDER size is also em-rounded in Word** — 8pt Calibri draws at 7.8pt effective, 10pt at 10.2pt — while Morph draws nominal sizes. Unmeasured ink-scale deviation of up to ~3% at half-point sizes; separate from advances, noted for completeness.

- **#45 Spreadsheet geometry: the measured width/scale/range rules LANDED 2026-08-14; the residuals below are what is still open.** The landed rules live as code comments on `SheetGridBuilder.MaxDigitWidth` / `ToPoints` and `SpreadsheetParser.ResolveRange` / `ResolveScale`, pinned by `ColumnWidthTests`: column width is `width * maxDigitAdvancePx` (exact advance, MAXIMUM digit not the zero, no `+5` — it is already inside the stored width per ECMA-376 §18.3.1.13), the fit scale floors to a whole percent, `dimension` intersects with the cells that actually exist, and `SheetGeometry` shares the measured unit so anchored art tracks the grid. Landing measured SSIM −0.3921 / AE −0.3298 over 165 raster pages with no page-count change on any backend; the SSIM sign is the known unreliability of that metric on this subsystem (below), the AE improvement and the probe evidence are why it landed. Still open:
  - **The worst movers are triaged (2026-08-14), and neither names a new quantity.** `home-contents-inventory-list` (−0.09 SSIM) is a metric misfire on a genuine improvement: Excel draws its table at a perfectly regular 104px column pitch, and the per-gridline error against Excel went from −1/−1/0/0/0/+1/+1/+2/+2 before the landing to +1/+1/+1/+1/0/0/0/0/0 after — mean 0.9px down to 0.4px — while SSIM punished nine full-height gridlines each sliding 1-2px. No defect; do not chase. `social-media-editorial-theme-calendar` (−0.10) is the body-font row factor with its first confirmed corpus victim — see next item.
  - **Row heights render at a BODY-FONT-dependent factor, and Arial-bodied corpus books now visibly pay it** (isolated 2026-08-14, `_probe_fontgraft`; victim confirmed by triage the same day). The same declared heights draw at ~15/16 under an Arial body and essentially exactly under a Verdana-11 theme. `social-media-editorial-theme-calendar` — Arial 12 body, all rows declared `ht` with `customHeight` — had body row pitch 260px against Excel's 259 BEFORE the landing and 267-268 after: the old too-small fit scale was cancelling the ~6% row-height excess, and the corrected scale exposed it (black header band 168 exact → 170, everything below drifting down cumulatively). Fixing it means applying the per-font factor to declared heights for the faces that carry it, which needs the factor measured per face on real-workbook-derived fixtures — do not model a constant. Likely mechanism (unverified): Excel's print scale derived from the Normal font's GDI metrics on screen vs printer. `household-organizer` measuring ~1.05 against the graft's 1.00 remains the one unexplained residue — plausibly non-`customHeight` rows re-auto-sized at open.
  - **`simple-basic-pink-blue-timesheet`'s width is still +5.7%** (649px against Excel's 614 after the landing, from +7.3% before). Its body font is Constantia 11 — bundled in `src/Fonts`, but NOT one of the six faces the width probes covered, so its max-digit advance has never been checked against Excel's fitted unit. One `_probe_grid_`-style fixture (built per the hygiene rules below) settles whether Constantia is another exact-advance face or a deviation.
  - **`autoRowHeightFactor = 1.31` is CONFIRMED** (a probe round claimed it wrong and was retracted — the rendered heights it compared against carry the per-font factor above; dividing it out reproduces 1.31 exactly). Do not revisit without dividing out the body-font factor first.
  - **Probe hygiene, learned twice.** (1) Verify the printer paper by rendering a fixture and checking for a 1123x794 page — Excel REPORTS the A4 it was asked for while a Letter driver silently exports Letter (~8% squeeze), and `Get-PrintConfiguration` reported Letter here while Excel exported A4. (2) Validate any hand-built fixture against a REAL workbook with its fit scale neutralised before trusting its numbers — every fixture cloned from the same minimal base carried an unrepresentative body font, which produced one refuted rule and one wrongly-retracted constant.
  - **Neither metric is trustworthy alone on this subsystem.** Placement fixes scored negative while visibly correct; `weekly-lesson-planner` scored +0.12 on a change that made its geometry measurably worse. Read extents and crops, per `docs/fidelity-audit.md`.

- **#26 `IsAnchorOnlyMark` is inert** (found 2026-08-06). The parser sets it for a paragraph whose only content was behind-text decorative art — "emit a marker with zero line height" — but nothing in the engine consumes it; the deleted production renderers did, so the agendas-minutes/11 behaviour it was written for was lost in the migration. Reviving it is NOT a free fix: honouring it in `CanonicalParagraphMeasurer` regressed 104 of 108 changed pages (aggregate mean |Word−render| 8.4 → 56.8; menus/08, brochures/01 and agendas-minutes/10 to ~190 grey levels), because those paragraphs anchor art whose placement depends on the line existing. Needs its own investigation into what the production renderer did with the reserved space.
- **#5 Floating/anchored decorative art missing or misplaced.** Ten-plus fix passes landed; architecture, parse-path authority rules and the attempted-and-reverted decision log are in `docs/floating-art-pipeline.md`. Still open: freeform/vector shapes in `brochures/04`/`06` (chevrons, balloon art, quote box — improved, residuals remain), `business/04`/`05` (banners, watercolour blobs), `cover-letters/06` (location-pin and phone glyphs) (its red bars remain, but the pale-blue page background now renders), `resumes/10`, and the `cards/05`/`18` fold guides (partially surfaced by the dashed-line pass). `labels/03`'s tear lines render but sit denser than Word's fine dots — Word likely draws round dot caps at wider spacing. `labels/04`'s light-blue hexagon accent needs a `PresetShapeGeometry` hexagon builder before it can render (the gradient guard drops it; unguarded emission painted saturated bounding boxes), and Word's soft look probably also needs gradient-stop alpha, which `GradientFill` doesn't model.
- **#6 Shape geometry defects.** Preset polygons, text-box chrome, picture flips, line alpha, connector assembly and outline-only/stroked-fill shapes are all resolved across both parse paths (`docs/word-features.md`, `docs/floating-art-pipeline.md`). Still open: `business-plans/02`'s arrow construction, and stray art that other subsystems may still place where Word hides it (the `labels/16` class — group-frame clipping fixed the `cards/04` class).
- **#8 Picture effects ignored.** Duotone is modelled as a two-colour ramp for raster block and floating images (`docs/word-features.md`). Still open: the PDF backend applies NO picture effects at all (PdfSharp has no pixel pipeline) and the HTML export ships the original bytes; group-shape pictures carry no effects; soft-focus/blur (`business-plans/02`) and warm-tone (`newsletters/07`) are unmodelled.
- **#9 Text measure inside shapes and text boxes.** The docDefaults `w:jc` cascade, math centring, HTML cell alignment and the pct-fixed-table column growth all landed. Still open: centred text can still wrap in a narrower measure than Word in containers other than pct tables — `cards/02`'s ticket-back TEXT BOX is ~40px narrow and its placeholder barely moved, `cards/16` is a flush-left 6-line variant of the same.
- **#10 Table-style conditional-region inheritance.** The autofit half of this item is closed. The distribution rule was never wrong: Word-probed with monospaced content whose preferred and minimum widths are arithmetic (a 15-token cell, a single 30-char token, a short cell), Word lays out 278.2 / 180.7 / 23.3pt against the CSS auto-table prediction of 276.5 / 180.0 / 25.5 — and refutes proportional-to-preferred, which wants 304.4 / 152.2 / 25.4. `CalculateContentBasedColumnWidths` already implements that rule and reproduces Word's columns and wrap point exactly. What skewed the `Detail` table was the *input*: its table style inherits `w:sz` through `w:basedOn`, which the whole-table `w:rPr` reader was dropping, so cells measured at 11pt instead of 9pt and every preferred width came out 11/9 too wide. With `ResolveStyleRunProperties` walking the chain the columns land within 0.9pt of Word, three of seven exact. Still open: the CONDITIONAL `w:tblStylePr` blocks do not inherit — a leaf's block shadows the base's for that region whole, where Word merges them per property.
- **#12 TOC page numbers.** Tab-stop clamp and Hyperlink-style suppression landed. Still open: numbers are the document's cached values (live PAGEREF needs a bookmark→page map), and they sit ~4pt left of Word because the clamp lands at the cell content edge where Word spills into the right cell padding. Deferred as risk-heavy for a 4pt MEDIUM: the clamp input is the layout's `maxWidth`, and letting it spill means plumbing the cell's right padding into paragraph layout, whose `pagedLayoutCache` is keyed by (paragraph, ContentWidth) — that width key is load-bearing, so a padding-aware clamp needs the padding in the cache key too.
- **#13 Footnotes/endnotes.** Reference marks and the PDF appendix landed. Still open: page-bottom pinning and the separator rule (both need page-level space reservation in layout); footnote text size is approximated at 10pt.
- **#14 Comment markup not rendered.** No balloon, no highlight, no markup-area page shrink (`comments/01`).
- **#15 SDT content controls.** Legacy `w:ffData` form fields render per-type like Word's print output. Still open: `content_control_inline` — `w:sdt` content controls still render as block widgets with chrome.
- **#16 Automatic hyphenation not implemented.** Word's hyphenated breaks don't happen (`hyphenation_auto`, `hyphenation_suppressed` para 3), and a word breaks mid-word without a hyphen in `letters/03` ("Customer S/ervice").
- **#21 Rotation reserves the un-rotated footprint.** Picture and text-box rotation render correctly in all backends, but layout still reserves the shape's un-rotated box (documented all-backend limitation). The HTML export applies no picture transforms at all.
- **#40 ImageSharp's painter anchors glyphs off its own font ascent, not the engine's baseline** (found 2026-08-12). `ImageSharpPainter.DrawTracked` positions text top-anchored at `baseline − ascent` where `ascent` comes from `ImageSharpRenderContext.GetFontMetrics` (the hhea `Ascender`), while the engine's `PlacedLine.Baseline` was computed from `FontMetrics.BaselineAscentPoints` (usWinAscent). The two disagree, so ImageSharp draws every run high by a constant fraction of the em — measured on `business-plans/13` p1 against Skia, which lands on Word: **11px at 48pt, 6px at 18pt, 2px at ~8pt** (150 DPI), i.e. ~0.11 em throughout. This was partly masked while compressed `lineRule="auto"` lines placed their baseline too low; landing that fix (see `docs/word-features.md`, Line Spacing) put Skia and PDF within 1px of Word and left ImageSharp overshooting by the painter's own offset, which is why that change reads −0.195/−0.192 on those two and +0.023 on this one. The fix is to make the painter ask the same metric the engine did rather than ImageSharp's — but the two live in different font stacks (`FontMetrics` vs `SixLabors.Fonts`), so it needs its own adjudication against the corpus.
- **#41 Morph's natural baseline ascent runs ~0.08 em large.** `_probe_linemultiple` measured Word's 48pt Aptos ascent at 44.53pt against Morph's 48.47, and 10.28 against 11.11 on 11/12pt reference lines — a constant offset, not a shape difference: the same probe's box HEIGHTS agree within 0.8pt at every multiple from 0.6 to 1.5. So `FontMetrics.BaselineAscentPoints` (usWinAscent) is not quite what Word uses to place the baseline inside the box, even though the hhea box is right for the pitch. Needs its own fixture — one font size per variant across several faces, since the usWinAscent/hhea gap is font-specific (0.071 em Aptos, 0.202 em Calibri) and a single-font probe cannot tell a wrong metric from a wrong constant.
- **#34 ImageSharp does not synthesise bold.** Skia emboldens a bold run that resolved a face lighter than 700; ImageSharp falls back Bold → Regular with no equivalent, so those runs render at normal weight. **Outline dilation is exhausted** — five stroke-the-fill versions were built and all reverted, and the multi-font calibration behind that verdict (Word's bold adds ~26% ink, Skia's synthesis ~46%, per-typeface spread 1.00–2.53) is in `docs/word-features.md` under Bold. Anything further needs real weight: bundle the missing bold faces, or instance a variable font's `wght` axis.

### HTML export

- **#25 (HTML) Anchored/floating objects linearized in flow order — LARGELY CLOSED 2026-08-07.** Wrap-NONE floating IMAGES were emitted as in-flow block paragraphs (`WriteFloatingImage` sent everything that was not Square/Tight/Through to `WriteImageParagraph`), so decorative art consumed flow height and stacked down the document; they now place absolutely like shapes. Both kinds also positioned from the DOCUMENT ORIGIN, which is wrong for the corpus majority — 128 of ~207 wrap-none floats declare `wp:positionV relativeFrom="paragraph"` against 24 page-relative — and collapsed every page's background art onto page one, since the export has no pages. Both now resolve against an empty zero-height `position: relative` wrapper at the float's own place in the flow. Corpus: 79 rendered exports moved, 39 collapsing (brochures/03 4565px → 1026, cards/19 12303 → 4074, labels/05 4268 → 1043), 34 unchanged, 6 taller and inspected clean (page-2 art now sits at its own anchor). `w:wrapTopAndBottom` deliberately keeps its paragraph — it is the one non-wrapping type that genuinely displaces text in Word. **A second slice closed the last placement gap: cell-anchored art was being DROPPED entirely.** The DOCX parser detaches a float out of a cell's flow content into `TableCell.Floats` (so the cell measures without it) and the exporter read only `Content`, so every cell-anchored drawing vanished — labels/14's blob artwork, business/04-05's banners and watercolour blobs, brochures/04/06/07, letters/13, menus/06 (8 scenarios, 10 anchors). Cells holding art now carry `position: relative` and their floats place against the cell, which is Word's own rule (`wp:anchor@layoutInCell`, default true). A first attempt fixed the `AppendCellContent` switch instead and changed NOTHING — that case is unreachable for DOCX because the parser has already removed the floats; the dead code was reverted rather than left in.

  **Corpus-wide the linearization is CLOSED**: no scenario now exceeds 2x Word's page height (median 0.75), where brochures/03 alone was ~2.7x and cards/19 far worse. Still open, and inherent rather than a placement bug: a page-RELATIVE float approximates its page top by the anchor's flow position, so a fixed-layout multi-panel document still overlaps where Word separates by page — brochures/03 is the case, its whole design living in two anchored groups of 29 pictures positioned per page. Closing that needs the export to paginate, which it deliberately does not.
- **#35 Cell paragraph spacing lost in the HTML export.** Table cells render their paragraphs inline, joined by `<br />`, so a non-blank paragraph's before/after spacing (`w:spacing`) is dropped — only the single line break survives. Letter/résumé templates that keep the body in a cell and separate blocks with paragraph spacing (rather than empty paragraphs) therefore read tighter than Word: cover-letters/05/07/09/10/12, letters/12, newsletters/03, compatibility_mode_14's entry headings. EMPTY separator paragraphs now DO render as a one-line spacer in both the cell and body paths (`HtmlExporter.WriteParagraph` / `AppendCellContent`, `docs/word-features.md`), so blank-separated letters are faithful (cleared a dozen findings); the residual is purely non-zero before/after-spacing on non-blank cell paragraphs, which the inline `<br />` model can't carry. Body-level paragraphs are unaffected — their spacing rides on the `<p>` margin. Reworking cells from inline to block-with-margins would fix it but is the deliberately-avoided cell-model change noted in #31.
- **#31 HTML/AltChunk input gaps.** Block-level CSS, named colours, image sizing, paragraph pitch and table styling all landed — the import model is documented in `docs/html-import.md`. Still open: cell-level inline formatting is flattened (`cell.TextContent` builds ONE run, so `<b>`/`<span style>` inside a cell lose their formatting); `margin-left` indents are ignored; shaded blocks render as full-width bands with no padding or border; `vertical-align` on cells is unmodelled; cell padding composes slightly tighter than Word.

### Spreadsheet input (`Inputs/excel`)

- **#36 Sheet drawings are pinned to the sheet's first page.** `SheetDrawingParser` emits every drawing
  paragraph-anchored, ahead of the table, so each binds to the page the sheet starts on. A sheet that
  paginates therefore keeps all of its art on page one. `invoice-accessibility-guide`'s first sheet is
  the extreme case and now renders a BLANK second page: its grid is twelve cells of narrow column-A
  text Excel all but clips away, and everything a reader sees — banner, contents list, thumbnail — is
  drawing. Excel's `expected_0002.png` is a full landscape page (1573 unique colours, ink over rows
  72-761). The three page-2 baselines are on `BaselineHealthTests.knownDegenerate` until this lands.
  Fixing it means placing a drawing on the page its anchor rows fall on, rather than on the first.
- **#37 Print centering is ignored.** `printOptions horizontalCentered` is set on **71 of the corpus's
  77 sheets** (12 of those also `verticalCentered`) and nothing reads it, so every grid narrower than
  the content box is drawn flush to the left margin where Excel centres it. Measured on
  `basic-business-invoice`: Excel's ink spans x=101..716, the render's x=67..706 — the grid starts at
  the margin instead of being centred. This is a whole-corpus horizontal offset and likely the single
  largest remaining Excel error metric.
- **#38 The fit-to-page scale is not quantised.** Excel's scale is a whole percent; the parser uses the
  exact ratio. `check-register` computes 70.74% where Excel picks 70%, leaving the sheet 1.4% tall
  (570px of ink against Excel's 562). Rounding DOWN to the whole percent would close it, but changes
  every fitted sheet's geometry, so it wants its own measured pass.
- **#39 No horizontal pagination.** A sheet wider than the page is scaled down rather than split into
  left/right page strips. `to-do-list` spans A:Q asking for no `fitToPage`, so Excel prints two strips
  and the render prints the left one and breaks vertically instead — landing on the right page count
  for the wrong reason, with a near-empty second page (allow-listed in `BaselineHealthTests`).

### Word-reference (`expected_*.png`) anomalies worth re-checking rather than "fixing"

- **#32** `newsletters/12` draws an olive stripe Word hides — verify against the DOCX before treating Word as wrong. (The `cards/04` half of this anomaly is resolved: the stray tree and bird flock were out-of-frame group children, removed by group-frame clipping — Word was right.)
- **#33** `feature_capture/01`: Skia/ImageSharp render a drop cap where Word's reference shows none (Word appears to ignore the `dropCap` property here). The shadow/glow half of this anomaly is resolved — the run's four `w14` effects are declared BARE, which is inert in Word, not an instruction to apply its UI defaults.

---

## Per-scenario findings

### agendas-minutes/01

- MEDIUM | all | p1 | vertical compression: schedule-table rows ~10% shorter and section gaps tighter, so the ADDITIONAL INFORMATION section ends ~130px (0.85in) higher than Word
- MINOR | html | - | classroom illustration stacked above the title instead of floating to the right of it

### agendas-minutes/02

- MEDIUM | pdf | p1 | bold weight lost: FINANCIAL MEETING/AGENDA title renders regular weight
- MEDIUM | skia,imagesharp | p1 | Date/Time/Facilitator values ("September 9", "11:00 am", "Mirjam Nilsson") render bold where Word has regular
- MINOR | pdf | p1 | letter-spaced title "FINANCIAL MEETING" renders ~15% narrower (tracking dropped)
- MINOR | skia | p1 | double-width word gap in title "FINANCIAL  MEETING"
- MINOR | all | p1 | agenda table rows slightly tighter, cumulative ~half-line upward drift by the last row
- MAJOR | html | - | header and roster text invisible: navy page background renders as a separate empty block, and the white text (FINANCIAL MEETING, AGENDA, date block, 8 roster names/titles) lands below it on the white page background

### agendas-minutes/03

- MINOR | all | p1 | cumulative upward drift ~1 line height by page bottom (Secretary / Date of approval signature block sits higher than Word)
- CLEAN: html

### agendas-minutes/04

- MEDIUM | all | p1 | cumulative vertical compression ~2 line heights: agenda list and CONCLUSION section end noticeably higher than Word

### agendas-minutes/05

- MINOR | all | p1 | content drifts up ~1.5 line heights by the action-items table

### agendas-minutes/06

- MEDIUM | all | p1 | cumulative vertical compression ~3 line heights: ADJOURNMENT section ends ~0.5in higher than Word

### agendas-minutes/07

- MINOR | all | p1 | content below the header sits ~1 line lower than Word
- MINOR | skia,imagesharp | p1,p2 | word spacing looser than Word with stray space before periods after names ("August Bergqvist .", "Allan Mattsson .")
- CLEAN: html

### agendas-minutes/08

- MEDIUM | all | p1 | Cumulative line-spacing drift: body sections creep upward down the page, PRINCIPAL'S REPORT block ends ~35px (~1.3 line heights) higher than Word
- MAJOR | html | - | Page-break decoration (blue band + orange ring shape) is emitted mid-flow and drawn across the "COMMITTEE REPORTS" heading, overlapping the text
- MINOR | html | - | Header decorative shapes distorted: orange half-donut renders as solid semicircle, title-band ring renders as rounded-square ring

### agendas-minutes/09

- MEDIUM | all | p1 | Cumulative upward drift: Secretary / Date of approval signature block sits ~46px (~2 line heights) higher than Word
- MINOR | html | - | "Meeting Minutes" title is centered across the page instead of sitting left-aligned next to the logo

### agendas-minutes/10

- MEDIUM | all | p1 | Cumulative upward drift: agenda table rows and "Additional Information" block end ~37px (~1.4 line heights) higher than Word
- CLEAN: html

### agendas-minutes/11

- MEDIUM | all | p1 | BUDGET paragraph wraps mid-word: "today's" is split across lines as "In to / day's meeting" (Word breaks before the whole word "today's")
- CLEAN: html

### agendas-minutes/12

- MINOR | all | p1 | Right edge of the [Date] gray bar and "Meeting Notes" dark banner is ~10-15px off vs Word (bar width mismatch, visible as a solid strip in the diff)
- MINOR | all | p1 | Bullet continuation lines ("want.]", "this one.]") are indented ~40px deeper than Word instead of aligning with the bullet text start
- MINOR | all | p1 | Slight upward drift: Discussion/Roundtable sections end ~1 line height higher than Word
- CLEAN: html

### agendas-minutes/13

- MEDIUM | all | p1 | Agenda table rows progressively shift/compress upward; table bottom border ends ~35px (>1 row of text) higher than Word
- CLEAN: html

### agendas-minutes/14

- MEDIUM | all | p1 | "Attendees: Helbe Sokk, ..." renders as two lines ("Attendees:" alone, names on next line) vs Word's single line, pushing all following content ~1 line lower
- MEDIUM | all | p1 | Wide roman numerals fuse to heading text with no separator: "III.APPROVAL OF MINUTES...", "IV.OPEN ISSUES", "VI.ADJOURNMENT" (PDF also "II.ROLL CALL", "V.NEW BUSINESS"); Word shows a clear gap after every numeral
- MAJOR | html | - | "Attendees:" label text missing — only the name list renders under the title
- MINOR | html | - | Decorative teal stripes at top and bottom of the page are missing

### agendas-minutes/15

- MEDIUM | skia,imagesharp | p1 | Numbered-list continuation lines indented deeper than Word (wrap lines don't return to the hanging-indent column), changing item 4's wrap ("...ribbon, click / an Insert option." vs Word's "...click an / Insert option.")
- MINOR | all | p1 | Body sits a constant ~6px above Word from the date block down (band 2 is +10, then −6 steady — a one-time element-height difference in the title/date area, not accumulation). The former ~0.6pt/line pitch drift is FIXED (2026-08-04): it was the parser's invented 1.04/1.08 default line-spacing multipliers — Word probes showed absent `w:line` is exactly single (see `DocumentParser`'s `lineSpacingMultiplier` comment); per-line pitch now matches Word identically.
- CLEAN: html

### agendas-minutes/16

- MINOR | all | p1 | Whole content block sits ~13px higher than Word; DATE/TIME/MEETING CALLED rows slightly shorter so the offset grows to ~17px by NEXT MEETING
- [known] MINOR | skia,imagesharp | p1 | Residual pixel-level differences on the pink leaf/dot decorations (Skia SVG render vs ImageSharp PNG fallback, documented in notes.md)
- MAJOR | html | - | Anchored decorative images (dot clusters + leaf) render as in-flow blocks at the top-left of the document, pushing the "Minutes" title ~850px down, instead of corner-anchored overlays

### agendas-minutes/17

- MINOR | all | p1 | Title block ("TEAM" / "AGENDA") sits ~30px lower than Word on skia/pdf and ~15px lower on imagesharp; info rows and the three bullet columns now align
- MINOR | html | - | First divider rule renders below the "Meeting time" row instead of above it, and both divider rules extend to the viewport's far left edge past the content margin

### agendas-minutes/18

- MEDIUM | skia,imagesharp | p1 | Numbered-list continuation lines indented deeper than Word (continuation not aligned to the hanging-indent column, e.g. "typing. Don't include..." in item 1)
- MINOR | all | p1 | Vertical spacing slightly tighter than Word; drift accumulates down the page so the action-items table rows sit ~15-20px higher by the last row
- MINOR | html | - | Action-items table header labels ("Action items", "Owner(s)", "Deadline", "Status") are centered over their columns instead of left-aligned as in Word

### agendas-minutes/19

- [known] MEDIUM | all | p1 | Contact-table rows (especially the empty ones) render shorter than Word (~25pt vs ~30pt), so rows drift progressively upward and the table ends well above Word's (documented in notes.md)
- CLEAN: html

### table_borders

- MEDIUM | all | p1 | cell text wraps where Word keeps it on one line: Word fits "Cell (1,1)" in 86px at 150 DPI with a 3px inset from the rule, Morph needs more than the ~89px available and breaks after "Cell", so every row is two lines deep. The column geometry is NOT the cause — Morph's content gaps measure 91px against Word's 90 — so this is a text-measure or default-font difference of ~4% on a style-less package (the export resolves both Aptos and Calibri in it, and the package has no `styles.xml` for either side to agree from). Exposed by fattening this fixture's borders from `sz=4` to `sz=24` on 2026-08-13: the 5px the wider rules take out of each column tipped an already-marginal measurement over the wrap threshold, where at 0.5pt it was invisible. Belongs to the default-font/measure family (#41, #42), not to borders.

### bar_tabs

- MINOR | skia,imagesharp,pdf | p1 | text lines drift upward progressively (~5-10px by the last paragraph) versus Word
- MAJOR | html | - | bar-tab vertical separator lines are not rendered at all and the tabbed columns collapse to single spaces ("Column one Column two Column three")

### border_style_variants

- MINOR | all | p1,p2 | the three-D bevel block is fitted to a single probe width — Word's 6pt groove spans 19px split ~12px dark / ~3px light, modelled as 1.2/0.3 units at 0.41x, and the proportions are unverified at other `w:sz`
- MINOR | all | p2 | at sz=96 (12pt) the border band is painted across the paragraph text instead of outside it, so the label reads "ingle, sz=96"

### brochures/01

- MEDIUM | all | p2 | bullet before "GET THE EXACT RESULTS YOU WANT" is drawn much smaller than Word's dot; in skia/imagesharp it is also teal instead of Word's pink/magenta
- MINOR | all | p1,p2 | body/contact text blocks sit ~5-10px lower than Word, drift growing down each panel
- MAJOR | html | - | second page's navy panel/artwork ends mid-content: "USE ICONS TO ADD" and "MAKE IT YOURS" headings are cut in half at the boundary, their white body paragraphs are invisible on the white page background, and the speech-bubble and hatched-wave shapes behind the quote are missing
- MAJOR | html | - | "EVENT SERIES NAME" heading is overlapped by the light-blue blob graphic (z-order wrong), truncating "SERIES" and "NAME" to "SERIE"/"NA"

### brochures/02

- MAJOR | pdf | p1,p2 | Word's red duotone recolor is not applied to any photo (p1 swimmer, p2 underwater diver and poolside-hug photos) — all render in original blue tones
- MEDIUM | skia | p1,p2 | short blue divider rule (below "Meet director: Ravi Costa" on p1, below the Day-2 finals list on p2) rendered as a multi-row hatched/striped block instead of a solid line (ImageSharp and PDF match Word)
- MINOR | all | p1,p2 | small block shifts: red title lines sit ~20px lower on skia/imagesharp, date/venue and schedule text offset ~10px on all backends
- MAJOR | html | - | photos keep original blue colors — red duotone recolor missing
- MEDIUM | html | - | red dashed decorative graphic overlaps the "Event officials" text lines (Judge's coordinator / Meet director)
- MINOR | html | - | divider rule renders as a tiny hatched box, and the "August 12th - 14th" line crowds the title's descenders

### brochures/03

- MEDIUM | all | p2 | right circle photo sits ~20pt higher than Word (its greyscale rendering landed 2026-07-19)
- MEDIUM | all | p2 | page content sits too high: Relecloud block ~0.3-0.45in up, itinerary rows ~0.25in up, and "ConnectAbove"/"Launch Event" footer links 60px too high (tucked under the card instead of centered in the navy band)
- MINOR | pdf | p1,p2 | photo interior crop wider than Word and the other backends (more scene, hands smaller)
- MINOR | pdf | p2 | heading stars drawn as faint outlines instead of solid white, and left 8-point star teal instead of white
- MAJOR | html | - | the Event-itinerary schedule renders dark-on-dark — the light teal card background is missing behind it
- MAJOR | html | - | second and third photos render as unclipped full-color rectangles (greyscale circle treatment missing)

### brochures/04

- MAJOR | all | p1,p2 | roof-chevron accent shapes missing everywhere (above the quote, above the brochure title, above each "Headline 1")
- MINOR | all | p2 | brick-wall photo (blip-filled custGeom, now contour-clipped): ImageSharp draws it unclipped (documented contour-mask gap)
- MAJOR | html | - | construction photo missing, and the brick-wall image overlaps the address block and the "Brochure Title"/subtitle area
- MEDIUM | html | - | roof-chevron accents missing

### brochures/05

- MAJOR | all | p2,p3 | placeholder paragraphs beside the product photos are missing (p2: texts beside BUFFET-STYLE MEALS and PLATED DINNERS; p3: all three "To replace any placeholder text..." blocks); only the appetizers paragraph survives
- MEDIUM | all | p4 | spice-tray and soup photos lose Word's tight crop — the full image is shown zoomed out with visibly smaller subjects
- MINOR | all | p1 | body paragraphs wrap at different words (same line count, different break points)
- MINOR | all | p1,p2,p3,p4 | headings/text blocks sit ~5-12px higher than Word throughout
- MAJOR | html | - | same photo-side placeholder paragraphs missing (p2/p3 content)
- MEDIUM | html | - | orange background panel behind the CONTACT US / logo block missing
- MINOR | html | - | table-of-contents dot leaders missing

### brochures/06

- MINOR | all | p2 | quote-box geometry residual: dash column sits at the box's right edge where Word hatches inside
- MAJOR | pdf | p2 | top-right couple photo rendered ~30% narrower and shifted ~110px right, bleeding to/clipped at the right page edge (left portion of Word's crop lost)
- MEDIUM | all | p2 | right-column reflow: "To replace any of the pictures" paragraph wraps 4 to 5 lines in a narrower block, and the services list ("Passport Expediting"..."Trip Insurance") sits 20-80px lower than Word
- MINOR | all | p1 | "MARGIE'S TRAVEL" title shifted down ~8-10px (in pdf the panel address block is also ~18px low)
- MINOR | skia,imagesharp | p2 | couple photo drawn slightly taller with content shifted ~8px vs Word's crop
- MAJOR | html | - | quote box text "We don't merely book your travel..." and attribution missing from the HTML export

### brochures/07

- MEDIUM | all | p1,p2 | body text (lorem paragraphs, contact text, right-rail paragraphs) rendered visibly bolder/heavier than Word, with shifted wrap points
- MINOR | all | p1,p2 | text blocks (CONTACT US group, LOREM IPSUM columns, ABOUT US group) uniformly shifted ~10px
- MAJOR | html | - | ABOUT US section's yellow panel background missing, so its white quote block (large " mark + white lorem text) is invisible/absent in the export
- MEDIUM | html | - | body text renders bold where Word shows regular weight

### brochures/08

- MAJOR | all | p1,p2 | navy/blue duotone recolor lost on every photo (skyline, ceiling structure, bottom building band, wavy panel, grid building) — all rendered plain greyscale
- MINOR | all | p1 | the "Contoso Logo" frame sits 8px low (Morph y=924-1194, Word y=916-1185); its width and x match Word exactly and it carries 17743 white pixels against Word's 19879
- MEDIUM | all | p1,p2 | thin heading rules missing: under "JOIN OUR TEAM" (p1), under "OUR STORY", under the "MAKE IT YOURS..." title, and the orange rule above the CONTACT US paragraph (p2)
- MINOR | pdf | p2 | numbered client list vertical spacing looser than Word (~50px vs ~38px between items)
- MAJOR | html | - | photos overlap text: bottom building photo covers the address block and the "OUR STORY" / "MAKE IT YOURS" / "CONTACT US" headings
- MAJOR | html | - | "MAKE IT YOURS" body paragraphs invisible (white text without the orange panel background) and numbered list items show bare "1. 2. 3." with no item text
- MAJOR | html | - | "Contoso Logo" framed box missing from the export
- MEDIUM | html | - | photos greyscale (navy duotone lost) in the export

### business-plans/02

- MEDIUM | skia,imagesharp | p1 | Cover title word gaps collapsed — "SUNKEN VALLEY FARM BUSINESS PLAN" reads as "SUNKENVALLEYFARM BUSINESSPLAN" (verified in zoom), and the title block sits ~20px lower than Word.
- MINOR | skia,imagesharp | p1 | Footer contact block (rule line, JI-MIN AN, phone/site columns) shifted down ~0.2in as a unit.
- MINOR | pdf | p1,p2,p3,p4,p5,p6 | Uniform small downward drift (~10px) of body content producing ghost doubling of text and table rules.
- MEDIUM | html | - | Hero wheat photo shows the sharp un-blurred original, missing Word's soft-focus treatment.

### business-plans/03

- MAJOR | all | p1 | "Prepared by" address block sits ~1in lower than Word: final line "Frankfort, KY 09876" is pushed off-page and lost, and "2345 Creek Road" is half-clipped at the bottom page edge in Skia, ImageSharp, and PDF (verified in zoom of all three originals).
- MEDIUM | all | p1 | Right-column sections (SUMMARY through FINANCIAL SUMMARY) drift progressively lower, ending ~0.3-0.5in below Word's positions, with re-wrapped line breaks (same line counts).
- CLEAN: html

### business-plans/04

- MEDIUM | imagesharp,pdf | p1 | Title "ONE PAGE PROPOSAL" rendered in a visibly lighter/regular serif weight instead of Word's heavy bold display weight (HTML export shows the correct bold, confirming the source asks for it).
- MEDIUM | all | p1 | Content below the intro drifts up cumulatively ~0.2-0.3in: 2x2 section-grid inner border sits ~25px above Word's and the outer frame bottom ~48px above (~27px in PDF).
- CLEAN: html

### business-plans/05

- MEDIUM | skia,imagesharp | p1 | Body content progressively compressed upward — section headings and the PREPARED BY/PREPARED FOR blocks end up to ~0.5in higher than Word (line spacing too tight for the display serif), with word-level rewraps.
- MINOR | skia,imagesharp | p1 | Irregular doubled inter-word gaps in body paragraphs (e.g. "designed to  improve", "Using  best  practices" in SOLUTION) where Word shows single spaces; PDF is normal.
- MINOR | pdf | p1 | Full-page cream background tint minutely off (em=0.99 — nearly every pixel faintly differs, invisible side-by-side).
- MINOR | pdf | p1 | Small downward drift (~10px) doubling the divider rules and footer block positions.
- MINOR | html | - | Same doubled inter-word gaps as the raster backends (e.g. "Using  best  practices", "scalable  solutions"); Word shows single spaces.

### business-plans/06

- MEDIUM | all | p1,p2 | Document-wide bold loss: cover title "CLIENT PROPOSAL", "Prepared for:/by:" labels, grey numerals 01-05 and yellow section headings all render regular/light instead of Word's heavy bold (title ink 25-50% lower)
- MINOR | all | p1 | title block sits ~20-30px lower than Word
- MINOR | skia,imagesharp | p1 | contact block ~40px lower than Word (PDF matches Word's position)
- MINOR | all | p2 | whole section stack shifted down uniformly ~40-50px; PROBLEM STATEMENT paragraph breaks lines at different words (same line count)
- MEDIUM | html | - | yellow cover background terminates mid-section 01, slicing through the SUMMARY paragraph (first lines on yellow, last on white)
- MEDIUM | html | - | title "CLIENT PROPOSAL" and numerals 01-05 render light instead of Word's heavy bold (same bold-loss as rasters)
- MINOR | html | - | "Prepared for:/by:" row starts immediately under "PROPOSAL" with no gap (crowded vs Word's clear spacing)

### business-plans/07

- MINOR | all | p1 | intro paragraph, four section blocks and footer contacts sit 30-50px lower than Word (footer band itself correctly placed)
- MEDIUM | html | - | pale-green footer band starts mid-contact-block (labels and first contact lines sit above/outside it) instead of enclosing the whole PREPARED section, and stops at content width
- MINOR | html | - | title line spacing collapsed so "PROPOSAL" caps touch the "BUSINESS" baseline

### business-plans/08

- MAJOR | pdf | p1 | last contact lines "Seattle, WA 89101" / "Santa Fe, NM 11121" pushed past the page bottom and entirely absent
- MAJOR | skia,imagesharp | p1 | "Seattle, WA 89101" / "Santa Fe, NM 11121" clipped mid-glyph at the page bottom edge (contact-block line spacing inflated ~38% pushes them into the margin)
- MEDIUM | skia,imagesharp | p1 | title "Business proposal" shifted right ~75px and down ~30-55px (Word has it flush at left margin)
- MEDIUM | pdf | p1 | second title line "proposal" indented ~75px right of "Business" (Word has both lines flush left); title also ~55px low
- MINOR | all | p2 | section stack drifts upward, ~80px high by section 5
- MAJOR | html | - | sections 1 (Summary) and 2 (Problem Statement) unreadable: green heading/body text rendered on the green cover background (only the "1."/"2." markers visible)
- MAJOR | html | - | title lines overlap — "proposal" glyphs collide with "Business" and the line is indented right
- MAJOR | html | - | top accent line missing (only a stray dot remains near its start position)
- MINOR | html | - | list numbers "3./4./5." rendered tiny beside large section headings (Word renders number and heading at the same size)

### business-plans/09

- MAJOR | all | p4 | "PUT THE PLAN INTO ACTION" table broken: header row ("Step/Action/Due date/% complete") plus the Action and Due-date columns missing; only a collapsed two-column Step+"%" fragment renders at top-left
- MEDIUM | all | p3 | "PUT THE PLAN INTO ACTION" heading pulled onto the bottom of p3 (Word starts the section on p4)
- MEDIUM | all | p1 | cover title "TARGET AUDIENCE PROFILING PLAN" and "INTERNAL DOCUMENT" render bold vs Word's light weight (ink +18% ImageSharp, +38-39% Skia/PDF)
- MEDIUM | skia | p2 | heading "QUESTIONS TO NARROW DOWN YOUR TARGET AUDIENCE" wraps to two lines (single line in Word, ImageSharp and PDF)
- MINOR | all | p2,p3 | footer sits ~68px lower than Word
- MAJOR | html | - | final "PUT THE PLAN INTO ACTION" table reduced to a Step+"%" fragment (header row, Action and Due-date columns missing)
- MEDIUM | html | - | blue cover background extends into the body and cuts through the QUESTIONS FOR CONSUMERS table
- MEDIUM | html | - | cover title bold vs Word's light weight

### business-plans/10

- MAJOR | skia,imagesharp | p3,p4,p5 | Pagination distribution wrong despite matching page count: "List/Define all pertinent items" pulled from p4 up onto p3, and the CAMPAIGN SIGN-OFF heading plus its intro line pulled from p5 up onto p4, so p5 starts mid-table at the first signature row
- MAJOR | pdf | p3,p4,p5 | Pagination distribution wrong despite matching page count: bullet "List all pertinent items." pulled onto p3, and the CAMPAIGN SIGN-OFF heading plus its intro line render on p4 (Word puts the whole sign-off section on p5), so p5 starts mid-table at the first signature row
- MEDIUM | all | p2,p3 | Italic paragraphs render upright: "Use the Tactical Marketing Plan...", "In this section, you need to define...", "Use this section to brainstorm...", and the BUDGET paragraph "Compile a list of pertinent items..." all lose their italic (HTML export keeps it)
- MEDIUM | all | p2,p3,p4 | Table grid incomplete: header row and first body row of each table (PLAN OVERVIEW, NECESSARY EVENT RESOURCES, APPROVAL) render with no outer box or vertical cell borders — only the horizontal rule under the header — while Word draws a full grid on every row
- MEDIUM | all | p2,p3,p4 | Table-style bold lost: header-row text ("Practice:"/"Name", "Resource"/"Role"/"Estimated Work Hours", "Title"/"Name"/"Date 1"/"Date 2") and first-column labels ("Name of Campaign:", "Campaign Manager:", "Subject Matter Expert:") render regular weight instead of bold
- MEDIUM | pdf | p1 | Cover subtitle "ADVANCING INTERNATIONAL STRATEGIES" sits almost touching the title — title block ~20px lower and the ~40px gap before the subtitle collapses to ~10px
- MINOR | all | p2,p3,p4 | Footer page number positioned ~40px lower on the page than Word's
- MAJOR | html | - | Cover date line "April 4, 20XX" missing — only "Version 3.0" renders above the title block
- MEDIUM | html | - | Same table defects as raster backends: header row and first body row of each table lack box/vertical borders, and header/first-column bold is lost

### business-plans/12

- FINDING 2026-08-06 (from the image-height measurement fix): the p1 cover renders the 520x461pt inline photo at its full height on both paths, but Word shows only ~264pt of it (photo visibly ends at ~311pt) — the lower half is overdrawn by later rows' fills in Word's cover collage, a z-order/overdraw the engine does not reproduce. The old baseline's band-error 0.0 was a compensating accident (rows measured without the image squeezed the layout back to Word's landmark positions while pixel error was 35.9 grey); with rows honestly sized the cover scores worse (60.7) until the overdraw is modelled. Undug.
- MAJOR | all | p3,p4,p5,p6,p7,p8,p9,p10,p11,p12,p13,p14,p15,p16,p17,p18 | footer page number missing on every content page (Word shows "3".."18" bottom-right; all three backends render nothing there)
- MEDIUM | all | p2 | the thick black rule below the "TABLE OF CONTENTS" heading is missing
- MAJOR | skia,imagesharp | p1 | cover text block pushed ~1in down: colored-arrows logo sits clipped at the bottom page edge and "First Up Consultants" is pushed off-page entirely (missing)
- MEDIUM | skia,imagesharp | p4,p5,p6,p8,p10,p11,p12 | numbered section headings lose the tab gap after the number — rendered run-together as "1.EXECUTIVE SUMMARY" (Word: "1.   EXECUTIVE SUMMARY")
- MEDIUM | skia,imagesharp | p3,p4,p5,p6,p8,p9,p10,p11,p12,p16,p18 | wrapped continuation lines of bulleted paragraphs indented ~3 characters deeper than Word, shifting wrap points and adding an extra line to several bullets
- MEDIUM | skia,imagesharp | p6,p7 | tighter list spacing pulls the last two lines of the "Note the difference…" sub-bullet ("law practice … various billing rates.") from page 7 back onto page 6, so page 7 starts at a different point than Word
- MINOR | all | p3,p4,p5,p6,p8,p9,p10,p11,p12,p16,p18 | vertical spacing slightly tighter than Word — content position drifts up to ~1 line higher by page bottom on bullet-heavy pages
- MEDIUM | all | p14 | extra blank table column inserted between the JUN and JUL columns of the profit-and-loss table (Word shows 13 contiguous month columns), compressing the other columns
- MEDIUM | all | p17 | row-label bolding inverted in the blank P&L appendix table — Word bolds the input rows (Estimated Product Sales, Less Sales Returns & Discounts, Service Revenue, Other Revenue, etc.) and leaves computed rows normal; all backends bold Net Sales/Cost of Goods Sold/Gross Profit/Total Expenses/Income Before Taxes/Income Tax Expense instead ("Office-Based Agency" also loses bold)
- MEDIUM | pdf | p15 | table header cell "TOTAL COST" wraps to two lines (single line in Word)
- MINOR | skia | p15 | table header cell "TOTAL COST" wraps to two lines (single line in Word)
- MINOR | all | p13,p15,p17 | appendix/start-up tables render with slightly shorter rows, ending up to ~1.5 rows higher than Word
- MAJOR | html | - | SWOT donut-ring graphic missing (same c:chart limitation as the raster note above)
- MEDIUM | html | - | blank P&L appendix table has the same inverted bolding as the raster backends (computed rows bold, input rows normal — opposite of Word)
- MINOR | html | - | SWOT list bullets black instead of their category colors
- MINOR | html | - | numbered section headings render the number much smaller than the heading text (tiny "3." before "BUSINESS DESCRIPTION")

### business-plans/13

- MINOR | all | p1 | Cover title sits ~9pt LOW. The ~24pt-high title AND subtitle recorded here is FIXED (the dropped anchor paragraph; `docs/word-features.md`, Paragraph Spacing) — the subtitle now lands within 1px of Word and only the title residue is left: Word places the baseline higher inside a `w:line="192" lineRule="auto"` (0.8×) compressed line box than Morph does, which puts the title ink at 1271–1348 against Word's 1252–1329. Morph's box height is right (46.58pt for 48pt text); only where the baseline sits inside it is wrong. **Needs a probe** — one fixture, a column of large-font paragraphs at several `w:line` multiples, baseline-to-baseline measured — because the two data points here disagree about the rule: the title (0.8×) fits proportional ascent scaling, while the subtitle (1.158×) fits a natural ascent with the extra below, and no corpus scenario separates them.
- MEDIUM | skia | p5-p23 | Skia drops run-level bold throughout the body: lead-ins "Opportunity:", "Company summary:", "Order fulfillment:", "Pricing:", "SWOT analysis:", "Step 1-4", "The Executive Summary should be written last", "Projected start-up costs" all render regular weight (imagesharp and pdf render them bold)
- MAJOR | all | p16,p17,p18 | Landscape P&L table's JUL column too narrow — values clipped mid-glyph ($22,500 → "$22,5(", $22,850 → "$22,8", $13,850 → "$13,8", $8,000 → "$8,00(", $12,100 → "$12,1(", $1,125 truncated) in skia/imagesharp p17-p18 and pdf p16
- MEDIUM | all | p16,p17 | P&L row label "Marketing/Advertising" does not wrap and collides with the JAN value "$400"
- MEDIUM | html | - | Cover's grey title-band background missing in the HTML export (title/subtitle on plain white); all other content, images, tables, and TOC page numbers are intact

### business-plans/15

- MAJOR | pdf | p4-p10 | Bold side-headings are drawn on the same baseline as adjacent body text, overlapping glyphs — e.g. "Suppliers" over "If you are providing only products or only services…" and "Management" over "How will you transport your products to market?" (p5).
- MAJOR | all | p17 | Body text overruns the bottom margin: the "Taxes Payable…sheet." line prints through the footer text and the final "Payroll Accrual—Salaries and wages…" bullet is clipped at the page bottom edge.
- MEDIUM | all | p11-p18 | All-caps formatting lost in table headers: "START-UP EXPENSES"→"Start-up expenses" (p11), "MONTH 1-12"→"Month 1-12" (p12-p13), "IND. %/JAN.-DEC./ANN. TOT./ANNUAL %"→"Ind. %/Jan.…" (p15), "STARTING MONTH, YEAR—…/BUDGET/AMOUNT OVER BUDGET"→mixed case (p16), "ASSETS/LIABILITIES"→"Assets/Liabilities" (p18).
- MEDIUM | all | p1 | Cover title block indented ~0.9" further right than Word so "Business Plan" wraps onto two lines (3-line title vs Word's 2), pushing the divider rule and "Caneiro Group" down; the Email/Phone/Address block sits ~0.9" lower than Word.
- MEDIUM | all | p2,p3 | Footer bar ("BUSINESS PLAN | APRIL 25, 20XX" + number) is drawn on the TOC pages where Word shows less. **Not the header/footer z-order rule** — landing that (`docs/floating-art-pipeline.md`) moved p2 by −0.0001 and left p3 untouched. And "suppresses entirely" overstates it: Word's own p2 footer strip carries 4129 dark pixels against Morph's 4483, so Word draws a footer there too; only p3 is lopsided (1866 against 4063). Re-read both pages before treating this as one finding.
- MINOR | all | p1 | Title underline rule spans only margin-to-margin instead of extending to the page's left edge as in Word.
- MINOR | all | p3-p19 | Footer line sits ~0.2" lower on the page than Word's footer position on every page.
- MEDIUM | html | - | Table headers lose all-caps formatting ("Start-up expenses", "Month 1…", "Ind. %/Jan.…", "Starting month, year—…", "Assets/Liabilities" vs Word's "START-UP EXPENSES", "MONTH 1", "IND. %", "ASSETS/LIABILITIES").

### business/01

- MEDIUM | all | p1 | memo header table columns too narrow — "Holiday closure" wraps to two lines under RE and the COMMENTS paragraph wraps to 3 lines vs Word's 2
- MEDIUM | all | p1 | footer block (CANEIRO GROUP, Tel/Fax, black rule) indented ~125px right of Word's left-margin position
- MINOR | skia,imagesharp | p1 | date "05.26.2023" rendered ~20px higher than Word (its underline aligns correctly)
- CLEAN: html

### business/02

- MEDIUM | all | p1 | COMMENTS label + paragraph row sits ~28px higher than Word (gap below the divider rule collapsed)
- MEDIUM | html | - | COMPANY NAME heading renders on white above the beige panel instead of inside it (shaded background starts too low)
- MINOR | html | - | thin divider rule between the header fields and COMMENTS is missing

### business/03

- MEDIUM | all | p1 | cover overlay box geometry off: white Company Name box ~40px narrower and navy Report Title box ~35px wider than Word, both shifted up ~15-25px
- MEDIUM | all | p2 | middle text column and top-right sample block start ~57px further left and are wider than Word, changing wrap points (sample block wraps 4 lines vs Word's 5)
- MINOR | all | p1,p2 | page content sits ~20-25px higher than Word while the footer page number sits ~25px lower
- MINOR | imagesharp | p1 | third body paragraph wraps at different words (lines end "...quodsi docendi." / "...Malis") though line count matches
- MEDIUM | html | - | cover collage flattened: Company Name and Report Title boxes render stacked below the photo instead of overlapping it
- MINOR | html | - | "Report Title" in the navy box renders double-struck/heavier than Word's light-weight title

### business/04

- MINOR | all | p1 | footer address block ~25px higher than Word
- MAJOR | html | - | same decorative graphics (banner bar, Contoso circle, watercolor blob) missing from the HTML export

### business/05

- MAJOR | html | - | same corner graphics missing in HTML
- MINOR | html | - | footer address left-aligned instead of Word's centered-right placement

### business/06

- MEDIUM | all | p1 | footer address block ~55-60px higher than Word
- MINOR | skia,imagesharp | p1 | body block (Memo heading + paragraphs) ~25px higher than Word
- MINOR | html | - | LOGO placeholder rendered as bare text without its outlined box

### cards/01

- MEDIUM | all | p1 | small gift icons on the left card halves placed ~35-70px too high vs Word
- MEDIUM | all | p1 | bottom card's green panel and caption sit ~45px higher than Word, shrinking the fold gap between the two card faces
- MINOR | all | p1 | green gift panels offset ~8px up and ~5px right with slightly different size than Word

### cards/02

- MAJOR | all | p1 | scroll-banner picture's three stars render as three orange squares on both tickets; the scroll art itself still diverges
- MINOR | all | p1 | ticket frame bottom edge sits a few px lower than Word over the code box
- MAJOR | all | p1 | "150220YY" rendered twice per ticket (once at ticket left edge, once below the box) while the white code box is empty and offset right — Word shows a single code centred inside the box
- MEDIUM | pdf | p1,p2 | text rendered bold where Word uses regular weight ("Keep ticket stub" on p1, card-back placeholder paragraph on p2, which also changes its line wraps)
- MEDIUM | all | p2 | ticket-back placeholder text block plus thumbs-up hand sketch sit ~0.5in higher than Word
- MEDIUM | imagesharp | p2 | placeholder text wraps at different words than Word ("just" pulled up to the first line)
- MINOR | all | p2 | polka-dot background pattern misaligned — dots at visibly different positions across both card backs
- MAJOR | html | - | blossom and scroll images not placed in their frames/tickets — stacked at the top-left of the export; both photo frames empty
- MAJOR | html | - | ticket content displaced: first ticket block contains only the three orange squares, the ADMIT ONE / Keep ticket stub / code texts fall below or outside their grey ticket blocks, an extra duplicate "150220YY" pair appears at top-left, and an extra third copy of the placeholder paragraph appears at the bottom
- MAJOR | html | - | stars render as orange squares (scroll images blank)

### cards/03

- MINOR | all | p1 | whole composition slightly offset (title ~5-8px lower, gift illustration shifted a few px), visible as ghost outlines across every shape in the diff

### cards/04

- MEDIUM | all | p1 | "Thinking of You…" and "From…" captions rendered ~50px (~2 line heights) higher than Word — caption sits above the ground line instead of below it
- MAJOR | html | - | red berry dots detached from the tree, scattered as a loose cloud at mid-left over the extra bird flock, leaving the berry tree's canopy bare

### cards/05

- MINOR | all | p1,p2,p3,p4,p5,p6,p7,p8 | whole card content block (picture+caption on odd pages, placeholder text on even pages) drawn ~10-15px (~0.1in) higher than Word; picture content/framing itself is faithful

### cards/06

- MEDIUM | all | p2 | invitation text blocks drawn too high with slightly tighter line spacing — improved 2026-07-19 (any-floating-shape empty-paragraph rule, −0.001..−0.0014/page): the residual offset is now ~0.13in on the top card
- MAJOR | html | - | "It's a Birthday Party!" heading drawn across/overlapping the candle artwork on both cards (Word places it in its own column to the right of the candles)
- MEDIUM | html | - | second card's candle image overflows below the teal card area and overlaps the following "Your Name/Address" text block
- MEDIUM | html | - | teal divider rule missing on both invitation backs

### cards/07

- MEDIUM | all | p1 | card 2's "Celebrate!" caption sits ~90px higher than Word (captions centre correctly now)
- MEDIUM | all | p1 | second card's teal picture block placed ~80px higher than Word (inter-card gap collapses from ~170px to ~100px); first card's picture starts 8-22px higher and both pictures are ~12px (2%) wider
- MEDIUM | all | p1 | small bunting clipart on the card backs (left half) placed 70-150px higher than Word on both cards
- CLEAN: html

### cards/08

- MINOR | all | p1,p3 | watercolor card-face photos shifted vertically as a whole (Skia/ImageSharp ~17px up, PDF ~5px down); size and content otherwise match Word
- MEDIUM | all | p2,p4 | placeholder message rendered in a heavy bold rounded typeface instead of Word's thin light face, wrapping 2 lines into 3 (both cards)
- MEDIUM | html | - | placeholder message shows the same wrong heavy bold typeface (Word uses thin light text)

### cards/09

- MEDIUM | all | p1 | card text blocks drift progressively upward down the sheet (up to ~45px by row 5) and the white name-underline rule ends up striking through "Seattle, WA 54321" on lower cards

### cards/10

- MINOR | skia,imagesharp | p1 | "THank You" artwork drawn ~13px higher than Word (x-position and size exact; PDF matches Word)
- CLEAN: pdf, html

### cards/11

- MEDIUM | all | p1 | card 2's "Celebrate!" caption sits ~85px higher than Word (captions centre correctly now)
- MEDIUM | all | p1 | second card's balloon picture block placed ~85px higher than Word (inter-card gap collapses from ~167px to ~100px); first card's picture starts 8-22px higher and both pictures are ~12px (2%) wider
- MEDIUM | all | p1 | small balloon clipart on the card backs (left half) placed 70-150px higher than Word on both cards
- CLEAN: html

### cards/12

- MEDIUM | all | p1 | "THANK YOU" sits ~20px higher than Word (centring correct now)
- MEDIUM | html | - | Both card-art images are hoisted out of the table to the top of the document, so the THANK YOU headings and pink messages render separately after/without their card backgrounds
- MINOR | html | - | Fold/cut guide borders (vertical divider, dashed mid-page line) not exported

### cards/13

- MEDIUM | all | p1 | Text grid vertically compressed (row pitch ~285px vs Word ~305px) while the white banners/squares stay at Word's positions: text drifts progressively up to ~110px by the bottom row, so titles float above their white banner and the banner instead overlaps the name/address lines. The banners themselves now surface correctly behind the titles (they were mis-ordered under the card art until the document-order group interleave, systemic #5 sixth pass — the old "white placeholder boxes" reading of them was this)
- MEDIUM | html | - | Card outline borders missing, cards blend into the blue background

### cards/15

- MEDIUM | all | p1 | Bottom-half card graphic shifted up ~85px (teal square top at y=739-742 vs Word 825, "Celebrate!" caption follows); top square also up 22px (skia,imagesharp) / 8px (pdf)
- MEDIUM | all | p1 | Small cake icon in the left column displaced ~70px (top) / ~134px (bottom) up
- MINOR | all | p1 | Teal squares rendered ~12px wider than Word (right edge x=1248 vs 1236)
- MINOR | html | - | Fold/cut guide borders not exported

### cards/16

- MINOR | skia,imagesharp | p1,p3,p5 | top-card illustration placed ~12-13px higher than Word (bottom-card copy is correctly placed)
- MINOR | skia,imagesharp | p1,p3,p5 | bottom-card "Merry Christmas" heading sits lower than Word (~18px skia, ~10px imagesharp; top-card heading ~6px low in skia only)
- MINOR | skia,pdf | p1,p3,p5 | 1px lightened fold rule across the page middle (y≈825) missing (present in Word's render and reproduced by ImageSharp)

### cards/18

- MINOR | all | p1 | "Happy Birthday!" script text sits ~30px higher than Word relative to the swoosh/candles (diff shows doubled text), both card halves
- MAJOR | html | - | flame-glow circles detach from the candles and render as a separate cluster at the top-left of the document instead of behind the flames
- MAJOR | html | - | "Happy Birthday!" texts detached from their cards: the first overlaps the middle of the second candle illustration, the second floats alone below both illustrations (anchored positions lost)
- MEDIUM | html | - | fold guide rules missing entirely

### cards/19

- MINOR | skia,imagesharp | p1,p2,p3,p4 | card content sits ~15-20px higher than Word (title text top-aligned instead of vertically centered in its box on p1/p3; contact block and background pattern correspondingly offset on p2/p4)
- MAJOR | html | - | card art decoupled from card text: hatch-pattern blocks render as separate stacked images with EMPTY title boxes, and all card text renders afterward as a separate block (text never appears inside its card)
- MAJOR | html | - | p3 card titles invisible: the white "VanArsdel, Ltd." text renders on the white page background instead of inside the dark boxes, leaving a large blank gap where the 10 titles should be

### column_breaks

- MEDIUM | all | p1,p2 | text following each column break starts at the top of the new column, one line higher than Word (Word starts the post-break column one line down)
- CLEAN: html

### comments/01

- MAJOR | all | p1 | comment markup missing entirely: right-side gray comments pane, balloon "Commented [R1]: Looks good to me.", pink highlight box on the commented text, and the dashed connector line are all absent
- MEDIUM | all | p1 | body text drawn full-size at the normal top margin instead of Word's shrunk-to-fit-markup layout (Word scales the body down and places it lower to reserve the markup column)
- MAJOR | html | - | comment content "Commented [R1]: Looks good to me." missing from the HTML export (only the body sentence is present)

### compatibility_mode_14

- MINOR | html | - | blank space above each education/work entry heading is collapsed, entries run together noticeably tighter than Word

### complex_spacing

- MEDIUM | skia,imagesharp | p1,p4,p5,p6 | hanging-indent paragraphs not outdented: first line drawn at the left indent instead of outdented left of it (Word puts the first line at left−hanging, into the margin), and continuation lines are pushed right by the hanging amount — Combination 7's mirror/hanging column narrows and wraps into 10 lines vs Word's 7
- MEDIUM | all | p1 | "Mirror indents enabled with left 1440" paragraph indented ~1 inch in all backends; Word renders it flush at the left margin
- MINOR | html | - | mirror-indents paragraph likewise indented ~1.3in while Word shows no indent

### complex_tables

- MEDIUM | all | p1,p2 | document title and all numbered section headings render smaller, blue, and non-bold (theme heading colors) instead of Word's larger bold black text
- MEDIUM | all | p1,p2 | vertical compression pulls the complex-merge table and the "5. Calendar-Style Layout" heading onto p1 (PDF also pulls its intro sentence), leaving p2 nearly empty except the calendar — Word's p2 holds the complex-merge table plus all of section 5
- MINOR | all | p1 | "COMPLEX MERGE" header cell text wraps to two stacked lines; single line in Word
- MINOR | all | p2 | calendar's merged "15-16 Event" cell wraps to two lines, making the last calendar row ~50% taller than Word's single-line version
- MEDIUM | html | - | title and section headings render blue/non-bold vs Word's bold black (same styling defect as the raster/PDF backends)

### content_control_inline

- MAJOR | all | p1 | inline content controls rendered as block-level form widgets — each "Label: value" line splits into three lines and dropdown/date get bordered input-box chrome (dropdown arrow, shaded field) where Word shows plain inline text "☒ Yes", "Medium", "2025-06-15"
- MEDIUM | html | - | inline controls likewise broken out of their sentences: checkbox glyph, "Yes"/"No", "Medium", "2025-06-15", "John Doe" each emitted as its own paragraph instead of flowing inline after the label

### cover-letters/01

- MEDIUM | all | p1 | header contact line wraps to two lines ("someone@example.com" drops to a second line; Word fits "T: … // W: … // E: …" on one line), pushing the whole letter below down one line
- MINOR | all | p1 | paragraph 2 wraps the whole word "evidence-based" to the next line where Word breaks at the hyphen ("…implementing evidence-" / "based medicine…")

### cover-letters/02

- MEDIUM | all | p1 | entire content column sits higher than Word — ~0.25" at the "MANASI GOYAL" header, growing to ~0.35" (about 2 line heights) by the signature, from tighter vertical spacing
- MAJOR | html | - | decorative flower graphic at bottom-right missing entirely
- MEDIUM | html | - | cream page background lost — page renders white

### cover-letters/03

- MEDIUM | skia,pdf | p1 | body wraps one word earlier per line — paragraph 1 becomes 7 lines vs Word's 6 (orphan "care."), landing the signature ~2 lines lower
- MINOR | imagesharp | p1 | letter body drifts about half a line lower by the signature
- MAJOR | html | - | right-side lighter sidebar stripe missing entirely (uniform navy background)

### cover-letters/04

- MINOR | imagesharp | p1 | paragraph 2 pulls "at" up to line 1 ("…as a Bookkeeper at") where Word breaks before it, leaving continuation lines starting with a stray leading space
- CLEAN: pdf

### cover-letters/05

- MEDIUM | pdf | p1,p2,p3 | body paragraph spacing wider than Word — right-column letter drifts progressively down, "Sincerely,/Yuuri Tanaka" ends ~2 lines lower
- MAJOR | html | - | corner decoration shapes lose their per-page theme colors — page-1 (teal/green/yellow) and page-2 (teal/magenta/orange) clusters all render in page-3's grey/black palette with only faint colored outlines
- MAJOR | html | - | grey diagonal decoration shapes overlap the second section's address text ("123 Elm Avenue / City, State 98052")
- MINOR | html | - | inter-paragraph spacing collapsed — paragraphs run together

### cover-letters/06

- MEDIUM | all | p1 | location-pin and phone glyphs missing from the contact strip
- MINOR | all | p1 | letter body drifts ~1 line lower than Word by the signature

### cover-letters/07

- MINOR | imagesharp | p1 | First paragraph wraps at different words than Word (line 1 ends "…Manager position", next line starts with a stray leading space)
- MINOR | all | p1 | Letter body drifts upward slightly with tighter paragraph spacing, ending ~0.7 line higher at "Victoria Burke"
- MINOR | html | - | greeting "DEAR RECIPIENT:" and the "Contact" heading sit tight above their following text (heading spacing dropped in the cell's <br />-joined layout, #35); the body paragraphs themselves now space correctly

### cover-letters/08

- MEDIUM | all | p1 | "Enclosure" renders upright instead of italic
- MEDIUM | skia,imagesharp | p1 | Accumulated tighter line/paragraph spacing makes the signature block ("Angelica Astrom / December 13, 20XX / Enclosure") end ~1.5 lines higher than Word
- MINOR | pdf | p1 | Signature block ends ~0.8 line higher than Word
- MINOR | skia,imagesharp | p1 | closing paragraph's wrapped line starts with a leading space (" your review,")

### cover-letters/09

- MAJOR | all | p1 | Sidebar content redistributed: "DIAN NUGRAHA" sits ~0.4in higher and the contact rows spread down the column so the email and website rows land on the yellow/pink waves (white-on-yellow, barely legible) instead of on the navy panel
- MEDIUM | all | p1 | Decorative wave shapes mis-rendered: bottom waves start higher than Word
- MEDIUM | all | p1 | Bullet "Knowledge of the latest technology in [industry or field]?" wraps with the "?" orphaned alone on the next line (Word breaks at "[industry or / field]?")
- MEDIUM | skia,imagesharp | p1 | Letter text drifts upward ~1–2 lines by the "Sincerely, / Dian Nugraha / Enclosure" block
- MINOR | imagesharp | p1 | Second how-to paragraph wraps at different words than Word (" place it appropriately." line starts with leading space)
- MINOR | html | - | Paragraph spacing partially collapsed (how-to paragraphs run together)

### cover-letters/10

- MEDIUM | all | p1 | Horizontal rule between "10 April 20XX" and the Adatum address is missing
- MEDIUM | skia,imagesharp | p1 | First body paragraph wraps to 6 lines vs Word's 5 (breaks at different words; wrapped lines gain stray leading spaces)
- MEDIUM | skia | p1 | Date, Adatum address block, and header contact info render in a visibly heavier weight than Word (imagesharp/pdf match)
- MINOR | pdf | p1 | Line spacing slightly larger — "Sara Steale" ends ~0.7 line lower than Word
- MAJOR | html | - | Black header band and its Contoso contact info (name, address, phone, email, web) are missing from the HTML export
- MEDIUM | html | - | The single date underline is repeated as rules under "10 April 20XX", "Adatum Corp." and "210 Stars Ave."
- MINOR | html | - | Cream page background not applied (white page)
- MINOR | html | - | Spacing collapsed between "Warm regards," and "Sara Steale"

### cover-letters/11

- MEDIUM | all | p1 | Title renders "Astrom" in the same bold weight as "Angelica" (Word shows "Astrom" in a light weight)
- MEDIUM | all | p1 | "Enclosure" renders upright instead of italic
- MEDIUM | skia,imagesharp | p1 | Accumulated tighter section/line spacing — letter and sidebar content end ~3 lines higher than Word by "Enclosure"
- MINOR | pdf | p1 | Content ends ~0.5 line higher than Word
- MEDIUM | html | - | "Astrom" bold instead of light in the HTML title (italic "Enclosure" is correct in HTML)

### cover-letters/12

- MEDIUM | all | p1 | "In my current role…" paragraph wraps to 5 lines vs Word's 4 ("residents." pushed to its own line); final paragraph also rewraps mid-sentence ("Thank / you" with a stray leading space on " on family health")
- MINOR | all | p1 | Entire content block sits ~1 line lower on the gradient page than Word
- MAJOR | html | - | Full-page four-color gradient background missing (plain white page)
- MAJOR | html | - | The "+" list markers before "9/9/20XX", "Contact" and "Dear Jozi Kos," render as generic round bullets instead of "+" glyphs
- MINOR | html | - | Paragraph spacing collapsed — letter paragraphs run together

### cover-letters/14

- MEDIUM | skia,imagesharp | p1 | Tighter paragraph spacing — letter ends ~1.5 lines higher than Word at "Tonnie Thomsen" (wrapped line " that supports students'…" also starts with a stray space)
- MINOR | pdf | p1 | Letter ends ~0.8 line higher than Word
- MINOR | skia,imagesharp | p1 | Right-hand recipient address ("4321 Maplewood Ave / Nashville, TN 65432") sits ~half a line higher than Word relative to the LILLI ALLIK block

### cover-letters/15

- MINOR | skia,imagesharp | p1 | Letter body drifts up ~0.5 line by the "Chanchal Sharma / January 13, 20XX" block

### cover-letters/16

- MEDIUM | all | p1 | body text drifts progressively lower than Word; "Sincerely,/Donna Robbins" block sits ~34px (≈1 line height) lower
- MEDIUM | imagesharp | p1 | paragraph 2 wraps at different points than Word (line 5 ends "QuickBooks, and" and line 6 ends "of a diverse", pulling "and"/"diverse" up a line)
- MINOR | all | p1 | leading space at start of wrapped lines not trimmed — lines "in my ability to provide…" (para 1) and "women and minority owned…" (para 3) are indented one space width off the left margin
- CLEAN: html

### decimal_tabs/01

- MEDIUM | all | p1 | line spacing far too tight — Word spaces the 4 rows at ~47px pitch, Morph ~30px, so "Dates 0.05" ends >1 line height higher (decimal-point alignment of the values is correct)
- MEDIUM | html | - | decimal tab alignment lost — values render immediately after labels separated by a single space ("Apples 12.50") instead of an aligned column

### deep_nested_list

- MINOR | html | - | level-4 and level-5 bullets both render as browser-default squares instead of Word's smaller square and triangle

### document_capture/01

- MAJOR | pdf | p1 | footnote/endnote separator lines missing — the notes render as invented "Footnotes"/"Endnotes" sections straight after the body instead of pinned to the page bottom
- MAJOR | skia,imagesharp | p1 | footnotes/endnotes rendered as invented "Footnotes"/"Endnotes" bold heading sections with "1." numbering placed directly after body — Word's separator lines absent and the footnote is not pinned to the page bottom
- MAJOR | skia,imagesharp | p1 | superscript footnote/endnote reference marks missing in body text — Word shows "Footnote ref1"/"Endnote refi", Morph renders plain "Footnote ref"/"Endnote ref"

### dot_points

- MINOR | all | p1 | line spacing slightly tighter than Word; cumulative upward drift reaches ~30px (≈0.7 line) at item F (per-level bullet fonts/glyphs •/o/▪ all render correctly)
- MINOR | html | - | bullet glyphs at levels 4-6 all render as squares instead of repeating Word's •/o/▪ cycle

### even_odd_headers/01

- MINOR | html | - | ODD HEADER / EVEN HEADER text omitted from HTML export (only the two body lines present)
- CLEAN: skia, imagesharp, pdf

### even_odd_headers/02

- MEDIUM | all | p1,p2,p3,p4 | footer text (ODD FOOTER / EVEN FOOTER) placed ~46px (≈0.3in, ~2 line heights) lower than Word on every page; header and body positions match
- MINOR | html | - | header and footer text omitted from HTML export (only the four body-content lines present)

### feature_capture/01

- MAJOR | skia,imagesharp | p1 | giant 3-line drop-cap "D" rendered where Word shows no drop cap ("Drop cap paragraph" reads as one normal line in Word), pushing the paragraph text and table ~0.5in lower
- MEDIUM | all | p1 | rotated table header cell not wrapped: single vertical "Header" line instead of Word's two stacked vertical lines "Hea/der", making the header row ~65% taller
- MINOR | html | - | "All features" paragraph left-aligned instead of right-aligned
- MINOR | html | - | rotated header cell rendered as horizontal bold centered text (rotation lost)

### field_codes_simple/01

- MEDIUM | html | - | HTML (and Markdown) export still emits the cached "Page 1 of 3" instead of "Page 1 of 1" — the exporters keep the cached value by design (no pagination) [systemic #1 residual (d)]; raster/PDF now render "of 1" correctly

### font_sizes

- MINOR | imagesharp | p1 | line spacing slightly tight — cumulative upward drift down the size list, "36pt text" baseline ends ~16px higher than Word
- CLEAN: html

### footer

- MEDIUM | all | p1 | footer "Document Footer - Confidential" is rendered ~45px (0.3") lower than Word, nearly a line height closer to the page bottom edge
- MAJOR | html | - | footer text "Document Footer - Confidential" is missing entirely from the HTML export

### hanging_indent

- MEDIUM | skia,imagesharp | p1 | Hanging indent not applied to first line: paragraph renders with first line at the 0.5" continuation indent (x=227 vs Word x=152) and continuation line at 1.0" (x=302 vs 227) — entire paragraph sits 0.5" right of Word, first line never outdents back to the margin
- CLEAN: html

### header

- MAJOR | html | - | Page-header content missing from HTML export: bold centered "Document Header" line absent; only the two body paragraphs are emitted
- CLEAN: skia, imagesharp, pdf

### header_banner_table

- MAJOR | html | - | Entire header banner table (slate "SAMPLE // BANNER" bar + spacer rows) missing from HTML export; only body heading and paragraphs emitted

### header_footer

- MEDIUM | all | p1,p2 | Paragraph spacing tighter than Word: page 1 fits paragraphs 1-28 vs Word's 1-24 (body also starts ~33px higher), so page 2 begins at paragraph 29 instead of 25 (page count still 2/2)
- MEDIUM | all | p1,p2 | Footer line "© 2024 Company Name. All rights reserved." renders ~51px lower than Word (y=1582-1600 vs 1531-1550 on a 1650px page)
- MAJOR | html | - | Header ("Company Name" / "Internal Document") and footer ("© 2024 Company Name. All rights reserved.") both missing from HTML export; body paragraphs 1-30 complete

### header_row_repeat/01

- MEDIUM | all | p1,p2,p3 | Table rows slightly shorter than Word, accumulating one extra row per page: p1 ends at Person 25 (Word: 24), p2 spans 26-50 (Word: 25-48), p3 starts at 51 (Word: 49); header row correctly repeats on p2/p3 in all backends and all 60 rows present
- MINOR | html | - | Repeated header cells "ID / Name / Notes" rendered centered in HTML while Word renders them left-aligned in their cells

### html_complex

- MEDIUM | all | p1 | Table interior cell gridlines missing (only the outer frame is drawn despite border=1 with border-collapse)
- MEDIUM | all | p1,p2 | "Visit our website for more information." paragraph spills to p2 top — the page break lands one element off Word's. BLOCKED by the intro-wrap root cause below (a narrow-measure issue); not cleanly fixable in isolation.
- MEDIUM | all | p2 | Info/Warning/Error boxes have no coloured border and no padding — the fills render as thin full-width bands rather than padded boxes, and land offset on p2 from the reflow above (AE +0.019)
- MEDIUM | all | p1 | **Intro paragraph wraps 3 lines vs Word's 2 — ATTEMPTED 2026-07-21, REVERTED (net regression).** TWO causes, not the sup/sub: (1) `HtmlParser` did NOT collapse HTML whitespace — literal source newlines in the `<p>` became hard breaks (the intro source has newlines after "and" and "have", exactly where Morph broke). (2) Morph's HTML body text measures ~6-9% NARROWER than Word (same font/size — first line ink height matches — so it's font metrics + sup/sub at 0.7×), so it UNDER-wraps. Fixing (1) alone (`CollapseWhitespace` on text nodes in `ParseInlineNodes`, char-by-char run→single-space, `<pre>` unaffected) is objectively correct HTML behaviour BUT over-corrects the intro to 1 line (Word's 2) because (2) then dominates, and REGRESSES the metric: html_complex p1 +0.068 AE / −0.021 SSIM, p2 +0.011, html_css_margin_padding +0.011 (only 3 scenarios changed; the newline-breaks had been *accidentally compensating* for the narrow measure). It also only SHIFTS the reflow (p2 then loses the "5. Styled Boxes" heading to p1) instead of fixing it. To truly land: fix the whitespace collapse AND match Word's text width (a corpus-wide font-metric issue — same class as the `header_footer`/`resumes` "wraps 3 vs 2 lines" findings), then the page break seats correctly.
- MAJOR | html | - | h2 headings rendered black instead of #4472C4
- MAJOR | html | - | Table styling lost in export: no interior gridlines, auto width instead of 100%
- MAJOR | html | - | Info/Warning/Error box backgrounds and borders missing (text colors kept)

### html_css_alignment

- MEDIUM | all | p1 | Table height:100px ignored (row 33px tall vs Word's 61px)
- MEDIUM | all | p1 | Interior column borders missing despite border=1 (outer frame only; Word shows all three cells ruled)
- MINOR | all | p1 | Justified paragraph breaks after "entire line" instead of Word's "fill the" (same 2-line count, different break word)
- MEDIUM | html | - | Table interior cell borders and width:100% lost in export (single content-width box)

### html_css_borders

- MAJOR | all | p1 | All seven CSS paragraph borders missing (1px solid black, 2px red, 3px dashed blue, 2px dotted green, 4px double purple, top-red/bottom-blue, 5px orange left bar) — plain unboxed text lines, and with the borders/padding gone the stack compresses ~2in upward
- MEDIUM | all | p1 | Table per-cell border styling lost: one uniform thin box, no 2px thick border on "Cell with thick border" vs 1px gray on "Cell with thin border", and no divider line between the two cells
- MAJOR | html | - | Same seven paragraph borders missing in the HTML export
- MEDIUM | html | - | Table cell border weight/color distinction and inner divider missing in the HTML export

> The CSS box-border/padding attempt of 2026-07-21 was reverted; what it proved, what it cost
> and what landing it requires are in `docs/html-import.md`.

### html_css_colors

- MAJOR | html | - | Yellow and div gray backgrounds missing in HTML export
- MAJOR | html | - | darkblue text color rendered black in HTML export

### html_css_margin_padding

- MEDIUM | all | p1 | margin-left:50px and 100px indents ignored — both paragraphs sit flush at the left margin (Word shows the staircase)
- MEDIUM | all | p1 | #0066CC border box and its padding missing (background fills render as full-width bands), and the margin-left staircase is still unindented
- MEDIUM | all | p1 | 20px div padding and 30px vertical margins collapsed — "Content inside padded div" not inset and "Paragraph with extra vertical margins" sits tight against its neighbors
- MAJOR | html | - | Same three backgrounds and the blue border missing in HTML export
- MEDIUM | html | - | 50px/100px left-margin indents lost in HTML export

### html_images

- MEDIUM | all | p1 | All four embedded images rendered ~33% larger than Word (px dimensions treated as pt instead of 0.75pt), pushing each subsequent caption/image progressively further down the page
- CLEAN: html

### html_inline_styles

- MAJOR | html | - | Same yellow and light-red backgrounds missing in HTML export
- MEDIUM | html | - | Same 18pt/8pt font sizes ignored in HTML export
- MEDIUM | html | - | Same Times New Roman/Courier New font families dropped in HTML export
- MEDIUM | html | - | Same bold/italic dropped in HTML export
- MEDIUM | html | - | Same underline/strikethrough dropped in HTML export

### html_lists

- MEDIUM | all | p1 | Spacing around lists missing: no blank gap after "Unordered list:"/before "Ordered list:" (Word has clear gaps), item leading slightly looser, whole block ends higher
- MINOR | all | p1 | Bullet glyph noticeably smaller/lighter than Word's large Symbol-font solid bullet

### html_nested_lists

- MEDIUM | all | p1 | Blank gaps before "Nested ordered lists:" and "Mixed nested lists:" headings missing (renders run sections together at uniform item spacing)

### html_paragraphs

- MEDIUM | all | p1 | Empty paragraph rendered as a full blank line before "Paragraph after an empty paragraph." — Word collapses it to a normal single paragraph gap
- MINOR | skia,imagesharp | p1 | Leading spaces not trimmed: last paragraph "Paragraph with leading spaces (may be trimmed)." starts indented ~2 spaces, Word (and PDF) render it flush left
- MINOR | all | p1 | Paragraph spacing slightly tighter than Word overall (block ends ~1 line higher)
- CLEAN: html

### html_table

- MEDIUM | all | p1 | Table far more compact than Word: row pitch 30px against Word's 58-59px at 150 DPI on identical ~17px text, and adjacent cells' text almost touches ("Row 2, Cell 1Row 2, Cell 2"). Two contributors, neither yet modelled — see the cell-height block below. Word also separates adjacent cells by the default 2px cellspacing even on a borderless table (probe `_probe_fill_noborder`: ~3px of page white between the fills), which Morph never applies because `cellSpacingPoints` is only read inside the `border`-attribute branch

> **ATTEMPTED TWICE 2026-07-24 — a flat cell-paragraph spacing-after. BOTH REVERTED.** Morph gives a
> cell paragraph no after-spacing at all, so a bare table's rows come out at just the line box. Adding
> a flat value fixes the bare table and breaks the padded one, because the two scenarios need
> different numbers:
>
> | | Word | Morph today | short by |
> |---|---|---|---|
> | `html_table` (no cellpadding) | 28.3pt/row | 14.4pt | ~14pt |
> | `html_complex` (`cellpadding=8`) | 35.0pt/row | 31.2pt | ~3.8pt |
>
> 14pt lands `html_table` exactly (14.4 + 14 = 28.4 against Word's 28.3) and overshoots
> `html_complex` by 10pt: **+0.3178 AE, html_complex alone +0.106 per backend.** 8pt measured
> **+0.0564 AE** and made BOTH worse. So the spacing is not flat — it interacts with cellpadding.
>
> **Probe data to model it from** (`_probe_cellpad_sweep`, one single-line cell per table, Word at
> 150 DPI). Box height, and the gaps from the box edge to the text ink:
>
> | cellpadding | box height | top gap | bottom gap | bottom − top |
> |---|---|---|---|---|
> | 0 | 30.7pt | 6.7pt | 14.9pt | 8.2pt |
> | 5 | 36.5pt | 10.1pt | 17.3pt | 7.2pt |
> | 8 | 39.8pt | 11.5pt | 19.2pt | 7.7pt |
> | 15 | 49.0pt | 15.4pt | 24.5pt | 9.1pt |
> | 25 | 60.5pt | 21.6pt | 29.8pt | 8.2pt |
> | *(no attribute)* | 32.2pt | 7.2pt | 15.8pt | 8.6pt |
>
> Two things fall out. The extra space BELOW the text is **~8pt and independent of cellpadding**, and
> a table with no cellpadding attribute behaves almost exactly like an explicit `cellpadding=0`
> (32.2pt vs 30.7pt), so Word applies no meaningful default padding. But box height grows at only
> **1.19pt per px of cellpadding** (0.596pt per side), NOT the 2 × 0.75 the px→pt rule predicts —
> which is the piece that does not fit, and is why a flat spacing-after cannot reconcile the two
> scenarios. Resolve that conversion before trying again; the measurements above have roughly ±3pt of
> extraction noise, so a retry wants tighter instrumentation (measure line-box tops, not ink).

### html_table_cell_margin_css

- MAJOR | all | p1 | 2x2 table grid disintegrates: each cell drawn as a separate box offset by its CSS margin with fragmented partial borders (floating "Left margin 20px" box top-right, bare over/underline on "Top/bottom margin", bracket-shaped border around "Default") instead of Word's coherent complete grid
- MEDIUM | skia | p1 | "Margin 10px, Padding 5px" cell text wraps to two lines (single line in Word, ImageSharp and PDF)
- MEDIUM | html | - | Inner cell gridlines missing — only a single outer border drawn where Word shows a full light-gray grid around every cell, and cell padding much smaller

### html_table_cell_padding_css

- MAJOR | all | p1 | interior cell borders missing — only a single black outer rectangle is drawn, while Word renders a light-gray border around every cell
- MEDIUM | all | p1 | CSS cell padding under-applied: table ~20% narrower and rows ~25% shorter than Word, right-column text ends flush against the table border
- MEDIUM | skia | p1 | "20px all sides" wraps to two lines (single line in Word, ImageSharp and PDF)
- MAJOR | html | - | interior cell borders missing (outer box only)
- MEDIUM | html | - | cell padding CSS entirely dropped — all rows collapse to tight single-text-line height

### html_table_cellpadding

- MAJOR | all | p1 | cells render as a single outer rectangle where Word draws a box around EVERY cell, separated by the cellspacing gap (the rule colour matches Word's grey; the cell font now matches too)
- MAJOR | html | - | interior cell borders missing (outer box only)
- MEDIUM | html | - | cellpadding=15 dropped — rows collapse to tight text height

> **NEXT — per-cell borders. The two blockers behind three reverts are now both cleared.** HTML4
> §11.3.1 makes `border` a border around the table AND all its cells, but `ParseTable` sets only
> `DefaultBorders`, so `TableLayout.ResolveCellBorders` takes every interior edge to
> `BorderEdge.None` and the table renders as a bare rectangle. The model is DETACHED, not collapsed:
> Word imports `cellspacing` (2px when absent) as `w:tblCellSpacing`, drawing an outer frame plus a
> box per cell with a gap (probe: `cellspacing` 0 / default / 10 → collapsed / small-gap / wide-gap),
> so the default case wants `CellSpacingPoints` and only `cellspacing=0` wants the inside edges.
>
> **FOUR attempts (2026-07-24) were all reverted — the per-cell borders are NOT font-blocked, that
> was a wrong diagnosis.** The first drew a collapsed grid (wrong model). The second used the detached
> model but measured **+0.0472 AE / −0.1001 SSIM** because the autofit column algorithm ignores
> spacing: `PageRendererBase` insets each cell box by `CellSpacingPoints` out of the column width
> while `CalculateContentBasedColumnWidths` never adds it back, so the spacing came out of the CONTENT
> area and re-wrapped text. The third added the `+2 × CellSpacingPoints` per-column term and rendered
> a pixel-correct detached grid in the probe. The fourth ran that same change AFTER the host-font fix
> landed (so cells are now Word's Aptos, columns sized right) — and STILL measured **+0.0489 AE /
> −0.1029 SSIM**. So the font was never the border blocker.
>
> **What the fourth attempt's crops actually showed (two real, separate root causes):**
> 1. **Fixed-width tables stretch to full width.** `html_table_styled`'s second table declares 100px
>    / 200px columns; Word renders it compact (~622px) with detached cells, Morph stretches it to the
>    full text column (~976px). This is a column-width bug independent of borders — the detached boxes
>    just make it obvious. Tracked separately in the html_table_styled findings below.
> 2. **On coloured tables the interior rules break up Word's continuous colour bar.** Word draws the
>    styled table's blue header as one solid bar; adding per-cell borders/gaps splits it into three
>    boxes with visible seams, which is what drove the biggest SSIM drop (html_table_styled −0.0147).
>    The border/background-fill interaction needs Word-probing before the borders can land on coloured
>    tables.
>
> **So the retry order is: fix fixed-width column sizing first (root cause #1), probe the
> border-vs-fill interaction (root cause #2), THEN add the borders.** The correct model itself is
> settled — detached `CellSpacingPoints` from the attribute, inside edges only for `cellspacing=0`,
> `+2 × CellSpacingPoints` per column in `CalculateContentBasedColumnWidths` (shared with DOCX
> `w:tblCellSpacing`, so `TableCellSpacingTests` / `TableCellSpacingCollapseTests` move with it), and
> `cellSpacingPoints` folded into the table's negative `IndentPoints`. The rule colour (grey
> `B2B2B2` at 0.75pt) already landed; the host-font fix landed and helped tables broadly, just not the
> borders.
>
> **Full-width auto tables stay on the old footing for now.** Word widens a `width:100%` table by the
> cell inset at each end so its text still spans the text column; Morph resolves columns against the
> container width alone, so with a fixed total width the padding drives the COLUMN distribution and
> the correct padding moved every column (`html_complex` +0.015 AE alone). The px→pt padding rule and
> the outdent are applied only to auto-width tables until that is modelled.

### html_table_styled

- MAJOR | all | p1 | cell gridlines/borders missing — only a thin outer rectangle is drawn
- MINOR | all | p1 | fixed-width table's flexible third column runs ~10px wide (Morph ~156px vs Word's 146px): Word renders the two DECLARED columns slightly wider than their 100px/200px (161/315 against 156/312), so its flexible remainder is correspondingly smaller. Table width and the first two columns now match — cell text lands within 2-9px of Word
- MAJOR | html | - | cell borders missing (outer box only)
- MEDIUM | html | - | table width styling ignored — the width:100% styled table renders content-width (206px of the 624px content box) and the 100px/200px fixed columns are exported as width:100pt/200pt (~33% too wide)

### hyperlinks

- MINOR | all | p1 | whole text block runs ~5-8% narrower than Word (lines end short of Word's line ends; link color/underline and line breaks correct)
- CLEAN: html

### hyphenation_auto

- MEDIUM | all | p1 | automatic hyphenation not applied: paragraph 1 lacks Word's "telecommunica-/tion" end-of-line break and paragraph 2 lacks Word's "hy-/phens" break, so both paragraphs break at different words
- CLEAN: html

### hyphenation_suppressed

- MEDIUM | all | p1 | automatic hyphenation missing in paragraph 3: Word breaks "Telecommu-/nications" and "syl-/lables" but backends end line 1 early at "again." and redistribute the paragraph's lines (same line count, clearly different breaks)
- CLEAN: html

### icon_svg

- MINOR | all | p1 | star icon drawn slightly larger than Word (skia ~4%, imagesharp/pdf ~9%: 86px vs 79px) and positioned up to ~12px higher/left
- CLEAN: html

### icon_with_text

- MINOR | all | p1 | second paragraph ("Icons can be placed inline...") sits 10-13px higher than Word because the line containing the inline star icon gets less line height
- CLEAN: html

### icons_multiple

- MAJOR | imagesharp,pdf | p1 | red and green star icons both rendered blue; Word and Skia show blue/red/green. **Not a recolor bug — an SVG-support gap.** Each icon carries an `a:svgBlip` SVG variant (3 in the document) and the colour lives ONLY there: the three PNG fallbacks are byte-identical (one sha, all blue 65,132,243). Skia renders the SVG via `SvgPreprocessor` + Svg.Skia and is right; `Morph.ImageSharp` and `Morph.Pdf` contain no SVG code at all, so they draw the identical fallbacks and cannot do better. Closing it means an SVG rasterizer in both backends — a new dependency each, not a fix in Morph's own code
- CLEAN: skia, html

### image_cropping/01

- MAJOR | html | - | same crop ignored in HTML export — image shown uncropped (small "Sample", full gradient) instead of the centre-50% crop

### image_rotation/01

- MAJOR | skia,imagesharp | p1 | 45°-rotated image not clipped to its layout box: full diamond drawn (386px tall vs Word's 257px clipped band), top corner overlaps and partially obscures the "Below is a sample image:" text, bottom corner extends ~44px lower than Word; rotation center also ~20px higher
- MAJOR | html | - | rotation missing in HTML export — image shown as plain unrotated rectangle

### image_wrap_square

- MINOR | imagesharp | p1 | Links paragraph wraps differently: "downloadable" pulled up to the first line ("...or even downloadable / documents...") vs Word's break after "even"
- MINOR | all | p1,p2 | cumulative vertical drift of blocks (~10-20px up on p1, down on p2, pie chart slightly offset) with structure intact
- MEDIUM | html | - | Simple Tables data rows styled like headers — all body cells bold and centered (Word shows left-aligned regular text; the complex table below is correct)
- MINOR | html | - | last line of the "Some images, such as charts or graphs..." paragraph rendered centered ("link on the image.") instead of left-aligned

### inline_group_rotation

- MINOR | all | p1 | residual differences in the rotated nested pieces (unvetted at crop level)
- MEDIUM | all | p1 | decorative double border frame renders since 2026-07-19 (outline-only emission); residual: ornamental corner details and the red accent line's exact geometry differ from Word
- MINOR | all | p1 | "Menu" lacks its white+mint outlined glyph style (the text colour itself is correct now)

### inline_shape_arrows

- MEDIUM | skia,imagesharp | p1 | colored-arrow row drawn ~27px left and ~25px above Word's position (arrow sizes themselves correct, gap to "Arrow variants:" label visibly too small)
- MEDIUM | pdf | p1 | all four colored arrows shifted up-left ~20px
- MINOR | all | p1 | "Thinner stroke" arrow and its label paragraph sit ~10-17px higher than Word
- CLEAN: html

### labels/01

- MEDIUM | all | p1 | line spacing inside every label much looser than Word — the 4-line Name/address block spreads to fill the entire cell height, ending ~20px lower and visibly misaligning text with the icons
- CLEAN: html

### labels/02

- MINOR | skia,imagesharp | p1 | TO:/FROM: text block sits ~8px higher than Word

### labels/03

- MEDIUM | all | p1 | ticket text leading ~1.5x looser than Word — "TICKET" sits ~15px lower and the 3-line block spreads down the ticket
- MEDIUM | html | - | dotted tear-line rules missing from all tickets

### labels/04

- MEDIUM | all | p1 | the small light-blue hexagon accent per label doesn't render — it is a gradient-filled hexagon preset with no built contours, so the gradient guard drops it (see #5)
- MAJOR | html | - | same hexagon artwork failure: no blue hexagon accents on any label
- MINOR | html | - | blue accent bars vertically misaligned with their label text (bar tops start ~a line below "Name") and flat cyan instead of gradient

### labels/05

- MAJOR | html | - | Label text detached from labels: all 30 label graphics render background+empty dashed box only (single column), with the 30 "Name / Address / City ST ZIP Code" text blocks dumped afterwards as a separate plain-text grid

### labels/06

- MEDIUM | all | p1 | "EVENT NAME" text block sits ~26px (about one line) too high inside every ticket, with an enlarged gap between the EVENT and NAME lines
- MINOR | all | p1 | Whole ticket grid shifted up ~13px on the page
- MINOR | skia,imagesharp | p1 | Centre "ADMIT ONE" pair crowds right against the middle divider instead of sitting inset within each ticket's inner edge
- MAJOR | html | - | Tickets decomposed into unassembled fragments: empty blue ticket shapes first, then a ~21-row dump of star glyphs, then ~20 stacked "ADMIT ONE" lines, then bare "EVENT NAME" blocks

### labels/07

- MEDIUM | all | p1 | "[Name]" sits at the top-left of each box instead of centered
- MAJOR | html | - | All eight "[Name]" texts are absent from the HTML export (zero occurrences in the file)

### labels/08

- MAJOR | html | - | Overlapping mass of purple ticket shapes followed by detached near-invisible white texture images, then all "YOUR EVENT NAME / TICKET" text blocks rendered separately below in cream-on-white (barely legible)

### labels/09

- MINOR | all | p1 | Uniform few-pixel shifts of ticket text, rules and borders throughout (visible ghosting in diff, structure intact)
- MINOR | html | - | Stub ticket numbers ("00 01" etc.) partially clipped by the narrow red stub columns

### labels/10

- MAJOR | all | p1 | Text block sits far too high: "YOUR NAME" overflows above the label shape into the row gap (columns 1 and 3) or hugs the very top (column 2), leaving the lower half of every label empty where Word centers the block
- MEDIUM | all | p1 | Teal rule that belongs directly under "YOUR NAME" renders detached lower in the label (under "Street Address" or "City, St Zip" depending on column)
- MEDIUM | html | - | "YOUR NAME" overflows above each label block instead of sitting inside it
- MINOR | html | - | Teal rule under "YOUR NAME" missing entirely

### labels/11

- MAJOR | html | - | label text invisible: white text emitted on the white page because the brush image is placed above the text block instead of behind it
- MEDIUM | html | - | the 30 brush images render stacked in a single left-hand column instead of the 3-across label grid

### labels/12

- MEDIUM | all | p1 | label columns pulled inward (~30px: col1 text sits 32px right, col3 34px left of Word, rules move with them) and the whole grid starts ~20px lower

### labels/13

- MAJOR | html | - | same purple arrow rendered vertically flipped in the HTML export (the HTML exporter applies no transforms — rotation or flips — to pictures)

### labels/14

- MAJOR | html | - | background artwork missing and text emitted white-on-white — export appears completely blank

### labels/15

- MINOR | all | p1 | script "from" glyph drawn ~9px further left and slightly wider than Word, nearly touching the preceding label's text; text rows/columns otherwise align within 1-2px
- MINOR | html | - | cream page background stops about two-thirds down the strip, leaving the last rows of labels on a white background

### labels/16

- MAJOR | html | - | all 30 bear icons missing from the HTML export (colored text only)

### letters/01

- MEDIUM | all | p1 | decorative header/footer bands are far too saturated — the render carries 5.7x Word's mean chroma (re-measured 2026-08-08; the earlier "~15%" understated it): circle sampled (138,180,254) against Word's paler (182,199,238), band (50,66,93) vs (59,64,77). The theme accents are purple (accent1 AD84C6), so the shapes resolve the right hue — the residual is an under-applied lightening transform (lumMod/lumOff), not the wrong colour the finding first claimed. Fills sit in a group so the transform chain is group-level
- MEDIUM | all | p1 | Recipient address block starts ~37px lower than Word, pushing the salutation, body and signature down by the same amount (was ~120px; the cell-measure contextual-spacing fix of 2026-08-08 took most of it, and the residue is upstream of the address block — the block is already ~19px low where its first line starts)
- MAJOR | html | - | decorative header/footer shape graphics missing entirely

### letters/02

- MINOR | all | p1 | body text block uniformly shifted down ~half a line
- MEDIUM | html | - | frame images present since the z-sort but the HTML export applies no duotone either (ships original blue-toned bytes)
- MINOR | html | - | right-aligned "Letter of Recommendation" title and Date lose their alignment (title centered-left, Date lands beside recipient block)

### letters/03

- MEDIUM | all | p1 | word "Service" broken mid-word across lines without hyphen ("...reach out to our Customer S / ervice team") in paragraph 2
- MINOR | all | p1 | body text block uniformly shifted down ~two-thirds of a line
- MAJOR | html | - | top and bottom blue gradient banner graphics missing entirely

### letters/04

- MAJOR | html | - | recipient address block and date rendered overlapping the navy header banner (first lines sit on top of the band)

### letters/05

- MAJOR | html | - | logo cluster malformed into a blue blob (overlapping circle + rectangle), hidden teal/purple shapes exposed
- MAJOR | html | - | purple decorative circles overlap the "Taylor Phillips" address text in the second letter section
- MEDIUM | html | - | decorative shapes inconsistent across sections: dashed elements absent everywhere and the third letter section has no shapes at all

### letters/06

- MINOR | skia,imagesharp | p1 | "SCHOOL OF" and "FINE ART" display titles sit 20-28px higher than Word
- CLEAN: html

### letters/07

- MEDIUM | all | p1 | second body paragraph wraps to 4 lines vs Word's 3 (text breaks earlier, column effectively narrower)
- MINOR | html | - | the header pattern band stops short of full page width (ends at x=817 of 1024)

### letters/08

- MEDIUM | all | p1 | first body paragraph wraps to 3 lines vs Word's 2 ("...recent visit to New / York.") and second paragraph breaks at different words, shifting the letter body
- MEDIUM | all | p1 | large signature "Joseph Price" rendered in bold/heavy weight instead of Word's light strokes
- MINOR | html | - | signature "Joseph Price" bold vs Word's light weight

### letters/09

- MEDIUM | all | p1 | first body paragraph wraps to 5 lines vs Word's 4 ("...advanced financial / forecasting."), pushing body text and the footer contact block ~1 line lower

### letters/10

- MEDIUM | all | p1 | body wraps differ (first paragraph 5 lines vs Word's 4, breaks at "regional / manager")
- MAJOR | html | - | signature image broken — placeholder "Image of signature" shown instead of the script signature
- MINOR | html | - | grey page background / white card styling not exported (plain white page)
- MINOR | html | - | recipient block lines separated by full blank-line gaps vs Word's tight block

### letters/11

- MINOR | all | p1 | body lines break at slightly different words (para 1 breaks after "Importers" vs "Importers to"), same line counts
- MINOR | html | - | recipient address block lines separated by paragraph gaps vs Word's tight lines

### letters/12

- MEDIUM | pdf | p1 | body text drifts progressively lower — "Jordan Mitchell / CEO" signature ends ~2 lines below Word (bottom contact block stays in place)
- MAJOR | html | - | bottom-right diagonal-stripe corner decoration missing entirely
- MINOR | html | - | paragraph spacing collapsed — paragraphs run together

### letters/13

- MINOR | skia,imagesharp | p1,p2,p3 | whole content block (text and NP logo) sits ~1-1.5 lines higher than Word
- MINOR | pdf | p1,p2 | content block ~1 line higher than Word
- MINOR | imagesharp,pdf | p1,p2,p3 | hatched (striped) banner wedges render as solid fills — imagesharp and pdf flatten several wedges (e.g. the second tile's upper and left wedges); skia now matches Word
- MINOR | imagesharp | p1,p3 | several paragraphs wrap at different words than Word (e.g. "...personal taste. Go /", "built-in font / combination")
- MAJOR | html | - | the three letter copies get inconsistent body-column widths (~586px, ~471px, ~622px)
- MINOR | html | - | page-3 left-edge banner rendered as an inline horizontal strip (side placement/rotation lost)

### line_numbers_continuous

- MINOR | skia,imagesharp | p1 | Margin number digits rendered noticeably smaller than Word's (Word draws them at body-text size)
- MINOR | html | - | Line-number gutter omitted entirely in HTML export (per-layout-line feature; HTML reflows)

### line_numbers_count_by_5

- MINOR | skia,imagesharp | p1 | Margin digits smaller than Word's body-size digits
- MINOR | html | - | Line-number gutter omitted entirely in HTML export

### line_numbers_custom_distance

- MINOR | skia,imagesharp | p1 | Margin digits smaller than Word's
- MINOR | html | - | Line-number gutter omitted entirely in HTML export

### line_numbers_restart_page

- MINOR | all | p1 | Body text line endings fall slightly short of Word's (6-11px on a 542px line)
- MINOR | skia,imagesharp | p1 | Margin digits smaller than Word's
- MINOR | html | - | Line-number gutter omitted entirely in HTML export

### line_numbers_restart_section

- MINOR | all | p1,p2 | Body text ~9% narrower than Word, line endings fall short
- MINOR | skia,imagesharp | p1,p2 | Margin digits smaller than Word's
- MINOR | html | - | Line-number gutter omitted entirely in HTML export (both sections' text content present and ordered correctly)

### line_numbers_suppressed

- MINOR | skia,imagesharp | p1 | Margin digits smaller than Word's
- MINOR | html | - | Line-number gutter omitted entirely in HTML export

### line_spacing_at_least

- MEDIUM | all | p1 | "At least" line spacing under-applied: gaps between the 12/18/24/36pt paragraphs are ~15–25% smaller than Word's (e.g. 24pt→36pt gap ≈66px vs Word's ≈88px at 150dpi), leaving the 36pt paragraph ~1 line height higher
- CLEAN: html

### line_spacing_exactly

- MEDIUM | all | p1 | "Exactly" line spacing under-computed: lines drift progressively upward vs Word — 24pt-spaced line ~6px high, 36pt-spaced line ~18-21px (nearly a full line) high, so the block ends clearly higher
- CLEAN: html

### menus/01

- MEDIUM | all | p1,p2,p3 | entire page content (text and, on p2/p3, the floral art) sits ~30-65px (150dpi) higher than Word with slightly compressed section spacing; offset is largest at the p3 title (~60px)
- MAJOR | html | - | light-grey page background missing: all text renders on white, only the first flower image block carries the grey (Word shows grey behind all 3 pages)

### menus/02

- MEDIUM | all | p1 | header ("New Year's Eve / CELEBRATION / MENU") and all menu items are centred on an axis ~36px left of Word's, shifting the whole text column left

### menus/03

- MAJOR | all | p1 | the gold "EVENT TITLE" heading now renders (SdtCell cells reach the model); its gold rule lines still render misplaced/mis-sized
- MAJOR | skia,pdf | p1 | full-height gold divider line between the two columns is missing (only a short gold tick at top-right remains); ImageSharp draws it
- MEDIUM | all | p1 | both text columns shifted left (instructions column ~25% of page width left of Word) and vertically compressed (menu column ends ~65px high)
- MINOR | skia,imagesharp | p1 | numbered steps render the number at the left indent with the step text centered separately, leaving a large gap (Word centers "2. Press Ctrl+C" as one unit; PDF matches Word)
- MINOR | skia,pdf | p1 | full-page navy background tint fractionally off Word's (em≈1.0 but below visible threshold)
- MAJOR | html | - | all content renders below the navy panel on the white page, leaving the navy block empty and every white-colored text run invisible (only gold headings and step titles visible)

### menus/04

- MEDIUM | all | p1 | colored meal cells end ~43px (~0.29") short on the right (fill to x=567 vs Word's 610), leaving a white strip along every week table
- MINOR | all | p1 | week tables drawn ~13-20px higher with slightly shorter rows; upward drift accumulates down each table
- MAJOR | html | - | table layout broken: the colored meal-entry column collapses to ~30px stubs instead of wide writing areas
- MINOR | html | - | stray light-grey rectangle rendered below the week tables

### menus/05

- MAJOR | pdf | p1,p3 | DESSERTS section drifts ~1in low and its body text is drawn on top of the bottom decorations (birds/leaves on p1, cornucopia+birds on p3)
- MAJOR | skia,imagesharp | p3 | DESSERTS body text overlaps the cornucopia and bird decorations at the page bottom
- MEDIUM | skia,imagesharp | p1 | sections accumulate ~80px downward drift — DESSERTS heading/body sit ~3 line heights lower than Word, last line grazes the bird decoration
- MINOR | all | p2 | two-column sections drift down slightly (~10-25px by the DESSERTS block), structure intact
- MEDIUM | html | - | page-1 and page-3 section headings render as "Appetizer"/"First Course" in a fallback bold font, losing the decorative all-caps display font Word uses (page-2 headings are correct)
- MEDIUM | html | - | menu title/text emitted below the green blob instead of overlaid on it, and page-2's orange blob is linearized above page-1 content (anchored art separated from its text)

### menus/06

- MAJOR | pdf | p3 | sheep logo at bottom right renders white, nearly invisible on the pale background. **Not a colour or SVG-fallback bug — PDF draws the WRONG PICTURE.** The document holds four inline `pic:pic` images: rooster (`image13.png` / `image22.svg`, both `#E20000`), a genuinely WHITE icon (`image32.png` / `image4.svg`, both `#F7F5EF`), the rooster again, and the sheep (`image5.png` / `image63.svg`, both `#E20000`). Counting exact pixels per page: the white icon belongs on p2 and ALL backends draw it there (~2140px); on p3 Word and Skia draw zero white while PDF draws 5480 — it renders p2's white icon a SECOND time, at the sheep's slot. So the model is right (Skia and ImageSharp are correct from it) and the defect is PDF-side picture identity. Ruled out: the SVG→raster fallback (both branches select identically to ImageSharp's), `PdfRenderContext.imageCache` (keyed by byte[] REFERENCE), `PdfTextEngine.layoutCache` (keyed by `ParagraphElement`, a class, so reference equality), and the sub-8-bit indexed PNG soft-mask bug that caused `cards/19` — both of menus/06's icons are depth-8, and this scenario did not move when that was fixed. Look next at how inline image items survive a page split
- MINOR | all | p1,p2,p3 | menu items drift progressively downward (~half a line by page bottom)
- MAJOR | html | - | page-3 red bars absent from the page-3 block (no bar at its top or bottom); only two bars render, one at the document top and one cutting through the page-2 "BISTRO MENU" title
- MEDIUM | html | - | pale-blue background covers only page-1 content; page-2/3 content renders on white

### menus/07

- MINOR | all | p1 | food photos shifted right ~15-25px

### menus/08

- MEDIUM | all | p1 | right instruction block wraps at a different word than Word ("...select the whole / cell.)" vs Word's "...select the / whole cell.)")

### menus/09

- MEDIUM | all | p1 | inner decorative frame renders since 2026-07-19 (outline-only emission; the +0.003 metric tick is the new-ink offset penalty — crops confirm the double frame + corner accents present). Residual: frame geometry slightly off Word's (octagon corner cuts vs rounded corners)
- MINOR | all | p1 | chalkboard ~16px narrower and ~9px shorter than Word (right/bottom edges pulled in)

### mixed_breaks

- MEDIUM | all | p3 | "Content after column break." starts ~1.5 line heights higher than Word, which places the text lower on the page after the column break
- CLEAN: html

### newsletters/01

- MAJOR | all | p1,p2,p3,p4 | White frame borders missing from every photo; images rescale to fill the full frame box so the visible crop differs (e.g. p3 mother-and-daughter photo shows extra scene at smaller scale, p1 kitchen photo sits flush with the tan panel edge)
- MEDIUM | all | p2 | Right-column photo box, its caption and the whole "Adding your own message" section pulled up ~130px vs Word
- MEDIUM | all | p1 | Left-column caption, "Happy holidays from our family to yours!" heading and body text sit ~50px higher than Word due to the resized kitchen photo
- MEDIUM | skia,imagesharp | p4 | Right-column article ("Write with ease using Editor" heading + body) sits ~2 lines higher than Word
- MEDIUM | pdf | p1,p4 | Right-column blocks drift ~2 lines lower than Word (p1 pull-quote, p4 big photo + caption end noticeably lower)
- MINOR | all | p1,p2,p3,p4 | "Page N" footer sits ~25-35px higher, overlapping the content panel bottom instead of sitting on the grey footer band
- MINOR | all | p1,p2,p4 | Body paragraphs re-wrap at different words than Word (line counts mostly unchanged)
- MAJOR | html | - | "Our family newsletter" title and "December 20XX" date are invisible — emitted as white (#ffffff) text with no red background behind them
- MAJOR | html | - | Background panels detach from content: an empty green/pink panel composition renders at the very top of the document, and page 1/2/4 text flows on plain white without its red/green page backgrounds (only page 3's red block wraps its photos)
- MEDIUM | html | - | Decorative illustrations (Santa, snowman, penguins, elves) render as detached images floating between sections instead of positioned inside their page compositions

### newsletters/02

- MAJOR | all | p2 | "The observer" byline and paragraphs are shifted ~80px left, overlapping the right edge of the numbers photo
- MEDIUM | all | p2 | Main-column paragraphs wrap to more lines than Word (bold lede 6 lines vs 5; following paragraph 5 vs 4)
- MEDIUM | all | p2 | "Work with the industry's best" column renders wider with fewer, longer lines, so the column ends ~1in higher than Word
- MINOR | all | p1 | Entire page content (masthead, sidebar, hero image, article) sits ~10px higher than Word
- MEDIUM | html | - | Hero network-figures image renders above "The Review" masthead — wrong content order vs the document

### newsletters/03

- MEDIUM | all | p1,p3 | Body text wraps to more lines than Word (p1 INDUSTRY NEWS lead paragraph 2 lines -> 3, both paragraphs; p3 HARNESSING "Have other images..." paragraph gains a line pushing "Once the image..." down); remaining pages show shifted break points from the same ~7% wider text
- MINOR | html | - | Inter-paragraph spacing lost — consecutive body paragraphs run together with no gap

### newsletters/04

- MAJOR | all | p3 | Grey "Breaking news" table section grows past the bottom margin: column text is clipped mid-line at the page edge and the page footer ("3 ——— Issue 10") is missing entirely
- MAJOR | all | p3,p4 | Spurious dark table cell borders drawn around section cells (boxes around byline column, text columns, pull-quote circle cell, "Save time..." cell, grey-box cells) — Word renders these tables borderless
- MEDIUM | all | p1,p2,p3,p4 | Line breaks differ from Word throughout, redistributing text across the newspaper columns (scoop/next-hot columns and sidebars break at different paragraphs, blocks end noticeably lower)
- MINOR | all | p1,p2,p3,p4 | Full-width banner photos slightly oversized vs Word (right edge extends further, solid strip in diffs; captions shift down accordingly)
- MAJOR | html | - | Pull-quote circular beige background missing — quote renders as plain text in a bordered cell
- MEDIUM | html | - | Spurious table cell borders visible around the "next hot" and Breaking-news section cells

### newsletters/05

- LARGELY FIXED 2026-08-06 (last real flip-gate stand): `CanonicalParagraphMeasurer.LayoutLines` — the measurement view feeding `TableHeightCalculator` — computed line heights from the font pitch alone, ignoring inline images, while placement (`LayoutLineContents`) maxes them in. A cell whose paragraph holds only the 213pt school photo measured as a ~12pt mark line, so every row below overlapped it by the difference (body text drew ON the photo). One-line fix: measure maxes with image heights, so measure = placement. Engine PDF vs Word: p1 36.8 → 17.2 (exact production tie), p2 21.2 → 8.7 (production 34.9 — engine 4× better, band count = Word's), p3 29.1 → 17.4 (tie), p4 26.8 → 19.1 (production 17.8; residual is a bottom block where both paths err ~35px in opposite directions). Corpus: newsletters/03/07 and brochures/07 (−151/−181 on p2) improved; regressions concentrate on collage covers (bp/12, brochures/04 p2, newsletters/04 p2) where images previously overlapped in ways that accidentally scored better — those pages carry separate unrelated defects (see bp/12's cover entry).
- MEDIUM | all | p1,p3 | body copy under "Welcome back to school!" wraps to one extra line (17 text bands vs Word's 16), block ends 26-80px lower
- MEDIUM | skia,imagesharp | p1,p3 | "Welcome back to school!" heading and body start ~45-50px lower than Word (extra gap inserted below the school photo)
- MEDIUM | skia,imagesharp | p1,p3 | sidebar "Ms. Tanaka" contact block ~22px and "Upcoming Events" block ~48px lower than Word (PDF within 10px)
- MEDIUM | all | p2,p4 | sidebar "Fall highlights" block sits ~210-235px (~1.5 inch) higher than Word
- MEDIUM | skia,imagesharp | p2,p4 | "Our next area of focus" heading and following paragraphs ~35-38px lower than Word (extra gap below classroom photo; PDF matches)
- MINOR | all | p1,p3 | school photo ~1% narrower than Word (right edge 7-9px short), lighting the whole photo in the diff
- MINOR | all | p2,p4 | classroom photo shifted ~9px left at identical size
- MAJOR | html | - | first (green) edition's chrome rendered in the blue edition's colors: pale-blue sidebar band and dark-blue corner shapes instead of lime band and dark-green shapes (text accents stay correctly green)
- MEDIUM | html | - | decorative page-corner shapes render mid-flow and overlap body text ("Recent highlights" heading/paragraph runs across a pale-blue quarter-circle)

### newsletters/06

- MAJOR | all | - | page-count mismatch: all three backends produce 6 pages vs Word's 4 (each 2-page edition spills onto a 3rd page; text sets ~10% wider so every column wraps earlier and blocks run longer). **NB: this mismatch means the scenario records NO per-page AE/SSIM at all (`PageDiffs` is null), so nothing here is metric-judgeable — crop-vet by hand.**
- MEDIUM | all | p1,p4 | masthead navy bar text loses its per-letter tracking: skia/imagesharp render bold words with huge word gaps, pdf renders compact bold text with no letter-spacing
- MEDIUM | all | p1,p4 | masthead contact line wraps to two lines ("www.sycamoremiddle.org" drops to its own line) vs one line in Word
- MEDIUM | all | p2 | "NOTES FROM THE COUNSELORS" left column collapses to ~1-2 words per line (column far too narrow) and the text snakes to the bottom of the page
- MEDIUM | all | p3 | overflow spill pages render on a white background instead of the section's page color (light blue / yellow)
- MINOR | all | p1,p4 | dotted-ornament window around "SYCAMORE NOTES" title is taller than Word's (dot rows beside the title partially dropped); small "SYCAMORE NOTES" strip logo on p2/p5 wraps to two lines
- MAJOR | html | - | page backgrounds wrong/missing: first edition gets a yellow background instead of light blue, and all content after the first section break renders on white (no blue/yellow backgrounds)
- MAJOR | html | - | all decorative icons render as filled squares (same placeholder issue as raster backends)
- MINOR | html | - | big title renders as "SYCAMORENOTES" — the word space collapses to the same width as the letter-spacing gaps

### newsletters/07

- MEDIUM | all | p1 | "MODERN LIVING" masthead, subtitle and sidebar headings ("WHAT'S NEW", "TAKE A LOOK INSIDE"...) lose expanded letter-spacing: skia/imagesharp show bold words with big word gaps, pdf compact bold
- MEDIUM | imagesharp | p1,p2 | body and sidebar text rendered in bold weight throughout vs Word's regular Century-Gothic-style face
- MEDIUM | all | p1,p2 | body text sets visibly larger than Word so paragraphs wrap to extra lines (lead paragraph 6 lines vs Word's 5)
- MEDIUM | html | - | content order wrong: living-room photo renders above the "MODERN LIVING" masthead/title block instead of below it
- MINOR | html | - | black accent rule renders overlapping the "Your guide to buy or rent" subtitle text

### newsletters/08

- MEDIUM | all | p1 | right-column masthead block ("HOUSE & HOME NEWS / WINTER ISSUE / EDITION 09, VOL. 10") and intro paragraphs sit ~40px (≈2 line heights) higher than Word
- MINOR | all | p1,p2 | decorative swoosh/band boundaries off by several px and the light-blue contact strip plus its text sit ~15px lower than Word
- MEDIUM | html | - | cover photo present since 2026-07-19 but as an unclipped rectangle (no freeform crop in the HTML export)
- MAJOR | html | - | page title "HOUSE & HOME NEWS" invisible — dark-navy h1 lands on the dark-navy background shape, only a letter fragment shows through the light swoosh
- MAJOR | html | - | "Join us on this journey..." paragraph invisible — white text on white background (no shape behind it)
- MEDIUM | html | - | background shapes misaligned with content: contact footer line has no light-blue band behind it, and "From interior design trends..." white text sits on pale blue instead of navy

### newsletters/09

- MAJOR | all | p1 | masthead "NEWS TODAY" wraps onto two lines (Word: single line), doubling the banner height and triggering the reflow
- MEDIUM | all | p1,p2,p3,p4 | body text wraps 1-2 words earlier per line in every column (wider glyph advances); headlines "New program launches" and "The scoop of the day" also wrap to two lines
- MAJOR | all | p1 | bottom teaser band (School budget / Police prevent crime / Athlete sets record) pushed to the page edge and clipped mid-content; bodies spill to p2 (PDF spills the entire band including headers)
- MEDIUM | all | p3,p4 | photo captions detach from their images and collide with neighbouring content (caption box overlaps the section rule above "Mirjam Nilsson" on p3; caption floats over the "scoop of the day" columns on p4)
- MAJOR | html | - | several article bodies dropped entirely: "Community rallies for charity" two-column body, Vanja Jovanovic's "The latest breaking news of the day" body (empty table row in export), and the Takuma Hayashi article's left-column paragraphs
- MEDIUM | html | - | teaser-band table column widths wrong — "Police prevent crime" column is ~one word wide, wrapping every word (Word has three equal columns)
- MINOR | html | - | full four-sided borders drawn around the bridge sidebar box and the pull-quote box where Word shows only top/bottom rules

### newsletters/10

- MEDIUM | imagesharp | p1 | the four green section headings ("Something that made me smile today…", "Currently dealing with...", "Thankful for...", "Looking forward to...") rendered bold instead of Word's light weight
- MINOR | all | p1 | content drifts progressively upward, ~15px by the bottom rule (each section slightly shorter than Word); DD/MM/YYYY and all rules offset
- MAJOR | html | - | "My Journal" title invisible — white h1 rendered below the leaf banner on the white page background instead of overlaid on the image
- MEDIUM | html | - | section headings bold dark-green instead of Word's light weight

### newsletters/11

- MINOR | all | p1,p2 | Small uniform vertical offsets of text blocks (~1 line): p2 columns and header sit ~20px higher than Word, p1 byline "By Robin Zupanc" sits ~1 line lower
- MEDIUM | html | - | Floating photos emitted in wrong order: hero photo appears before the "LAWN AND LANDSCAPE" masthead, group photo appears before the "Tony's landscapes and more" headline

### newsletters/12

- MAJOR | skia | p1 | "ISSUE NO | MONTH - MONTH YEAR | VOLUME" line drawn overlapping the bottom of "TITLE HERE" (glyphs collide)
- MEDIUM | imagesharp,pdf | p1 | Title lines shifted ~40-50px down so "ISSUE NO..." line touches "TITLE HERE" (Word has ~45px clear gap)
- MAJOR | all | p2 | Text right of the olive quote box is positioned left/too wide and drawn over the olive stripe graphic (text/graphic overlap, different line wraps than Word)
- MINOR | all | p2 | "MARGIE'S TRAVEL OFFERS..." section and 01-04 items shifted up ~1 line; quote text re-wrapped inside its box
- MAJOR | html | - | Pull-quote text ("We don't merely book your travel..." + "- Henriette Andersen") missing entirely; olive quote box renders empty
- MAJOR | html | - | Absolutely-positioned blocks collide: couple photo covers TOPIC 01 sidebar text, olive stripe/quote block drawn over hero photo, photo-grid images overlap TOPIC 03 and body text
- MEDIUM | html | - | Decorative overlay graphics missing (purple squiggles on photo, white photo dashes, purple dash column)

### newsletters/13

- MINOR | imagesharp | p1 | ImageSharp draws the arch crop unclipped (documented contour-mask gap)
- MEDIUM | html | - | Still-life photo content missing from the HTML export — the arch outline renders but the photo inside it does not

### newsletters/14

- MINOR | skia,imagesharp | p1 | Second title line "Newsletter" indented 20px right of line 1 (Word left-aligns both at x=92)
- MINOR | html | - | Graduation photo present since 2026-07-19; long-scroll preview metric ticked +0.015 from layout-order placement
- MAJOR | html | - | Orange "Holiday Recitals" sidebar text clipped at its left edge — first characters of every line cut off ("y Recitals", "s out", "nel, include...")
- MAJOR | html | - | Page-2 footer text "You can easily change the formatting..." missing; its orange block is mispositioned, overlapping the quote area and the "SPORTS & ACTIVITES" heading
- MEDIUM | html | - | "DECEMBER" banner rendered as red outline only (no coral fill) and the quote loses its green box background (sits directly on orange)

### nonstandard_main_part_name

Added 2026-07-21. The corpus's only package whose main part is `word/document2.xml` rather than `word/document.xml`; also the only one carrying `w:pgSz/@code`. Parsing is correct (the SDK resolves the part from the `.rels` relationship, and `<w:b w:val="false"/>` correctly un-bolds the ", John" run) — every finding below is layout.

- MEDIUM | all | p1 | "Notes:" box 249px tall vs Word's 238 (**+4.6%**, uniform at every break count). Its single cell holds only seven `<w:br/>` runs, so the height is entirely break-only line boxes and the error is the line-spacing multiplier, not the line count. **Measured against Word** by rendering probe copies of this fixture with 1/3/7 breaks through RenderHelper: Word grows 26.17px per break (12.56pt ≈ Arial 11pt's 12.649pt hhea line box) and its intercept (~27.7px = the 6+6pt before/after spacing plus borders) fits only the N+1-line model. Morph grows 27.50px = 13.16pt, because an unspecified `w:spacing/@w:line` falls back to the invented **1.04** multiplier: 12.649 × 1.04 = 13.155. The docDefaults `w:line` cascade does not reach this document — it declares `docDefaults` with no `w:spacing` at all — so closing this means changing that global fallback, which is the systemic line-pitch family and needs its own corpus-wide judging pass. Do not tune it against this one box.
- MINOR | html | - | page-number fields in an exported header/footer keep their cached value, so a pageless HTML export can read "Page 2 of 2" (`nonstandard_main_part_name`) or "Pg.02" (`business/01`). It is the document's own cached text and harmless, but meaningless without pagination — Word's own HTML export drops page fields.

### numbered_list_tracking

- MINOR | all | p1 | Glyph advance widths ~10% narrower than Word, so each list line ends slightly short of Word's (no wrap changes)
- CLEAN: html

### office_math

- MEDIUM | all | p1 | Built-up OMML fraction 1/2 (numerator stacked over denominator with fraction bar) is flattened to inline text "1/2"
- MEDIUM | all | p1 | Equations rendered in the italic body sans font instead of Cambria Math serif, and math operator spacing is dropped ("a²+b²=c²" instead of "a² + b² = c²")
- MINOR | html | - | Same math linearization in the HTML export: fraction as plain "1/2" and compact "a²+b²=c²" in italic body font

### page_borders/01

- MINOR | all | p1 | Page border box drawn ~3px (~1.5pt) closer to the page edge on top/left (border rectangle slightly larger than Word's); thickness matches
- MAJOR | html | - | Decorative page border is missing entirely from the HTML export (only the sentence is emitted, no border around content)

### page_letter

- MINOR | all | p1 | body text tracks ~8-9% narrower than Word, so each single-line paragraph ends ~0.4in short of Word's line end (no rewrap)
- CLEAN: html

### page_numbers

- MEDIUM | all | p1,p2 | footer line positioned ~0.3in lower than Word, close to the page bottom edge
- CLEAN: html

### paragraph_borders

- MEDIUM | all | p1 | vertical spacing around bordered paragraphs compressed (the three w:between boxes are visibly shorter than Word's); cumulative drift leaves the last paragraph ending ~1in higher than Word
- MEDIUM | html | - | the three w:between paragraphs render as three separate fully-boxed paragraphs with white gaps instead of one merged box with single shared rules between adjacent paragraphs

### paragraph_spacing

- MINOR | all | p1 | text tracks slightly narrower than Word
- CLEAN: html

### postcards/02

- MEDIUM | skia,imagesharp | p1 | bottom row of postcard images shifted up ~24px (inter-row gap 52px vs Word's 76px), so the two rows sit visibly closer together; PDF matches Word
- MINOR | skia,imagesharp | p2 | bottom-row card placeholder text and address lines sit ~7px higher than Word (same row-gap cause as p1)
- CLEAN: pdf, html

### postcards/03

- MEDIUM | skia,imagesharp | p1 | bottom row of postcard images shifted up ~23px (row gap collapsed, same defect as postcards/02); PDF matches Word's positions
- MEDIUM | all | p2 | placeholder "Click or tap here to enter text." rendered in a substituted bold dark sans, much larger than Word's small light script font, wrapping to 2 lines instead of 1
- MINOR | skia,imagesharp | p2 | bottom-row placeholder text and address lines sit ~7px higher than Word
- MEDIUM | html | - | same placeholder font substitution: heavy dark rounded font instead of Word's light handwriting script

### postcards/04

- MAJOR | all | p2 | boy photo rendered uncropped/too wide (393px vs Word's 315px at identical height, exposing extra background scenery) so it abuts the cupcake photo where Word has an 85px gap
- MEDIUM | all | p1,p2,p3 | vertical layout compressed on every page: card panels ~25px shorter, title-to-photo and inter-card gaps ~25-35px smaller, cumulating so the second card sits 60-140px higher than Word
- MINOR | all | p2 | cupcake photo ~5% wider (429px vs 408px) than Word
- MINOR | all | p3 | rightmost (tilted-boy) photo ~9px wider than Word
- MAJOR | html | - | p2-style cards show the same uncropped boy photo (wide field of view with extra trees) instead of Word's tight crop

### resumes/02

- MINOR | all | p1 | header text and both body columns sit ~8-13px higher than Word (uniform upward drift; artwork and divider positions otherwise match)
- MAJOR | html | - | header text block (KAI CARTER, GENERAL PRACTITIONER, CONTACT, phone/website/email) not visible — large blank white area below the black band where the white-on-black text should be
- MEDIUM | html | - | X-brush artwork sits at the left edge of the black header band instead of the right side as in Word

### resumes/03

- MEDIUM | all | p1 | Dashed rules rendered solid: header rule, rule below summary, and the dotted vertical column divider all lose their dash pattern
- MEDIUM | skia,imagesharp | p1 | SKILLS entries vertically compressed (bar touches label, no gap between entries) so the block ends ~135px higher than Word; PDF spacing matches Word
- MEDIUM | all | p1 | Education text "Laude" broken mid-word ("...Biology, Cum Lau / de, outstanding...") where Word wraps before "Laude"
- MEDIUM | pdf | p1 | Right-column HOBBIES/CONTACT blocks drift progressively lower (~2.5 line heights by the CONTACT block)
- MINOR | html | - | Email address styled as blue underlined hyperlink; Word shows plain black text

### resumes/04

- MAJOR | all | p1 | Last 2-3 lines of OBJECTIVE text overlap the yellow/pink wave shapes at the sidebar bottom (white-on-yellow, barely readable); waves also drawn ~100px higher than Word
- MEDIUM | all | p1 | Sidebar contact entries (address/phone/email/website) spaced ~2x further apart than Word, pushing OBJECTIVE down
- MEDIUM | skia,imagesharp | p1 | COMMUNICATION paragraph wraps to 6 lines vs Word's 7; PDF matches Word

### resumes/05

- MEDIUM | all | p1 | Vertical spacing compressed throughout: right-column sections end ~107px (4+ lines) higher than Word (REFERENCES at y1331 vs 1438) and the sidebar box is ~100px shorter

### resumes/06

- MEDIUM | all | p1,p2,p3 | Education/skills rows roughly double-spaced (Creativity/Leadership/Problem Solving) with thicker bars; the Problem Solving row lands ~90px lower on top of the bottom decorative rectangle (on p3 the black bar merges invisibly into the black block)
- MAJOR | html | - | Page-1's white cut-out shapes render black: black corner square on the blue section, and a black bottom bar that covers the following section's contact lines (taylor@example.com hidden)
- MAJOR | html | - | Corner strip and bottom rectangle missing entirely for the 2nd and 3rd page sections (only one pair of shapes rendered)

### resumes/07

- MEDIUM | skia,imagesharp | p1 | Bold weight lost on most template rows — College, location / Company, location / Graduation year / most Month Year runs / Project title / Activity / Leadership experience / SKILLS labels render regular; PDF keeps bold
- MINOR | all | p1 | Italic sub-lines (Bachelor of Arts Degree GPA, Relevant course work:) drawn ~0.25" further left than Word, starting left of their parent rows
- MINOR | html | - | SKILLS lines entirely bold — the value text after "Programming languages:" etc. should be regular weight

### resumes/08

- MEDIUM | all | p1 | "CONNORS" in the name rendered at the same bold weight as "MORGAN"; Word renders it in a light weight (name also becomes wider)
- MEDIUM | all | p1 | Tracking lost on spaced-caps text (UI/UX DESIGNER subtitle, SENIOR UI/UX DESIGNER job titles, section headings): skia/imagesharp produce uneven word gaps, pdf compact
- MEDIUM | all | p1 | Vertical compression: sidebar sections (ABOUT ME/EDUCATION/SKILLS) end ~112-135px (4-5 lines) higher than Word and the left column ~30-50px higher
- MEDIUM | html | - | "CONNORS" bold instead of light weight
- MINOR | html | - | Thin separator rules missing (above EXPERIENCE and between sidebar sections CONTACT/ABOUT ME/EDUCATION)

### resumes/09

- MEDIUM | pdf | p1 | Sidebar contact entries drift progressively lower (Website ~90px / ~2 lines below Word); skia/imagesharp match Word
- MINOR | all | p1 | Main content column uniformly shifted ~26px left of Word's position

### resumes/10

- LARGELY FIXED 2026-08-05 (flip-gate stand): the dominant component was `ParseSectionBreak` reading the ENDING sectPr's w:type — ECMA-376 §17.6.22 puts the break's type on the FOLLOWING section's sectPr (resumes/10: section 1 authors continuous for its own start; sections 2-3 author none → nextPage). The engine never broke at the section boundaries, so each page's decorative circle (paragraph-anchored to the next section's first paragraph) stranded at the previous page's bottom. With the lookahead fix all three circles sit at their page tops matching Word exactly and p2's band error halved (11.0 → 6.5). A Word probe en route (_probe_float_push) established that a paragraph-anchored shape NEVER pushes its anchor to the next page — it clips at the page edge. Residual (engine 6.2-6.5 vs production 3.6-4.3): two characterized components — the interior-edge row growth around the red heading rules lands one row late (heading envelopes net zero but the heading baseline sits ~3.8pt high / the post-heading gap ~3.8pt large), and cell empty-spacer marks resolve to the 11pt default font (13.43pt line) where Word's chain gives ~11.6-12.0pt (cells are excluded from the mark-rPr style resolution, DocumentParser ~9519).
- MEDIUM | pdf | p1,p2,p3 | Progressive downward drift through the page — SKILLS/ACTIVITIES sections end ~20-26px (~1 line) lower than Word
- MINOR | html | - | SKILLS bullets black instead of accent color

### resumes/11

- MINOR | all | p1 | Name block starts ~17px lower than Word, and the three thin divider rules sit ~15-20px higher (re-measured 2026-08-08; the body text between them tracks Word to +-1px)
- MINOR | html | - | Short section-divider rules above EDUCATION and SKILLS missing (only the contact-row rule is kept)

### resumes/12

- MINOR | all | p1 | the coral rule below "Manager" sits 23px high — Morph draws it at y=477-483, Word at y=500-506. Geometry is otherwise exact (123x7px at x=132-254, 861 coral pixels in both), so this is purely the inline shape's vertical placement in its paragraph
- MINOR | pdf | p1 | "VICTORIA BURKE" name block sits ~20px lower than Word

### resumes/13

- MAJOR | skia,imagesharp | p2 | Overflowing content ("Publications" heading and intro paragraph, plus a section rule) is drawn through/below the footer row and clipped at the bottom page edge
- MEDIUM | all | p3,p5 | Sidebar headings wrap mid-word — "Presentations and invited l/ectures", "Professional t/raining", "Professional a/ffiliations" (Word breaks between whole words; skia/imagesharp on their p3, pdf on its p5)
- MEDIUM | all | p1 | "ELECTRICAL ENGINEER" subtitle loses its expanded letter-spacing and renders slightly larger (raster backends add an extra-wide word gap)
- CLEAN: html

### resumes/14

- MEDIUM | pdf | p1 | vertical spacing drifts: sections sit progressively lower than Word, ~0.3in (~2 line heights) by "Skills & abilities"
- MINOR | skia,imagesharp | p1 | header word-spacing off: contact-line "Philadelphia, PA" / pipe separators cramped
- MINOR | html | - | right-aligned tab dates ("20XX – 20XX", "20XX") render inline after the job/degree titles instead of at the right margin

### resumes/15

- MEDIUM | html | - | full-width lavender band behind "Janna Gardner" missing (section-heading shading is present)

### resumes/16

- MEDIUM | all | p1 | "Chanchal Sharma" name block sits ~20px lower than Word
- MEDIUM | html | - | Skills 3-column table collapsed: cells run together on single lines ("Project management Data analysis Communication")

### resumes/17

- MEDIUM | all | p1 | two-column body misplaced: column divider and right column (Skills/Hobbies/Profile) sit ~0.8in left of Word's position, and the narrower left column wraps its text differently
- CLEAN: html

### resumes/18

- MEDIUM | all | p1 | date column ("20XX – 20XX", "June 20XX") rendered bold; Word shows regular weight
- MEDIUM | skia,imagesharp | p1 | sections drift progressively upward (~0.35in by LEADERSHIP) from tighter spacing between Experience/Education entries
- MEDIUM | html | - | experience/education tables collapsed: date cell merges onto the title line as one bold run ("20XX – 20XX Senior Editor, Surat, Gujarat"), losing the two-column layout

### resumes/19

- MEDIUM | pdf | p1,p2,p3 | 2nd and 3rd Experience entries drift progressively lower (~0.33in by the third entry) from oversized gaps between entries
- MAJOR | html | - | colored content-panel backgrounds wrong: first panel grey instead of light blue, and the yellow (2nd) and grey (3rd) panels missing entirely

### rtl_paragraph

- MEDIUM | html | - | bidi heading and paragraph render left-aligned instead of right-aligned
- CLEAN: skia, imagesharp

### section_break_continuous

- MEDIUM | skia,imagesharp | p1,p2 | Section 2 (heading plus paragraphs 2-7) flows onto page 1 immediately below section 1 instead of starting at the top of page 2 as Word does (section 2's default next-page break rendered as continuous); page 2 therefore begins at paragraph 8 instead of the "Section 2 content" heading.
- CLEAN: html

### tab_stops

- MEDIUM | html | - | tab formatting collapses to single spaces — TOC dot leaders, tabbed column alignment (Name/Role/Team/Location), left/center/right tab positions, and the signature underline fill are all lost

### table_alignment/01

- MEDIUM | html | - | table alignment lost — the centered and right-aligned tables render left-aligned (all three tables at the left margin with identical widths)

### table_autofit_no_widths

- MEDIUM | all | p1 | autofit column widths distributed differently from Word — "Full Name" column too narrow (header and "Jane Smith" wrap to two lines vs one in Word) while "Hire Date" is too wide (dates fit one line vs Word's two)
- CLEAN: html

### table_cell_margin_per_cell

- MEDIUM | all | p1 | "Left margin emphasis" text sits at the top of cell 2 (~33px too high at 150dpi): Word applies the row's largest top cell margin (cell 1's 15pt) to both cells, the renders honor only cell 2's own 2.5pt top margin
- MEDIUM | html | - | per-cell margins dropped: cell 1's large top margin and cell 2's large left margin are not represented — both texts render with identical compact padding, losing the emphasis the cells demonstrate

### table_cell_padding_varied

- MEDIUM | html | - | varied per-cell padding lost: all six cells render with identical compact padding, so "Large padding (20pt)", "More left/right" and "No padding" cells all look the same

### table_cell_spacing/01

- MEDIUM | all | p1 | cell-spacing gaps collapsed vertically: outer table border sits only ~4px from the cell borders vs Word's ~11px and cell boxes are 38-40px tall vs 47-48, so the detached-border table is 146px tall vs Word's 188 and starts ~17px higher (width and horizontal gaps are close)
- MEDIUM | html | - | detached-border effect lost entirely: renders as an ordinary collapsed-border grid with single shared lines and no gaps (tblCellSpacing ignored)

### table_default_cell_margin

- MEDIUM | html | - | large default cell margins dropped — cells render with small default padding, losing the spacious look that is this document's feature

### table_default_style

- MINOR | all | p1 | bottom double border drawn as two touching lines that read as one ~4px thick bar (second line rendered pale in skia/pdf) instead of Word's two clearly separated rules
- MINOR | html | - | bottom border renders as a single thick solid bar instead of a double rule

### table_default_style_inside_h

- MINOR | all | p1 | table rows each ~2-3px shorter than Word so the two inside-horizontal borders and following rows sit progressively higher; table bottom edge ends ~6-9px above Word's (y279 vs y285)
- CLEAN: html

### table_default_style_outer_borders

- MEDIUM | all | p1 | double-line top/bottom outer border drawn at ~half Word's line pitch so the two lines merge into a single bar: PDF collapses both edges to solid ~2px black bars, Skia's top edge becomes one grey bar, ImageSharp's bottom edge merges; Word shows two clearly separated lines spanning ~8px
- MINOR | all | p1 | bottom border sits ~9-12px higher than Word (table bottom y261 vs y273) — last row does not reserve the double-border height
- MINOR | html | - | double outer border emitted as CSS "1.5pt double", below the ~3px browsers need to draw two lines, so it renders as a single thin line

### table_diagonal_borders/01

- MINOR | all | p1 | whole table sits ~17px too high: the ~8pt gap Word leaves between the intro paragraph and the table is dropped (table top border at y179 vs Word y196; label itself at identical y)
- MINOR | all | p1 | diagonal borders and cell borders drawn ~2x thicker and fully saturated (2-3px solid black / bold red+blue X) versus Word's ~1px grey/light hairlines; directions and red-tl2br/blue-tr2bl colors are correct
- MAJOR | html | - | diagonal cell borders completely missing in HTML export — all three cells render as plain bordered boxes with no tl2br/tr2bl/X lines (nothing emitted in the markup)

### table_explicit_heights

- MEDIUM | html | - | table column widths not preserved — bare `<table>` collapses columns to content width, so "Cell 2" hugs column 1's text instead of Word's ~half-page-wide columns

### table_grid_styling_padding

- MEDIUM | all | p1 | column widths differ from Word: Full Name column narrower (header "Full Name" and "Jane Smith" wrap to 2 lines; Word keeps both on 1) while Hire Date column is wider (all four dates fit on one line; Word wraps all four dates to 2 lines)
- MEDIUM | all | p1 | rows with two text lines are ~16px (~26%) taller than Word (77-79px vs Word's uniform 62px, header 78 vs 62), making the table ~46px taller overall (bottom border y507 vs y461)
- MINOR | all | p1 | whole table displaced ~12px right: Word draws the left border at x137 (offset left of the margin by the cell left padding) while all backends start it at the margin x149; right edge likewise 1126 vs Word 1138
- CLEAN: html

### table_indent

- MEDIUM | html | - | table indentation and alignment lost in HTML export: the 480/720/1440-dxa indented tables and the centred table all render flush left, and the "100% width" baseline table collapses to content width (no width/margin emitted on four of five tables)

### table_layout_tall_row

- MAJOR | all | p1,p2 | tall second table row's company block ("Company Name", "123 Main Street") is absent from page 1 and rendered whole at top-right of page 2 instead of splitting at the page break like Word, which also exposes an extra "City, State 12345" line that Word's exact row height clips (never visible in Word).
- MEDIUM | all | p2 | letter body (Recipient Name through Title) starts ~170px (~4 line heights) lower than Word because the deferred tall row occupies the top of page 2.

### table_multipage

- MEDIUM | all | p1,p2 | table page-break lands two rows late: slightly shorter row heights let Rows 24-25 fit on page 1 (Word breaks after Row 23), so page 2 holds only Rows 26-29 vs Word's Rows 24-29.
- CLEAN: html

### table_of_contents/01

- MINOR | all | p1 | dot leaders stop a visible gap short of the page numbers and start flush against the entry text, whereas Word runs the dots up to the number and leaves a small gap after the text.
- MINOR | html | - | TOC tab leaders and right-aligned page numbers dropped — numbers render inline immediately after each entry ("Introduction 1").

### table_of_contents/02

- MINOR | all | p1 | leaders start flush after the entry text (Word leaves a small gap before the hyphen/underscore/middle-dot leaders) and the middle-dot leader ends short of the "40" that Word joins to the number.
- MINOR | html | - | TOC tab leaders and right-aligned page numbers dropped — numbers render inline after each chapter title.

### table_of_contents/03

- MINOR | all | p1 | TOC cell content shifted up a few px vs Word (5-8px skia/imagesharp, 2px pdf).
- MINOR | all | p1 | dot leaders in the narrow cell stop short of the right cell border, whereas Word runs the dots flush to the border.
- MINOR | html | - | TOC tab leaders dropped — page numbers (1, 4, 9, 15, 27) render inline after each entry instead of leader-aligned (Word clips them at the narrow cell edge).

### table_text_direction

- MINOR | all | p1 | table rows slightly shorter than Word — header/Q1/Q2 row bottoms sit ~5-12px high and the table bottom border ends ~12px early
- MEDIUM | html | - | rotated header-cell text "Quarter" rendered horizontal in HTML export (vertical bottom-to-top text direction lost)

### table_two_column_layout

- MEDIUM | all | p1 | vertical spacing inside cells tighter than Word — right-column "Line 1..10" list drifts upward reaching a full line by Line 10, and the table bottom border sits ~40px higher than Word

### tracked_changes/01

- MINOR | all | p1 | left-margin change bar (vertical revision line) missing — Word draws a black rule in the left margin beside any line carrying a revision

### wedding/01

- MEDIUM | all | p1 | invitation cards start ~0.26" higher than Word (intro-paragraph spacing compressed) and the text inside the cards drifts further up (~0.4" by the SATURDAY/RECEPTION lines) from tighter line spacing
- MAJOR | pdf | p1 | small "TO" rendered at SARA's baseline overlapping the SARA letterforms instead of on its own line between the two names
- MINOR | all | p1 | intro paragraph rewraps: "Create New Theme Colors" kept on line 2 so line 3 starts with an orphaned period (". Select your own colors...")
- MAJOR | html | - | card text (THE PLEASURE.../SARA/TO/EVAN/date block) rendered on white below the two watercolor background images instead of overlaid on them

### wedding/02

- MEDIUM | all | p1 | PLEASE JOIN + pink banner block shifted up (~0.4" card 1, ~0.7" card 2), banner slightly shorter, rosebud now touching/overlapping the banner top edge
- MEDIUM | all | p2 | yellow DATE/TIME/LOCATION banner ~1/3 shorter (compressed line spacing) and shifted up ~0.35" (card 1) / ~1" (card 2); card-2 banner overlaps the right poppy and covers the small leaves beneath it
- MEDIUM | all | p1,p2 | text left inset (~0.5") lost: "PLEASE JOIN", banner DATE/TIME/LOCATION text, and "Registered at:/RSVP" block all flush with the column/banner edge instead of indented
- MEDIUM | all | p2 | table rows end higher than Word, so the bottom leaf pair renders below the card's bottom border (outside the card)
- MINOR | pdf | p1,p2 | thin gray bounding-box outlines drawn around the rotated floral images
- MAJOR | html | - | all floral images render as a stacked column down the left margin detached from the invitation tables (not composed around the text); poppy group also vertically flipped and clipped to half its width

### wedding/03

- MEDIUM | all | p1 | small gold-rings image displaced ~0.5" up (card 1) / ~1" up (card 2)
- MEDIUM | all | p1 | second card's content (pink picture + caption) sits ~0.5" higher than Word (first row shorter)
- MINOR | all | p1 | pink rings picture rendered ~3% larger and shifted slightly up/right

### wedding/04

- MAJOR | all | p1,p2 | green section rules missing: horizontal rules attached to every section heading (and p1's left vertical bracket line) not drawn — only the long center column divider renders. NOTE 2026-07-20: the filled-walk-stroke pass made the divider a hairline FATTER than Word (+0.0008..+0.0010 — the thin filled rect now also strokes its same-colour `a:ln`, widening it by the line width); accepted as the cost of landing letters/05's triangle class. The missing horizontal rules did NOT appear under that pass — they are a different mechanism (not stroked filled rects)
- MEDIUM | all | p1,p2 | checklist line spacing compressed — columns end ~0.85-0.95" higher than Word (e.g. "Obtain a marriage license" / "Remember to eat something"); p2 first item fits one line vs Word's two
- MEDIUM | all | p1 | right-column items lose the gap between checkbox and text (box glued to text: "☐Choose the members of your wedding party.")
- MAJOR | html | - | column divider rule renders full page height crossing the title and garland, and the header garland stacks as three repeated bands above the title instead of one composed arrangement

### wedding/05

- MEDIUM | pdf | p1 | date block also rendered bold where Word uses regular weight
- MEDIUM | all | p1,p2 | lists start too high: wedding-party list ~0.6in above Word's position on p1, menu list ~1 line high on p2 (gap below the wash heading too small)
- MEDIUM | html | - | watercolor washes exported as standalone images stacked above the panels instead of backgrounds behind the headings

### wedding/06

- MEDIUM | all | p1,p2 | card rows too short: fold/borders sit ~1in higher than Word and second-card content ~0.9in high; p2 bottom flower clusters straddle the card borders, p1 card2's poppy dips into the pink banner corner
- MEDIUM | all | p2 | invitation text line spacing compressed ~30%, block ends ~0.5in higher
- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/08

- MEDIUM | all | p1 | the circled "&" badge glyph renders BLACK; Word draws it WHITE. The Ampersand paragraph style names no colour — the white comes from the text box's `wps:style/a:fontRef` `schemeClr lt1`, a DrawingML fallback below the style chain that `ParseWordArt` does not yet consult (it resolves the style chain, which yields the doc-default black here). Needs a fontRef colour fallback that stays BELOW an explicit run/style colour so it does not flip menus/03-style boxes (whose white comes from the style, over a `dk1` fontRef)
- MINOR | all | p1 | the circled "&" badge green ellipse + ampersand placement is sub-pixel off within the cell

- MEDIUM | all | p1,p2 | card panels shorter than Word (p1 borders end ~2in early, p2 ~0.4in) with content blocks 0.4-0.8in higher (Thanks-and-Dedication and time/venue blocks)
- MEDIUM | all | p1 | "dinner and dancing to follow" rendered upright instead of italic
- MAJOR | html | - | green circled "&" badge missing

### wedding/09

- MEDIUM | all | p1 | invitation banners pinned to top of card rows instead of vertically centered (card1 ~1.5in high, card2 ~2.9in high); card frames end at ~70% page height leaving the bottom unframed
- MAJOR | all | p1 | card2's relocated "on our wedding day"/banner corner collides with the poppy image (text drawn over the flower; pdf card1 poppy also overlaps the yellow "&" box)
- MEDIUM | all | p2 | invitation text line spacing compressed ~30%, block ends ~0.5in higher
- MEDIUM | all | p2 | bottom purple/yellow cluster sits on the card boundary/page bottom instead of inside the cards
- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/10

- MAJOR | html | - | floral header graphic mis-composed the same way (vertical branch-and-rose arrangement instead of Word's horizontal spray above the title)
- MINOR | all | p1 | the two "Candid photos..." placeholder items' checkboxes render light grey instead of dark like every other row in Word
- MINOR | all | p1,p2,p3,p4,p5 | checklist rows drift vertically up to ~10px from Word's positions (cumulative spacing difference down the page)

### wedding/11

- MAJOR | all | p1,p2 | watercolor artwork oversized and un-clipped: right card's top blob bleeds left of the card border, left card's bottom blob spills below/right of the card bottom edge
- MEDIUM | all | p1 | line wraps differ: "The pleasure of your company..." sentence wraps to 4 lines vs Word's 2, and "SAVE THE DATE" wraps to two lines vs Word's one
- MEDIUM | all | p1,p2 | card border geometry off: border lines extend past card corners (right card's left border runs below the card) and bottom borders sit higher than Word
- MEDIUM | pdf | p1,p2 | teal "SATURDAY"/"10.25.20XX" date lines (both cards) and "ACCEPT | DECLINE" render bold where Word shows regular weight
- MEDIUM | html | - | the four watercolor images render as detached standalone blocks above/beside the cards instead of as artwork inside the card frames
- MINOR | all | p2 | couple photo content cropped tighter (~8-10% zoomed in, less headroom above heads) and photo/text block sits ~20px higher than Word

### wide_table

- MEDIUM | all | p1 | all six table columns ~12% narrower than Word (~54px vs ~61px per column; table right edge at x≈475 vs Word's 519)
- CLEAN: html

### wordart

- MAJOR | skia | p2 | "Arc Text Up" rendered ~15% larger and higher than Word, its glyphs overlap the subtitle line "These use DrawingML WordArt transforms"
- MEDIUM | all | p2,p3,p4 | WordArt items flow one page early vs Word: "Wavy WordArt" appears on p2 instead of p3 and "Slanted Down" on p3 instead of p4, shifting p3/p4 content up ~130px (total page count still matches)
- MEDIUM | skia,imagesharp | p2,p3 | Path-warp WordArt drawn at nominal font size instead of stretched to the shape bbox — p3 items (Wavy WordArt, Chevron Up/Down, Fade Effect, Slanted Up/Down) span ~180px where Word fills ~430px page width (hard ~23%)
- MINOR | skia,imagesharp | p2 | Arc/circle warps off position/size: ImageSharp places "Circle Text" ~85px left of Word's position; Skia draws Arc Text Down and Circle Text ~10-30% larger and shifted right
- [known] MINOR | skia,imagesharp | p2 | residual vertical offset of warped glyphs vs Word (inline-drawing layout-cursor drift documented in notes.md)
- MEDIUM | all | p9 | Colored underlines lost: Word's thick red underline under "UNDERLINED TEXT" and blue double underline under "DOUBLE UNDERLINE" both render as a single text-colored gray/black underline
- MINOR | pdf | p10 | highlight bars render ~10% more ink than Word's (magenta 3049 sampled units against 2622): the box spans the font's full ascent-to-descent where Word's sits slightly tighter. All five colours are correct and present
- MEDIUM | all | p14 | Emboss/shadow character effects flattened: black "EMBOSSED" and "SHADOWED" lose their offset drop shadows in every backend; "IMPRINTED" engrave two-tone lost in ImageSharp/PDF (Skia approximates it with a light offset)
- MINOR | skia,imagesharp | p5,p6,p7,p8,p9,p12,p13,p14,p15 | line spacing slightly larger than Word so each section's content drifts progressively lower (~15-20px by the last line of a block)
- MINOR | pdf | p5,p6,p7,p8,p9,p12,p13,p14,p15 | vertical drift accumulates to ~40px by the lower lines of each section
- MAJOR | html | - | the 12 WordArt shape texts export as plain small unstyled black paragraphs at the top (no warp, color, or display size), while all other sections keep full styling
- MEDIUM | html | - | red underline and blue double underline lost — both render as plain single dark underlines
- MINOR | html | - | emboss/engrave/shadow effects render flat (no drop shadows on "EMBOSSED"/"SHADOWED", no engrave on "IMPRINTED")

### wordart-envelope

- MAJOR | html | - | The four WordArt words are exported as small plain black text with no color, size, or warp styling — the blue/green/orange/red large display text is lost (heading and subtitle export correctly).
- MEDIUM | imagesharp | p1 | "Can Up" and "Can Down" are squashed to roughly half Word's glyph height — "Can Up" becomes a low flat ribbon hugging the bottom of its band leaving a large blank gap below "Deflate", and "Can Down" bows into a deep flattened smile arc instead of Word's full-height gently-warped letters.
- [known] MEDIUM | skia,imagesharp | p1 | Envelope warp shape deviates from Word on the Can Up/Can Down lines: sin-curve amplitude is much stronger and edge glyphs shrink to ~55% height so the leading capital "C" reads as lowercase, vs Word's near-uniform letter heights with a gentle arch (envelope curve + 0.55 minRatio design documented in notes.md).
- MINOR | skia | p1 | WordArt stack drifts upward with inter-line gaps nearly eliminated — "Can Down" sits ~60px higher than Word and almost touches "Can Up", where Word keeps clear separation between all four lines.

---

## Clean scenarios (faithful on skia, imagesharp, pdf and html)

`align_center`, `align_justified`, `align_left`, `align_mixed`, `align_right`, `all_caps`, `block_quote`, `bold_text`, `bullet_list`, `colored_text`, `complex_document`, `custom_margins`, `document_protection/01`, `embedded_font`, `explicit_break_blank_page`, `first_line_indent`, `font_families`, `form_checkboxes`, `form_dropdowns`, `form_text_fields`, `gutter_margins/01`, `headings`, `html_basic_formatting`, `html_font_tag`, `html_links`, `hyphenation_nonbreaking`, `hyphenation_soft`, `inline_group_crop`, `inline_image`, `italic_text`, `left_indent`, `line_breaks`, `line_spacing`, `long_paragraph`, `mixed_formatting`, `multiple_images`, `multiple_pages`, `multiple_paragraphs`, `nested_list`, `numbered_list`, `numbered_list_restart`, `page_a4`, `page_breaks`, `page_landscape`, `page_legal`, `pct_pos_offset`, `section_break_even_page`, `section_break_next_page`, `section_break_odd_page`, `simple_paragraph`, `simple_table`, `small_caps`, `strikethrough_text`, `subscript_superscript`, `table_borders`, `table_cell_padding`, `table_colors`, `table_default_cell_margin_start_end`, `table_default_style_first_row_run_color`, `table_default_style_first_row_shading`, `table_page_break`, `table_vmerge_basic`, `table_vmerge_explicit_heights`, `text_wrapping_break`, `three_columns`, `two_columns`, `underline_text`, `wedding/07`
