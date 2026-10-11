# Rendering fidelity todo

Deep comparison of every scenario in `src/Tests/Inputs/` (401 fixtures: 335 Word with 602 reference pages, 40 Excel, 26 PowerPoint): `expected_*.png` (Word, 150 DPI, via RenderHelper) versus `skia_result#page_*.verified.png`, `imagesharp_result#page_*.verified.png`, `pdf_result#page_*.verified.png` (PDFium render), and `html_result.verified.png` (headless-browser screenshot of the HTML export).

Each finding: `severity | backends | pages | description`. `all` = skia+imagesharp+pdf. HTML findings ignore pagination/viewport-width reflow by design and only flag content/styling errors. Not reported: anti-aliasing texture, 1-2px subpixel shifts, ImageSharp's softer glyph rasterization. `[known]` = already documented as accepted in that scenario's notes.md. How a difference is judged — crops versus metrics, and what counts as noise — is `docs/fidelity-audit.md`.

**This file lists only what is still wrong.** A finding is deleted the moment it lands, and its durable knowledge moves to `docs/word-features.md` (feature behaviour and the evidence behind it), `docs/floating-art-pipeline.md` (anchored/floating art), `docs/html-import.md` (HTML and AltChunk input), `src/page_counts.md` (page-count experiment ledger) or `docs/fidelity-audit.md` (comparison method). Nothing that has shipped is described here — for how a fix was reached, read those docs and the git history. This is a temporary working document; it is expected to shrink to nothing.

**Open: 9 major, 99 medium, 120 minor = 228 findings across 123 scenarios.** The tally is recounted from the `severity | backends | pages` lines below rather than hand-adjusted (hand-adjusted, it drifted twice). Not every finding has been re-read against the current baselines: every MAJOR was re-judged against Word's page on 2026-10-01; the findings on the 105 fixtures whose Word references were regenerated in Calibri on 2026-10-02 were measured against the Aptos references they replaced and have not been re-read since; and a finding that names a specific block N px off is usually still live where one describing whole-page drift is usually stale (aligning each page's ink profile against Word's gives a median page offset of 2px, but a median worst local displacement of 11px). How a landing moved the corpus is in its commit message and in `src/page_counts.md`, not here.

## Systemic issues (cross-scenario root causes)

These patterns repeat across many scenarios; fixing one clears whole families of the per-scenario findings below. **IDs are stable** — findings reference them by number — so a closed issue's number is retired rather than reused, and the list is deliberately gappy.

### All raster + PDF backends

- **#1 Per-section headers, footers and page numbering** — landed 2026-09-06 (`SectionBands`, `Fragmenter.SelectVariant` / `PageNumbers`; the laws and the three Word-read fixtures are in `docs/word-features.md`, Headers & Footers). Still open: `@w:chapStyle`/`@w:chapSep` chapter numbering; the band a page carrying a CONTINUOUS section break takes (the engine keeps the settings in force when the page ends — unprobed). HTML/Markdown evaluate page fields as a one-page document ("Page 1 of 1") by design — the reflow really is one page.
- **#25 Word's row-split trigger** — landed 2026-08-07 with the strict floor fit, the vMerge tie rule and style-inherited `w:pageBreakBefore`, and whole-table floor strictness on 2026-09-05 (`docs/word-features.md`, Multi-page Tables / Page Break Before; the attempts and their measurements are in the git history). Still open, un-probed LibreOffice leads (hypotheses, not evidence): Writer charges a split row's trHeight floor once ACROSS fragments (follow minimum = declared − earlier fragments' heights, tabfrm.cxx:5123) where the engine floors only a whole row carried to a region top; Writer synthesizes split-edge rules by borrowing the neighbour row's border and suppresses them under repeated headlines (paintfrm.cxx:2858/2932) where the engine draws the fragment's own full box; `COLLAPSE_EMPTY_CELL_PARA` (a cell whose sole paragraph is empty collapses to 1 twip); and the broader always-on flag inventory (`sw/inc/IDocumentSettingAccess.hxx:36-106`, DOCX defaults `WriterFilter.cxx:300-341`) — `HEADER_SPACING_BELOW_LAST_PARA`, `TREAT_SINGLE_COLUMN_BREAK_AS_PAGE_BREAK` and `IGNORE_TABS_AND_BLANKS_FOR_LINE_CALCULATION`.
- **#43 Text advances: the breaking and autofit rules are settled; what is still open is listed here.** Word breaks lines and sizes autofit columns on the font's plain linear advances at the size as authored, with GPOS kerning scaled the same way (Word-probed 2026-10-02: `docs/layout-engine.md`, "The crux"; `docs/word-features.md`, Table Auto-fit and Kerning), and every face has measured that way since. The per-glyph `.wordadvances` sidecars and the space-compression wedge that stood in for it from 2026-08-30 are gone, with no page count moved (`src/page_counts.md`, experiment 23). Kerning followed the same day: `w:kern` comes down the style ladder as Word takes it, and the measurer adds it linearly and across spaces (`docs/word-features.md`, Kerning). Still open, each measured:
  - **A ligature's advance is not measured.** Word's layout takes the ligature glyph's advance where `w14:ligatures` is on, and the measurer sums the letters'. One line of evidence, indirect: of the 1,606 paragraphs of the 2026-10-11 justified probes the engine misses one, a sentence whose first line holds `off`, which Word squeezes at a measure where the letters' widths put the squeeze-to-stretch ratio at 0.521 against the 0.5 limit the other fifteen thresholds bracket between 0.497 and 0.503. Calibri's `ff` ligature is 65 units narrower than `f` + `f` (0.63px at 12pt), which is what brings that line under the limit. Not probed directly (step a measure over a sentence full of `fi`/`ff`/`fl`, with `w14:ligatures` on and off), and the painters would have to draw the ligature for the ink to end where the width says: Skia and PDF substitute none today.
  - **An autofit column takes its text's unrounded linear width, and Word's runs a fraction of a pixel over that**: 0.3px in compatibility mode 12 and 0.7px in mode 15, behind a hairline border (990 probed tables). Unmodelled; it is under a pixel and moves no break.
  - **Glyph placement inside a line is not Word's.** Word draws whole-pixel advances at the em rounded to a whole pixel on its 120-dpi grid (the 26.6 scaled advance `round(units × ppem × 64 / upm)`, rounded up from 30/64px in mode 14 and older and to the nearest pixel in mode 15), lets the pen fall about 5px behind its linear position inside a run, brings it up at a space, never pulls it back, and narrows the spaces of a line that would overrun its measure. A painter draws each run at the backend's own advances at the nominal size. Where the whole-pixel advances sum wider than linear it shows: 12pt Calibri is drawn 3.6% wider (784px for a sentence of 757), so Word's full lines reach the measure and Morph's stop short of it (multiple_pages p1: Word's first lines end at x=1124, Morph's at 1095; two_columns the same; both moved 0.006 AE further when the wedge, which had been spreading such lines, went). 11pt Calibri draws at about its linear width (690px for a sentence of 693.9) and shows nothing. Closing it needs per-word or per-glyph placement in the painters (`PlacedGlyphRun`, deferred). The glyph RENDER size is em-rounded as well (8pt Calibri draws at 7.8pt), an unmeasured ink-scale deviation of up to ~3% at half-point sizes. Every painter kerns its ink as the line was measured (2026-10-03); Word's own drawing of a kerned pair snaps the kern to 1/16px and rounds the pair's first glyph to a whole pixel (`docs/word-features.md`, Kerning), which is part of the same whole-pixel placement.
  - Text set in a face that is not bundled is measured on a substitute's metrics: business-plans/15's Univers falls back to Tahoma (measured closest at −5.9% against the real Univers installed on the reference machine), and Segoe UI Semibold, Franklin Gothic Demi and Avenir Next LT Pro Light are not bundled either (business-plans/13, set in the last, reproduces 150 of the 258 long lines Word sets). Their wraps are approximations; the honest fix for Univers is a licensed Univers-metric face, which is out of Morph's hands. **Several findings below are a missing face and nothing else, and the face is in Office's cloud-font cache on the reference machine:** Grandview Display (letters/08's wraps and its light signature), Quire Sans regular (brochures/07's heavy body text), Avenir Next LT Pro Light and Demi (letters/07, business-plans/13), Trade Gothic Next Bold and Cond Bold (business-plans/02, /03). They are proprietary, which `src/Fonts/README.md` rules out bundling; the open-licensed ones the cache holds went in on 2026-10-11 (Karla, Source Sans Pro, Playfair Display and Work Sans faces) and closed letters/09, letters/10, business-plans/04 and business-plans/06.
  - The suite's confirmation pass has not always been deterministic on near-fit scenarios: on 2026-09-06 `regenerate-baselines.sh` promoted resumes/16 from its first pass and the second pass rendered both Skia and ImageSharp pages differently on five consecutive regenerations, as did a third Skia-only run, and business-plans/15 did the same on 2026-09-05. Host renders of the same input are byte-identical across four processes, so the variance lives in the parallel test process, not in the layout rules. Re-examined 2026-09-24 and NOT reproduced: every Word scenario rendered on Skia and ImageSharp in two separate host processes (12-way parallel, shuffled order) hashed byte-identical, as did a sequential pass against three in-process parallel rounds; the measurer's caches are per-conversion and nothing in the engine shares mutable state across conversions. The 2026-09-24 and 2026-10-02 regenerations both confirmed clean, the second reproducing an earlier run of the same code byte for byte across 782 files. `regenerate-baselines.sh` restores any promoted PNG whose decoded pixels match the previous baseline (`scripts/png-identical`), so encoder-only churn does not reach the tree.

