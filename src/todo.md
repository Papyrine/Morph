# Rendering fidelity todo

Deep comparison of every scenario in `src/Tests/Inputs/` (387 fixtures: 321 Word with 557 reference pages, 40 Excel, 26 PowerPoint): `expected_*.png` (Word, 150 DPI, via RenderHelper) versus `skia_result#page_*.verified.png`, `imagesharp_result#page_*.verified.png`, `pdf_result#page_*.verified.png` (PDFium render), and `html_result.verified.png` (headless-browser screenshot of the HTML export).

Each finding: `severity | backends | pages | description`. `all` = skia+imagesharp+pdf. HTML findings ignore pagination/viewport-width reflow by design and only flag content/styling errors. Not reported: anti-aliasing texture, 1-2px subpixel shifts, ImageSharp's softer glyph rasterization. `[known]` = already documented as accepted in that scenario's notes.md. How a difference is judged — crops versus metrics, and what counts as noise — is `docs/fidelity-audit.md`.

**This file lists only what is still wrong.** A finding is deleted the moment it lands, and its durable knowledge moves to `docs/word-features.md` (feature behaviour and the evidence behind it), `docs/floating-art-pipeline.md` (anchored/floating art), `docs/html-import.md` (HTML and AltChunk input), `src/page_counts.md` (page-count experiment ledger) or `docs/fidelity-audit.md` (comparison method). Nothing that has shipped is described here — for how a fix was reached, read those docs and the git history. This is a temporary working document; it is expected to shrink to nothing.

**Open: 3 major, 22 medium, 52 minor = 77 findings across 56 scenarios.** The tally is recounted from the `severity | backends | pages` lines below rather than hand-adjusted (hand-adjusted, it drifted twice). Not every finding has been re-read against the current baselines: every MAJOR was re-judged against Word's page on 2026-10-01; the findings on the 105 fixtures whose Word references were regenerated in Calibri on 2026-10-02 were measured against the Aptos references they replaced and have not been re-read since; and a finding that names a specific block N px off is usually still live where one describing whole-page drift is usually stale (aligning each page's ink profile against Word's gives a median page offset of 2px, but a median worst local displacement of 11px). How a landing moved the corpus is in its commit message and in `src/page_counts.md`, not here.

## Systemic issues (cross-scenario root causes)

These patterns repeat across many scenarios; fixing one clears whole families of the per-scenario findings below. **IDs are stable** — findings reference them by number — so a closed issue's number is retired rather than reused, and the list is deliberately gappy.

### All raster + PDF backends

