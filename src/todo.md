# Rendering fidelity todo

Deep comparison of every scenario in `src/Tests/Inputs/` (325 scenarios, 548 Word reference pages): `expected_*.png` (Word, 150 DPI, via RenderHelper) versus `skia_result#page_*.verified.png`, `imagesharp_result#page_*.verified.png`, `pdf_result#page_*.verified.png` (PDFium render), and `html_result.verified.png` (headless-browser screenshot of the HTML export).

Each finding: `severity | backends | pages | description`. `all` = skia+imagesharp+pdf. HTML findings ignore pagination/viewport-width reflow by design and only flag content/styling errors. Not reported: anti-aliasing texture, 1-2px subpixel shifts, ImageSharp's softer glyph rasterization. `[known]` = already documented as accepted in that scenario's notes.md. How a difference is judged — crops versus metrics, and what counts as noise — is `docs/fidelity-audit.md`.

**This file lists only what is still wrong.** A finding is deleted the moment it lands, and its durable knowledge moves to `docs/word-features.md` (feature behaviour and the evidence behind it), `docs/floating-art-pipeline.md` (anchored/floating art), `docs/html-import.md` (HTML and AltChunk input), `src/page_counts.md` (page-count experiment ledger) or `docs/fidelity-audit.md` (comparison method). Nothing that has shipped is described here — for how a fix was reached, read those docs and the git history. This is a temporary working document; it is expected to shrink to nothing.