- **#45 Spreadsheet geometry** — the width/scale/range rules landed 2026-08-14 (code comments on `SheetGridBuilder.MaxDigitWidth` / `ToPoints` and `SpreadsheetParser.ResolveRange` / `ResolveScale`, pinned by `ColumnWidthTests`). Still open:
  - **Row heights render at a BODY-FONT-dependent factor, and Arial-bodied corpus books now visibly pay it** (isolated 2026-08-14, `_probe_fontgraft`; victim confirmed by triage the same day). The same declared heights draw at ~15/16 under an Arial body and essentially exactly under a Verdana-11 theme. `social-media-editorial-theme-calendar` — Arial 12 body, all rows declared `ht` with `customHeight` — had body row pitch 260px against Excel's 259 BEFORE the landing and 267-268 after: the old too-small fit scale was cancelling the ~6% row-height excess, and the corrected scale exposed it (black header band 168 exact → 170, everything below drifting down cumulatively). Fixing it means applying the per-font factor to declared heights for the faces that carry it, which needs the factor measured per face on real-workbook-derived fixtures — do not model a constant. Likely mechanism (unverified): Excel's print scale derived from the Normal font's GDI metrics on screen vs printer. `household-organizer` measuring ~1.05 against the graft's 1.00 remains the one unexplained residue — plausibly non-`customHeight` rows re-auto-sized at open.
  - **`simple-basic-pink-blue-timesheet`'s width is still +5.7%** (649px against Excel's 614 after the landing, from +7.3% before). Its body font is Constantia 11 — bundled in `src/Fonts`, but NOT one of the six faces the width probes covered, so its max-digit advance has never been checked against Excel's fitted unit. One `_probe_grid_`-style fixture (built per the hygiene rules below) settles whether Constantia is another exact-advance face or a deviation.
  - **`autoRowHeightFactor = 1.31` is CONFIRMED** (a probe round claimed it wrong and was retracted — the rendered heights it compared against carry the per-font factor above; dividing it out reproduces 1.31 exactly). Do not revisit without dividing out the body-font factor first.
  - **Probe hygiene, learned twice.** (1) Verify the printer paper by rendering a fixture and checking for a 1123x794 page — Excel REPORTS the A4 it was asked for while a Letter driver silently exports Letter (~8% squeeze), and `Get-PrintConfiguration` reported Letter here while Excel exported A4. (2) Validate any hand-built fixture against a REAL workbook with its fit scale neutralised before trusting its numbers — every fixture cloned from the same minimal base carried an unrepresentative body font, which produced one refuted rule and one wrongly-retracted constant.
  - **Neither metric is trustworthy alone on this subsystem.** Placement fixes scored negative while visibly correct — `home-contents-inventory-list` read −0.09 SSIM on the landing that took its gridline error against Excel's regular 104px pitch from a mean 0.9px to 0.4px (no defect; do not chase) — and `weekly-lesson-planner` scored +0.12 on a change that made its geometry measurably worse. Read extents and crops, per `docs/fidelity-audit.md`.