- **#1 Per-section headers, footers and page numbering** — landed 2026-09-06 (`SectionBands`, `Fragmenter.SelectVariant` / `PageNumbers`; the laws and the three Word-read fixtures are in `docs/word-features.md`, Headers & Footers). Still open: `@w:chapStyle`/`@w:chapSep` chapter numbering; the band a page carrying a CONTINUOUS section break takes (the engine keeps the settings in force when the page ends — unprobed). HTML/Markdown evaluate page fields as a one-page document ("Page 1 of 1") by design — the reflow really is one page.
- **#25 Word's row-split trigger** — landed 2026-08-07 with the strict floor fit, the vMerge tie rule and style-inherited `w:pageBreakBefore`, and whole-table floor strictness on 2026-09-05 (`docs/word-features.md`, Multi-page Tables / Page Break Before; the attempts and their measurements are in the git history). Still open, un-probed LibreOffice leads (hypotheses, not evidence): Writer charges a split row's trHeight floor once ACROSS fragments (follow minimum = declared − earlier fragments' heights, tabfrm.cxx:5123) where the engine floors only a whole row carried to a region top; Writer synthesizes split-edge rules by borrowing the neighbour row's border and suppresses them under repeated headlines (paintfrm.cxx:2858/2932) where the engine draws the fragment's own full box; `COLLAPSE_EMPTY_CELL_PARA` (a cell whose sole paragraph is empty collapses to 1 twip); and the broader always-on flag inventory (`sw/inc/IDocumentSettingAccess.hxx:36-106`, DOCX defaults `WriterFilter.cxx:300-341`) — `HEADER_SPACING_BELOW_LAST_PARA`, `TREAT_SINGLE_COLUMN_BREAK_AS_PAGE_BREAK` and `IGNORE_TABS_AND_BLANKS_FOR_LINE_CALCULATION`.
- **#43 Text advances: the breaking and autofit rules are settled; what is still open is listed here.** Word breaks lines and sizes autofit columns on the font's plain linear advances at the size as authored, with GPOS kerning scaled the same way (Word-probed 2026-10-02: `docs/layout-engine.md`, "The crux"; `docs/word-features.md`, Table Auto-fit and Kerning), and every face has measured that way since. The per-glyph `.wordadvances` sidecars and the space-compression wedge that stood in for it from 2026-08-30 are gone, with no page count moved (`src/page_counts.md`, experiment 23). Kerning followed the same day: `w:kern` comes down the style ladder as Word takes it, and the measurer adds it linearly and across spaces (`docs/word-features.md`, Kerning). Still open, each measured:
  - **A ligature's advance is not measured.** Word's layout takes the ligature glyph's advance where `w14:ligatures` is on, and the measurer sums the letters'. One line of evidence, indirect: of the 1,606 paragraphs of the 2026-10-11 justified probes the engine misses one, a sentence whose first line holds `off`, which Word squeezes at a measure where the letters' widths put the squeeze-to-stretch ratio at 0.521 against the 0.5 limit the other fifteen thresholds bracket between 0.497 and 0.503. Calibri's `ff` ligature is 65 units narrower than `f` + `f` (0.63px at 12pt), which is what brings that line under the limit. Not probed directly (step a measure over a sentence full of `fi`/`ff`/`fl`, with `w14:ligatures` on and off), and the painters would have to draw the ligature for the ink to end where the width says: Skia and PDF substitute none today.
  - **An autofit column takes its text's unrounded linear width, and Word's runs a fraction of a pixel over that**: 0.3px in compatibility mode 12 and 0.7px in mode 15, behind a hairline border (990 probed tables). Unmodelled; it is under a pixel and moves no break.
  - **Glyph placement inside a line is not Word's.** Word draws whole-pixel advances at the em rounded to a whole pixel on its 120-dpi grid (the 26.6 scaled advance `round(units × ppem × 64 / upm)`, rounded up from 30/64px in mode 14 and older and to the nearest pixel in mode 15), lets the pen fall about 5px behind its linear position inside a run, brings it up at a space, never pulls it back, and narrows the spaces of a line that would overrun its measure. A painter draws each run at the backend's own advances at the nominal size. Where the whole-pixel advances sum wider than linear it shows: 12pt Calibri is drawn 3.6% wider (784px for a sentence of 757), so Word's full lines reach the measure and Morph's stop short of it (multiple_pages p1: Word's first lines end at x=1124, Morph's at 1095; two_columns the same; both moved 0.006 AE further when the wedge, which had been spreading such lines, went). 11pt Calibri draws at about its linear width (690px for a sentence of 693.9) and shows nothing. Closing it needs per-word or per-glyph placement in the painters (`PlacedGlyphRun`, deferred). The glyph RENDER size is em-rounded as well (8pt Calibri draws at 7.8pt), an unmeasured ink-scale deviation of up to ~3% at half-point sizes. Every painter kerns its ink as the line was measured (2026-10-03); Word's own drawing of a kerned pair snaps the kern to 1/16px and rounds the pair's first glyph to a whole pixel (`docs/word-features.md`, Kerning), which is part of the same whole-pixel placement.
  - Text set in a face that is not bundled is measured on a substitute's metrics: business-plans/15's Univers falls back to Tahoma (measured closest at −5.9% against the real Univers installed on the reference machine), and Segoe UI Semibold, Franklin Gothic Demi and Avenir Next LT Pro Light are not bundled either. Their wraps are approximations; the honest fix for Univers is a licensed Univers-metric face, which is out of Morph's hands. **Several findings below are a missing face and nothing else, and the face is in Office's cloud-font cache on the reference machine:** Grandview Display (letters/08's wraps and its light signature), Avenir Next LT Pro Light and Demi (letters/07). They are proprietary, which `src/Fonts/README.md` rules out bundling; the open-licensed ones the cache holds went in on 2026-10-11 (Karla, Source Sans Pro, Playfair Display and Work Sans faces) and closed letters/09, letters/10, business-plans/04 and business-plans/06.
  - The suite's confirmation pass has not always been deterministic on near-fit scenarios: on 2026-09-06 `regenerate-baselines.sh` promoted resumes/16 from its first pass and the second pass rendered both Skia and ImageSharp pages differently on five consecutive regenerations, as did a third Skia-only run, and business-plans/15 did the same on 2026-09-05. Host renders of the same input are byte-identical across four processes, so the variance lives in the parallel test process, not in the layout rules. Re-examined 2026-09-24 and NOT reproduced: every Word scenario rendered on Skia and ImageSharp in two separate host processes (12-way parallel, shuffled order) hashed byte-identical, as did a sequential pass against three in-process parallel rounds; the measurer's caches are per-conversion and nothing in the engine shares mutable state across conversions. The 2026-09-24 and 2026-10-02 regenerations both confirmed clean, the second reproducing an earlier run of the same code byte for byte across 782 files. `regenerate-baselines.sh` restores any promoted PNG whose decoded pixels match the previous baseline (`scripts/png-identical`), so encoder-only churn does not reach the tree.

- **#45 Spreadsheet geometry** — the width/scale/range rules landed 2026-08-14 (code comments on `SheetGridBuilder.MaxDigitWidth` / `ToPoints` and `SpreadsheetParser.ResolveRange` / `ResolveScale`, pinned by `ColumnWidthTests`). Still open:
  - **Row heights render at a BODY-FONT-dependent factor, and Arial-bodied corpus books now visibly pay it** (isolated 2026-08-14, `_probe_fontgraft`; victim confirmed by triage the same day). The same declared heights draw at ~15/16 under an Arial body and essentially exactly under a Verdana-11 theme. `social-media-editorial-theme-calendar` — Arial 12 body, all rows declared `ht` with `customHeight` — had body row pitch 260px against Excel's 259 BEFORE the landing and 267-268 after: the old too-small fit scale was cancelling the ~6% row-height excess, and the corrected scale exposed it (black header band 168 exact → 170, everything below drifting down cumulatively). Fixing it means applying the per-font factor to declared heights for the faces that carry it, which needs the factor measured per face on real-workbook-derived fixtures — do not model a constant. Likely mechanism (unverified): Excel's print scale derived from the Normal font's GDI metrics on screen vs printer. `household-organizer` measuring ~1.05 against the graft's 1.00 remains the one unexplained residue — plausibly non-`customHeight` rows re-auto-sized at open.
  - **`simple-basic-pink-blue-timesheet`'s width is still +5.7%** (649px against Excel's 614 after the landing, from +7.3% before). Its body font is Constantia 11 — bundled in `src/Fonts`, but NOT one of the six faces the width probes covered, so its max-digit advance has never been checked against Excel's fitted unit. One `_probe_grid_`-style fixture (built per the hygiene rules below) settles whether Constantia is another exact-advance face or a deviation.
  - **`autoRowHeightFactor = 1.31` is CONFIRMED** (a probe round claimed it wrong and was retracted — the rendered heights it compared against carry the per-font factor above; dividing it out reproduces 1.31 exactly). Do not revisit without dividing out the body-font factor first.
  - **Probe hygiene, learned twice.** (1) Verify the printer paper by rendering a fixture and checking for a 1123x794 page — Excel REPORTS the A4 it was asked for while a Letter driver silently exports Letter (~8% squeeze), and `Get-PrintConfiguration` reported Letter here while Excel exported A4. (2) Validate any hand-built fixture against a REAL workbook with its fit scale neutralised before trusting its numbers — every fixture cloned from the same minimal base carried an unrepresentative body font, which produced one refuted rule and one wrongly-retracted constant.
  - **Neither metric is trustworthy alone on this subsystem.** Placement fixes scored negative while visibly correct — `home-contents-inventory-list` read −0.09 SSIM on the landing that took its gridline error against Excel's regular 104px pitch from a mean 0.9px to 0.4px (no defect; do not chase) — and `weekly-lesson-planner` scored +0.12 on a change that made its geometry measurably worse. Read extents and crops, per `docs/fidelity-audit.md`.