**Open: 49 major, 202 medium, 171 minor = 422 findings across 177 scenarios.** Established by a full page-by-page re-audit on 2026-07-20 and maintained by deletion since; the 2026-07-21..23 fix batches were pruned as they landed. The tally is recounted from the `severity | backends | pages` lines below rather than hand-adjusted — it had read 807 against an actual 769 on 2026-08-08, having drifted as later batches were pruned without moving it, and 760 against an actual 753 on 2026-08-13 for the same reason. The 2026-08-13 recount also folds in the five `border_style_variants` findings added that day. Recounted 2026-08-19 after the built-in-default-spacing / synthetic-italic / underline-colour / HTML-export batch landed (29 findings deleted, 13 of them stale — verified already fixed against the current baselines — plus systemic #37/#38, both shipped with the 2026-08-14 spreadsheet landing). Recounted again the same day after the second batch — mirror indents (Word-probed: the flag drops left/right/hanging), the column-break mark line, inline content controls, page borders restored post-engine-flip, the tracked-changes bar, declared-tab-stop layout in the HTML export, and the drop-cap anchoring guard — 21 findings deleted (5 measured stale) plus anomaly #33, one trimmed residue added for `complex_spacing`'s page boundary. A third same-day batch swept the corpus band structure against the references (24 position findings verified stale — the 2026-08 engine landings had quietly fixed them), restored line numbering (another engine-flip orphan: the gutters were MISSING, not small — w:start probed as the value before the first line), retiled tab leaders at the glyph's natural advance, set math in serif with spaced operators, collapsed contextual spacing in the HTML export, drew bar-tab rules, neutralized browser link styling, and evaluated page fields as a one-page document in the text exporters — 40 more findings deleted, two trimmed to measured residues. A fourth same-day batch (39 deleted, one MAJOR trimmed): the HTML export gained paragraph shading, picture rotation/flip/crop transforms, cell writing-modes, declared cell margins as padding, the detached cell-spacing model, diagonal-border gradients and full-stack CSS double widths; the HTML import gained margin-left indents (Word-measured 0.75pt/px) and Word's drop-empty-`<p>` rule; the cell-double border law was re-probed at four magnitudes (`_probe_celldouble`: line = w:sz, gap = w:sz — the old total-width reading was an under-amplified misread) and `BorderStroke` now draws per-line in both scopes; wedding/08's badge took the `a:fontRef` glyph-colour fallback (`w:color auto` defers to it); menus/06's white sheep was PDFsharp deduping recoloured indexed PNGs by pixel hash alone (collision guard re-encodes the collider as RGBA — which also turned brochures/03's PDF stars solid white, closing that finding); and ten findings measured stale, including all of cover-letters/16 and the `html_inline_styles` formatting family. The six line-number HTML-gutter entries left with the batch as documented non-goals — the export deliberately reflows, and a per-layout-line gutter has no place in it (`docs/word-features.md`, Line Numbering). A fifth batch (2026-08-19, 41 deleted): the `exact`/`atLeast` baseline laws landed (exact hard-sets the baseline at 80% of the declared box, a growing atLeast bottom-anchors its ink — both fixtures now match Word within 1px at four magnitudes); the no-break space measures and paints as an ordinary space while staying unbreakable (business-plans/05's doubled gaps, agendas-minutes/07, resumes/14); `w:tblPrEx` inside-border `none` overrides stick and border resolution is neighbour-aware following vertical merges (newsletters/04's spurious rules); the detached cell-spacing geometry landed from `_probe_cellspacing` (every gap 2 × spacing, frame emitted as its own box); a trailing `<w:br/>` paragraph keeps its mark line (nonstandard_main_part_name's Notes box, now 238px vs Word's 237); #35 closed with spaced cell paragraphs as real blocks; and the sweep measured stale the whole mid-word-break family, the tracked-caps letter-spacing family (per-glyph tracking had landed without the findings being re-read), menus/03's divider, wordart's drift set, the ~8-10% "narrower" claims (all measure 1-2.5% — the residual is systemic #43's advance band), and a dozen small drifts. Aggregate: −0.345 AE over 172 changed pages, every mover an improvement, no page-count changes. A sixth batch (2026-08-19, 35 deleted + 5 trimmed + 2 reclassified): a visual MAJOR-claim sweep found twenty-odd headline defects already fixed by earlier landings and never re-read — brochures/05's placeholders, business-plans/09's action table, business-plans/03's off-page address, business-plans/13's clipped JUL column, business-plans/15's p17 overrun / all-caps / PDF-overlap claims, newsletters/09's masthead and teaser band, menus/04 and menus/05's geometry, wedding/04's section rules, wedding/11 wholesale, postcards/04's boy crop, resumes/13's footer overflow, labels/01's label spacing. Two code fixes: the Skia engine painter gained a:xfrm rotation/flips for inline images (it drew them UPRIGHT — image_rotation/01's finding now records the real residual, the unprobed Word clip), and the HTML export emits pct cell widths (menus/04's meal columns). complex_tables' blue-heading findings moved to #32: the package's own styles declare the blue Morph draws, so the bold-black reference is a Word repair artefact. A seventh batch (2026-08-20, 47 deleted net): cell paragraphs gained their w:pBdr border boxes — the engine's cell arm tracks the same border-group run the page flow does and MeasureCellHeight charges identical reserves, closing cover-letters/10's missing date rule, brochures/08's heading rules, and business-plans/12's TOC rule — while the HTML export gained the group law (top edge to the first member, w:between at internal boundaries, bottom to the last) plus bordered cell paragraphs as real blocks, closing the resumes/08/11, business/02 and cover-letters/10 rule families; character-style TOGGLE semantics landed (ECMA-376 §17.7.3 — a bare <w:b/> in a character style FLIPS the paragraph-chain state), closing the resumes/18 date-bold pair, cover-letters/11's "Astrom", resumes/08's "CONNORS", agendas-minutes/02's date values and resumes/07's SKILLS values; tabbed HEADINGS take the flex layout; w:t sheds unpreserved XML edge whitespace and endnote marks default to lowercase roman (document_capture/01); and a MAJOR-claim sweep of the HTML exports measured twenty-five findings stale — whole-scenario clears for cover-letters/02, labels/14 and cards/19, the invisible-white-text family (agendas-minutes/02, menus/03, newsletters/08/10, resumes/02, brochures/01), newsletters/09's "dropped" bodies (all present), newsletters/12's pull-quote, and the kerning-fixed table_borders wrap. Aggregate: −0.139 AE over 54 pages, no page-count changes; the only positive movers are new correct ink (business-plans/12's TOC rule at +0.004, brochures/06's quote-box style border at +0.003). An eighth batch (2026-08-20, 69 deleted net): the HTML `border`-attribute table model landed from `_probe_htmlborders` — detached per-cell 1px grey boxes separated by cellspacing (default 2px, halved into `CellSpacingPoints`), collapsed inside rules under `cellspacing=0`/`border-collapse`, the frame at the attribute width, the spacing insets joining the column measure and the outdent — closing the html_table_cellpadding / html_table_styled / html_table_cell_padding_css / html_table_cell_margin_css / html_css_alignment / html_complex gridline family in render AND export (the colored-fill fear behind four reverts was REFUTED: Word itself seams a colored header row; +0.045 AE is the thin-rule offset penalty, the crops match Word box for box); labels/04's gradient hexagons render (gradient shapes now build preset contours); and three mechanical sweeps pruned ~50 stale findings — a full-corpus band sweep (paragraph_borders' compression, wedding/04/05's inch-scale claims, newsletters/14's title indent measured at 0), a PDF-vs-Skia divergence sweep that dissolved the pdf-only drift family (resumes/10/14, letters/12, business-plans/10, cover-letters/05/10/11/14 — the engine paginates all backends identically, so the pre-flip readings were stale), and the continued MAJOR-claim export sweep (letters/01/03/04/05, resumes/02/06/19, cards/04/06/12/18/19, newsletters/01/05/14, wedding/01/02/04/10, labels/05/08/10/16, menus/05/06, brochures/01/03/04/08, business-plans/06). feature_capture/01's rotated cell re-read WORSE than recorded (the engine renders it horizontal — an engine-flip orphan) and business-plans/10's cover date renders white-on-white below the cover art rather than missing. Recounted 2026-08-27 when the numbering-marker and HTML-export branch merged: `w:lvlJc="right"` right-anchors the marker and an overflowing left-aligned marker pushes the first line's text to the next default tab stop (`_probe_numtab`), closing agendas-minutes/14's fused romans and business-plans/12's run-together section headings; imported list items dropped to the bare line box with one closing block gap and blocks shed their edge whitespace. The regeneration that followed measured the edge shed: html_css_margin_padding p1 halved to 0.0274 AE / 0.9399 SSIM (imagesharp 0.0286 / 0.9389) with the #CCE5FF box's two stray `<br />` gone, closing its whitespace finding; html_complex p2 improved to 0.0835 AE / 0.9437 SSIM while p1 ticked up to 0.0978 AE / 0.8556 SSIM — shedding the edge breaks removes hard breaks that were compensating the intro's narrow measure, the root cause recorded under html_complex below, so p1 waits on that. html_lists and html_nested_lists reproduced byte-identically; their entries below are not yet re-judged against the landing. Recounted 2026-08-29 after the picture-recolour landing (systemic #8): two brochures/02 findings and one letters/02 finding deleted, brochures/03's HTML finding trimmed to its ellipse-clip half and downgraded to MEDIUM, and brochures/08's "lost navy duotone" rewritten as the `a:alphaModFix` transparency it actually is — measured, not inferred, since that package carries no `a:duotone` at all. The brochures/02 raster finding that scoping implied did not exist in the file: the audit had recorded the duotone loss as pdf+html only, having been taken before the engine flip orphaned the effect on every backend. The `a:alphaModFix` landing later the same day deleted brochures/08's MAJOR, and its HTML finding went too once the transparency reached `WriteShape`'s SVG `<image>`.

### Re-validation status (2026-08-08)

A partial re-measurement pass ran against the current baselines. **What it settled:**

- **Whole-page vertical alignment is essentially solved; block-local displacement is not.** Aligning each page's ink profile against Word's gives a median page offset of 2px. But a sliding-window local alignment (150px windows, ambiguous windows discarded) puts the median WORST local displacement at 11px and the p75 at 43px. So a finding describing the whole page drifting or compressing is usually stale, while one naming a specific block that sits N px off is usually still live. Findings are not yet individually pruned on this basis.

**What it did NOT settle — do not read an unpruned finding as re-validated.** The `missing`, `weight`, `wrap` and `colour` classes and all 267 HTML findings have no mechanical test here; a page-level chroma comparison was tried for `colour` and discarded as non-decisive (a duotone photo is too small a fraction of the page to move the page mean). Those need the crop-vetting loop in `docs/fidelity-audit.md`. Findings that predate the current baselines remain suspect where they describe vertical drift.

## Systemic issues (cross-scenario root causes)

These patterns repeat across many scenarios; fixing one clears whole families of the per-scenario findings below. **IDs are stable** — findings reference them by number — so a closed issue's number is retired rather than reused, and the list is deliberately gappy.

### All raster + PDF backends

- **#1 Page-number fields on long documents.** PAGE/NUMPAGES/SECTIONPAGES evaluate per page and section restarts work (`docs/word-features.md`). Still open: `business-plans/12`'s footer numbers need per-section headers/footers, which are unmodelled. HTML/Markdown evaluate page fields as a one-page document ("Page 1 of 1") since 2026-08-19 — the reflow really is one page.
- **#25 Word's row-split trigger — LANDED 2026-08-07 (third attempt), floor-fit CORRECTED the same day; measured residues below.** The full bundle: table-level fit routing (a table that does not fit the space left flows row by row, mirroring the whole-table move's slack exactly), the split-acceptance test (a first fragment offered only a region remainder must genuinely split — something continues AND the placed content fits — else the row moves whole), the vMerge tie rule (a continuation row never breaks from its predecessor; it stacks, overflowing and clipping, pinned by `CanonicalFragmenterTests.A_merge_continuation_row_stacks_rather_than_breaking`), the strict floor fit (below), and style-inherited `w:pageBreakBefore` with the inline `w:val="0"` override (the old parser comment claiming Word only honours it inline was refuted by probe). Probe evidence, mechanics and the failed attempts' history: `docs/word-features.md` (Multi-page Tables / Page Break Before) and git history. Corpus: page counts 325/325/325; the trigger landing measured 7.799 → 7.631 over 41 changed pages, and the floor-fit correction a further 12 better / 0 worse on `business-plans/13` (4.744 → 4.586, its split points now band-for-band Word's).

  **The floor-fit law flipped once within the day, both directions Word-measured.** A letters/04 in-situ reading ("Word keeps a floored row whose content fits the remainder, floor overflowing the margin") briefly landed a content-only fit; four controlled fixtures (`_probe_floorfit_single`/`_last`/`_mid`/`_enddoc`) then showed Word MOVING such a row in every structure, and the letters/04 keep dissolved into upstream height drift — Word's letter runs ~50pt more compact, so the floor simply fits its layout (the engine keeps the page count through the whole-table move's slack instead). Final law, pinned by `A_floored_row_whose_content_fits_but_floor_does_not_moves_whole`: **an atLeast floor participates in row break decisions and reserves STRICTLY against the hard content bottom** (business-plans/13's landscape pages break exactly where the 21.6pt-floored row's floor crosses the margin — box to 519.4, floor to 541.2, bottom 540 — a fact hidden until the pages were read at their true 612pt height; the "8-11pt short header block" recorded here was this extra packed row plus the landscape-scale inflation, and is RESOLVED); **content-driven rows keep HasSpaceFor's shared slack** (Word keeps business-plans/15's content-sized 79.6pt boundary table 13pt past the margin, drawn and clipped); **a row carrying any vertical merge — span head included — is exempt from the strict test** (a merge span is one drawn unit Word clips rather than moves; without the exemption resumes/06's span head at 1.1pt over re-split 3 pages into 6); and **repeated `w:tblHeader` rows do not cost the row beneath them its floor** (fixed 2026-08-12, pinned by `A_row_carried_under_repeated_headers_keeps_its_declared_height` — `PlaceSplitRow` read `atRegionTop` *after* the header loop had cleared it, so a row carried whole to a fresh region under re-emitted headers was sized by content: business-plans/13's first data row came out 11pt against its declared 21.6pt on pages 14, 16 and 20, shifting the eight rows below it 23px up the page at 150 DPI, and the fix took those pages 0.122/0.248/0.140 → 0.034/0.067/0.030 AE identically on all three backends). Still open, each measured:
  - The engine absorbs a trailing empty-PARAGRAPH row Word carries (`_probe_trail2_para`: AFTER ink at 75.36pt vs Word's 174.72) — and LibreOffice has no absorption rule at all, so `lastVisibleRow` may be scoped too wide. Unpicking it touches the rule that stops a letter template's spacer rows from starting a page; needs its own adjudication.
  - `FinishPage` drops the natural-overflow blank page Word renders (`_probe_trail2_flowblank`: 54 exact lines plus a trailing empty paragraph is 2 pages in Word, page 2 genuinely blank; the engine gives 1, and the code comment claiming Word does not render that page is refuted). LibreOffice corroborates: its `RemoveSuperfluous` only trims pages with no content frame, and an empty paragraph is one.
  - The whole-TABLE move still carries the 4% slack a floored single-row table should not get: `_probe_floorfit_enddoc` (30pt floor into a 24pt remainder, whole-table path) stays on the engine where Word moves it. **The letters/04 blocker on this is GONE (2026-08-08)** — the ~50pt of "upstream height drift" its one-page render depended on the slack to mask was the cell-measure contextual-spacing hole (`docs/word-features.md`, Contextual Spacing), now fixed, and that page is band-for-band Word. Making the whole-table path floor-strict can be retried on its own merits. resumes/06 carries a similar debt: its rows 0-9 accumulate ~13.6pt over Word (the two ~77.5pt content rows are the suspects), which is what pushed its span head onto the strict-fit knife edge in the first place.
  - Word's flow and cell genuinely differ under auto spacing (42 vs 41 lines against the same band, engine reproduces both) — a separate, uncharacterised difference in a cell's usable height.
  - Un-probed LibreOffice leads retained (hypotheses, not evidence): exact spacing hard-sets the ascent to 80% of the declared box (`itrform2.cxx:2403-2421`; the engine keeps the font's natural ratio — measurable); Writer charges a split row's trHeight floor once ACROSS fragments (follow minimum = declared − earlier fragments' heights, tabfrm.cxx:5123) where the engine floors only a whole row carried to a region top; Writer synthesizes split-edge rules by borrowing the neighbour row's border and suppresses them under repeated headlines (paintfrm.cxx:2858/2932) where the engine draws the fragment's own full box; `COLLAPSE_EMPTY_CELL_PARA` (a cell whose sole paragraph is empty collapses to 1 twip); the broader always-on flag inventory (`sw/inc/IDocumentSettingAccess.hxx:36-106`, DOCX defaults `WriterFilter.cxx:300-341`) — notably `TAB_OVER_SPACING`'s side effect (Word ≥2013 drops the body's upper spacing at the top of non-first pages), `HEADER_SPACING_BELOW_LAST_PARA`, `TREAT_SINGLE_COLUMN_BREAK_AS_PAGE_BREAK`, and `IGNORE_TABS_AND_BLANKS_FOR_LINE_CALCULATION`.
- **#42 Rules that were fitted to a single measurement and could be settled by an amplified probe** (opened 2026-08-13). Each of these is currently a constant or a shape that reproduces one observation without anyone knowing whether it is the right MODEL — the failure mode described under "Ad hoc Word probes" in CLAUDE.md, where a realistic-sized fixture cannot separate the hypotheses. All are cheap: one fixture per item, the variable declared large, and measured at two or more magnitudes. Ranked by how much rides on the answer:
  - **Cell padding / row-height fudges** — `DocumentParser` (~line 4836) and `TableHeightCalculator` (~line 362) both carry comments about compensating fudges since removed or reworked; a probe sweeping `w:tblCellMar` and `w:trHeight` at exaggerated values would confirm what is left is right.
  - **Subscript/superscript at "approximately 58%"** (`SkiaRenderContext`) — a convention, never measured. One 96pt probe answers it.
  - **Footnote text size approximated at 10pt** (#13).
  - **#33's uncharacterised difference in a cell's usable height** (Word fits 42 lines in flow against 41 in a cell for the same band) — amplify by making the cell very tall so the discrepancy is many lines rather than one.

- **#43 LANDED 2026-08-30 as a bundle — Calibri-12 default flip, live `.wordadvances` sidecars, the autofit border term, and the space-compression wedge; open residues below.** The full history (advance model, kerning, the parked years) moved to `docs/word-features.md` (Fonts/Kerning) and the code comments on `DocumentParser.builtInDefaultFontFamily`, `TableLayout.CalculateContentBasedColumnWidths` and `CanonicalParagraphMeasurer.TryCompress`. What unblocked it: the "autofit slack" was a MISMEASUREMENT (Word's preferred width is `advance + Σ max(cellMargin, borderWidth/2)` per side — three swept probes, no slack term exists), and resumes/16's page-count loss was Word's space-compression wedge (spaces shrink one 120-dpi pixel each, per-space, only-as-needed, rather than wrap), not advances. The wedge is GATED to sidecar-backed faces — ungated it turned near-fit lines on approximately-measured fonts into coin flips (agendas-minutes/15 +0.055 AE, business-plans/15 19→18 pages); extending it to another font means generating sidecars for that font, not widening the gate. Adjudication: page counts 330/330 vs Word (parity kept; resumes/16's page now stands on measured rules rather than luck); over the 400 changed page/backend pairs 83 better / 163 worse / 154 flat, mean AE 0.0491 → 0.0497 (+0.0006) and SSIM −0.0035 — a flat aggregate carrying structural wins in the table-geometry family this entry was opened for (table_cell_margin_per_cell −0.010 closing its 13px column divergence, table_default_style −0.012, complex_document −0.013) against wrap/pitch residues concentrated in six scenarios, each recorded per-scenario below (complex_spacing, image_wrap_square, business/03, business/04, letters/04, and newsletters/03's known chaos page). Still open:
  - Page counts 329/330 → 330/330 held only because business-plans/15's Univers now falls back to Tahoma (measured closest at −5.9% vs the reference's REAL Univers, which is installed on the reference machine); its wraps remain approximations and its metrics will drift with any wedge/advance change. The honest fix is bundling a licensed Univers-metric face, which is out of Morph's hands.
  - Space advances remain excluded from the sidecars (a run of spaces measures differently from a single inter-word space — see the wedge in `CanonicalParagraphMeasurer`, which now explains the 4px/5px "same document" mystery as compression, not context).
  - Glyph RENDER size is em-rounded in Word (8pt Calibri draws at 7.8pt effective) while Morph draws nominal sizes — unmeasured ink-scale deviation up to ~3% at half-point sizes; advances unaffected.
  - Sidecars exist for the five Calibri faces only; the wrap-agreement gate (`CanonicalWrapAgreementTests`) now excludes sidecar-backed faces and its residuals name the next candidates: Century Gothic, Trebuchet MS, Arial 12pt, Avenir Next LT Pro.

- **#45 Spreadsheet geometry: the measured width/scale/range rules LANDED 2026-08-14; the residuals below are what is still open.** The landed rules live as code comments on `SheetGridBuilder.MaxDigitWidth` / `ToPoints` and `SpreadsheetParser.ResolveRange` / `ResolveScale`, pinned by `ColumnWidthTests`: column width is `width * maxDigitAdvancePx` (exact advance, MAXIMUM digit not the zero, no `+5` — it is already inside the stored width per ECMA-376 §18.3.1.13), the fit scale floors to a whole percent, `dimension` intersects with the cells that actually exist, and `SheetGeometry` shares the measured unit so anchored art tracks the grid. Landing measured SSIM −0.3921 / AE −0.3298 over 165 raster pages with no page-count change on any backend; the SSIM sign is the known unreliability of that metric on this subsystem (below), the AE improvement and the probe evidence are why it landed. Still open:
  - **The worst movers are triaged (2026-08-14), and neither names a new quantity.** `home-contents-inventory-list` (−0.09 SSIM) is a metric misfire on a genuine improvement: Excel draws its table at a perfectly regular 104px column pitch, and the per-gridline error against Excel went from −1/−1/0/0/0/+1/+1/+2/+2 before the landing to +1/+1/+1/+1/0/0/0/0/0 after — mean 0.9px down to 0.4px — while SSIM punished nine full-height gridlines each sliding 1-2px. No defect; do not chase. `social-media-editorial-theme-calendar` (−0.10) is the body-font row factor with its first confirmed corpus victim — see next item.
  - **Row heights render at a BODY-FONT-dependent factor, and Arial-bodied corpus books now visibly pay it** (isolated 2026-08-14, `_probe_fontgraft`; victim confirmed by triage the same day). The same declared heights draw at ~15/16 under an Arial body and essentially exactly under a Verdana-11 theme. `social-media-editorial-theme-calendar` — Arial 12 body, all rows declared `ht` with `customHeight` — had body row pitch 260px against Excel's 259 BEFORE the landing and 267-268 after: the old too-small fit scale was cancelling the ~6% row-height excess, and the corrected scale exposed it (black header band 168 exact → 170, everything below drifting down cumulatively). Fixing it means applying the per-font factor to declared heights for the faces that carry it, which needs the factor measured per face on real-workbook-derived fixtures — do not model a constant. Likely mechanism (unverified): Excel's print scale derived from the Normal font's GDI metrics on screen vs printer. `household-organizer` measuring ~1.05 against the graft's 1.00 remains the one unexplained residue — plausibly non-`customHeight` rows re-auto-sized at open.
  - **`simple-basic-pink-blue-timesheet`'s width is still +5.7%** (649px against Excel's 614 after the landing, from +7.3% before). Its body font is Constantia 11 — bundled in `src/Fonts`, but NOT one of the six faces the width probes covered, so its max-digit advance has never been checked against Excel's fitted unit. One `_probe_grid_`-style fixture (built per the hygiene rules below) settles whether Constantia is another exact-advance face or a deviation.
  - **`autoRowHeightFactor = 1.31` is CONFIRMED** (a probe round claimed it wrong and was retracted — the rendered heights it compared against carry the per-font factor above; dividing it out reproduces 1.31 exactly). Do not revisit without dividing out the body-font factor first.
  - **Probe hygiene, learned twice.** (1) Verify the printer paper by rendering a fixture and checking for a 1123x794 page — Excel REPORTS the A4 it was asked for while a Letter driver silently exports Letter (~8% squeeze), and `Get-PrintConfiguration` reported Letter here while Excel exported A4. (2) Validate any hand-built fixture against a REAL workbook with its fit scale neutralised before trusting its numbers — every fixture cloned from the same minimal base carried an unrepresentative body font, which produced one refuted rule and one wrongly-retracted constant.
  - **Neither metric is trustworthy alone on this subsystem.** Placement fixes scored negative while visibly correct; `weekly-lesson-planner` scored +0.12 on a change that made its geometry measurably worse. Read extents and crops, per `docs/fidelity-audit.md`.

- **#26 `IsAnchorOnlyMark` is inert** (found 2026-08-06). The parser sets it for a paragraph whose only content was behind-text decorative art — "emit a marker with zero line height" — but nothing in the engine consumes it; the deleted production renderers did, so the agendas-minutes/11 behaviour it was written for was lost in the migration. Reviving it is NOT a free fix: honouring it in `CanonicalParagraphMeasurer` regressed 104 of 108 changed pages (aggregate mean |Word−render| 8.4 → 56.8; menus/08, brochures/01 and agendas-minutes/10 to ~190 grey levels), because those paragraphs anchor art whose placement depends on the line existing. Needs its own investigation into what the production renderer did with the reserved space.
- **#5 Floating/anchored decorative art missing or misplaced.** Ten-plus fix passes landed; architecture, parse-path authority rules and the attempted-and-reverted decision log are in `docs/floating-art-pipeline.md`. Still open: freeform/vector shapes in `brochures/04`/`06` (chevrons, balloon art, quote box — improved, residuals remain), `business/04`/`05` (banners, watercolour blobs), `cover-letters/06` (location-pin and phone glyphs) (its red bars remain, but the pale-blue page background now renders), `resumes/10`, and the `cards/05`/`18` fold guides (partially surfaced by the dashed-line pass). `labels/03`'s tear lines render but sit denser than Word's fine dots — Word likely draws round dot caps at wider spacing. `labels/04`'s hexagon accents render since 2026-08-20 (gradient shapes build preset contours; the guard was dropping them because a gradient carries its start colour as `FillColorHex`, which skipped the contour build) — the residual is Word's softer look, which needs gradient-stop alpha that `GradientFill` doesn't model.
- **#6 Shape geometry defects.** Preset polygons, text-box chrome, picture flips, line alpha, connector assembly and outline-only/stroked-fill shapes are all resolved across both parse paths (`docs/word-features.md`, `docs/floating-art-pipeline.md`). Still open: `business-plans/02`'s arrow construction, and stray art that other subsystems may still place where Word hides it (the `labels/16` class — group-frame clipping fixed the `cards/04` class).
- **#8 Picture effects — the COLOUR transforms landed 2026-08-29 on all four outputs; what is left is the transforms that were never modelled.** Duotone/greyscale/washout had been dead code since the engine flip: parsed onto `ImageElement`/`FloatingImageElement`/`Run`, but `PlacedImage` carried no field for them, so every `GetProcessedImage` call passed `None` and Skia and PDF had no consumer at all. One shared recipe (`ImageRecolor.Rows`) now feeds a Skia colour filter, ImageSharp's decode pipeline, PDF bytes recoloured through `IImageEffects`, and an SVG `feColorMatrix` in the HTML export — see `docs/word-features.md`. All five affected scenarios improved, mean AE −0.0290 over 16 page/backend pairs. The `a:alphaModFix` transparency followed on the same day, and its lesson is that it rides on SHAPE FILLS, not pictures: brochures/08's five blips are all `wps:spPr/a:blipFill`, so the read is `ShapeParser` → `FloatingShapeElement.ImageOpacity` and each backend uses its own alpha primitive rather than the colour matrix's alpha row. Still open: shape fills carry no COLOUR transforms (`FloatingShapeElement` has no effect fields; nothing in the corpus needs it yet); `a:clrChange`/`a:biLevel` parse to `None`; soft-focus/blur (`business-plans/02`) and warm-tone (`newsletters/07`) are unmodelled.
- **#9 Text measure inside shapes and text boxes.** The docDefaults `w:jc` cascade, math centring, HTML cell alignment and the pct-fixed-table column growth all landed. Still open: centred text can still wrap in a narrower measure than Word in containers other than pct tables — `cards/02`'s ticket-back TEXT BOX is ~40px narrow and its placeholder barely moved, `cards/16` is a flush-left 6-line variant of the same.
- **#10 Table-style conditional-region inheritance.** The autofit half of this item is closed. The distribution rule was never wrong: Word-probed with monospaced content whose preferred and minimum widths are arithmetic (a 15-token cell, a single 30-char token, a short cell), Word lays out 278.2 / 180.7 / 23.3pt against the CSS auto-table prediction of 276.5 / 180.0 / 25.5 — and refutes proportional-to-preferred, which wants 304.4 / 152.2 / 25.4. `CalculateContentBasedColumnWidths` already implements that rule and reproduces Word's columns and wrap point exactly. What skewed the `Detail` table was the *input*: its table style inherits `w:sz` through `w:basedOn`, which the whole-table `w:rPr` reader was dropping, so cells measured at 11pt instead of 9pt and every preferred width came out 11/9 too wide. With `ResolveStyleRunProperties` walking the chain the columns land within 0.9pt of Word, three of seven exact. Still open: the CONDITIONAL `w:tblStylePr` blocks do not inherit — a leaf's block shadows the base's for that region whole, where Word merges them per property.
- **#12 TOC page numbers.** Tab-stop clamp and Hyperlink-style suppression landed. Still open: numbers are the document's cached values (live PAGEREF needs a bookmark→page map), and they sit ~4pt left of Word because the clamp lands at the cell content edge where Word spills into the right cell padding. Deferred as risk-heavy for a 4pt MEDIUM: the clamp input is the layout's `maxWidth`, and letting it spill means plumbing the cell's right padding into paragraph layout, whose `pagedLayoutCache` is keyed by (paragraph, ContentWidth) — that width key is load-bearing, so a padding-aware clamp needs the padding in the cache key too.
- **#13 Footnotes/endnotes.** Reference marks and the PDF appendix landed. Still open: page-bottom pinning and the separator rule (both need page-level space reservation in layout); footnote text size is approximated at 10pt.
- **#14 Comment markup not rendered.** No balloon, no highlight, no markup-area page shrink (`comments/01`).
- **#15 SDT content controls.** Legacy `w:ffData` form fields render per-type like Word's print output. Still open: `content_control_inline` — `w:sdt` content controls still render as block widgets with chrome.
- **#16 Automatic hyphenation not implemented.** Word's hyphenated breaks don't happen (`hyphenation_auto`, `hyphenation_suppressed` para 3). (The `letters/03` "Customer S/ervice" mid-word break once noted here measured FIXED 2026-08-19, along with the whole mid-word-break family — agendas-minutes/11, resumes/03, resumes/13 — the cross-run word merge handles them all.)
- **#21 Rotation reserves the un-rotated footprint.** Picture and text-box rotation render correctly in all backends, but layout still reserves the shape's un-rotated box (documented all-backend limitation). The HTML export emits picture rotation/flip/crop as CSS since 2026-08-19; CSS transforms don't take layout space, which coincides with the same un-rotated reservation.
- **#34 ImageSharp does not synthesise bold.** Skia emboldens a bold run that resolved a face lighter than 700; ImageSharp falls back Bold → Regular with no equivalent, so those runs render at normal weight. **Outline dilation is exhausted** — five stroke-the-fill versions were built and all reverted, and the multi-font calibration behind that verdict (Word's bold adds ~26% ink, Skia's synthesis ~46%, per-typeface spread 1.00–2.53) is in `docs/word-features.md` under Bold. Anything further needs real weight: bundle the missing bold faces, or instance a variable font's `wght` axis.

### HTML export

- **#25 (HTML) Anchored/floating objects linearized in flow order — LARGELY CLOSED 2026-08-07.** Wrap-NONE floating IMAGES were emitted as in-flow block paragraphs (`WriteFloatingImage` sent everything that was not Square/Tight/Through to `WriteImageParagraph`), so decorative art consumed flow height and stacked down the document; they now place absolutely like shapes. Both kinds also positioned from the DOCUMENT ORIGIN, which is wrong for the corpus majority — 128 of ~207 wrap-none floats declare `wp:positionV relativeFrom="paragraph"` against 24 page-relative — and collapsed every page's background art onto page one, since the export has no pages. Both now resolve against an empty zero-height `position: relative` wrapper at the float's own place in the flow. Corpus: 79 rendered exports moved, 39 collapsing (brochures/03 4565px → 1026, cards/19 12303 → 4074, labels/05 4268 → 1043), 34 unchanged, 6 taller and inspected clean (page-2 art now sits at its own anchor). `w:wrapTopAndBottom` deliberately keeps its paragraph — it is the one non-wrapping type that genuinely displaces text in Word. **A second slice closed the last placement gap: cell-anchored art was being DROPPED entirely.** The DOCX parser detaches a float out of a cell's flow content into `TableCell.Floats` (so the cell measures without it) and the exporter read only `Content`, so every cell-anchored drawing vanished — labels/14's blob artwork, business/04-05's banners and watercolour blobs, brochures/04/06/07, letters/13, menus/06 (8 scenarios, 10 anchors). Cells holding art now carry `position: relative` and their floats place against the cell, which is Word's own rule (`wp:anchor@layoutInCell`, default true). A first attempt fixed the `AppendCellContent` switch instead and changed NOTHING — that case is unreachable for DOCX because the parser has already removed the floats; the dead code was reverted rather than left in.

  **Corpus-wide the linearization is CLOSED**: no scenario now exceeds 2x Word's page height (median 0.75), where brochures/03 alone was ~2.7x and cards/19 far worse. Still open, and inherent rather than a placement bug: a page-RELATIVE float approximates its page top by the anchor's flow position, so a fixed-layout multi-panel document still overlaps where Word separates by page — brochures/03 is the case, its whole design living in two anchored groups of 29 pictures positioned per page. Closing that needs the export to paginate, which it deliberately does not.
- **#35 Cell paragraph spacing in the HTML export — CLOSED 2026-08-19.** A cell paragraph that declares before/after `w:spacing` now leaves the inline `<br />` join and renders as a real `<p>` with explicit margins (`HtmlExporter.AppendCellContent`); zero-spacing cell paragraphs keep the compact inline model, so only cells that actually carry spacing changed. Cleared the family (cover-letters/05/07/09/10/12, letters/12, newsletters/03, compatibility_mode_14), all verified against the regenerated renders. EMPTY separator paragraphs were already spacers; body-level paragraphs were never affected.
- **#31 HTML/AltChunk input gaps.** Block-level CSS, named colours, image sizing, paragraph pitch, table styling, `margin-left` indents (0.75pt/px, 2026-08-19), the `border`-attribute table model (2026-08-20: detached per-cell boxes with cellspacing gaps, collapsed inside rules under `border-collapse`/`cellspacing=0`, the frame at the attribute width — `_probe_htmlborders`) and the CSS box model (2026-08-24: paragraph/div borders as `w:pBdr` with padding as `w:space`, per-edge longhands, per-cell CSS borders as `w:tcBorders`, vertical margins with Word's collapse rule — probed by round-tripping amplified fixtures through Word's own HTML import, `docs/html-import.md`), imported list items at the bare line box with one closing block gap and block edge-whitespace shedding (2026-08-21, `HtmlParser.EndListBlock` / `TrimEdgeWhitespace`) all landed. Still open: cell-level inline formatting is flattened (`cell.TextContent` builds ONE run, so `<b>`/`<span style>` inside a cell lose their formatting); `vertical-align` on cells is unmodelled; cell padding composes slightly tighter than Word; per-cell CSS margins render as a uniform grid.

### Spreadsheet input (`Inputs/excel`)

- **#36 Sheet drawings are pinned to the sheet's first page.** `SheetDrawingParser` emits every drawing
  paragraph-anchored, ahead of the table, so each binds to the page the sheet starts on. A sheet that
  paginates therefore keeps all of its art on page one. `invoice-accessibility-guide`'s first sheet is
  the extreme case and now renders a BLANK second page: its grid is twelve cells of narrow column-A
  text Excel all but clips away, and everything a reader sees — banner, contents list, thumbnail — is
  drawing. Excel's `expected_0002.png` is a full landscape page (1573 unique colours, ink over rows
  72-761). The three page-2 baselines are on `BaselineHealthTests.knownDegenerate` until this lands.
  Fixing it means placing a drawing on the page its anchor rows fall on, rather than on the first.
- **#39 No horizontal pagination.** A sheet wider than the page is scaled down rather than split into
  left/right page strips. `to-do-list` spans A:Q asking for no `fitToPage`, so Excel prints two strips
  and the render prints the left one and breaks vertically instead — landing on the right page count
  for the wrong reason, with a near-empty second page (allow-listed in `BaselineHealthTests`).

### Word-reference (`expected_*.png`) anomalies worth re-checking rather than "fixing"

- **#32** `newsletters/12` draws an olive stripe Word hides — verify against the DOCX before treating Word as wrong. `complex_tables`'s reference renders its title and section headings LARGE BOLD BLACK while the package's own styles.xml declares Heading1/Heading2 as 16pt/13pt BLUE (2E74B5) non-bold — Morph renders the declared styles, so the reference reflects a Word style-repair or rebuild of this hand-authored fixture, not a Morph defect (moved out of the per-scenario tally 2026-08-19). (The `cards/04` half of this anomaly is resolved: the stray tree and bird flock were out-of-frame group children, removed by group-frame clipping — Word was right.)

---

## Per-scenario findings

### agendas-minutes/01

- MEDIUM | all | p1 | vertical compression: schedule-table rows ~10% shorter and section gaps tighter, so the ADDITIONAL INFORMATION section ends ~130px (0.85in) higher than Word
- MINOR | html | - | classroom illustration stacked above the title instead of floating to the right of it

### agendas-minutes/02

- MEDIUM | pdf | p1 | bold weight lost: FINANCIAL MEETING/AGENDA title renders regular weight
- MINOR | all | p1 | agenda table rows slightly tighter, cumulative ~half-line upward drift by the last row

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
- CLEAN: html

### agendas-minutes/08

- MAJOR | html | - | Page-break decoration (blue band + orange ring shape) is emitted mid-flow and drawn across the "COMMITTEE REPORTS" heading, overlapping the text
- MINOR | html | - | Header decorative shapes distorted: orange half-donut renders as solid semicircle, title-band ring renders as rounded-square ring

### agendas-minutes/10

- MEDIUM | all | p1 | Cumulative upward drift: agenda table rows and "Additional Information" block end ~37px (~1.4 line heights) higher than Word
- CLEAN: html

### agendas-minutes/12

- MINOR | all | p1 | Right edge of the [Date] gray bar and "Meeting Notes" dark banner is ~10-15px off vs Word (bar width mismatch, visible as a solid strip in the diff)
- CLEAN: html

### agendas-minutes/14

- MEDIUM | all | p1 | "Attendees: Helbe Sokk, ..." renders as two lines ("Attendees:" alone, names on next line) vs Word's single line, pushing all following content ~1 line lower
- MINOR | html | - | Decorative teal stripes at top and bottom of the page are missing

### agendas-minutes/15

- MEDIUM | skia,imagesharp | p1 | Numbered-list continuation lines indented deeper than Word (wrap lines don't return to the hanging-indent column), changing item 4's wrap ("...ribbon, click / an Insert option." vs Word's "...click an / Insert option.")
- MINOR | all | p1 | Body sits a constant ~6px above Word from the date block down (band 2 is +10, then −6 steady — a one-time element-height difference in the title/date area, not accumulation). The former ~0.6pt/line pitch drift is FIXED (2026-08-04): it was the parser's invented 1.04/1.08 default line-spacing multipliers — Word probes showed absent `w:line` is exactly single (see `DocumentParser`'s `lineSpacingMultiplier` comment); per-line pitch now matches Word identically.
- CLEAN: html

### agendas-minutes/16

- MINOR | all | p1 | Whole content block sits ~13px higher than Word; DATE/TIME/MEETING CALLED rows slightly shorter so the offset grows to ~17px by NEXT MEETING
- [known] MINOR | skia,imagesharp | p1 | Residual pixel-level differences on the pink leaf/dot decorations (Skia SVG render vs ImageSharp PNG fallback, documented in notes.md)

### agendas-minutes/17

- MINOR | all | p1 | Title block ("TEAM" / "AGENDA") sits ~30px lower than Word on skia/pdf and ~15px lower on imagesharp; info rows and the three bullet columns now align
- MINOR | html | - | First divider rule renders below the "Meeting time" row instead of above it, and both divider rules extend to the viewport's far left edge past the content margin

### agendas-minutes/19

- [known] MEDIUM | all | p1 | Contact-table rows (especially the empty ones) render shorter than Word (~25pt vs ~30pt), so rows drift progressively upward and the table ends well above Word's (documented in notes.md)
- CLEAN: html

### bar_tabs

- MINOR | skia,imagesharp,pdf | p1 | text lines drift upward progressively (~5-10px by the last paragraph) versus Word

### border_style_variants

- MINOR | all | p1 | a run border (`w:bdr`) does not push its glyphs right by the drawn stack plus floored `w:space` the way Word does (`_probe_runbdr`: 1.2pt at 0.75pt/1pt, 6.6pt at 3pt/4pt); the engine grows the box outward from the run instead, so section 1's text sits that much further left than Word's inside its boxes
- MINOR | all | p2 | at sz=96 (12pt) the border band is painted across the paragraph text instead of outside it, so the label reads "ingle, sz=96"

### brochures/01

- MEDIUM | all | p2 | bullet before "GET THE EXACT RESULTS YOU WANT" is drawn much smaller than Word's dot; in skia/imagesharp it is also teal instead of Word's pink/magenta
- MINOR | all | p1,p2 | body/contact text blocks sit ~5-10px lower than Word, drift growing down each panel

### brochures/02

- MEDIUM | skia | p1,p2 | short blue divider rule (below "Meet director: Ravi Costa" on p1, below the Day-2 finals list on p2) rendered as a multi-row hatched/striped block instead of a solid line (ImageSharp and PDF match Word)
- MINOR | all | p1,p2 | small block shifts: red title lines sit ~20px lower on skia/imagesharp, date/venue and schedule text offset ~10px on all backends
- MEDIUM | html | - | red dashed decorative graphic overlaps the "Event officials" text lines (Judge's coordinator / Meet director)
- MINOR | html | - | divider rule renders as a tiny hatched box, and the "August 12th - 14th" line crowds the title's descenders

### brochures/03

- MEDIUM | all | p2 | right circle photo sits ~20pt higher than Word (its greyscale rendering landed 2026-07-19)
- MEDIUM | all | p2 | page content sits too high: Relecloud block ~0.3-0.45in up, itinerary rows ~0.25in up, and "ConnectAbove"/"Launch Event" footer links 60px too high (tucked under the card instead of centered in the navy band)
- MINOR | pdf | p1,p2 | photo interior crop wider than Word and the other backends (more scene, hands smaller)
- MEDIUM | html | - | second and third photos render as unclipped rectangles — the export has no ellipse clip (no `border-radius`, `ClipToEllipse` unread by `HtmlExporter`); their greyscale landed 2026-08-29 with the recolour, and the Event-itinerary card's teal background renders since the sweep re-read 2026-08-20

### brochures/04

- MAJOR | all | p1,p2 | roof-chevron accent shapes missing everywhere (above the quote, above the brochure title, above each "Headline 1")
- MINOR | all | p2 | brick-wall photo (blip-filled custGeom, now contour-clipped): ImageSharp draws it unclipped (documented contour-mask gap)

### brochures/05

- MEDIUM | all | p4 | spice-tray and soup photos lose Word's tight crop — the full image is shown zoomed out with visibly smaller subjects
- MINOR | all | p1 | body paragraphs wrap at different words (same line count, different break points)
- MINOR | all | p4 | headings/text blocks sit ~7px higher than Word (p1/p2 re-measured 2026-08-19 at 1-4px — stale)
- MEDIUM | html | - | orange background panel behind the CONTACT US / logo block missing
- MINOR | html | - | table-of-contents dot leaders missing

### brochures/06

- MINOR | all | p2 | quote-box geometry residual: dash column sits at the box's right edge where Word hatches inside
- MAJOR | pdf | p2 | top-right couple photo rendered ~30% narrower and shifted ~110px right, bleeding to/clipped at the right page edge (left portion of Word's crop lost)
- MEDIUM | all | p2 | right-column reflow: "To replace any of the pictures" paragraph wraps 4 to 5 lines in a narrower block, and the services list ("Passport Expediting"..."Trip Insurance") sits 20-80px lower than Word
- MINOR | skia,imagesharp | p2 | couple photo drawn slightly taller with content shifted ~8px vs Word's crop
- MAJOR | html | - | quote box text "We don't merely book your travel..." and attribution missing from the HTML export

### brochures/07

- MEDIUM | all | p1,p2 | body text (lorem paragraphs, contact text, right-rail paragraphs) rendered visibly bolder/heavier than Word, with shifted wrap points
- MINOR | all | p1,p2 | text blocks (CONTACT US group, LOREM IPSUM columns, ABOUT US group) uniformly shifted ~10px
- MAJOR | html | - | the ABOUT US quote (large " mark + white lorem text) is missing — its yellow panel renders since the float-placement passes but EMPTY, and overlaps the second column's LOREM IPSUM heading (re-read 2026-08-20)
- MEDIUM | html | - | body text renders bold where Word shows regular weight

### brochures/08

- MINOR | all | p1 | the "Contoso Logo" frame sits 8px low (Morph y=924-1194, Word y=916-1185); its width and x match Word exactly and it carries 17743 white pixels against Word's 19879
- MINOR | pdf | p2 | numbered client list vertical spacing looser than Word (~50px vs ~38px between items)
- MAJOR | html | - | "Contoso Logo" framed box missing from the export (the photo-overlap and invisible-MAKE-IT-YOURS claims measured stale 2026-08-20 — headings clear, white text on its orange panel)

### business-plans/02

- MINOR | pdf | p1,p2,p3,p4,p5,p6 | Uniform small downward drift (~10px) of body content producing ghost doubling of text and table rules. (The two skia/imagesharp p1 position MINORs measured stale 2026-08-20 — the page's bands sit within 11px of Word.)
- MEDIUM | html | - | Hero wheat photo shows the sharp un-blurred original, missing Word's soft-focus treatment.

### business-plans/03

- MEDIUM | all | p1 | title "B2B BUSINESS PROPOSAL" fits one line where Word wraps it to two ("B2B BUSINESS / PROPOSAL"), pulling the whole left column up; right-column sections otherwise align (re-measured 2026-08-19 — the old off-page-address reading is stale)
- CLEAN: html

### business-plans/04

- MEDIUM | imagesharp,pdf | p1 | Title "ONE PAGE PROPOSAL" rendered in a visibly lighter/regular serif weight instead of Word's heavy bold display weight (HTML export shows the correct bold, confirming the source asks for it).
- MEDIUM | all | p1 | Content below the intro drifts up cumulatively ~0.2-0.3in: 2x2 section-grid inner border sits ~25px above Word's and the outer frame bottom ~48px above (~27px in PDF).
- CLEAN: html

### business-plans/05

- MEDIUM | skia,imagesharp | p1 | Body content progressively compressed upward — section headings and the PREPARED BY/PREPARED FOR blocks end up to ~0.5in higher than Word (line spacing too tight for the display serif), with word-level rewraps.
- MINOR | pdf | p1 | Full-page cream background tint minutely off (em=0.99 — nearly every pixel faintly differs, invisible side-by-side).
- MINOR | pdf | p1 | Small downward drift (~10px) doubling the divider rules and footer block positions.

### business-plans/06

- MEDIUM | all | p1,p2 | Document-wide bold loss: cover title "CLIENT PROPOSAL", "Prepared for:/by:" labels, grey numerals 01-05 and yellow section headings all render regular/light instead of Word's heavy bold (title ink 25-50% lower)
- MINOR | all | p1 | title block sits ~20-30px lower than Word
- MINOR | skia,imagesharp | p1 | contact block ~40px lower than Word (PDF matches Word's position)
- MINOR | all | p2 | whole section stack shifted down uniformly ~40-50px; PROBLEM STATEMENT paragraph breaks lines at different words (same line count)
- MEDIUM | html | - | title "CLIENT PROPOSAL" and numerals 01-05 render light instead of Word's heavy bold (same bold-loss as rasters; the yellow-terminates-mid-section and no-gap claims measured stale 2026-08-20)

### business-plans/07

- MINOR | all | p1 | intro paragraph, four section blocks and footer contacts sit 30-50px lower than Word (footer band itself correctly placed)
- MEDIUM | html | - | pale-green footer band starts mid-contact-block (labels and first contact lines sit above/outside it) instead of enclosing the whole PREPARED section, and stops at content width
- MINOR | html | - | title line spacing collapsed so "PROPOSAL" caps touch the "BUSINESS" baseline

### business-plans/08

- MAJOR | pdf | p1 | last contact lines "Seattle, WA 89101" / "Santa Fe, NM 11121" pushed past the page bottom and entirely absent
- MAJOR | skia,imagesharp | p1 | "Seattle, WA 89101" / "Santa Fe, NM 11121" clipped mid-glyph at the page bottom edge (contact-block line spacing inflated ~38% pushes them into the margin)
- MEDIUM | skia,imagesharp | p1 | title "Business proposal" shifted right ~75px and down ~30-55px (Word has it flush at left margin)
- MEDIUM | pdf | p1 | second title line "proposal" indented ~75px right of "Business" (Word has both lines flush left); title also ~55px low
- MINOR | html | - | top accent line renders as a short block near its start position instead of the full line (re-read 2026-08-20 — the "missing, only a stray dot" and green-on-green/title-collision claims are stale: sections 1-2 render green-on-white and the title lines no longer overlap; the line-2 indent matches the raster finding above)
- MINOR | html | - | list numbers "3./4./5." rendered tiny beside large section headings (Word renders number and heading at the same size)

### business-plans/09

- MEDIUM | all | p3 | "PUT THE PLAN INTO ACTION" heading pulled onto the bottom of p3 (Word starts the section on p4)
- MEDIUM | all | p1 | cover title "TARGET AUDIENCE PROFILING PLAN" and "INTERNAL DOCUMENT" render bold vs Word's light weight (ink +18% ImageSharp, +38-39% Skia/PDF)
- MEDIUM | skia | p2 | heading "QUESTIONS TO NARROW DOWN YOUR TARGET AUDIENCE" wraps to two lines (single line in Word, ImageSharp and PDF)
- MINOR | all | p2,p3 | footer sits ~68px lower than Word
- MEDIUM | html | - | blue cover background extends into the body and cuts through the QUESTIONS FOR CONSUMERS table
- MEDIUM | html | - | cover title bold vs Word's light weight

### business-plans/10

- MAJOR | skia,imagesharp | p3,p4,p5 | Pagination distribution wrong despite matching page count: "List/Define all pertinent items" pulled from p4 up onto p3, and the CAMPAIGN SIGN-OFF heading plus its intro line pulled from p5 up onto p4, so p5 starts mid-table at the first signature row
- MAJOR | pdf | p3,p4,p5 | Pagination distribution wrong despite matching page count: bullet "List all pertinent items." pulled onto p3, and the CAMPAIGN SIGN-OFF heading plus its intro line render on p4 (Word puts the whole sign-off section on p5), so p5 starts mid-table at the first signature row
- MEDIUM | all | p2,p3,p4 | Table grid incomplete: header row and first body row of each table (PLAN OVERVIEW, NECESSARY EVENT RESOURCES, APPROVAL) render with no outer box or vertical cell borders — only the horizontal rule under the header — while Word draws a full grid on every row
- MEDIUM | all | p2,p3,p4 | Table-style bold lost: header-row text ("Practice:"/"Name", "Resource"/"Role"/"Estimated Work Hours", "Title"/"Name"/"Date 1"/"Date 2") and first-column labels ("Name of Campaign:", "Campaign Manager:", "Subject Matter Expert:") render regular weight instead of bold
- MEDIUM | html | - | Cover date line "April 4, 20XX" renders white-on-white below the cover art — each cover line repeats its 342pt before-spacing, pushing the date (and the title block) past the fabric image onto the white page (the old "missing" reading re-read 2026-08-20; the pdf subtitle-gap claim measured stale — pdf matches skia within 1px and skia matches Word within 2)
- MEDIUM | html | - | Same table defects as raster backends: header row and first body row of each table lack box/vertical borders, and header/first-column bold is lost

### business-plans/12

- FINDING 2026-08-06 (from the image-height measurement fix): the p1 cover renders the 520x461pt inline photo at its full height on both paths, but Word shows only ~264pt of it (photo visibly ends at ~311pt) — the lower half is overdrawn by later rows' fills in Word's cover collage, a z-order/overdraw the engine does not reproduce. The old baseline's band-error 0.0 was a compensating accident (rows measured without the image squeezed the layout back to Word's landmark positions while pixel error was 35.9 grey); with rows honestly sized the cover scores worse (60.7) until the overdraw is modelled. Undug.
- MAJOR | all | p3,p4,p5,p6,p7,p8,p9,p10,p11,p12,p13,p14,p15,p16,p17,p18 | footer page number missing on every content page (Word shows "3".."18" bottom-right; all three backends render nothing there)
- MAJOR | skia,imagesharp | p1 | cover text block pushed ~1in down: colored-arrows logo sits clipped at the bottom page edge and "First Up Consultants" is pushed off-page entirely (missing)
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

- MEDIUM | all | p5-p23 | "Avenir Next LT Pro Demi" headings and lead-ins render at the bundled 700 weight where Word draws the lighter Demi (re-measured 2026-08-19: the old "Skia drops run-level bold" reading is stale — Skia and ImageSharp now render identically heavy; `_probe_demi` showed all backends resolving named-Demi families alike, and Word resolving Demi+bold to a lighter face than 700). Closing it means bundling a 600-weight Avenir face; no code path is wrong given the faces available
- MEDIUM | html | - | Cover's grey title-band background missing in the HTML export (title/subtitle on plain white); all other content, images, tables, and TOC page numbers are intact

### business-plans/15

- MEDIUM | all | p1 | Cover title block indented ~0.9" further right than Word so "Business Plan" wraps onto two lines (3-line title vs Word's 2), pushing the divider rule and "Caneiro Group" down; the Email/Phone/Address block sits ~0.9" lower than Word.
- MEDIUM | all | p2,p3 | Footer bar ("BUSINESS PLAN | APRIL 25, 20XX" + number) is drawn on the TOC pages where Word shows less. **Not the header/footer z-order rule** — landing that (`docs/floating-art-pipeline.md`) moved p2 by −0.0001 and left p3 untouched. And "suppresses entirely" overstates it: Word's own p2 footer strip carries 4129 dark pixels against Morph's 4483, so Word draws a footer there too; only p3 is lopsided (1866 against 4063). Re-read both pages before treating this as one finding.
- MINOR | all | p1 | Title underline rule spans only margin-to-margin instead of extending to the page's left edge as in Word.

### business/01

- MEDIUM | all | p1 | memo header table columns too narrow — "Holiday closure" wraps to two lines under RE and the COMMENTS paragraph wraps to 3 lines vs Word's 2
- MEDIUM | all | p1 | footer block (CANEIRO GROUP, Tel/Fax, black rule) indented ~125px right of Word's left-margin position
- MINOR | skia,imagesharp | p1 | date "05.26.2023" rendered ~20px higher than Word (its underline aligns correctly)
- CLEAN: html

### business/02

- MEDIUM | all | p1 | COMMENTS label + paragraph row sits ~28px higher than Word (gap below the divider rule collapsed)
- MEDIUM | html | - | COMPANY NAME heading renders on white above the beige panel instead of inside it (shaded background starts too low)

### business/03

- MEDIUM | all | p1 | one ink band fewer than Word (16 vs 17) with the LAST band displaced +221px after the #43 bundle (theme Calibri, sidecars + wedge apply) — a block-level reflow, unattributed; the pre-bundle render had 17 bands
- MEDIUM | all | p1 | cover overlay box geometry off: white Company Name box ~40px narrower and navy Report Title box ~35px wider than Word, both shifted up ~15-25px
- MEDIUM | all | p2 | middle text column and top-right sample block start ~57px further left and are wider than Word, changing wrap points (sample block wraps 4 lines vs Word's 5)
- MINOR | all | p2 | page content sits high and the footer page number low (p1 re-measured 2026-08-19 at 2px — stale)
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

### cards/05

- MINOR | all | p2,p4,p6,p8 | placeholder-text pages differ in band structure from Word (odd picture pages re-measured 2026-08-19 within 2px — stale)

### cards/06

- MEDIUM | all | p2 | invitation text blocks drawn too high with slightly tighter line spacing — improved 2026-07-19 (any-floating-shape empty-paragraph rule, −0.001..−0.0014/page): the residual offset is now ~0.13in on the top card
- MEDIUM | html | - | teal divider rule missing on both invitation backs (the heading-overlap and candle-overflow claims measured stale 2026-08-20 — the heading sits in its own column and the backs are clean)

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

### cards/11

- MEDIUM | all | p1 | card 2's "Celebrate!" caption sits ~85px higher than Word (captions centre correctly now)
- MEDIUM | all | p1 | second card's balloon picture block placed ~85px higher than Word (inter-card gap collapses from ~167px to ~100px); first card's picture starts 8-22px higher and both pictures are ~12px (2%) wider
- MEDIUM | all | p1 | small balloon clipart on the card backs (left half) placed 70-150px higher than Word on both cards
- CLEAN: html

### cards/12

- MINOR | html | - | Fold/cut guide borders (vertical divider, dashed mid-page line) not exported (the hoisted-art claim measured stale 2026-08-20 — THANK YOU renders on its card art)

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

- MEDIUM | html | - | fold guide rules missing entirely (the flame-glow and detached-text claims measured stale 2026-08-20 — glows sit behind the flames and each "Happy Birthday!" composes with its candles)

### cards/19

- MINOR | skia,imagesharp | p1 | card content sits ~15-20px higher than Word (title text top-aligned instead of vertically centered in its box; p2-p4 measured within 5px 2026-08-20 — trimmed to p1, whose band structure still differs 5 vs 10)

### comments/01

- MAJOR | all | p1 | comment markup missing entirely: right-side gray comments pane, balloon "Commented [R1]: Looks good to me.", pink highlight box on the commented text, and the dashed connector line are all absent
- MEDIUM | all | p1 | body text drawn full-size at the normal top margin instead of Word's shrunk-to-fit-markup layout (Word scales the body down and places it lower to reserve the markup column)
- MAJOR | html | - | comment content "Commented [R1]: Looks good to me." missing from the HTML export (only the body sentence is present)

### complex_spacing

- MEDIUM | all | p2 | two ink bands fewer than Word (23 vs 25) after the #43 bundle — p1's identical deficit was the fractional sidecar space and closed when the space rounded to a whole layout pixel, so p2's remaining merge is in its line-RULE sections (exact/atLeast), not the advance track
- MEDIUM | all | p7 | Combination 7's spacing collapses differently: Morph applies its full before/after 840 (87px each) where Word shows ~37px less above and ~87px less below (contextualSpacing against a DIFFERENT-styled neighbour?), and Morph's p7 top sits 34px lower than Word's — the mirror landing fixed the block's indents and 6-line wrap band-for-band, these vertical gaps are what remains (+124px max displacement below the block)
- MEDIUM | all | p6 | mid-page +13..16px displacement from Combination 3-5's spacing (before 480/atLeast 350/exactly 550 interactions); band count and indent geometry match Word after the mirror landing
- MINOR | all | p1 | bands 18+ sit 14-49px low because the "Hanging indent 1440" paragraph wraps in 2 lines against Word's 3 — Word breaks one word earlier on a 106-character first line (sub-point fit margin, sidecar advances live); the old line-pitch-quantisation attribution is REFUTED by `_probe_pitch2` (pitch is NOT quantised — see docs/word-features.md, Line Spacing)

### complex_tables

- MEDIUM | all | p1,p2 | vertical compression pulls the complex-merge table and the "5. Calendar-Style Layout" heading onto p1 (PDF also pulls its intro sentence), leaving p2 nearly empty except the calendar — Word's p2 holds the complex-merge table plus all of section 5
- MINOR | all | p1 | "COMPLEX MERGE" header cell text wraps to two stacked lines; single line in Word
- MINOR | all | p2 | calendar's merged "15-16 Event" cell wraps to two lines, making the last calendar row ~50% taller than Word's single-line version

### cover-letters/01

- MINOR | all | p1 | paragraph 2 wraps the whole word "evidence-based" to the next line where Word breaks at the hyphen ("…implementing evidence-" / "based medicine…") — the header contact-line wrap once noted here measured FIXED 2026-08-20 (23 bands align within 2px post-kerning)

### cover-letters/03

- MEDIUM | skia,pdf | p1 | body wraps one word earlier per line — paragraph 1 becomes 7 lines vs Word's 6 (orphan "care."), landing the signature ~2 lines lower
- MINOR | imagesharp | p1 | letter body drifts about half a line lower by the signature

### cover-letters/04

- MINOR | imagesharp | p1 | paragraph 2 pulls "at" up to line 1 ("…as a Bookkeeper at") where Word breaks before it, leaving continuation lines starting with a stray leading space
- CLEAN: pdf

### cover-letters/05

- MEDIUM | pdf | p2 | body paragraph spacing wider than Word on p2 (25 bands vs skia's 22; p1/p3 measured identical to skia 2026-08-20, and the ~2-lines-lower claim dissolves with them)
- MINOR | html | - | a teal corner triangle overlaps the footer contact block's phone line (the per-page-palette and address-overlap claims measured stale 2026-08-20 — both clusters render their own theme colors clear of the address)

### cover-letters/06

- MEDIUM | all | p1 | location-pin and phone glyphs missing from the contact strip
- MINOR | all | p1 | letter body drifts ~1 line lower than Word by the signature

### cover-letters/07

- MINOR | imagesharp | p1 | First paragraph wraps at different words than Word (line 1 ends "…Manager position", next line starts with a stray leading space)
- MINOR | all | p1 | Letter body drifts upward slightly with tighter paragraph spacing, ending ~0.7 line higher at "Victoria Burke"

### cover-letters/08

- MINOR | all | p1 | signature block ends ~10px (~0.4 line) higher than Word (re-measured 2026-08-19; the old ~1.5-line reading is stale)
- MINOR | skia,imagesharp | p1 | closing paragraph's wrapped line starts with a leading space (" your review,")

### cover-letters/09

- MAJOR | all | p1 | Sidebar content redistributed: "DIAN NUGRAHA" sits ~0.4in higher and the contact rows spread down the column so the email and website rows land on the yellow/pink waves (white-on-yellow, barely legible) instead of on the navy panel
- MEDIUM | all | p1 | Decorative wave shapes mis-rendered: bottom waves start higher than Word
- MEDIUM | all | p1 | Bullet "Knowledge of the latest technology in [industry or field]?" wraps with the "?" orphaned alone on the next line (Word breaks at "[industry or / field]?")
- MEDIUM | skia,imagesharp | p1 | Letter text drifts upward ~1–2 lines by the "Sincerely, / Dian Nugraha / Enclosure" block
- MINOR | imagesharp | p1 | Second how-to paragraph wraps at different words than Word (" place it appropriately." line starts with leading space)

### cover-letters/10

- MEDIUM | skia,imagesharp | p1 | First body paragraph wraps to 6 lines vs Word's 5 (breaks at different words; wrapped lines gain stray leading spaces)
- MEDIUM | skia | p1 | Date, Adatum address block, and header contact info render in a visibly heavier weight than Word (imagesharp/pdf match; the pdf 0.7-line drift claim measured stale 2026-08-20 — pdf matches skia to the pixel)

### cover-letters/11

- MEDIUM | all | p1 | Accumulated tighter section/line spacing — letter and sidebar content end ~3 lines higher than Word by "Enclosure" (pdf matches skia within 1px, so the separate 0.5-line pdf reading collapsed into this — 2026-08-20)

### cover-letters/12

- MEDIUM | all | p1 | "In my current role…" paragraph wraps to 5 lines vs Word's 4 ("residents." pushed to its own line); final paragraph also rewraps mid-sentence ("Thank / you" with a stray leading space on " on family health")
- MINOR | all | p1 | Entire content block sits ~1 line lower on the gradient page than Word

### cover-letters/14

- MEDIUM | all | p1 | Tighter paragraph spacing — letter ends ~1.5 lines higher than Word at "Tonnie Thomsen" (wrapped line " that supports students'…" also starts with a stray space; pdf matches skia within 1px, so its separate 0.8-line reading collapsed into this — 2026-08-20)
- MINOR | skia,imagesharp | p1 | Right-hand recipient address ("4321 Maplewood Ave / Nashville, TN 65432") sits ~half a line higher than Word relative to the LILLI ALLIK block

### cover-letters/15

- MINOR | skia,imagesharp | p1 | Letter body drifts up ~0.5 line by the "Chanchal Sharma / January 13, 20XX" block

### document_capture/01

- MAJOR | pdf | p1 | footnote/endnote separator lines missing — the notes render as invented "Footnotes"/"Endnotes" sections straight after the body instead of pinned to the page bottom
- MAJOR | skia,imagesharp | p1 | footnotes/endnotes rendered as invented "Footnotes"/"Endnotes" bold heading sections with "1." numbering placed directly after body — Word's separator lines absent and the footnote is not pinned to the page bottom

### feature_capture/01

- MEDIUM | all | p1 | rotated table header cell renders HORIZONTAL: the engine never consumes `TableCellProperties.TextDirection` (an engine-flip orphan — the production renderers rotated it; re-read 2026-08-20, worse than the old "single vertical line" reading). Word shows two stacked vertical lines "Hea/der". Needs a rotated-content mechanism on `PlacedCell` plus the wrap-at-column-width equilibrium
- MINOR | html | - | "All features" paragraph left-aligned instead of right-aligned

### header_row_repeat/01

- MEDIUM | all | p1,p2,p3 | Table rows slightly shorter than Word, accumulating one extra row per page: p1 ends at Person 25 (Word: 24), p2 spans 26-50 (Word: 25-48), p3 starts at 51 (Word: 49); header row correctly repeats on p2/p3 in all backends and all 60 rows present

### html_complex

- MEDIUM | all | p1,p2 | "Visit our website for more information." paragraph spills to p2 top — the page break lands one element off Word's. BLOCKED by the intro-wrap root cause below (a narrow-measure issue); not cleanly fixable in isolation. (The interior-gridlines finding landed 2026-08-20 — border-collapse maps to inside rules.)
- MEDIUM | all | p1 | **Intro paragraph wraps 3 lines vs Word's 2 — ATTEMPTED 2026-07-21, REVERTED (net regression).** TWO causes, not the sup/sub: (1) `HtmlParser` did NOT collapse HTML whitespace — literal source newlines in the `<p>` became hard breaks (the intro source has newlines after "and" and "have", exactly where Morph broke). (2) Morph's HTML body text measures ~6-9% NARROWER than Word (same font/size — first line ink height matches — so it's font metrics + sup/sub at 0.7×), so it UNDER-wraps. Fixing (1) alone (`CollapseWhitespace` on text nodes in `ParseInlineNodes`, char-by-char run→single-space, `<pre>` unaffected) is objectively correct HTML behaviour BUT over-corrects the intro to 1 line (Word's 2) because (2) then dominates, and REGRESSES the metric: html_complex p1 +0.068 AE / −0.021 SSIM, p2 +0.011, html_css_margin_padding +0.011 (only 3 scenarios changed; the newline-breaks had been *accidentally compensating* for the narrow measure). It also only SHIFTS the reflow (p2 then loses the "5. Styled Boxes" heading to p1) instead of fixing it. To truly land: fix the whitespace collapse AND match Word's text width (a corpus-wide font-metric issue — same class as the `header_footer`/`resumes` "wraps 3 vs 2 lines" findings), then the page break seats correctly.

### html_css_alignment

- MEDIUM | all | p1 | Table height:100px ignored (row 33px tall vs Word's 61px; interior borders land with the 2026-08-20 model)
- MINOR | all | p1 | Justified paragraph breaks after "entire line" instead of Word's "fill the" (same 2-line count, different break word)
- MEDIUM | html | - | width:100% lost in the export (content-width table; interior borders land with the 2026-08-20 model)

### html_css_margin_padding

### html_images

- MINOR | all | p1 | third image+caption block renders ~30px taller than Word (y354-566 vs 362-544), accumulating ~17px drift by the page bottom — re-measured 2026-08-19; the four-images-33%-oversized px-as-pt claim landed with the px→pt attribute rule
- CLEAN: html

### html_lists

- MEDIUM | all | p1 | Spacing around lists missing: no blank gap after "Unordered list:"/before "Ordered list:" (Word has clear gaps), item leading slightly looser, whole block ends higher
- MINOR | all | p1 | Bullet glyph noticeably smaller/lighter than Word's large Symbol-font solid bullet

### html_nested_lists

- MEDIUM | all | p1 | Blank gaps before "Nested ordered lists:" and "Mixed nested lists:" headings missing (renders run sections together at uniform item spacing)

### html_table

- MEDIUM | all | p1 | Table rows far more compact than Word: row pitch against Word's 58-59px at 150 DPI on identical ~17px text — the missing ~14pt/row cell-paragraph after-spacing below. (The touching-cells half landed 2026-08-20: the default 2px cellspacing now separates every html table's cells, borderless included.)

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

- MINOR | all | p1 | per-cell CSS margins render as a uniform grid — Word offsets each cell's box by its own margin (the 10px-margin cell inset, the 20px-left-margin cell pushed right) where Morph draws equal boxes. The disintegrated-grid claim is gone with the 2026-08-20 border model: both sides now draw a coherent per-cell grid, and the old skia wrap claim went with it

### html_table_cell_padding_css

- MEDIUM | all | p1 | CSS cell padding under-applied: table ~20% narrower and rows ~25% shorter than Word, right-column text ends flush against the table border (per-cell borders land with the 2026-08-20 model; the export now carries the padding too, though as raw px-as-pt values — covered by this conversion question)
- MEDIUM | skia | p1 | "20px all sides" wraps to two lines (single line in Word, ImageSharp and PDF)

### html_table_cellpadding

- MINOR | all | p1 | first column runs slightly narrow — "Cell with 15px padding" wraps where Word keeps one line (the detached per-cell border model landed 2026-08-20 from `_probe_htmlborders`; the per-cell boxes, cellspacing gaps, frame and exported padding all match Word — see docs/html-import.md for the probe law and the four earlier reverts' history)

### html_table_styled

- MINOR | all | p1 | fixed-width table's flexible third column runs ~10px wide (Morph ~156px vs Word's 146px): Word renders the two DECLARED columns slightly wider than their 100px/200px (161/315 against 156/312), so its flexible remainder is correspondingly smaller. Table width and the first two columns now match — cell text lands within 2-9px of Word (gridlines land with the 2026-08-20 border model)
- MEDIUM | html | - | table width styling ignored — the width:100% styled table renders content-width (206px of the 624px content box) and the 100px/200px fixed columns are exported as width:100pt/200pt (~33% too wide)

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

### image_rotation/01

- MEDIUM | all | p1 | rotated inline image not clipped: all three backends draw the full 386px diamond where Word clips to a 255px band with a flat top edge at the frame top (Skia drew rotated inline images UPRIGHT until 2026-08-19 — the engine painter never had the transform; it now rotates like the others). The clip law is unprobed — the fixture's wp:effectExtent is all-zero yet Word still lets the sides and bottom overflow while clipping the top

### image_wrap_square

- MEDIUM | all | p1 | one ink band fewer than Word (26 vs 27) after the #43 bundle (theme Calibri — sidecars and the wedge apply): a wrap merges two of Word's lines; same class as complex_spacing p2's residue, not closed by the whole-pixel space
- MINOR | imagesharp | p1 | Links paragraph wraps differently: "downloadable" pulled up to the first line ("...or even downloadable / documents...") vs Word's break after "even"
- MINOR | all | p1 | cumulative vertical drift of blocks (~10-20px up, pie chart slightly offset) with structure intact (p2 measured within 2px on 2026-08-20 — trimmed to p1)
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

### labels/02

- MINOR | skia,imagesharp | p1 | TO:/FROM: text block sits ~8px higher than Word

### labels/03

- MEDIUM | html | - | dotted tear-line rules missing from all tickets

### labels/04

- MINOR | all | p1 | the light-blue hexagon accents render since 2026-08-20 (gradient shapes now build preset contours) but more saturated than Word's soft look — gradient-stop alpha is unmodelled in `GradientFill`
- MINOR | html | - | blue accent bars vertically misaligned with their label text (bar tops start ~a line below "Name")

### labels/06

- MEDIUM | all | p1 | the "ADMIT ONE" stubs render HORIZONTAL and overlap each other at the ticket seam — Word rotates them vertical along each ticket's edge (a rotated text-box path that loses its rotation; re-read 2026-08-19)
- MEDIUM | html | - | the ~20 "ADMIT ONE" stub texts stack as a column of horizontal lines at mid-sheet instead of along each ticket's stub edge (the decomposed-fragments claim measured stale 2026-08-20 — tickets assemble with stars and EVENT NAME composed)

### labels/09

- MINOR | html | - | Stub ticket numbers ("00 01" etc.) partially clipped by the narrow red stub columns

### labels/10

- MEDIUM | all | p1 | text block sits ~20px high inside each label — toward the top edge where Word centers it (re-measured 2026-08-19: the old overflow-above-the-label reading is stale, the block is inside the shape now)
- MEDIUM | all | p1 | Teal rule that belongs directly under "YOUR NAME" renders detached lower in the label (under "Street Address" or "City, St Zip" depending on column)
- MINOR | html | - | Teal rule under "YOUR NAME" missing entirely; the text grid also runs one label late against the pill artwork (first pill empty — the labels/11 drift family; the old overflow-above claim measured stale 2026-08-20)

### labels/11

- MAJOR | html | - | the first label rows' white text lands above the brush artwork on the white page (invisible) — the brushes place at Word-page offsets from their anchor wrappers while the text grid reflows at its own pitch, so text and brush drift apart until mid-page (re-read 2026-08-20; the brushes do render in a 3-across grid behind the later rows)

### labels/15

- MINOR | all | p1 | script "from" glyph drawn ~9px further left and slightly wider than Word, nearly touching the preceding label's text; text rows/columns otherwise align within 1-2px
- MINOR | html | - | cream page background stops about two-thirds down the strip, leaving the last rows of labels on a white background

### letters/01

- MEDIUM | all | p1 | decorative header/footer bands are far too saturated — the render carries 5.7x Word's mean chroma (re-measured 2026-08-08; the earlier "~15%" understated it): circle sampled (138,180,254) against Word's paler (182,199,238), band (50,66,93) vs (59,64,77). The theme accents are purple (accent1 AD84C6), so the shapes resolve the right hue — the residual is an under-applied lightening transform (lumMod/lumOff), not the wrong colour the finding first claimed. Fills sit in a group so the transform chain is group-level
- MEDIUM | all | p1 | Recipient address block starts ~37px lower than Word, pushing the salutation, body and signature down by the same amount (was ~120px; the cell-measure contextual-spacing fix of 2026-08-08 took most of it, and the residue is upstream of the address block — the block is already ~19px low where its first line starts)

### letters/02

- MINOR | all | p1 | body text block uniformly shifted down ~half a line
- MINOR | html | - | right-aligned "Letter of Recommendation" title and Date lose their alignment (title centered-left, Date lands beside recipient block)

### letters/03

- MINOR | all | p1 | body text block uniformly shifted down ~two-thirds of a line

### letters/05

- MINOR | html | - | dashed decorative elements absent, and the purple circle grazes the "Contoso" sender block at the later sections' tops (the malformed-logo, address-overlap and no-shapes-in-section-3 claims all measured stale 2026-08-20 — every section renders its full shape set)

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
- MAJOR | html | - | signature image broken — placeholder "Image of signature" shown instead of the script signature (the grey-background claim measured stale 2026-08-20 — the grey page and white card render)

### letters/11

- MINOR | all | p1 | body lines break at slightly different words (para 1 breaks after "Importers" vs "Importers to"), same line counts

### letters/12

- MAJOR | html | - | bottom-right diagonal-stripe corner decoration missing entirely (the pdf 2-lines-lower claim measured stale 2026-08-20 — pdf matches skia within 1px and skia matches Word within 2)

### letters/13

- MINOR | skia,imagesharp | p1,p2,p3 | whole content block (text and NP logo) sits ~1-1.5 lines higher than Word
- MINOR | pdf | p1,p2 | content block ~1 line higher than Word
- MINOR | imagesharp,pdf | p1,p2,p3 | hatched (striped) banner wedges render as solid fills — imagesharp and pdf flatten several wedges (e.g. the second tile's upper and left wedges); skia now matches Word
- MINOR | imagesharp | p1,p3 | several paragraphs wrap at different words than Word (e.g. "...personal taste. Go /", "built-in font / combination")
- MAJOR | html | - | the three letter copies get inconsistent body-column widths (~586px, ~471px, ~622px)
- MINOR | html | - | page-3 left-edge banner rendered as an inline horizontal strip (side placement/rotation lost)

### menus/01

- MEDIUM | all | p1,p2,p3 | entire page content (text and, on p2/p3, the floral art) sits ~30-65px (150dpi) higher than Word with slightly compressed section spacing; offset is largest at the p3 title (~60px)
- MAJOR | html | - | light-grey page background missing: all text renders on white, only the first flower image block carries the grey (Word shows grey behind all 3 pages)

### menus/03

- MAJOR | all | p1 | the gold "EVENT TITLE" heading now renders (SdtCell cells reach the model); its gold rule lines still render misplaced/mis-sized
- MEDIUM | all | p1 | both text columns shifted left (instructions column ~25% of page width left of Word) and vertically compressed (menu column ends ~65px high)
- MINOR | skia,imagesharp | p1 | numbered steps render the number at the left indent with the step text centered separately, leaving a large gap (Word centers "2. Press Ctrl+C" as one unit; PDF matches Word)
- MINOR | skia,pdf | p1 | full-page navy background tint fractionally off Word's (em≈1.0 but below visible threshold)

### menus/04

- MINOR | html | - | stray light-grey rectangle rendered below the week tables

### menus/05

- MEDIUM | html | - | page-1 and page-3 section headings render as "Appetizer"/"First Course" in a fallback bold font, losing the decorative all-caps display font Word uses (page-2 headings are correct; the below-the-blob and linearized-blob claims measured stale 2026-08-20 — title and menu overlay the green blob)

### menus/06

- MINOR | all | p1 | menu items drift progressively downward (~half a line by page bottom; p2 measured within 1px and p3's vertical drift at 0 on 2026-08-20 — trimmed to p1)
- MINOR | html | - | the p3 block's bottom red bar overlaps its last menu lines (the bars-absent and pale-blue-page-1-only claims measured stale 2026-08-20 — every section sits on pale blue with its bars)

### menus/07

- MINOR | all | p1 | food photos shifted right ~15-25px

### menus/08

- MEDIUM | all | p1 | right instruction block wraps at a different word than Word ("...select the whole / cell.)" vs Word's "...select the / whole cell.)")

### menus/09

- MEDIUM | all | p1 | inner decorative frame renders since 2026-07-19 (outline-only emission; the +0.003 metric tick is the new-ink offset penalty — crops confirm the double frame + corner accents present). Residual: frame geometry slightly off Word's (octagon corner cuts vs rounded corners)
- MINOR | all | p1 | chalkboard ~16px narrower and ~9px shorter than Word (right/bottom edges pulled in)

### newsletters/01

- MAJOR | all | p1,p2,p3,p4 | White frame borders missing from every photo; images rescale to fill the full frame box so the visible crop differs (e.g. p3 mother-and-daughter photo shows extra scene at smaller scale, p1 kitchen photo sits flush with the tan panel edge)
- MEDIUM | all | p2 | Right-column photo box, its caption and the whole "Adding your own message" section pulled up ~130px vs Word
- MEDIUM | all | p1 | Left-column caption, "Happy holidays from our family to yours!" heading and body text sit ~50px higher than Word due to the resized kitchen photo
- MEDIUM | skia,imagesharp | p4 | Right-column article ("Write with ease using Editor" heading + body) sits ~2 lines higher than Word
- MEDIUM | pdf | p1,p4 | Right-column blocks drift ~2 lines lower than Word (p1 pull-quote, p4 big photo + caption end noticeably lower)
- MINOR | all | p1,p2,p3,p4 | "Page N" footer sits ~25-35px higher, overlapping the content panel bottom instead of sitting on the grey footer band
- MINOR | all | p1,p2,p4 | Body paragraphs re-wrap at different words than Word (line counts mostly unchanged)

### newsletters/02

- MAJOR | all | p2 | "The observer" byline and paragraphs are shifted ~80px left, overlapping the right edge of the numbers photo
- MEDIUM | all | p2 | Main-column paragraphs wrap to more lines than Word (bold lede 6 lines vs 5; following paragraph 5 vs 4)
- MEDIUM | all | p2 | "Work with the industry's best" column renders wider with fewer, longer lines, so the column ends ~1in higher than Word
- MINOR | all | p1 | Entire page content (masthead, sidebar, hero image, article) sits ~10px higher than Word
- MEDIUM | html | - | Hero network-figures image renders above "The Review" masthead — wrong content order vs the document

### newsletters/03

- MEDIUM | imagesharp | p1 | INDUSTRY NEWS lead paragraph wraps to one extra line (12 bands vs Word's 11; skia matches the band count since the kerning landing, within 12px). The p3 extra-line half of the old finding measured stale 2026-08-20 — 8 bands both sides within 8px, with only the last three lines breaking at different words

### newsletters/04

- MAJOR | all | p3 | Grey "Breaking news" table section grows past the bottom margin: column text is clipped mid-line at the page edge and the page footer ("3 ——— Issue 10") is missing entirely
- MEDIUM | all | p1,p2,p3,p4 | Line breaks differ from Word throughout, redistributing text across the newspaper columns (scoop/next-hot columns and sidebars break at different paragraphs, blocks end noticeably lower)
- MINOR | all | p1,p2,p3,p4 | Full-width banner photos slightly oversized vs Word (right edge extends further, solid strip in diffs; captions shift down accordingly)
- MAJOR | html | - | Pull-quote circular beige background missing — quote renders as plain text in a bordered cell

### newsletters/05

- LARGELY FIXED 2026-08-06 (last real flip-gate stand): `CanonicalParagraphMeasurer.LayoutLines` — the measurement view feeding `TableHeightCalculator` — computed line heights from the font pitch alone, ignoring inline images, while placement (`LayoutLineContents`) maxes them in. A cell whose paragraph holds only the 213pt school photo measured as a ~12pt mark line, so every row below overlapped it by the difference (body text drew ON the photo). One-line fix: measure maxes with image heights, so measure = placement. Engine PDF vs Word: p1 36.8 → 17.2 (exact production tie), p2 21.2 → 8.7 (production 34.9 — engine 4× better, band count = Word's), p3 29.1 → 17.4 (tie), p4 26.8 → 19.1 (production 17.8; residual is a bottom block where both paths err ~35px in opposite directions). Corpus: newsletters/03/07 and brochures/07 (−151/−181 on p2) improved; regressions concentrate on collage covers (bp/12, brochures/04 p2, newsletters/04 p2) where images previously overlapped in ways that accidentally scored better — those pages carry separate unrelated defects (see bp/12's cover entry).
- MEDIUM | all | p1,p3 | body copy under "Welcome back to school!" wraps to one extra line (17 text bands vs Word's 16), block ends 26-80px lower
- MEDIUM | skia,imagesharp | p1,p3 | "Welcome back to school!" heading and body start ~45-50px lower than Word (extra gap inserted below the school photo)
- MEDIUM | skia,imagesharp | p1,p3 | sidebar "Ms. Tanaka" contact block ~22px and "Upcoming Events" block ~48px lower than Word (PDF within 10px)
- MEDIUM | all | p2,p4 | sidebar "Fall highlights" block sits ~210-235px (~1.5 inch) higher than Word
- MEDIUM | skia,imagesharp | p2,p4 | "Our next area of focus" heading and following paragraphs ~35-38px lower than Word (extra gap below classroom photo; PDF matches)
- MINOR | all | p1,p3 | school photo ~1% narrower than Word (right edge 7-9px short), lighting the whole photo in the diff
- MINOR | all | p2,p4 | classroom photo shifted ~9px left at identical size

### newsletters/06

- MAJOR | all | - | page-count mismatch: all three backends produce 6 pages vs Word's 4 (each 2-page edition spills onto a 3rd page; text sets ~10% wider so every column wraps earlier and blocks run longer). **NB: this mismatch means the scenario records NO per-page AE/SSIM at all (`PageDiffs` is null), so nothing here is metric-judgeable — crop-vet by hand.**
- MEDIUM | all | p1,p4 | masthead contact line wraps to two lines ("www.sycamoremiddle.org" drops to its own line) vs one line in Word
- MEDIUM | all | p2 | "NOTES FROM THE COUNSELORS" left column collapses to ~1-2 words per line (column far too narrow) and the text snakes to the bottom of the page
- MEDIUM | all | p3 | overflow spill pages render on a white background instead of the section's page color (light blue / yellow)
- MINOR | all | p1,p4 | dotted-ornament window around "SYCAMORE NOTES" title is taller than Word's (dot rows beside the title partially dropped); small "SYCAMORE NOTES" strip logo on p2/p5 wraps to two lines
- MAJOR | html | - | page backgrounds wrong/missing: first edition gets a yellow background instead of light blue, and all content after the first section break renders on white (no blue/yellow backgrounds)
- MAJOR | html | - | all decorative icons render as filled squares (same placeholder issue as raster backends)
- MINOR | html | - | big title renders as "SYCAMORENOTES" — the word space collapses to the same width as the letter-spacing gaps

### newsletters/07

- MEDIUM | imagesharp | p1,p2 | body and sidebar text rendered in bold weight throughout vs Word's regular Century-Gothic-style face
- MEDIUM | all | p1,p2 | body text sets visibly larger than Word so paragraphs wrap to extra lines (lead paragraph 6 lines vs Word's 5)
- MEDIUM | html | - | content order wrong: living-room photo renders above the "MODERN LIVING" masthead/title block instead of below it
- MINOR | html | - | black accent rule renders overlapping the "Your guide to buy or rent" subtitle text

### newsletters/08

- MEDIUM | all | p1 | right-column masthead block ("HOUSE & HOME NEWS / WINTER ISSUE / EDITION 09, VOL. 10") and intro paragraphs sit ~40px (≈2 line heights) higher than Word
- MINOR | all | p1,p2 | decorative swoosh/band boundaries off by several px and the light-blue contact strip plus its text sit ~15px lower than Word
- MEDIUM | html | - | cover photo present since 2026-07-19 but as an unclipped rectangle (no freeform crop in the HTML export)

### newsletters/09

- MEDIUM | all | p3,p4 | photo captions detach from their images and collide with neighbouring content (caption box overlaps the section rule above "Mirjam Nilsson" on p3; caption floats over the "scoop of the day" columns on p4)
- MINOR | html | - | full four-sided borders drawn around the bridge sidebar box and the pull-quote box where Word shows only top/bottom rules

### newsletters/10

- MEDIUM | imagesharp | p1 | the four green section headings ("Something that made me smile today…", "Currently dealing with...", "Thankful for...", "Looking forward to...") rendered bold instead of Word's light weight
- MINOR | all | p1 | content drifts progressively upward, ~15px by the bottom rule (each section slightly shorter than Word); DD/MM/YYYY and all rules offset
- MEDIUM | html | - | section headings bold dark-green instead of Word's light weight (the "My Journal title invisible" claim measured stale 2026-08-20 — the white h1 overlays the leaf banner)

### newsletters/11

- MINOR | all | p2 | p2 columns and header sit ~20px higher than Word (p1 re-measured 2026-08-19 within 4px — stale)
- MEDIUM | html | - | Floating photos emitted in wrong order: hero photo appears before the "LAWN AND LANDSCAPE" masthead, group photo appears before the "Tony's landscapes and more" headline

### newsletters/12

- MEDIUM | imagesharp | p1 | ImageSharp clips the top half of the "NEWSLETTER" title line (a horizontal cut through the glyphs; re-read 2026-08-20 — the old shifted-down reading is superseded, pdf renders the title clean, and skia's glyph-collision claim measured stale with bands matching Word within 2px)
- MAJOR | all | p2 | Text right of the olive quote box is positioned left/too wide and drawn over the olive stripe graphic (text/graphic overlap, different line wraps than Word)
- MINOR | all | p2 | "MARGIE'S TRAVEL OFFERS..." section and 01-04 items shifted up ~1 line; quote text re-wrapped inside its box
- MAJOR | html | - | Absolutely-positioned blocks collide: right-column text renders across the purple dash column, and body text under the photo-grid blocks (re-read 2026-08-20 — the pull-quote now renders in its olive box and the white photo dashes/dash columns are present)

### newsletters/13

- MINOR | imagesharp | p1 | ImageSharp draws the arch crop unclipped (documented contour-mask gap)
- MEDIUM | html | - | Still-life photo content missing from the HTML export — the arch outline renders but the photo inside it does not

### newsletters/14

- MINOR | html | - | Graduation photo present since 2026-07-19; long-scroll preview metric ticked +0.015 from layout-order placement (the title-indent claim measured stale 2026-08-20 — both lines start at x=92, Word's own position)

### office_math

- MEDIUM | all | p1 | Built-up OMML fraction 1/2 (numerator stacked over denominator with fraction bar) is flattened to inline text "1/2"
- MINOR | html | - | Built-up fraction linearized as plain "1/2" in the HTML export (the serif math face and operator spacing land via the model)

### paragraph_borders

- MINOR | html | - | w:between group members render as separate CSS boxes with hairline gaps in the side rules (the border-group law landed 2026-08-20 — internal boundaries now show single shared between-rules, top/bottom edges belong to the group's first/last member)

### paragraph_spacing

- MINOR | all | p1 | text tracks slightly narrower than Word
- CLEAN: html

### postcards/03

- MEDIUM | skia,imagesharp | p1 | bottom row of postcard images shifted up ~23px (row gap collapsed, same defect as postcards/02); PDF matches Word's positions
- MEDIUM | all | p2 | placeholder "Click or tap here to enter text." rendered in a substituted bold dark sans, much larger than Word's small light script font, wrapping to 2 lines instead of 1
- MINOR | skia,imagesharp | p2 | bottom-row placeholder text and address lines sit ~7px higher than Word
- MEDIUM | html | - | same placeholder font substitution: heavy dark rounded font instead of Word's light handwriting script

### postcards/04

- MEDIUM | all | p1,p2,p3 | vertical layout compressed on every page: card panels ~25px shorter, title-to-photo and inter-card gaps ~25-35px smaller, cumulating so the second card sits 60-140px higher than Word
- MINOR | all | p2 | cupcake photo ~5% wider (429px vs 408px) than Word
- MINOR | all | p3 | rightmost (tilted-boy) photo ~9px wider than Word

### resumes/02

- MINOR | all | p1 | header text and both body columns sit ~8-13px higher than Word (uniform upward drift; artwork and divider positions otherwise match)

### resumes/03

- MEDIUM | skia,imagesharp | p1 | SKILLS entries vertically compressed (bar touches label, no gap between entries) so the block ends ~135px higher than Word; PDF spacing matches Word
- MEDIUM | pdf | p1 | Right-column HOBBIES/CONTACT blocks drift progressively lower (~2.5 line heights by the CONTACT block)

### resumes/04

- MAJOR | all | p1 | Last 2-3 lines of OBJECTIVE text overlap the yellow/pink wave shapes at the sidebar bottom (white-on-yellow, barely readable); waves also drawn ~100px higher than Word
- MEDIUM | all | p1 | Sidebar contact entries (address/phone/email/website) spaced ~2x further apart than Word, pushing OBJECTIVE down

### resumes/05

- MINOR | all | p1 | right-column sections end ~25px (~1 line) higher than Word and the sidebar box ~25px shorter (re-measured 2026-08-19, from ~107px)

### resumes/06

- MEDIUM | all | p1,p2,p3 | Education/skills rows roughly double-spaced (Creativity/Leadership/Problem Solving) with thicker bars; the Problem Solving row lands ~90px lower on top of the bottom decorative rectangle (on p3 the black bar merges invisibly into the black block)

### resumes/07

- MINOR | skia,imagesharp | p1 | Template rows ("College, location", "Graduation year", SKILLS labels) render ~10-16% lighter than Word (ink 888/824 against Word's 982 on the College row): Word synthesizes a heavier bold over Franklin Gothic Book than the raster synthesis produces, while PDF resolves Book+bold to the Demi face and lands closest at 1032 (`_probe_demi`; re-measured 2026-08-19 — the old "renders regular / PDF keeps bold" reading overstated all three)
- MINOR | all | p1 | Italic sub-lines (Bachelor of Arts Degree GPA, Relevant course work:) drawn ~0.25" further left than Word, starting left of their parent rows

### resumes/08

- MEDIUM | all | p1 | Vertical compression: sidebar sections (ABOUT ME/EDUCATION/SKILLS) end ~112-135px (4-5 lines) higher than Word and the left column ~30-50px higher

### resumes/09

- MEDIUM | pdf | p1 | Sidebar contact entries drift progressively lower (Website ~90px / ~2 lines below Word); skia/imagesharp match Word
- MINOR | all | p1 | Main content column uniformly shifted ~26px left of Word's position

### resumes/10

- LARGELY FIXED 2026-08-05 (flip-gate stand): the dominant component was `ParseSectionBreak` reading the ENDING sectPr's w:type — ECMA-376 §17.6.22 puts the break's type on the FOLLOWING section's sectPr (resumes/10: section 1 authors continuous for its own start; sections 2-3 author none → nextPage). The engine never broke at the section boundaries, so each page's decorative circle (paragraph-anchored to the next section's first paragraph) stranded at the previous page's bottom. With the lookahead fix all three circles sit at their page tops matching Word exactly and p2's band error halved (11.0 → 6.5). A Word probe en route (_probe_float_push) established that a paragraph-anchored shape NEVER pushes its anchor to the next page — it clips at the page edge. Residual (engine 6.2-6.5 vs production 3.6-4.3): two characterized components — the interior-edge row growth around the red heading rules lands one row late (heading envelopes net zero but the heading baseline sits ~3.8pt high / the post-heading gap ~3.8pt large), and cell empty-spacer marks resolve to the 11pt default font (13.43pt line) where Word's chain gives ~11.6-12.0pt (cells are excluded from the mark-rPr style resolution, DocumentParser ~9519).
- MINOR | html | - | SKILLS bullets black instead of accent color (the pdf progressive-drift MEDIUM measured stale 2026-08-20 — pdf matches skia within 1px on all three pages)

### resumes/11

- MINOR | all | p1 | Name block starts ~17px lower than Word, and the three thin divider rules sit ~15-20px higher (re-measured 2026-08-08; the body text between them tracks Word to +-1px)

### resumes/12

- MINOR | all | p1 | the coral rule below "Manager" sits 23px high — Morph draws it at y=477-483, Word at y=500-506. Geometry is otherwise exact (123x7px at x=132-254, 861 coral pixels in both), so this is purely the inline shape's vertical placement in its paragraph
- MINOR | pdf | p1 | "VICTORIA BURKE" name block sits ~20px lower than Word

### resumes/14

- MINOR | html | - | right-aligned tab dates ("20XX – 20XX", "20XX") render inline after the job/degree titles instead of at the right margin (the pdf drift MEDIUM measured stale 2026-08-20 — pdf matches skia within 1px)

### resumes/16

- MEDIUM | all | p1 | "Chanchal Sharma" name block sits ~20px lower than Word

### resumes/17

- MEDIUM | all | p1 | two-column body misplaced: column divider and right column (Skills/Hobbies/Profile) sit ~0.8in left of Word's position, and the narrower left column wraps its text differently
- CLEAN: html

### resumes/19

- MEDIUM | pdf | p2,p3 | 2nd and 3rd Experience entries drift lower on p2/p3 (band counts differ from skia by one; p1 measured identical 2026-08-20)

### table_autofit_no_widths

- MEDIUM | all | p1 | autofit column widths distributed differently from Word — "Full Name" column too narrow (header and "Jane Smith" wrap to two lines vs one in Word) while "Hire Date" is too wide (dates fit one line vs Word's two)
- CLEAN: html

### table_cell_spacing/01

- MINOR | all | p1 | detached table reads 181px tall against Word's 188 (~1px per gap): Word's 2×spacing gap runs between the border rules' inner FACES while the engine spaces their centres — the half-rule-width term (`_probe_cellspacing` law landed 2026-08-19, from 173px and structureless gaps)

### table_default_style_outer_borders

- MINOR | all | p1 | bottom border sits ~8px higher than Word (re-measured 2026-08-19 after the per-line cell-double fix; the line pitch itself now matches Word's two-rule structure)

### table_diagonal_borders/01

- MINOR | all | p1 | diagonal borders and cell borders drawn ~2x thicker and fully saturated (2-3px solid black / bold red+blue X) versus Word's ~1px grey/light hairlines; directions and red-tl2br/blue-tr2bl colors are correct

### table_grid_styling_padding

- MEDIUM | all | p1 | column widths differ from Word: Full Name column narrower (header "Full Name" and "Jane Smith" wrap to 2 lines; Word keeps both on 1) while Hire Date column is wider (all four dates fit on one line; Word wraps all four dates to 2 lines)
- MEDIUM | all | p1 | rows with two text lines are ~16px (~26%) taller than Word (77-79px vs Word's uniform 62px, header 78 vs 62), making the table ~46px taller overall (bottom border y507 vs y461)
- MINOR | all | p1 | whole table displaced ~12px right: Word draws the left border at x137 (offset left of the margin by the cell left padding) while all backends start it at the margin x149; right edge likewise 1126 vs Word 1138
- CLEAN: html

### table_layout_tall_row

- MAJOR | all | p1,p2 | tall second table row's company block ("Company Name", "123 Main Street") is absent from page 1 and rendered whole at top-right of page 2 instead of splitting at the page break like Word, which also exposes an extra "City, State 12345" line that Word's exact row height clips (never visible in Word).
- MEDIUM | all | p2 | letter body (Recipient Name through Title) starts ~170px (~4 line heights) lower than Word because the deferred tall row occupies the top of page 2.

### table_multipage

- MEDIUM | all | p1,p2 | table page-break lands two rows late: slightly shorter row heights let Rows 24-25 fit on page 1 (Word breaks after Row 23), so page 2 holds only Rows 26-29 vs Word's Rows 24-29.
- CLEAN: html

### table_of_contents/03

- MINOR | html | - | TOC tab leaders dropped — page numbers (1, 4, 9, 15, 27) render inline after each entry instead of leader-aligned (Word clips them at the narrow cell edge).

### wedding/01

- MEDIUM | all | p1 | invitation cards start ~0.26" higher than Word (intro-paragraph spacing compressed) and the text inside the cards drifts further up (~0.4" by the SATURDAY/RECEPTION lines) from tighter line spacing
- MAJOR | pdf | p1 | small "TO" rendered at SARA's baseline overlapping the SARA letterforms instead of on its own line between the two names
- MINOR | all | p1 | intro paragraph rewraps: "Create New Theme Colors" kept on line 2 so line 3 starts with an orphaned period (". Select your own colors...")

### wedding/02

- MEDIUM | all | p1 | PLEASE JOIN + pink banner block shifted up (~0.4" card 1, ~0.7" card 2), banner slightly shorter, rosebud now touching/overlapping the banner top edge
- MEDIUM | all | p2 | yellow DATE/TIME/LOCATION banner ~1/3 shorter (compressed line spacing) and shifted up ~0.35" (card 1) / ~1" (card 2); card-2 banner overlaps the right poppy and covers the small leaves beneath it
- MEDIUM | all | p1,p2 | text left inset (~0.5") lost: "PLEASE JOIN", banner DATE/TIME/LOCATION text, and "Registered at:/RSVP" block all flush with the column/banner edge instead of indented
- MEDIUM | all | p2 | table rows end higher than Word, so the bottom leaf pair renders below the card's bottom border (outside the card)
- MINOR | pdf | p1,p2 | thin gray bounding-box outlines drawn around the rotated floral images
- MEDIUM | html | - | floral pieces compose around the invitation since the float passes but off Word's arrangement — the rose sits high of the banner, single leaves scatter, and the pink banner overlays the name line (the left-margin-stack and flipped-poppy claims measured stale 2026-08-20)

### wedding/03

- MEDIUM | all | p1 | small gold-rings image displaced ~0.5" up (card 1) / ~1" up (card 2)
- MEDIUM | all | p1 | second card's content (pink picture + caption) sits ~0.5" higher than Word (first row shorter)
- MINOR | all | p1 | pink rings picture rendered ~3% larger and shifted slightly up/right

### wedding/04

- MEDIUM | all | p2 | first checklist item fits one line vs Word's two (the p1 0.85-0.95" compression measured stale 2026-08-20 — p1's bands sit within 4px of Word)
- MEDIUM | all | p1 | right-column items lose the gap between checkbox and text (box glued to text: "☐Choose the members of your wedding party.")

### wedding/05

- MEDIUM | pdf | p1 | date block also rendered bold where Word uses regular weight
- MEDIUM | all | p2 | menu list starts ~1 line high on p2 (gap below the wash heading too small; the p1 0.6in claim measured stale 2026-08-20 — p1's bands sit within 2px of Word)
- MEDIUM | html | - | watercolor washes exported as standalone images stacked above the panels instead of backgrounds behind the headings

### wedding/06

- MEDIUM | all | p1,p2 | card rows too short: fold/borders sit ~1in higher than Word and second-card content ~0.9in high; p2 bottom flower clusters straddle the card borders, p1 card2's poppy dips into the pink banner corner
- MEDIUM | all | p2 | invitation text line spacing compressed ~30%, block ends ~0.5in higher
- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/08

- MINOR | all | p1 | the circled "&" badge green ellipse + ampersand placement is sub-pixel off within the cell

- MEDIUM | all | p1,p2 | card panels shorter than Word (p1 borders end ~2in early, p2 ~0.4in) with content blocks 0.4-0.8in higher (Thanks-and-Dedication and time/venue blocks)
- MAJOR | html | - | green circled "&" badge missing

### wedding/09

- MEDIUM | all | p1 | invitation banners pinned to top of card rows instead of vertically centered (card1 ~1.5in high, card2 ~2.9in high); card frames end at ~70% page height leaving the bottom unframed
- MAJOR | all | p1 | card2's relocated "on our wedding day"/banner corner collides with the poppy image (text drawn over the flower; pdf card1 poppy also overlaps the yellow "&" box)
- MEDIUM | all | p2 | invitation text line spacing compressed ~30%, block ends ~0.5in higher
- MEDIUM | all | p2 | bottom purple/yellow cluster sits on the card boundary/page bottom instead of inside the cards
- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/10

- MINOR | all | p1 | the two "Candid photos..." placeholder items' checkboxes render light grey instead of dark like every other row in Word

### wedding/11

- MEDIUM | pdf | p1,p2 | teal "SATURDAY"/"10.25.20XX" date lines (both cards) and "ACCEPT | DECLINE" render bold where Word shows regular weight
- MEDIUM | html | - | the four watercolor images render as detached standalone blocks above/beside the cards instead of as artwork inside the card frames

### wordart

- MAJOR | skia | p2 | "Arc Text Up" rendered ~15% larger and higher than Word, its glyphs overlap the subtitle line "These use DrawingML WordArt transforms"
- MEDIUM | all | p2,p3,p4 | WordArt items flow one page early vs Word: "Wavy WordArt" appears on p2 instead of p3 and "Slanted Down" on p3 instead of p4, shifting p3/p4 content up ~130px (total page count still matches)
- MEDIUM | skia,imagesharp | p2,p3 | Path-warp WordArt drawn at nominal font size instead of stretched to the shape bbox — p3 items (Wavy WordArt, Chevron Up/Down, Fade Effect, Slanted Up/Down) span ~180px where Word fills ~430px page width (hard ~23%)
- MINOR | skia,imagesharp | p2 | Arc/circle warps off position/size: ImageSharp places "Circle Text" ~85px left of Word's position; Skia draws Arc Text Down and Circle Text ~10-30% larger and shifted right
- [known] MINOR | skia,imagesharp | p2 | residual vertical offset of warped glyphs vs Word (inline-drawing layout-cursor drift documented in notes.md)
- MINOR | pdf | p10 | highlight bars render ~10% more ink than Word's (magenta 3049 sampled units against 2622): the box spans the font's full ascent-to-descent where Word's sits slightly tighter. All five colours are correct and present
- MEDIUM | all | p14 | Emboss/shadow character effects flattened: black "EMBOSSED" and "SHADOWED" lose their offset drop shadows in every backend; "IMPRINTED" engrave two-tone lost in ImageSharp/PDF (Skia approximates it with a light offset)
- MAJOR | html | - | the 12 WordArt shape texts export as plain small unstyled black paragraphs at the top (no warp, color, or display size), while all other sections keep full styling
- MINOR | html | - | emboss/engrave/shadow effects render flat (no drop shadows on "EMBOSSED"/"SHADOWED", no engrave on "IMPRINTED")

### wordart-envelope

- MAJOR | html | - | The four WordArt words are exported as small plain black text with no color, size, or warp styling — the blue/green/orange/red large display text is lost (heading and subtitle export correctly).
- MEDIUM | imagesharp | p1 | "Can Up" and "Can Down" are squashed to roughly half Word's glyph height — "Can Up" becomes a low flat ribbon hugging the bottom of its band leaving a large blank gap below "Deflate", and "Can Down" bows into a deep flattened smile arc instead of Word's full-height gently-warped letters.
- [known] MEDIUM | skia,imagesharp | p1 | Envelope warp shape deviates from Word on the Can Up/Can Down lines: sin-curve amplitude is much stronger and edge glyphs shrink to ~55% height so the leading capital "C" reads as lowercase, vs Word's near-uniform letter heights with a gentle arch (envelope curve + 0.55 minRatio design documented in notes.md).
- MINOR | skia | p1 | WordArt stack drifts upward with inter-line gaps nearly eliminated — "Can Down" sits ~60px higher than Word and almost touches "Can Up", where Word keeps clear separation between all four lines.

---

## Clean scenarios (faithful on skia, imagesharp, pdf and html)

`agendas-minutes/09`, `agendas-minutes/11`, `agendas-minutes/13`, `agendas-minutes/18`, `align_center`, `font_sizes`, `labels/05`, `labels/08`, `labels/16`, `letters/04`, `table_default_style_inside_h`, `align_justified`, `align_left`, `align_mixed`, `align_right`, `all_caps`, `block_quote`, `bold_text`, `bullet_list`, `colored_text`, `column_breaks`, `complex_document`, `content_control_inline`, `compatibility_mode_14`, `cover-letters/02`, `custom_margins`, `decimal_tabs/01`, `cards/10`, `deep_nested_list`, `document_protection/01`, `dot_points`, `embedded_font`, `labels/14`, `resumes/18`, `table_borders`, `even_odd_headers/01`, `even_odd_headers/02`, `explicit_break_blank_page`, `first_line_indent`, `font_families`, `field_codes_simple/01`, `footer`, `form_checkboxes`, `form_dropdowns`, `form_text_fields`, `gutter_margins/01`, `hanging_indent`, `header`, `header_banner_table`, `header_footer`, `headings`, `html_basic_formatting`, `html_css_colors`, `html_font_tag`, `html_inline_styles`, `html_links`, `html_paragraphs`, `hyperlinks`, `hyphenation_nonbreaking`, `hyphenation_soft`, `image_cropping/01`, `inline_group_crop`, `inline_image`, `italic_text`, `labels/01`, `labels/07`, `labels/12`, `labels/13`, `left_indent`, `letters/06`, `line_breaks`, `line_numbers_continuous`, `line_numbers_count_by_5`, `line_numbers_custom_distance`, `line_numbers_restart_page`, `line_numbers_restart_section`, `line_numbers_suppressed`, `line_spacing`, `line_spacing_at_least`, `line_spacing_exactly`, `long_paragraph`, `nonstandard_main_part_name`, `numbered_list_tracking`, `page_letter`, `cover-letters/16`, `resumes/15`, `menus/02`, `mixed_breaks`, `mixed_formatting`, `multiple_images`, `multiple_pages`, `multiple_paragraphs`, `nested_list`, `numbered_list`, `numbered_list_restart`, `page_a4`, `page_borders/01`, `page_breaks`, `page_landscape`, `page_legal`, `page_numbers`, `pct_pos_offset`, `postcards/02`, `resumes/13`, `rtl_paragraph`, `section_break_continuous`, `section_break_even_page`, `section_break_next_page`, `section_break_odd_page`, `simple_paragraph`, `simple_table`, `small_caps`, `strikethrough_text`, `subscript_superscript`, `tab_stops`, `table_alignment/01`, `table_cell_margin_per_cell`, `table_cell_padding`, `table_cell_padding_varied`, `table_colors`, `table_default_cell_margin`, `table_default_cell_margin_start_end`, `table_default_style`, `table_default_style_first_row_run_color`, `table_default_style_first_row_shading`, `table_explicit_heights`, `table_indent`, `table_text_direction`, `table_of_contents/01`, `table_of_contents/02`, `table_page_break`, `table_two_column_layout`, `table_vmerge_basic`, `table_vmerge_explicit_heights`, `text_wrapping_break`, `wide_table`, `three_columns`, `tracked_changes/01`, `two_columns`, `underline_text`, `wedding/07`