- **#26 `IsAnchorOnlyMark` is inert** (found 2026-08-06). The parser sets it for a paragraph whose only content was behind-text decorative art — "emit a marker with zero line height" — but nothing in the engine consumes it; the deleted production renderers did, so the agendas-minutes/11 behaviour it was written for was lost in the migration. Reviving it is NOT a free fix: honouring it in `CanonicalParagraphMeasurer` regressed 104 of 108 changed pages (aggregate mean |Word−render| 8.4 → 56.8; menus/08, brochures/01 and agendas-minutes/10 to ~190 grey levels), because those paragraphs anchor art whose placement depends on the line existing. Needs its own investigation into what the production renderer did with the reserved space.
- **#5 Floating/anchored decorative art missing or misplaced.** Architecture, parse-path authority rules and the attempted-and-reverted decision log are in `docs/floating-art-pipeline.md`. Still open: `brochures/06`'s quote-box hatching (the dash column sits at the box's right edge where Word hatches inside), and in the HTML export the `cards/12`/`18` fold guides and `labels/03`'s tear lines.
- **#6 Shape geometry defects.** Preset polygons, text-box chrome, picture flips, line alpha, connector assembly and outline-only/stroked-fill shapes are resolved across both parse paths (`docs/word-features.md`, `docs/floating-art-pipeline.md`). Still open: `business-plans/02`'s arrow construction.
- **#8 Picture effects.** The colour transforms (duotone/greyscale/washout through one shared `ImageRecolor.Rows` recipe) and the `a:alphaModFix` transparency on shape fills landed 2026-08-29 on all four outputs (`docs/word-features.md`). Still open: shape fills carry no COLOUR transforms (`FloatingShapeElement` has no effect fields; nothing in the corpus needs it yet); `a:clrChange`/`a:biLevel` parse to `None`; soft-focus/blur (`business-plans/02`) and warm-tone (`newsletters/07`) are unmodelled.
- **#47 Charts other than pie and doughnut are empty space**, and no chart draws its title, legend or data labels (`docs/word-features.md`, Charts). `business-plans/12`'s SWOT ring draws since 2026-10-11 without its "SWOT" title; the spreadsheet and presentation inputs do not go through that path at all.
- **#9 Text measure inside shapes and text boxes.** Centred text can still wrap in a narrower measure than Word in containers other than pct tables — `cards/02`'s ticket-back TEXT BOX is ~40px narrow and its placeholder barely moved.
- **#10 Table-style conditional-region inheritance.** The conditional `w:tblStylePr` blocks merge through `w:basedOn` per property (`DocumentParser.ResolveStyleConditionals`, `TableStyleConditionalInheritanceTests`), modelled on the whole-table rules' measured behaviour and not probed for the conditional regions themselves. The autofit half is closed: Word distributes columns by the CSS auto-table rule, probed (`docs/word-features.md`, Table Auto-fit).
- **#12 TOC page numbers.** Tab-stop clamp and Hyperlink-style suppression landed. Still open: numbers are the document's cached values (live PAGEREF needs a bookmark→page map), and they sit ~4pt left of Word because the clamp lands at the cell content edge where Word spills into the right cell padding. Deferred as risk-heavy for a 4pt MEDIUM: the clamp input is the layout's `maxWidth`, and letting it spill means plumbing the cell's right padding into paragraph layout, whose `pagedLayoutCache` is keyed by (paragraph, ContentWidth) — that width key is load-bearing, so a padding-aware clamp needs the padding in the cache key too.
- **#13 Footnotes/endnotes** — page-bottom pinning, the separator rule and document-end endnotes landed 2026-09-05 (`Fragmenter.CommitFootnotes` / `PlaceEndnotes`, `FootnoteLayoutTests`; the laws and the fourteen `_probe_fn_*` readouts are in `docs/word-features.md`, Footnotes). Still open, each unprobed: `w:footnotePr/w:pos="beneathText"` (the area flows under the text instead of pinning; every corpus document is `pageBottom`); `w:endnotePr/w:pos="sectEnd"` and per-section endnote restarts (the notes flow at document end only); a table inside a note body is not laid out; a note's paragraph keeps no widow/orphan pairing when it splits at the page bottom; and a second reference on a page whose earlier note is already spilling queues its note behind the spill rather than moving its line (Word's ordering there is unmeasured).
- **#14 Comment markup not rendered.** No balloon, no highlight, no markup-area page shrink (`comments/01`).
- **#16 Automatic hyphenation not implemented.** Word's hyphenated breaks don't happen (`hyphenation_auto`, `hyphenation_suppressed` para 3).
- **#21 Rotation reserves the un-rotated footprint.** Picture and text-box rotation render correctly in all backends, but layout still reserves the shape's un-rotated box (documented all-backend limitation). The HTML export emits picture rotation/flip/crop as CSS since 2026-08-19; CSS transforms don't take layout space, which coincides with the same un-rotated reservation.
- **#34 ImageSharp does not synthesise bold.** Skia emboldens a bold run that resolved a face lighter than 700; ImageSharp falls back Bold → Regular with no equivalent, so those runs render at normal weight. **Outline dilation is exhausted** — five stroke-the-fill versions were built and all reverted, and the multi-font calibration behind that verdict (Word's bold adds ~26% ink, Skia's synthesis ~46%, per-typeface spread 1.00–2.53) is in `docs/word-features.md` under Bold. Anything further needs real weight: bundle the missing bold faces, or instance a variable font's `wght` axis.

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

### agendas-minutes/01

- MINOR | html | - | classroom illustration stacked above the title instead of floating to the right of it

### agendas-minutes/02

- MEDIUM | pdf | p1 | bold weight lost: FINANCIAL MEETING/AGENDA title renders regular weight
- MINOR | all | p1 | agenda table rows slightly tighter, cumulative ~half-line upward drift by the last row

### agendas-minutes/08

- MAJOR | html | - | page-break decoration (blue band + orange circles) is emitted mid-flow and drawn across the text: over "Principal Ian Hansson presented his report" and the NEW BUSINESS heading and its first bullet
- MINOR | html | - | Header decorative shapes distorted: orange half-donut renders as solid semicircle, title-band ring renders as rounded-square ring

### agendas-minutes/12

- MINOR | all | p1 | Right edge of the [Date] gray bar and "Meeting Notes" dark banner is ~10-15px off vs Word (bar width mismatch, visible as a solid strip in the diff)
- CLEAN: html

### agendas-minutes/14

- MEDIUM | all | p1 | "Attendees: Helbe Sokk, ..." renders as two lines ("Attendees:" alone, names on next line) vs Word's single line, pushing all following content ~1 line lower
- MINOR | html | - | Decorative teal stripes at top and bottom of the page are missing

### agendas-minutes/16

- MINOR | all | p1 | Whole content block sits ~13px higher than Word; DATE/TIME/MEETING CALLED rows slightly shorter so the offset grows to ~17px by NEXT MEETING
- [known] MINOR | skia,imagesharp | p1 | Residual pixel-level differences on the pink leaf/dot decorations (Skia SVG render vs ImageSharp PNG fallback, documented in notes.md)

### agendas-minutes/17

- MINOR | all | p1 | Title block ("TEAM" / "AGENDA") sits ~30px lower than Word on skia/pdf and ~15px lower on imagesharp; info rows and the three bullet columns now align
- MINOR | html | - | First divider rule renders below the "Meeting time" row instead of above it, and both divider rules extend to the viewport's far left edge past the content margin

### bar_tabs

- MINOR | skia,imagesharp,pdf | p1 | text lines drift upward progressively (~5-10px by the last paragraph) versus Word

### border_style_variants

- MINOR | all | p2 | at sz=96 (12pt) the border band is painted across the paragraph text instead of outside it, so the label reads "ingle, sz=96"
- MINOR | all | p2 | section 3's wrapped fourth box (`thinThickThinMediumGap`, alone on its line) is drawn inside its line like its neighbours, where Word draws it about 21pt tall around the font box without growing the line for it (band 1573-1617 at 150 DPI against the neighbours' 26px); and on the rows above it Word's boxes stand about 6px further apart than the render's (second box of the thinThick row from x=262 against 255, read off a crop — the glyph origins in Word's XPS agree with the engine's insets, so it is the drawn rules that differ). Unmeasured
- MINOR | all | p1 | section 2's `nil` and `none` paragraphs (edges that draw nothing but declare `w:sz` 24 and `w:space` 4) stand 25.6pt apart on Word's page and 21.6pt apart in the render, and the `single` box under them 3.4pt lower in Word: an edge that is not drawn still seems to hold its `w:space` open. Read off one fixture at one size; not probed
- MINOR | all | p1 | section 1's boxes that hold a bordered run (ridge, inset, outset) are a few points shorter in Word and stand further apart, so the middle of the page runs up to 19px low before it comes back to Word's at section 2