- **#26 `IsAnchorOnlyMark` is inert** (found 2026-08-06). The parser sets it for a paragraph whose only content was behind-text decorative art — "emit a marker with zero line height" — but nothing in the engine consumes it; the deleted production renderers did, so the agendas-minutes/11 behaviour it was written for was lost in the migration. Reviving it is NOT a free fix: honouring it in `CanonicalParagraphMeasurer` regressed 104 of 108 changed pages (aggregate mean |Word−render| 8.4 → 56.8; menus/08, brochures/01 and agendas-minutes/10 to ~190 grey levels), because those paragraphs anchor art whose placement depends on the line existing. Needs its own investigation into what the production renderer did with the reserved space.
- **#5 Floating/anchored decorative art missing or misplaced.** Architecture, parse-path authority rules and the attempted-and-reverted decision log are in `docs/floating-art-pipeline.md`. Still open: `brochures/06`'s quote-box hatching (the dash column sits at the box's right edge where Word hatches inside), and in the HTML export the `cards/12`/`18` fold guides and `labels/03`'s tear lines. Also `ole_chart_preview`'s chart, a VML float 8.5pt below its (empty) paragraph, sits 23px (11pt) higher than Word's measured from the last bullet above it, which is that bullet paragraph's 11.25pt `w:after`: the engine's paragraph-anchor reference is the cursor before the collapsed gap (`paragraphPreSpacingTops`), and Word's looks to be after the PREVIOUS paragraph's after-spacing, which the `_probe_anchor_ref` probe (60pt before, on the anchor paragraph) could not tell apart. Unprobed: settle it with an amplified fixture (a 48pt `w:after` above an anchor paragraph with none, and with some `w:before`), in DrawingML as well as VML, before moving it — it would move every paragraph-anchored float that follows space-after.
- **#8 Picture effects.** The colour transforms (duotone/greyscale/washout through one shared `ImageRecolor.Rows` recipe) and the `a:alphaModFix` transparency on shape fills landed 2026-08-29 on all four outputs (`docs/word-features.md`). Still open: shape fills carry no COLOUR transforms (`FloatingShapeElement` has no effect fields; nothing in the corpus needs it yet); `a:clrChange`/`a:biLevel` parse to `None`; soft-focus/blur and warm-tone (`newsletters/07`) are unmodelled.
- **#47 Charts** go through the sheet chart composer since 2026-10-21 (`docs/word-features.md`, Charts). Still open: 3-D charts with `c:rAngAx` 0 are drawn obliquely where PowerPoint draws them in perspective (`chart-3d-pie`'s 3-D line, `chart-3d-area`, `chart-surface`), and a 3-D line's ribbon and a 3-D area's slab depth are not measured; PowerPoint shades a 3-D surface's bands by their slope, and its contour plot overlays shading, where they are drawn flat; a 3-D bubble is shaded by a linear gradient where PowerPoint lights a sphere; data labels on 3-D and surface charts are not drawn; pie of pie and bar of pie draw as a plain pie; a line chart beside a right legend plots about 12pt wider than Word's; lines end square where `cap="rnd"` asks for round; and the HTML export draws only a pie or doughnut's slices.
- **#9 Text measure inside shapes and text boxes.** Centred text can still wrap in a narrower measure than Word in containers other than pct tables — `cards/02`'s ticket-back TEXT BOX is ~40px narrow and its placeholder barely moved.
- **#10 Table-style conditional-region inheritance.** The conditional `w:tblStylePr` blocks merge through `w:basedOn` per property (`DocumentParser.ResolveStyleConditionals`, `TableStyleConditionalInheritanceTests`), modelled on the whole-table rules' measured behaviour and not probed for the conditional regions themselves. The autofit half is closed: Word distributes columns by the CSS auto-table rule, probed (`docs/word-features.md`, Table Auto-fit).
- **#12 TOC page numbers.** Tab-stop clamp and Hyperlink-style suppression landed. Still open: numbers are the document's cached values (live PAGEREF needs a bookmark→page map), and they sit ~4pt left of Word because the clamp lands at the cell content edge where Word spills into the right cell padding. Deferred as risk-heavy for a 4pt MEDIUM: the clamp input is the layout's `maxWidth`, and letting it spill means plumbing the cell's right padding into paragraph layout, whose `pagedLayoutCache` is keyed by (paragraph, ContentWidth) — that width key is load-bearing, so a padding-aware clamp needs the padding in the cache key too.
- **#13 Footnotes/endnotes** — page-bottom pinning, the separator rule and document-end endnotes landed 2026-09-05 (`Fragmenter.CommitFootnotes` / `PlaceEndnotes`, `FootnoteLayoutTests`; the laws and the fourteen `_probe_fn_*` readouts are in `docs/word-features.md`, Footnotes). Still open, each unprobed: `w:footnotePr/w:pos="beneathText"` (the area flows under the text instead of pinning; every corpus document is `pageBottom`); `w:endnotePr/w:pos="sectEnd"` and per-section endnote restarts (the notes flow at document end only); a table inside a note body is not laid out; a note's paragraph keeps no widow/orphan pairing when it splits at the page bottom; and a second reference on a page whose earlier note is already spilling queues its note behind the spill rather than moving its line (Word's ordering there is unmeasured).
- **#16 Automatic hyphenation not implemented.** Word's hyphenated breaks don't happen (`hyphenation_auto`, `hyphenation_suppressed` para 3).
- **#21 Rotation reserves the un-rotated footprint.** Picture and text-box rotation render correctly in all backends, but layout still reserves the shape's un-rotated box (documented all-backend limitation). The HTML export emits picture rotation/flip/crop as CSS since 2026-08-19; CSS transforms don't take layout space, which coincides with the same un-rotated reservation.

### HTML export

- **#25 (HTML) Anchored/floating objects** — the linearization closed 2026-08-07: wrap-none floats and cell-anchored art place absolutely against their anchor's place in the flow, and no export exceeds 2x Word's page height (median 0.75). Still open, and inherent rather than a placement bug: a page-RELATIVE float approximates its page top by the anchor's flow position, so a fixed-layout multi-panel document still overlaps where Word separates by page — `brochures/03` is the case, its whole design living in two anchored groups of 29 pictures positioned per page. Closing that needs the export to paginate, which it deliberately does not.
- **#31 HTML/AltChunk input gaps** — what landed, and the probes behind it, are in `docs/html-import.md`. Still open: block children of a cell (`<p>`, `<div>`) flatten into one paragraph; `vertical-align` on cells is unmodelled; cell padding composes slightly tighter than Word; per-cell CSS margins render as a uniform grid; and a cell paragraph's after-spacing, which is not flat (`html_table`; the `_probe_cellpad_sweep` data is in `docs/html-import.md`, Known gaps).

### Spreadsheet input (`Inputs/excel`)

- **#36 Sheet drawings follow their rows since 2026-10-11** (`Fragmenter.PlaceSheetUnderFloats`: each
  page gets the drawings shifted to its own rows and clipped to them, and a drawing marked
  `fPrintsWithSheet="0"` stays off the page). Still open on the same pages: Excel's page footer
  (`invoice-accessibility-guide` prints "Page 2 of 10" where the render has none — a sheet's
  `headerFooter` is not read), and a drawing is clipped to its page's rows but not to its columns,
  which only matters once #39 lands.
- **#48 A sheet's text box in the HTML export stands at its sheet offset over a table whose columns are the browser's.** `basic-business-invoice`'s "LOGO GOES HERE" box overlaps "Company Name" since text boxes are placed absolutely (2026-10-12): the exported sheet table's columns come out narrower than Excel's, so the offset lands on the neighbouring cell. Four sheets were looked at; the others were not.
- **#39 No horizontal pagination.** A sheet wider than the page is scaled down rather than split into
  left/right page strips. `to-do-list` spans A:Q asking for no `fitToPage`, so Excel prints two strips
  and the render prints the left one and breaks vertically instead — landing on the right page count
  for the wrong reason, with a near-empty second page (allow-listed in `BaselineHealthTests`).
  Re-read 2026-10-11 and NOT settled: the committed `expected_0002.png` shows the rows BELOW page
  one's (the list's empty rows and the lower half of the Task Summary chart, cut at the same right
  edge), which is a second page down the left strip rather than the right strip, and the workbook
  declares a print area of `$A:$M`. Whether Excel prints further strips after those two pages could
  not be asked: reference generation needs a printer driver and this machine had none
  ("No printers are installed"). Render the fixture again before building the strip rule on it.