### brochures/01

- MEDIUM | all | p2 | bullet before "GET THE EXACT RESULTS YOU WANT" is drawn much smaller than Word's dot; in skia/imagesharp it is also teal instead of Word's pink/magenta
- MINOR | all | p1,p2 | body/contact text blocks sit ~5-10px lower than Word, drift growing down each panel

### brochures/02

- MEDIUM | skia | p1,p2 | short blue divider rule (below "Meet director: Ravi Costa" on p1, below the Day-2 finals list on p2) rendered as a multi-row hatched/striped block instead of a solid line (ImageSharp and PDF match Word)
- MINOR | all | p1,p2 | small block shifts: red title lines sit ~20px lower on skia/imagesharp, date/venue and schedule text offset ~10px on all backends
- MEDIUM | html | - | red dashed decorative graphic overlaps the "Event officials" text lines (Judge's coordinator / Meet director)
- MINOR | html | - | divider rule renders as a tiny hatched box, and the "August 12th - 14th" line crowds the title's descenders

### brochures/03

- MEDIUM | all | p2 | right circle photo sits ~20pt higher than Word
- MEDIUM | all | p2 | page content sits too high: Relecloud block ~0.3-0.45in up, itinerary rows ~0.25in up, and "ConnectAbove"/"Launch Event" footer links 60px too high (tucked under the card instead of centered in the navy band)
- MINOR | pdf | p1,p2 | photo interior crop wider than Word and the other backends (more scene, hands smaller)
- MINOR | html | - | the lower-left photo renders as an unclipped rectangle: it is an INLINE picture with `a:prstGeom prst="ellipse"`, and an inline picture carries no clip geometry in the model (the custGeom-cut one above it is clipped since 2026-10-11)

### brochures/05

- MEDIUM | all | p4 | spice-tray and soup photos lose Word's tight crop — the full image is shown zoomed out with visibly smaller subjects
- MINOR | all | p1 | body paragraphs wrap at different words (same line count, different break points)
- MINOR | all | p4 | headings/text blocks sit ~7px higher than Word
- MEDIUM | html | - | orange background panel behind the CONTACT US / logo block missing
- MINOR | html | - | table-of-contents dot leaders missing

### brochures/06

- MINOR | all | p2 | quote-box geometry residual: dash column sits at the box's right edge where Word hatches inside
- MINOR | skia,imagesharp | p2 | couple photo drawn slightly taller with content shifted ~8px vs Word's crop
- MINOR | all | p2 | the services table's rows under "OUR SERVICES INCLUDE" sit 5px high: a row whose cells hold only empty paragraphs, one of them with an Auto space-after that Word's rule makes nothing at a cell's end (`_probe_autospace_d`), is 5pt taller in Word than in the render. What makes it taller is not found

### brochures/07

- MEDIUM | all | p1,p2 | body text (lorem paragraphs, contact text, right-rail paragraphs) rendered visibly bolder/heavier than Word, with shifted wrap points
- MINOR | all | p1,p2 | text blocks (CONTACT US group, LOREM IPSUM columns, ABOUT US group) uniformly shifted ~10px
- MEDIUM | html | - | the ABOUT US quote now renders in its yellow panel (2026-10-01), but the lightbulb photo block overlaps the second column's LOREM IPSUM heading
- MEDIUM | html | - | body text renders bold where Word shows regular weight

### brochures/08

- MINOR | all | p1 | the "Contoso Logo" frame sits 8px low (Morph y=924-1194, Word y=916-1185); its width and x match Word exactly and it carries 17743 white pixels against Word's 19879
- MINOR | pdf | p2 | numbered client list vertical spacing looser than Word (~50px vs ~38px between items)

### business-plans/02

- MINOR | pdf | p1,p2,p3,p4,p5,p6 | Uniform small downward drift (~10px) of body content producing ghost doubling of text and table rules
- MEDIUM | html | - | Hero wheat photo shows the sharp un-blurred original, missing Word's soft-focus treatment.
- MEDIUM | all | p2-p6 | the section headings ("EXECUTIVE SUMMARY" and the rest) render at regular weight where Word draws Trade Gothic Next Cond Bold, which is not bundled

### business-plans/03

- CLEAN: html
- MEDIUM | all | p1 | the cover title and the section headings render at regular weight where Word draws Trade Gothic Next Bold: the bold face is not bundled (the Skia painter's synthetic bold falls well short of it at 42pt)

### business-plans/05

- MEDIUM | skia,imagesharp | p1 | Body content progressively compressed upward — section headings and the PREPARED BY/PREPARED FOR blocks end up to ~0.5in higher than Word (line spacing too tight for the display serif), with word-level rewraps.
- MINOR | pdf | p1 | Full-page cream background tint minutely off (em=0.99 — nearly every pixel faintly differs, invisible side-by-side).
- MINOR | pdf | p1 | Small downward drift (~10px) doubling the divider rules and footer block positions.

### business-plans/07

- MEDIUM | html | - | pale-green footer band starts mid-contact-block (labels and first contact lines sit above/outside it) instead of enclosing the whole PREPARED section, and stops at content width
- MINOR | html | - | title line spacing collapsed so "PROPOSAL" caps touch the "BUSINESS" baseline

### business-plans/08

- MINOR | html | - | top accent line renders as a short block near its start position instead of the full line
- MINOR | html | - | list numbers "3./4./5." rendered tiny beside large section headings (Word renders number and heading at the same size)

### business-plans/09

- MEDIUM | all | p1 | cover title "TARGET AUDIENCE PROFILING PLAN" and "INTERNAL DOCUMENT" render bold vs Word's light weight (ink +18% ImageSharp, +38-39% Skia/PDF)
- MEDIUM | html | - | blue cover background extends into the body and cuts through the QUESTIONS FOR CONSUMERS table
- MEDIUM | html | - | cover title bold vs Word's light weight

### business-plans/10

- MEDIUM | html | - | Cover date line "April 4, 20XX" renders white-on-white below the cover art — each cover line repeats its 342pt before-spacing, pushing the date (and the title block) past the fabric image onto the white page

### business-plans/12

- MEDIUM | all | p1 | the cover's 520x461pt inline photo renders at its full height where Word shows only ~264pt of it (the photo visibly ends at ~311pt): the lower half is overdrawn by later rows' fills in Word's cover collage, a z-order/overdraw the engine does not reproduce. Undug
- MINOR | all | p9 | the SWOT ring draws without its "SWOT" title (a chart title, #47)
- MINOR | html | - | SWOT list bullets black instead of their category colors
- MINOR | html | - | numbered section headings render the number much smaller than the heading text (tiny "3." before "BUSINESS DESCRIPTION")

### business-plans/13

- MEDIUM | all | p5-p23 | "Avenir Next LT Pro Demi" headings and lead-ins render at the bundled 700 weight where Word draws the lighter Demi (`_probe_demi`: every backend resolves a named-Demi family alike, and Word resolves Demi+bold to a lighter face than 700). Closing it means bundling a 600-weight Avenir face; no code path is wrong given the faces available
- MEDIUM | html | - | Cover's grey title-band background missing in the HTML export (title/subtitle on plain white); all other content, images, tables, and TOC page numbers are intact

### business-plans/15

- MEDIUM | all | p2,p3 | Footer bar ("BUSINESS PLAN | APRIL 25, 20XX" + number) is drawn on the TOC pages where Word shows less. **Not the header/footer z-order rule** — landing that (`docs/floating-art-pipeline.md`) moved p2 by −0.0001 and left p3 untouched. And "suppresses entirely" overstates it: Word's own p2 footer strip carries 4129 dark pixels against Morph's 4483, so Word draws a footer there too; only p3 is lopsided (1866 against 4063). Re-read both pages before treating this as one finding.

### business/01

- MEDIUM | all | p1 | memo header table columns too narrow — "Holiday closure" wraps to two lines under RE and the COMMENTS paragraph wraps to 3 lines vs Word's 2
- MEDIUM | all | p1 | footer block (CANEIRO GROUP, Tel/Fax, black rule) indented ~125px right of Word's left-margin position
- MINOR | skia,imagesharp | p1 | date "05.26.2023" rendered ~20px higher than Word (its underline aligns correctly)
- CLEAN: html

### business/02

- MEDIUM | html | - | COMPANY NAME heading renders on white above the beige panel instead of inside it (shaded background starts too low)

### business/03

- MEDIUM | all | p1 | cover overlay box geometry off: white Company Name box ~40px narrower and navy Report Title box ~35px wider than Word, both shifted up ~15-25px
- MEDIUM | html | - | cover collage flattened: Company Name and Report Title boxes render stacked below the photo instead of overlapping it
- MINOR | html | - | "Report Title" in the navy box renders double-struck/heavier than Word's light-weight title

### business/05

- MINOR | html | - | footer address left-aligned instead of Word's centered-right placement

### business/06

- MINOR | all | p1 | the body block sits 11-13px high of Word, measured 2026-10-11 (17 bands, the first and last at Word's)
- MINOR | html | - | LOGO placeholder rendered as bare text without its outlined box

### cards/01

- MINOR | all | p1 | green gift panels offset ~8px up and ~5px right with slightly different size than Word

### cards/02

- MINOR | all | p1 | ticket frame bottom edge sits a few px lower than Word over the code box
- MINOR | all | p1 | the divider above the code box draws solid where Word dashes it (a:prstDash val="dash" on the group's straight connector)
- MEDIUM | pdf | p1,p2 | text rendered bold where Word uses regular weight ("Keep ticket stub" on p1, card-back placeholder paragraph on p2, which also changes its line wraps)
- MEDIUM | all | p2 | ticket-back placeholder text block plus thumbs-up hand sketch sit ~0.5in higher than Word
- MEDIUM | imagesharp | p2 | placeholder text wraps at different words than Word ("just" pulled up to the first line)
- MINOR | all | p2 | polka-dot background pattern misaligned — dots at visibly different positions across both card backs
- MAJOR | html | - | ticket content displaced: the code text boxes' "150220YY" pair shows above the first ticket (the export does not hide a fixed-size text box's overflow), the first ticket's star ribbon lands on ADMIT ONE, "Keep ticket stub" and the table's code fall below or on the ticket's edge, and an extra third copy of the placeholder paragraph appears at the bottom

### cards/06

- MEDIUM | html | - | teal divider rule missing on both invitation backs

### cards/07

- MINOR | all | p1 | both teal pictures ~12px (2%) wider than Word (their vertical placement, the captions and the bunting clipart sit at Word's since the 2026-10-01 cell-height laws)
- CLEAN: html

### cards/08

- MEDIUM | html | - | placeholder message shows the same wrong heavy bold typeface (Word uses thin light text)

### cards/11

- MINOR | all | p1 | both balloon pictures ~12px (2%) wider than Word (their vertical placement, the captions and the balloon clipart sit at Word's since the 2026-10-01 cell-height laws)
- CLEAN: html

### cards/12

- MINOR | html | - | Fold/cut guide borders (vertical divider, dashed mid-page line) not exported

### cards/13

- MEDIUM | html | - | Card outline borders missing, cards blend into the blue background

### cards/15

- MINOR | all | p1 | Teal squares rendered ~12px wider than Word (right edge x=1248 vs 1236)
- MINOR | html | - | Fold/cut guide borders not exported

### cards/18

- MEDIUM | html | - | fold guide rules missing entirely

### comments/01

- MAJOR | all | p1 | comment markup missing entirely: right-side gray comments pane, balloon "Commented [R1]: Looks good to me.", pink highlight box on the commented text, and the dashed connector line are all absent
- MEDIUM | all | p1 | body text drawn full-size at the normal top margin instead of Word's shrunk-to-fit-markup layout (Word scales the body down and places it lower to reserve the markup column)
- MAJOR | html | - | comment content "Commented [R1]: Looks good to me." missing from the HTML export (only the body sentence is present)

### cover-letters/05

- MEDIUM | pdf | p2 | body paragraph spacing wider than Word on p2 (25 bands vs skia's 22; p1/p3 measured identical to skia 2026-08-20, and the ~2-lines-lower claim dissolves with them)
- MINOR | html | - | a teal corner triangle overlaps the footer contact block's phone line

### cover-letters/10

- MEDIUM | skia | p1 | Date, Adatum address block, and header contact info render in a visibly heavier weight than Word (imagesharp/pdf match)

### cover-letters/15

- MINOR | skia,imagesharp | p1 | Letter body drifts up ~0.5 line by the "Chanchal Sharma / January 13, 20XX" block

### feature_capture/01

- MINOR | all | p1 | Word breaks the rotated header cell's "Header" mid-word into two stacked columns against a 24pt row where Morph grows the row to the word's 35pt: Word sizes the row by the other cells and wraps the rotated text against it, Morph lets `MeasureVerticalCellHeight` grow the row to the rotated text's natural width — the wrap-at-row-height equilibrium is unprobed
- MINOR | html | - | "All features" paragraph left-aligned instead of right-aligned

### html_complex
- MEDIUM | all | p1,p2 | everything under the data table sits about 31pt high of Word's: the table's rows stand 32.6pt apart against Word's 35.4 (the cell-paragraph spacing of html_table), and the "3. Lists" heading opens 20pt closer to the table than Word's, whose headings keep their space above after a table. Page 1 so takes the "5. Styled Boxes" heading that opens Word's page 2. The intro wraps as Word's since 2026-10-11


### html_css_alignment

- MEDIUM | all | p1 | Table height:100px ignored (row 33px tall vs Word's 61px; interior borders land with the 2026-08-20 model)
- MEDIUM | html | - | width:100% lost in the export (content-width table; interior borders land with the 2026-08-20 model)

### html_images

- MINOR | all | p1 | third image+caption block renders ~30px taller than Word (y354-566 vs 362-544), accumulating ~17px drift by the page bottom
- CLEAN: html

### html_lists

- MINOR | all | p1 | Bullet glyph noticeably smaller/lighter than Word's large Symbol-font solid bullet

### html_table

- MEDIUM | all | p1 | Table rows far more compact than Word: row pitch against Word's 58-59px at 150 DPI on identical ~17px text — the missing ~14pt/row cell-paragraph after-spacing, which is not flat (attempted twice and reverted; the `_probe_cellpad_sweep` data to model it from is in `docs/html-import.md`, Known gaps)

### html_table_cell_margin_css

- MINOR | all | p1 | per-cell CSS margins render as a uniform grid — Word offsets each cell's box by its own margin (the 10px-margin cell inset, the 20px-left-margin cell pushed right) where Morph draws equal boxes

### html_table_cell_padding_css

- MEDIUM | all | p1 | CSS cell padding under-applied: table ~20% narrower and rows ~25% shorter than Word, right-column text ends flush against the table border (per-cell borders land with the 2026-08-20 model; the export now carries the padding too, though as raw px-as-pt values — covered by this conversion question)

### html_table_styled

- MINOR | all | p1 | fixed-width table's flexible third column runs ~10px wide (Morph ~156px vs Word's 146px): Word renders the two DECLARED columns slightly wider than their 100px/200px (161/315 against 156/312), so its flexible remainder is correspondingly smaller
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

### icons_multiple

- MAJOR | imagesharp,pdf | p1 | red and green star icons both rendered blue; Word and Skia show blue/red/green. **Not a recolor bug — an SVG-support gap.** Each icon carries an `a:svgBlip` SVG variant (3 in the document) and the colour lives ONLY there: the three PNG fallbacks are byte-identical (one sha, all blue 65,132,243). Skia renders the SVG via `SvgPreprocessor` + Svg.Skia and is right; `Morph.ImageSharp` and `Morph.Pdf` contain no SVG code at all, so they draw the identical fallbacks and cannot do better. Closing it means an SVG rasterizer in both backends — a new dependency each, not a fix in Morph's own code
- CLEAN: skia, html

### image_rotation/01

- MEDIUM | all | p1 | rotated inline image not clipped: all three backends draw the full 386px diamond where Word clips to a 255px band with a flat top edge at the frame top (Skia drew rotated inline images UPRIGHT until 2026-08-19 — the engine painter never had the transform; it now rotates like the others). The clip law is unprobed — the fixture's wp:effectExtent is all-zero yet Word still lets the sides and bottom overflow while clipping the top

### image_wrap_square

- MINOR | all | p1 | cumulative vertical drift of blocks (~10-20px up, pie chart slightly offset) with structure intact (p2 measured within 2px on 2026-08-20 — trimmed to p1)
- MINOR | html | - | last line of the "Some images, such as charts or graphs..." paragraph rendered centered ("link on the image.") instead of left-aligned

### inline_group_rotation

- MINOR | all | p1 | residual differences in the rotated nested pieces (unvetted at crop level)
- MINOR | all | p1 | the frame's hairline accent rule (a 0.25pt rounded rectangle in the first accent colour) reads fainter in Word than in the render; the octagon band's corners are Word's since 2026-10-11
- MINOR | all | p1 | "Menu" lacks its white+mint outlined glyph style (the text colour itself is correct now)

### inline_shape_arrows

- MEDIUM | skia,imagesharp | p1 | colored-arrow row drawn ~27px left and ~25px above Word's position (arrow sizes themselves correct, gap to "Arrow variants:" label visibly too small)
- MEDIUM | pdf | p1 | all four colored arrows shifted up-left ~20px
- MINOR | all | p1 | "Thinner stroke" arrow and its label paragraph sit ~10-17px higher than Word
- CLEAN: html

### labels/03

- MEDIUM | html | - | dotted tear-line rules missing from all tickets

### labels/04

- MINOR | html | - | blue accent bars vertically misaligned with their label text (bar tops start ~a line below "Name")

### labels/06

- MEDIUM | html | - | the ~20 "ADMIT ONE" stub texts stack as a column of horizontal lines at mid-sheet instead of along each ticket's stub edge

### labels/09

- MINOR | html | - | Stub ticket numbers ("00 01" etc.) partially clipped by the narrow red stub columns

### labels/10

- MINOR | html | - | Teal rule under "YOUR NAME" missing entirely; the text grid also runs one label late against the pill artwork (first pill empty — the labels/11 drift family)

### labels/11

- MAJOR | html | - | the first label rows' white text lands above the brush artwork on the white page (invisible) — the brushes place at Word-page offsets from their anchor wrappers while the text grid reflows at its own pitch, so text and brush drift apart until mid-page (the brushes do render in a 3-across grid behind the later rows)

### labels/15

- MINOR | all | p1 | script "from" glyph drawn ~9px further left and slightly wider than Word, nearly touching the preceding label's text; text rows/columns otherwise align within 1-2px
- MINOR | html | - | cream page background stops about two-thirds down the strip, leaving the last rows of labels on a white background

### letters/01

- MEDIUM | all | p1 | decorative header/footer bands are far too saturated — the render carries 5.7x Word's mean chroma: circle sampled (138,180,254) against Word's paler (182,199,238), band (50,66,93) vs (59,64,77). The theme accents are purple (accent1 AD84C6), so the shapes resolve the right hue — the residual is an under-applied lightening transform (lumMod/lumOff). Fills sit in a group so the transform chain is group-level

### letters/02

- MINOR | html | - | right-aligned "Letter of Recommendation" title and Date lose their alignment (title centered-left, Date lands beside recipient block)

### letters/05

- MINOR | html | - | dashed decorative elements absent, and the purple circle grazes the "Contoso" sender block at the later sections' tops

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


### letters/13

- MINOR | imagesharp,pdf | p1,p2,p3 | hatched (striped) banner wedges render as solid fills — imagesharp and pdf flatten several wedges (e.g. the second tile's upper and left wedges)
- MINOR | imagesharp | p1,p3 | several paragraphs wrap at different words than Word (e.g. "...personal taste. Go /", "built-in font / combination")
- MAJOR | html | - | the three letter copies get inconsistent body-column widths (~586px, ~471px, ~622px)
- MINOR | html | - | page-3 left-edge banner rendered as an inline horizontal strip (side placement/rotation lost)

### menus/03

- MEDIUM | all | p1 | "EVENT INTRO" sits ~120px left of Word's, over the bubble art, where Word sets it against the gold divider; the gold rules now match Word (2026-10-01)
- MEDIUM | all | p1 | both text columns shifted left (instructions column ~25% of page width left of Word) and vertically compressed (menu column ends ~65px high)
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

- MINOR | all | p1 | Left-column caption, "Happy holidays" heading and body sit 8px (3.8pt) higher than Word — the image-only line's height: Word's picture line measures 2.4-3.5pt taller than the extent on `_probe_picln2`, a descent the engine does not charge
- MINOR | all | p1 | Right-column pull-quote lines sit 10px lower than Word
- MINOR | all | p1,p2,p4 | Body paragraphs re-wrap at different words than Word (line counts mostly unchanged)

### newsletters/02

- MEDIUM | all | p2 | Main-column paragraphs wrap to more lines than Word (bold lede 6 lines vs 5; following paragraph 5 vs 4)
- MEDIUM | all | p2 | "Work with the industry's best" column renders wider with fewer, longer lines, so the column ends ~1in higher than Word
- MEDIUM | html | - | Hero network-figures image renders above "The Review" masthead — wrong content order vs the document
- MINOR | all | p2 | the article under the photo sits 4px low: Word's line for the photo reads about a point shorter than the picture's own box, and the line now carries the 1pt of leading the paragraph's 1.08 spacing adds under a picture (`_probe_picline`)

### newsletters/04

- MEDIUM | all | p1,p2,p3,p4 | Line breaks differ from Word throughout, redistributing text across the newspaper columns (scoop/next-hot columns and sidebars break at different paragraphs, blocks end noticeably lower)
- MINOR | all | p1,p2,p3,p4 | Full-width banner photos slightly oversized vs Word (right edge extends further, solid strip in diffs; captions shift down accordingly)
- MINOR | html | - | the page-4 pull-quote circle renders (2026-10-01) but wider than its column, overlapping the left column's line ends

### newsletters/05

- MEDIUM | all | p1,p3 | body copy under "Welcome back to school!" wraps to one extra line (17 text bands vs Word's 16), block ends 26-80px lower
- MEDIUM | skia,imagesharp | p1,p3 | sidebar "Ms. Tanaka" contact block ~22px and "Upcoming Events" block ~48px lower than Word (PDF within 10px)
- MEDIUM | all | p2,p4 | sidebar "Fall highlights" block sits ~210-235px (~1.5 inch) higher than Word
- MINOR | all | p1,p3 | school photo ~1% narrower than Word (right edge 7-9px short), lighting the whole photo in the diff
- MINOR | all | p2,p4 | classroom photo shifted ~9px left at identical size

### newsletters/06

- MINOR | all | p1,p4 | dotted-ornament window around "SYCAMORE NOTES" title is taller than Word's (dot rows beside the title partially dropped); small "SYCAMORE NOTES" strip logo on p2/p5 wraps to two lines
- MINOR | html | - | big title renders as "SYCAMORENOTES" — the word space collapses to the same width as the letter-spacing gaps

### newsletters/07

- MEDIUM | imagesharp | p1,p2 | body and sidebar text rendered in bold weight throughout vs Word's regular Century-Gothic-style face
- MEDIUM | all | p1,p2 | body text sets visibly larger than Word so paragraphs wrap to extra lines (lead paragraph 6 lines vs Word's 5)
- MEDIUM | html | - | content order wrong: living-room photo renders above the "MODERN LIVING" masthead/title block instead of below it
- MINOR | html | - | black accent rule renders overlapping the "Your guide to buy or rent" subtitle text

### newsletters/08

- MEDIUM | html | - | cover photo renders as an unclipped rectangle (no freeform crop in the HTML export)

### newsletters/09

- MEDIUM | all | p3,p4 | photo captions detach from their images and collide with neighbouring content (caption box overlaps the section rule above "Mirjam Nilsson" on p3; caption floats over the "scoop of the day" columns on p4)
- MINOR | html | - | full four-sided borders drawn around the bridge sidebar box and the pull-quote box where Word shows only top/bottom rules

### newsletters/10

- MEDIUM | imagesharp | p1 | the four green section headings ("Something that made me smile today…", "Currently dealing with...", "Thankful for...", "Looking forward to...") rendered bold instead of Word's light weight
- MEDIUM | html | - | section headings bold dark-green instead of Word's light weight

### newsletters/11

- MINOR | all | p2 | the left column's text box is 8-24px narrower than Word's (XPS: text from 63.05pt to 255.25pt, 320px at 120 dpi); no line breaks differently for it while the face measures on linear advances
- MEDIUM | html | - | Floating photos emitted in wrong order: hero photo appears before the "LAWN AND LANDSCAPE" masthead, group photo appears before the "Tony's landscapes and more" headline

### newsletters/12

- MEDIUM | imagesharp | p1 | ImageSharp clips the top half of the "NEWSLETTER" title line (a horizontal cut through the glyphs; pdf and skia render it clean)
- MAJOR | html | - | Absolutely-positioned blocks collide: right-column text renders across the purple dash column, and body text under the photo-grid blocks

### newsletters/13

- MEDIUM | html | - | Still-life photo content missing from the HTML export — the arch outline renders but the photo inside it does not

### newsletters/14

- MINOR | html | - | the Graduation photo sits at its layout-order position rather than Word's (long-scroll preview +0.015)

### office_math

- MEDIUM | all | p1 | Built-up OMML fraction 1/2 (numerator stacked over denominator with fraction bar) is flattened to inline text "1/2"
- MINOR | html | - | Built-up fraction linearized as plain "1/2" in the HTML export (the serif math face and operator spacing land via the model)

### paragraph_borders

- MINOR | html | - | w:between group members render as separate CSS boxes with hairline gaps in the side rules

### paragraph_spacing

- MINOR | all | p1 | text tracks slightly narrower than Word
- CLEAN: html

### postcards/03

- MEDIUM | skia,imagesharp | p1 | bottom row of postcard images shifted up ~23px (row gap collapsed, same defect as postcards/02); PDF matches Word's positions
- MINOR | skia,imagesharp | p2 | bottom-row placeholder text and address lines sit ~7px higher than Word
- MEDIUM | html | - | same placeholder font substitution: heavy dark rounded font instead of Word's light handwriting script

### postcards/04

- MINOR | all | p2 | cupcake photo ~5% wider (429px vs 408px) than Word
- MINOR | all | p3 | rightmost (tilted-boy) photo ~9px wider than Word

### resumes/02

- MINOR | all | p1 | header text and both body columns sit ~8-13px higher than Word (uniform upward drift; artwork and divider positions otherwise match)

### resumes/03

- MEDIUM | skia,imagesharp | p1 | SKILLS entries vertically compressed (bar touches label, no gap between entries) so the block ends ~135px higher than Word; PDF spacing matches Word
- MEDIUM | pdf | p1 | Right-column HOBBIES/CONTACT blocks drift progressively lower (~2.5 line heights by the CONTACT block)

### resumes/07

- MINOR | skia,imagesharp | p1 | Template rows ("College, location", "Graduation year", SKILLS labels) render ~10-16% lighter than Word (ink 888/824 against Word's 982 on the College row): Word synthesizes a heavier bold over Franklin Gothic Book than the raster synthesis produces, while PDF resolves Book+bold to the Demi face and lands closest at 1032 (`_probe_demi`)
- MINOR | all | p1 | Italic sub-lines (Bachelor of Arts Degree GPA, Relevant course work:) drawn ~0.25" further left than Word, starting left of their parent rows

### resumes/08

- MEDIUM | all | p1 | Vertical compression: sidebar sections (ABOUT ME/EDUCATION/SKILLS) end ~112-135px (4-5 lines) higher than Word and the left column ~30-50px higher

### resumes/09

- MEDIUM | pdf | p1 | Sidebar contact entries drift progressively lower (Website ~90px / ~2 lines below Word); skia/imagesharp match Word
- MINOR | all | p1 | Main content column uniformly shifted ~26px left of Word's position

### resumes/10

- MINOR | html | - | SKILLS bullets black instead of accent color

### resumes/12

- MINOR | pdf | p1 | "VICTORIA BURKE" name block sits ~20px lower than Word

### resumes/14

- MINOR | html | - | right-aligned tab dates ("20XX – 20XX", "20XX") render inline after the job/degree titles instead of at the right margin

### resumes/16

- MEDIUM | all | p1 | "Chanchal Sharma" name block sits ~20px lower than Word

### resumes/17

- MEDIUM | all | p1 | two-column body misplaced: column divider and right column (Skills/Hobbies/Profile) sit ~0.8in left of Word's position, and the narrower left column wraps its text differently
- CLEAN: html

### resumes/19

- MEDIUM | pdf | p2,p3 | 2nd and 3rd Experience entries drift lower on p2/p3 (band counts differ from skia by one; p1 measured identical 2026-08-20)

### table_of_contents/03

- MINOR | html | - | TOC tab leaders dropped — page numbers (1, 4, 9, 15, 27) render inline after each entry instead of leader-aligned (Word clips them at the narrow cell edge).

### wedding/01

- MEDIUM | all | p1 | invitation cards start ~0.26" higher than Word (intro-paragraph spacing compressed) and the text inside the cards drifts further up (~0.4" by the SATURDAY/RECEPTION lines) from tighter line spacing
- MINOR | all | p1 | intro paragraph rewraps: "Create New Theme Colors" kept on line 2 so line 3 starts with an orphaned period (". Select your own colors...")

### wedding/02

- MEDIUM | all | p1,p2 | text left inset (~0.5") lost: "PLEASE JOIN", banner DATE/TIME/LOCATION text, and "Registered at:/RSVP" block all flush with the column/banner edge instead of indented
- MEDIUM | all | p2 | table rows end higher than Word, so the bottom leaf pair renders below the card's bottom border (outside the card)
- MINOR | pdf | p1,p2 | thin gray bounding-box outlines drawn around the rotated floral images
- MEDIUM | html | - | floral pieces compose around the invitation off Word's arrangement — the rose sits high of the banner, single leaves scatter, and the pink banner overlays the name line

### wedding/03

- MINOR | all | p1 | pink rings picture rendered ~3% larger and shifted slightly up/right

### wedding/05

- MEDIUM | pdf | p1 | date block also rendered bold where Word uses regular weight
- MEDIUM | html | - | watercolor washes exported as standalone images stacked above the panels instead of backgrounds behind the headings

### wedding/06

- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/08

- MINOR | html | - | the green "&" badge renders (2026-10-01) as a square where Word draws a circle

### wedding/09

- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/11

- MEDIUM | pdf | p1,p2 | teal "SATURDAY"/"10.25.20XX" date lines (both cards) and "ACCEPT | DECLINE" render bold where Word shows regular weight
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

`agendas-minutes/03`, `agendas-minutes/04`, `agendas-minutes/05`, `agendas-minutes/07`, `agendas-minutes/09`, `agendas-minutes/11`, `agendas-minutes/13`, `agendas-minutes/15`, `agendas-minutes/18`, `agendas-minutes/19`, `business/04`, `cards/03`, `cards/04`, `cards/16`, `cards/19`, `complex_tables`, `cover-letters/06`, `cover-letters/08`, `cover-letters/09`, `cover-letters/11`, `labels/02`, `resumes/04`, `html_nested_lists`, `icon_with_text`, `table_autofit_no_widths`, `table_cell_spacing/01`, `table_diagonal_borders/01`, `table_grid_styling_padding`, `wedding/10`, `align_center`, `font_sizes`, `labels/05`, `labels/08`, `labels/16`, `letters/04`, `table_default_style_inside_h`, `align_justified`, `align_left`, `align_mixed`, `align_right`, `all_caps`, `block_quote`, `bold_text`, `bullet_list`, `colored_text`, `column_breaks`, `complex_document`, `content_control_inline`, `compatibility_mode_14`, `cover-letters/02`, `custom_margins`, `decimal_tabs/01`, `cards/10`, `deep_nested_list`, `document_protection/01`, `dot_points`, `embedded_font`, `labels/14`, `resumes/18`, `table_borders`, `even_odd_headers/01`, `even_odd_headers/02`, `explicit_break_blank_page`, `first_line_indent`, `font_families`, `field_codes_simple/01`, `footer`, `form_checkboxes`, `form_dropdowns`, `form_text_fields`, `gutter_margins/01`, `hanging_indent`, `header`, `header_banner_table`, `header_footer`, `headings`, `html_basic_formatting`, `html_css_colors`, `html_font_tag`, `html_inline_styles`, `html_links`, `html_paragraphs`, `hyperlinks`, `hyphenation_nonbreaking`, `hyphenation_soft`, `image_cropping/01`, `inline_group_crop`, `inline_image`, `italic_text`, `labels/01`, `labels/07`, `labels/12`, `labels/13`, `left_indent`, `letters/06`, `line_breaks`, `line_numbers_continuous`, `line_numbers_count_by_5`, `line_numbers_custom_distance`, `line_numbers_restart_page`, `line_numbers_restart_section`, `line_numbers_suppressed`, `line_spacing`, `line_spacing_at_least`, `line_spacing_exactly`, `long_paragraph`, `nonstandard_main_part_name`, `numbered_list_tracking`, `page_letter`, `cover-letters/16`, `resumes/15`, `menus/02`, `mixed_breaks`, `mixed_formatting`, `multiple_images`, `multiple_pages`, `multiple_paragraphs`, `nested_list`, `numbered_list`, `numbered_list_restart`, `page_a4`, `page_borders/01`, `page_breaks`, `page_landscape`, `page_legal`, `page_numbers`, `pct_pos_offset`, `postcards/02`, `resumes/13`, `rtl_paragraph`, `section_break_continuous`, `section_break_even_page`, `section_break_next_page`, `section_break_odd_page`, `simple_paragraph`, `simple_table`, `small_caps`, `strikethrough_text`, `subscript_superscript`, `tab_stops`, `table_alignment/01`, `table_cell_margin_per_cell`, `table_cell_padding`, `table_cell_padding_varied`, `table_colors`, `table_default_cell_margin`, `table_default_cell_margin_start_end`, `table_default_style`, `table_default_style_first_row_run_color`, `table_default_style_first_row_shading`, `table_explicit_heights`, `table_indent`, `table_text_direction`, `table_of_contents/01`, `table_of_contents/02`, `table_page_break`, `table_two_column_layout`, `table_vmerge_basic`, `table_vmerge_explicit_heights`, `text_wrapping_break`, `wide_table`, `three_columns`, `tracked_changes/01`, `two_columns`, `underline_text`, `wedding/07`, `agendas-minutes/06`, `brochures/04`, `business-plans/04`, `business-plans/06`, `html_table_cellpadding`, `letters/09`, `menus/09`, `resumes/11`, `cards/05`