### Word-reference (`expected_*.png`) anomalies worth re-checking rather than "fixing"

- **#32** `newsletters/12` draws an olive stripe Word hides — verify against the DOCX before treating Word as wrong.

---

## Per-scenario findings

### border_style_variants

- MINOR | all | p2 | at sz=96 (12pt) the border band is painted across the paragraph text instead of outside it, so the label reads "ingle, sz=96"
- MINOR | all | p2 | section 3's wrapped fourth box (`thinThickThinMediumGap`, alone on its line) is drawn inside its line like its neighbours, where Word draws it about 21pt tall around the font box without growing the line for it (band 1573-1617 at 150 DPI against the neighbours' 26px); and on the rows above it Word's boxes stand about 6px further apart than the render's (second box of the thinThick row from x=262 against 255, read off a crop — the glyph origins in Word's XPS agree with the engine's insets, so it is the drawn rules that differ). Unmeasured
- MINOR | all | p1 | section 2's `nil` and `none` paragraphs (edges that draw nothing but declare `w:sz` 24 and `w:space` 4) stand 25.6pt apart on Word's page and 21.6pt apart in the render, and the `single` box under them 3.4pt lower in Word: an edge that is not drawn still seems to hold its `w:space` open. Read off one fixture at one size; not probed
- MINOR | all | p1 | section 1's boxes that hold a bordered run (ridge, inset, outset) are a few points shorter in Word and stand further apart, so the middle of the page runs up to 19px low before it comes back to Word's at section 2

### brochures/06

- MINOR | all | p2 | quote-box geometry residual: dash column sits at the box's right edge where Word hatches inside

### business/02

- MINOR | html | - | the beige panel still starts a little below the COMPANY NAME heading it backs: the reflowed text above it is set tighter than Word's, a paragraph's line-height still being the bare multiple of the em where Word's scales the face's own line (headings take the face's line since 2026-10-27). Lifting it onto paragraphs and the `<body>` was tried 2026-10-28 and made it worse, since cell content joined inline inherits the body's line (`docs/word-features.md`, Exporters)

### html_complex

- MEDIUM | all | p1,p2 | the "3. Lists" heading opens 20pt closer to the data table than Word's, whose headings keep their space above after a table, so everything under it sits high. The table's rows are Word's since 2026-10-21 (35.20pt against 35.28, `docs/html-import.md`, Cell paragraph spacing); the intro wraps as Word's since 2026-10-11

### html_css_alignment

- MEDIUM | all | p1 | Table height:100px ignored (row 33px tall vs Word's 61px; interior borders land with the 2026-08-20 model)

### html_images

- MINOR | all | p1 | third image+caption block renders ~30px taller than Word (y354-566 vs 362-544), accumulating ~17px drift by the page bottom
- CLEAN: html

### html_lists

- MINOR | all | p1 | Bullet glyph noticeably smaller/lighter than Word's large Symbol-font solid bullet

### html_table

- MINOR | all | p1 | rows stand 26.35pt apart against Word's 28.08 (read off its XPS): the cell-paragraph spacing is Word's since 2026-10-21, and this table, with no `cellpadding` or `border` attribute, is 1.7pt a row short of the rest — HTML's default 1px padding is the likeliest cause, unprobed (`docs/html-import.md`, Known gaps)

### html_table_cell_margin_css

- MINOR | all | p1 | per-cell CSS margins render as a uniform grid — Word offsets each cell's box by its own margin (the 10px-margin cell inset, the 20px-left-margin cell pushed right) where Morph draws equal boxes

### html_table_cell_padding_css

- MEDIUM | all | p1 | CSS cell padding under-applied: table ~20% narrower and rows ~25% shorter than Word, right-column text ends flush against the table border (per-cell borders land with the 2026-08-20 model; the export now carries the padding too, though as raw px-as-pt values — covered by this conversion question)

### html_table_styled

- MINOR | all | p1 | fixed-width table's flexible third column runs ~10px wide (Morph ~156px vs Word's 146px): Word renders the two DECLARED columns slightly wider than their 100px/200px (161/315 against 156/312), so its flexible remainder is correspondingly smaller

### hyphenation_auto

- MEDIUM | all | p1 | automatic hyphenation not applied: paragraph 1 lacks Word's "telecommunica-/tion" end-of-line break and paragraph 2 lacks Word's "hy-/phens" break, so both paragraphs break at different words
- CLEAN: html

### hyphenation_suppressed

- MEDIUM | all | p1 | automatic hyphenation missing in paragraph 3: Word breaks "Telecommu-/nications" and "syl-/lables" but backends end line 1 early at "again." and redistribute the paragraph's lines (same line count, clearly different breaks)
- CLEAN: html

### icon_svg

- MINOR | all | p1 | the star is drawn 4-5% larger than Word's (39.4 x 37.0pt against 37.4 x 35.5), about its centre: every backend draws the SVG since 2026-10-12, in the frame the drawing declares, and Word's star stands a point inside that frame on each side
- CLEAN: html

### image_rotation/01

- MEDIUM | all | p1 | rotated inline image not clipped: all three backends draw the full 386px diamond where Word clips to a 255px band with a flat top edge at the frame top (Skia drew rotated inline images UPRIGHT until 2026-08-19 — the engine painter never had the transform; it now rotates like the others). The clip law is unprobed — the fixture's wp:effectExtent is all-zero yet Word still lets the sides and bottom overflow while clipping the top

### image_wrap_square

- MINOR | all | p1 | cumulative vertical drift of blocks (~10-20px up, pie chart slightly offset) with structure intact (p2 measured within 2px on 2026-08-20 — trimmed to p1)

### inline_group_rotation

- MINOR | all | p1 | residual differences in the rotated nested pieces (unvetted at crop level)
- MINOR | all | p1 | the frame's hairline accent rule (a 0.25pt rounded rectangle in the first accent colour) reads fainter in Word than in the render; the octagon band's corners are Word's since 2026-10-11
- MINOR | all | p1 | "Menu" lacks its white+mint outlined glyph style (the text colour itself is correct now)

### labels/04

- MINOR | html | - | blue accent bars vertically misaligned with their label text (bar tops start ~a line below "Name")

### labels/09

- MINOR | html | - | Stub ticket numbers ("00 01" etc.) partially clipped by the narrow red stub columns

### labels/10

- MINOR | html | - | the first column's label text sits about 9px higher in its pill than the other two columns' (the teal rules and the grid's registration against the pills are Word's since 2026-10-12)

### labels/15

- MINOR | all | p1 | script "from" glyph drawn ~9px further left and slightly wider than Word, nearly touching the preceding label's text; text rows/columns otherwise align within 1-2px
- MINOR | html | - | cream page background stops about two-thirds down the strip, leaving the last rows of labels on a white background

### letters/07

- MEDIUM | all | p1 | second body paragraph wraps to 4 lines vs Word's 3 (text breaks earlier, column effectively narrower)
- MINOR | html | - | the header pattern band stops short of full page width (ends at x=817 of 1024)

### letters/08

- MEDIUM | all | p1 | first body paragraph wraps to 3 lines vs Word's 2 ("...recent visit to New / York.") and second paragraph breaks at different words, shifting the letter body
- MEDIUM | all | p1 | large signature "Joseph Price" rendered in bold/heavy weight instead of Word's light strokes
- MINOR | html | - | signature "Joseph Price" bold vs Word's light weight

### letters/10

- MAJOR | html | - | signature image broken — placeholder "Image of signature" shown instead of the script signature

### letters/11

- MINOR | all | p1 | body lines break at slightly different words (para 1 breaks after "Importers" vs "Importers to"), same line counts
- MINOR | html | - | a 7.5pt navy rule is drawn at the head of the export: it stands in the DEFAULT header, which is the one the export writes, and Word never reaches it on this one-page `w:titlePg` document (the rule was hidden until 2026-10-12 only because line shapes were not exported)

### letters/13

- MAJOR | html | - | the three letter copies get inconsistent body-column widths (~586px, ~471px, ~622px)
- MINOR | html | - | page-3 left-edge banner rendered as an inline horizontal strip (side placement/rotation lost)

### menus/03

- MINOR | skia,pdf | p1 | full-page navy background tint fractionally off Word's (em≈1.0 but below visible threshold)

### menus/04

- MINOR | html | - | stray light-grey rectangle rendered below the week tables

### menus/05

- MEDIUM | html | - | page-1 and page-3 section headings render as "Appetizer"/"First Course" in a fallback bold font, losing the decorative all-caps display font Word uses (page-2 headings are correct)

### menus/06

- MINOR | html | - | the p3 block's bottom red bar overlaps its last menu lines

### menus/07

- MINOR | all | p1 | food photos shifted right ~15-25px
- MINOR | all | p1 | the mint wave rules sit ~8pt above Word's: each is an inline filled custGeom rotated -4.55° whose wp:effectExtent (t=3pt, b=3.2pt) is the rotation's spill, on a 16pt text-dominated line; Morph draws the shape unrotated and raises it by the bottom edge as for a picture, which Word does not — probe a rotated inline shape before modelling it (inline_group_crop, inline_group_rotation and menus/09 share the wave)

### menus/08

- MEDIUM | all | p1 | right instruction block wraps at a different word than Word ("...select the whole / cell.)" vs Word's "...select the / whole cell.)")

### newsletters/01

- MINOR | all | p1,p2,p4 | Body paragraphs re-wrap at different words than Word (line counts mostly unchanged)

### newsletters/02

- MINOR | all | p2 | the article under the photo sits 4px low: Word's line for the photo reads about a point shorter than the picture's own box, and the line now carries the 1pt of leading the paragraph's 1.08 spacing adds under a picture (`_probe_picline`)

### newsletters/04

- MEDIUM | all | p1,p2,p3,p4 | Line breaks differ from Word throughout, redistributing text across the newspaper columns (scoop/next-hot columns and sidebars break at different paragraphs, blocks end noticeably lower)
- MINOR | html | - | the page-4 pull-quote circle renders (2026-10-01) but wider than its column, overlapping the left column's line ends

### newsletters/05

- MINOR | all | p1,p3 | school photo ~1% narrower than Word (right edge 7-9px short), lighting the whole photo in the diff

### newsletters/06

- MINOR | all | p1,p4 | dotted-ornament window around "SYCAMORE NOTES" title is taller than Word's (dot rows beside the title partially dropped); small "SYCAMORE NOTES" strip logo on p2/p5 wraps to two lines
- MINOR | html | - | big title renders as "SYCAMORENOTES" — the word space collapses to the same width as the letter-spacing gaps

### newsletters/07

- MEDIUM | all | p1,p2 | body text sets visibly larger than Word so paragraphs wrap to extra lines (lead paragraph 6 lines vs Word's 5)

### newsletters/09

- MINOR | html | - | full four-sided borders drawn around the bridge sidebar box and the pull-quote box where Word shows only top/bottom rules

### newsletters/10

- MEDIUM | html | - | section headings bold dark-green instead of Word's light weight

### newsletters/11

- MINOR | all | p2 | the left column's text box is 8-24px narrower than Word's (XPS: text from 63.05pt to 255.25pt, 320px at 120 dpi); no line breaks differently for it while the face measures on linear advances

### newsletters/12

- MAJOR | html | - | Absolutely-positioned blocks collide: right-column text renders across the purple dash column, and body text under the photo-grid blocks

### office_math

- MEDIUM | all | p1 | Built-up OMML fraction 1/2 (numerator stacked over denominator with fraction bar) is flattened to inline text "1/2"
- MINOR | html | - | Built-up fraction linearized as plain "1/2" in the HTML export (the serif math face and operator spacing land via the model)

### paragraph_borders

- MINOR | html | - | w:between group members render as separate CSS boxes with hairline gaps in the side rules

### paragraph_spacing

- MINOR | all | p1 | text tracks slightly narrower than Word
- CLEAN: html

### resumes/09

- MINOR | all | p1 | the main column's lines end 9-23px short of Word's: the column starts where Word's does and breaks at the same words, and Word draws its 10pt Gill Sans MT wider (the glyph placement of #43)

### resumes/19

- MINOR | all | p1,p2,p3 | the left column sits 4-5px high of Word's from the first Experience entry down, on each of the three copies (it sat 11-13px low until the entry icons' lines took their own run's font, 2026-10-12)

### wedding/01

- MEDIUM | all | p1 | invitation cards start ~0.26" higher than Word (intro-paragraph spacing compressed) and the text inside the cards drifts further up (~0.4" by the SATURDAY/RECEPTION lines) from tighter line spacing
- MINOR | all | p1 | intro paragraph rewraps: "Create New Theme Colors" kept on line 2 so line 3 starts with an orphaned period (". Select your own colors...")

### wedding/02

- MEDIUM | html | - | floral pieces compose around the invitation off Word's arrangement — the rose sits high of the banner, single leaves scatter, and the pink banner overlays the name line
- MINOR | html | - | the export runs 44% longer than its text: a 0.5pt near-white fold rule anchored 700pt below the last paragraph is drawn where that lands

### wedding/05

- MEDIUM | html | - | watercolor washes exported as standalone images stacked above the panels instead of backgrounds behind the headings

### wedding/06

- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/08

- MINOR | html | - | "00:00 PM" wraps to two lines in the export's time cell where Word sets it on one (cells are sized border-box since 2026-10-12, so the cell's measure no longer hides the tight fit), and the export runs 31% longer than its text for a rule anchored below the last paragraph (the wedding/02 case)

### wedding/09

- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/11

- MEDIUM | html | - | the four watercolor images render as detached standalone blocks above/beside the cards instead of as artwork inside the card frames

### wordart

- MINOR | all | p2 | the arches run ~4% narrower than Word's ("Arc Text Up" 289px against 302): Word draws the bold Impact with synthetic bold, which widens every advance and thickens the strokes, and the WordArt drawers never embolden
- MINOR | pdf | p10 | highlight bars render ~10% more ink than Word's (magenta 3049 sampled units against 2622): the box spans the font's full ascent-to-descent where Word's sits slightly tighter. All five colours are correct and present
- MEDIUM | all | p14 | Emboss/shadow character effects flattened: black "EMBOSSED" and "SHADOWED" lose their offset drop shadows in every backend; "IMPRINTED" engrave two-tone lost in ImageSharp/PDF (Skia approximates it with a light offset)
- MEDIUM | html | - | the 12 WordArt shape texts export in their colours and display face (2026-10-01) but at body size and unwarped, stacked at the top
- MINOR | html | - | emboss/engrave/shadow effects render flat (no drop shadows on "EMBOSSED"/"SHADOWED", no engrave on "IMPRINTED")

### wordart-envelope

- MEDIUM | html | - | the four WordArt words export in their colours and face (2026-10-01) but small and unwarped
---

## Clean scenarios (faithful on skia, imagesharp, pdf and html)

`agendas-minutes/03`, `agendas-minutes/04`, `agendas-minutes/05`, `agendas-minutes/07`, `agendas-minutes/09`, `agendas-minutes/11`, `agendas-minutes/13`, `agendas-minutes/15`, `agendas-minutes/18`, `agendas-minutes/19`, `business/04`, `cards/03`, `cards/04`, `cards/16`, `cards/19`, `complex_tables`, `cover-letters/06`, `cover-letters/08`, `cover-letters/09`, `cover-letters/11`, `labels/02`, `resumes/04`, `html_nested_lists`, `icon_with_text`, `table_autofit_no_widths`, `table_cell_spacing/01`, `table_diagonal_borders/01`, `table_grid_styling_padding`, `wedding/10`, `font_sizes`, `labels/05`, `labels/08`, `labels/16`, `letters/04`, `table_default_style_inside_h`, `align_mixed`, `all_caps`, `block_quote`, `bold_text`, `bullet_list`, `colored_text`, `column_breaks`, `complex_document`, `content_control_inline`, `compatibility_mode_14`, `cover-letters/02`, `custom_margins`, `decimal_tabs/01`, `cards/10`, `deep_nested_list`, `dot_points`, `embedded_font`, `labels/14`, `resumes/18`, `table_borders`, `even_odd_headers/01`, `even_odd_headers/02`, `explicit_break_blank_page`, `first_line_indent`, `font_families`, `field_codes_simple/01`, `footer`, `form_checkboxes`, `form_dropdowns`, `form_text_fields`, `gutter_margins/01`, `hanging_indent`, `header`, `header_banner_table`, `header_footer`, `headings`, `html_basic_formatting`, `html_css_colors`, `html_font_tag`, `html_inline_styles`, `html_links`, `html_paragraphs`, `hyperlinks`, `hyphenation_nonbreaking`, `hyphenation_soft`, `image_cropping/01`, `inline_group_crop`, `inline_image`, `italic_text`, `labels/01`, `labels/07`, `labels/12`, `labels/13`, `left_indent`, `letters/06`, `line_breaks`, `line_numbers_continuous`, `line_numbers_count_by_5`, `line_numbers_custom_distance`, `line_numbers_restart_page`, `line_numbers_restart_section`, `line_numbers_suppressed`, `line_spacing`, `line_spacing_at_least`, `line_spacing_exactly`, `long_paragraph`, `nonstandard_main_part_name`, `numbered_list_tracking`, `page_letter`, `cover-letters/16`, `resumes/15`, `menus/02`, `mixed_breaks`, `mixed_formatting`, `multiple_images`, `multiple_pages`, `multiple_paragraphs`, `nested_list`, `numbered_list`, `numbered_list_restart`, `page_a4`, `page_borders/01`, `page_breaks`, `page_landscape`, `page_legal`, `page_numbers`, `pct_pos_offset`, `postcards/02`, `resumes/13`, `rtl_paragraph`, `section_break_continuous`, `section_break_even_page`, `section_break_next_page`, `section_break_odd_page`, `simple_paragraph`, `simple_table`, `small_caps`, `strikethrough_text`, `subscript_superscript`, `tab_stops`, `table_alignment/01`, `table_cell_margin_per_cell`, `table_cell_padding`, `table_cell_padding_varied`, `table_colors`, `table_default_cell_margin`, `table_default_cell_margin_start_end`, `table_default_style`, `table_default_style_first_row_run_color`, `table_default_style_first_row_shading`, `table_explicit_heights`, `table_indent`, `table_text_direction`, `table_of_contents/01`, `table_of_contents/02`, `table_page_break`, `table_two_column_layout`, `table_vmerge_basic`, `table_vmerge_explicit_heights`, `text_wrapping_break`, `wide_table`, `three_columns`, `tracked_changes/01`, `two_columns`, `underline_text`, `wedding/07`, `agendas-minutes/06`, `brochures/04`, `business-plans/04`, `business-plans/06`, `html_table_cellpadding`, `letters/09`, `menus/09`, `resumes/11`, `cards/05`, `agendas-minutes/12`, `bar_tabs`, `business-plans/05`, `business/01`, `cover-letters/10`, `cover-letters/15`, `postcards/04`, `resumes/02`, `resumes/03`, `resumes/08`, `resumes/12`, `resumes/16`, `resumes/17`, `letters/01`, `brochures/08`, `cards/01`, `wedding/03`, `cards/07`, `resumes/07`, `agendas-minutes/01`, `agendas-minutes/08`, `agendas-minutes/17`, `brochures/05`, `business-plans/10`, `cards/02`, `cards/06`, `cards/08`, `cards/12`, `cards/13`, `cards/18`, `cover-letters/05`, `inline_shape_arrows`, `labels/03`, `labels/06`, `labels/11`, `letters/02`, `newsletters/08`, `newsletters/13`, `resumes/10`, `table_of_contents/03`, `agendas-minutes/16`, `business-plans/08`, `icons_multiple`, `letters/05`, `newsletters/14`, `resumes/14`, `agendas-minutes/02`, `agendas-minutes/14`, `brochures/01`, `brochures/02`, `brochures/03`, `business-plans/07`, `business/03`, `business/05`, `business/06`, `business-plans/12`, `business-plans/15`
