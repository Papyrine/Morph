# Page-count divergence analysis

Word page counts (Word COM via RenderHelper → `expected_*.png`) versus each backend's verified
output (`skia_result#page_*`, `imagesharp_result#page_*`, `pdf_result#page_*`) across the scenario
directories in `src/Tests/Inputs/`. The match is *recorded*, not asserted, by the scenario tests —
these mismatches never fail the suite; closing them improves Word fidelity.

**Current state (full recount, 2026-08-06): Skia 325, ImageSharp 325, PDF 325 of 325 — every scenario
in the corpus now renders Word's page count, on every backend.** The three-way divergence this document
exists to track is over, and so is the gap to Word.
All three backends paginate through the one engine (`Fragmenter`), so every remaining mismatch is
identical across backends; no scenario disagrees *between* backends anymore. `resumes/13` — the
archetype (raster short, PDF over, Word between) — left the list entirely: all three backends now
render Word's 5 pages.


`newsletters/06` left the list on 2026-08-06 and now matches Word at 4 pages. **Its recorded cause —
an `atLeast`-table knife-edge whose residual was the raster line-height "dead-end lever" — was wrong
on both counts.** Line metrics agree with Word (pitch 12.48 vs 12.60pt, ink height 8.64pt on both)
and its fonts are bundled. The defect was COLUMN WIDTH: `TableLayout` made the declared `w:tblGrid`
authoritative only for fixed-layout tables, and the per-cell width loop reads only single-span cells
carrying an explicit `w:tcW` — so under autofit a column covered exclusively by spanned cells stayed
at zero and split the leftover evenly. The declared grid
`[42.4, 141.6, 14.0, 9.0, 157.5, 13.5, 4.5, 7.3, 150.2]`pt came out
`[42.4, 35.3, 35.3, 35.3, 157.5, 13.5, 35.3, 35.3, 150.2]` — five columns flattened to their 176.4pt
total divided five ways. Measured on page 1, the three newspaper columns rendered 159/**114**/165pt
against Word's 151/156/141: the middle column 42pt narrow, so its text wrapped far longer and
inflated one row by 88-210pt until two of the four page-tables crossed the 745.75pt content height
and spilled a row. An authored grid is Word's starting point under autofit too. Corpus effect of
making the grid authoritative everywhere: 257 pages moved, 62 better / 153 same / 42 worse, aggregate
mean |Word−render| 10.916 → 10.461, largest win −17.6 and largest loss +1.3, no page count lost.

`image_wrap_square` left the list on 2026-08-06 and now matches Word at 2 pages. It was never a
knife-edge: an empty paragraph carrying the section's `sectPr` sat between the "Columns" heading and
the column text, and the engine gave it a line Word does not draw. Both agree the heading ends at
617pt, but Word's first column line starts at 635.5pt against the engine's 650.2 — exactly one line
pitch, which cost the left column its sixth line and pushed the second paragraph onto a third page.
Word-probed (`_probe_sect_break`): ALPHA / empty paragraph / BRAVO at single spacing puts BRAVO
27.3pt below ALPHA when the empty paragraph is ordinary (two line pitches) but **13.4pt — one pitch —
when it carries the `sectPr`**. The rule is applied to CONTINUOUS breaks only, which is what the
fixture can measure: for a next-page break the two paragraphs land on different pages, so the gap is
unobservable and the mark's line instead decides whether the *preceding* content still fits. Applying
it there unprobed moved `sample.docx` 6 pages to 5 with nothing to adjudicate against.

**business-plans/15 was closed 2026-08-06 by implementing table-row splitting**, the last page-count
gap. The engine used to NEVER split a table row across a page: `PlaceTableRowByRow` moved a row that
would not fit whole to the next region, and `LayoutCellContent` took no page breaks at all ("the row
height already accommodates the content"). That held until a single row was taller than the content
height — then it simply overflowed off the page.

This document wraps whole prose sections in one-row tables, so that case is the norm rather than an
edge: **table35 is a single row of 23 paragraphs** (the balance-sheet guidance), table39 a single row
of 12 (break-even) and table40 a single row of 12 (miscellaneous documents). Measured on page 16, the
engine places cell lines at y = 755, 767, 779, 800, 812, 832pt against a content bottom of 756 — the
last two bullets are drawn over the footer and clipped at the page edge, and the footer band lands at
781.9pt where Word's sits at 761.8. Word splits that row: its page 16 stops after "Payroll Accrual"
and its page 17 opens with "Long-term Liabilities" and "Net Worth", the two bullets the engine
overflowed. One page saved, and the same thing happens again at table39/table40, which is why the
engine's last page carries both the break-even body and the whole miscellaneous block.

**The implemented rule is narrow on purpose: a row splits only when it is taller than a whole empty
region.** Such a row cannot be rescued by moving it, so the old behaviour was provably wrong there;
a row that does fit keeps moving whole, which is what matches Word across the other 324 scenarios.
`LayoutCellFragment` carries a start point (element index + line index) and a height budget, returning
what fit and where to resume, so each cell continues independently; the unlimited path is unchanged.
A fragment that continues drops its bottom border and a resumed one drops its top, so the split edge
draws no rule; `w:tblHeader` rows re-emit above each continuation; cell-anchored art draws only on the
first fragment; vertical alignment is skipped for a split row (its content no longer has one box to be
centred in, and Word tops each fragment out); and a row carrying a vertical merge is excluded, since a
merged cell's height derives from the rows it spans. Corpus effect: business-plans/15 and nothing else
— all nine changed pages improved (aggregate mean |Word−render| 17.85 → 15.39, page 18 by −7.7) and
the new page 19 lands at 2.9 grey levels from Word.

The splitter applies keep-lines and widow/orphan control, and porting `PlaceParagraph`'s version
verbatim was NOT enough: Word settles the two rules in order and lets the second act on the first's
result. The three-line "Long-term Liabilities" bullet is the case — two lines fit, the widow carry
takes that to one, and the orphan rule then takes it to none, which is exactly where Word breaks.
Checking orphan-then-widow as mutually exclusive branches (what the flow path does) stops after the
first step and leaves a lone line behind: page 16 came out with 44 ink bands against Word's 43. With
the ordered form it is 43, and pages 16/17 improve by 0.6 and 3.8 grey levels. `PlaceParagraph` was brought to the ordered form
too, on its own gate: the corpus does not contain a single instance of the differing case, so the
scenario suite is byte-unchanged either way and `CanonicalFragmenterTests` pins the rule instead
(the three-line test fails under the alternative reading, which is what makes it a guard rather than
decoration).

`w:cantSplit` is now read and honoured, settled by its own probe (`_probe_cantsplit_*`, four fixtures
at 12pt-exact lines): **Word keeps the attribute even when splitting is the only way to show the
content.** A flagged row taller than any page ran to 791.5pt on a 792pt page with the following
paragraph alone overleaf, where the unflagged control split 53 lines / 17 — so Word would rather lose
the overflow off the paper edge than break a flagged row. Letting a flagged row fall through to the
move-whole path reproduces that exactly, and two `CanonicalFragmenterTests` pin it since no corpus
document sets the attribute.

The same probe turned up a wider divergence, filed as todo.md #25 rather than folded in: **Word splits
a row that merely does not fit the space left**, even when the row would fit a fresh page — 168pt free
against a 240pt row put 13 lines on page 1 and 7 overleaf. The engine splits only a row taller than a
whole region, so it currently behaves as though every row carried `w:cantSplit`. **Widening the
trigger was attempted on its own gate and reverted**: it reproduced Word's shape on the probe (both
unflagged fixtures split) but moved 129 corpus pages, 87 of them worse against 38 better, aggregate
10.375 → 13.299 with individual pages up to +20.7. The last-line rule was probed next
(`_probe_lastline_*`) and refutes BOTH readings: at 13pt exact spacing Word breaks the flow at 49
lines where the engine's ascent test takes 50, but switching the flow to the full line box regressed
`image_wrap_square` from 2 pages to 3 and was reverted. Auto spacing cannot separate the two tests,
which is why the ascent version survived so long. The ink-box candidate (ascent + descent fitting,
only the external leading allowed to hang) was built and tried: it matches Word on all four controlled
fixtures — 49/49/42/41 — yet the corpus result came back byte-identical to the full-box attempt and
`image_wrap_square` lost its page again. **For a zero-leading font the ink box IS the line box**, so
the two rules only differ where the font declares a line gap, and the corpus's fonts mostly do not.
That clears the fit rule of `image_wrap_square`'s extra page: Word's own render puts that column's last
line at 713.8-720.0pt against a content bottom of exactly 720, so it fits by a hair under any reading,
and the engine losing it means the line is measured slightly taller than Word's — a line-height
question, not a page-break one. Word's flow and cell also differ
from each other under auto spacing (42 lines against 41, which the engine reproduces): a separate,
uncharacterised difference in a cell's usable height, not the last-line rule.

The 2026-07-25 recount (Skia 322 / ImageSharp 322 / PDF 321, with per-backend disagreements) and the
"experiment 19 committed: 315/315/316 of 321" snapshot below are the historical figures the ledger
was written against.

**newsletters/06 is a near-full-page `atLeast`-table knife-edge, not a content rule.** Each of the
four newspaper "pages" is one 10-row layout table whose explicit `w:trHeight` rows already sum to
almost the full 746pt content height (table A 662pt + three auto rows; table B 713pt), all `atLeast`.
The big text rows (row 7 `[7p,4empty]`, row 9 `[9p,5empty]`) hold body paragraphs separated by
empty-paragraph spacers. A ~30–80pt cumulative over-measurement in those rows grows the table past
the page, tipping the last ~184–216pt row (row 9) entirely onto a fresh page — measured: skia page 2
fits with **13pt** to spare, then row 9 bumps, and the spilled row renders on a *white* page because
the light-blue background is a float anchored to the section's first page. The empty spacers are NOT
the lever (Word lays an empty paragraph out as a full line — verified on the `empty_paragraphs`
scenario), so the residual is the raster text-line-height difference the root-cause lesson below
calls a dead-end lever. Two of the four tables (the taller layout) cross the edge; the other two clear it.

**The "backend-metric divergence" label on these was partly wrong.** business-plans/02 and /12 were
both filed under it — Skia/ImageSharp and PdfSharp producing different per-line/per-cell heights,
supposedly needing opposite per-backend adjustments rather than a content-level rule. Both turned
out to be exactly a content-level rule (experiment 19: the Auto multiplier must not scale an inline
image), and the reason the raster and PDF counts straddled Word is that PdfSharp already implemented
that rule while the raster backends did not. Treat the remaining eight as unattributed rather than
as established knife-edges.

**Protocol per experiment:** implement across all three backends, validate in-container with a
no-regen scenario run isolating exactly which scenarios move, accept only net Word-match gains —
washes and regressions revert, but the measured knowledge is kept.

## Pass 4 experiment ledger (newest first)

- **22 — exact row fit landed for plain content rows, narrowing experiment 12's "load-bearing"
  verdict (2026-08-19).** Found off-corpus: a 1216-row summary table over 52 landscape pages (COMPASS
  stocktake report, via Parchment's resolved PAGEREFs), where `HasSpaceFor`'s 2% slack is 9.6pt
  against a 17.7pt single-line row. A boundary row 1.5pt past the margin was kept where Word moves
  it — one extra row on roughly every fourth page, a full page lost by the table's end, and every
  PAGEREF below the table one page low (Word-COM verified: du4 55 vs engine 54, total 64 vs 63).
  The narrowing exp 12 never tried: strict fit ONLY in `PlaceTableRowByRow`'s per-row decision, for
  rows without a vertical merge; the whole-table move, the merge-head exemption and every paragraph
  decision keep the slack. Re-running exp 12's global zero now costs 3 rather than its net −6
  (business-plans/15 19→21, newsletters/09 4→5, resumes/06 3→6) — the height-model fixes since have
  absorbed the rest — and all three ride the whole-table and merge-head slack this leaves alone.
  **Corpus effect of the narrowed form: zero page-count changes anywhere, and 45 pages across 6
  scenarios move on pixels — aggregate AE 12.4397 → 12.3668 (−0.0729), SSIM 35.7479 → 36.0566
  (+0.3087), 24 pages better / 6 worse.** Biggest win `word/business/03` page 2 (SSIM +0.12 Skia,
  +0.12 PDF, +0.05 ImageSharp): its ink used to start at row 43 against Word's 70 and now starts at
  71, so the page's whole content block was 27px high and is now within a pixel. Biggest loss
  `excel/social-media-editorial-theme-calendar` page 2 (AE +0.04, SSIM −0.016) against a small gain
  on its page 1; `word/border_style_variants` page 5 moves −0.005 SSIM against +0.007 on page 4.
  **Four of the six affected scenarios are spreadsheets** — a sheet paginates through the same row
  loop — and three improve on every backend (basic-business-invoice,
  inventory-list-with-highlighting, wedding-invitation-tracker).
  The calendar is the one that goes the other way, and its shape is worth recording: its page-2 ink
  runs rows 80-857 against Excel's 72-811, where it used to run 71-834. The split moved one row
  later, so **Excel keeps a boundary row that Word's exact fit moves** — a hint that Excel's own
  row tolerance is not Word's. `PlaceTableRowByRow` serves both, so if spreadsheet fidelity ever
  needs it, the fit is the place to split the two rules; the corpus does not justify that yet
  (3 sheets better, 1 worse).
  The engine now reproduces Word's row-by-row page boundaries on the stocktake table exactly; the
  earlier strict-fit revert ("costing business-plans/15 a page") predated PlaceSplitRow's
  split-acceptance test, which is what turns the pathological boundary splits back into whole-row
  moves. Word's exact fit (`widorp.cxx:134,157`) is now complied with for the row case; the slack
  survives elsewhere as exp 12 found, compensating residual over-measurement. Pinned by
  `CanonicalFragmenterTests.A_row_barely_past_the_bottom_margin_moves_rather_than_overhanging`.
- **20 — the table-style `w:pPr` cascade step: landed, and it unblocked the docDefaults `w:line`
  cascade.** ECMA-376 resolves a paragraph inside a table as `docDefaults → table style w:pPr →
  paragraph style chain → direct w:pPr`; Morph skipped the middle step entirely. `resumes/07`'s
  tables use `TableGrid` declaring `w:after="0" w:line="240"`, so in Word its cell paragraphs are
  single-spaced with no after — immune to that document's 8pt after and 1.158 docDefault line.
  Word-probe verified by stripping the style-level `w:pPr`: Word goes 1 page → 2. Implemented via
  `StyleParagraphSpacing` (nullable fields, because the resolved `ParagraphProperties` cannot tell
  "declared zero" from "inherited zero") plus a save/restore of the active table style id around
  `ParseTableCore`.
  Alone it is near-neutral (21 scenarios, −0.05 AE, **−0.25 SSIM**) — its value is as the
  prerequisite. **With the docDefaults `w:line` cascade on top: 75 scenarios, 55 better / 20 worse,
  net −1.8694 AE, SSIM +3.8378 agreeing with AE on 342 of 362 pages, and 0 page-count changes in
  either direction.** That cascade had been attempted and reverted twice before; `resumes/07` was
  its last blocker and this is why it now holds Word's single page.
  A `business-plans/02` regression of +0.17 AE was first recorded here as "a compensating error
  elsewhere in that scenario". **That was wrong** — it was a precedence bug in this very step, and
  the correction is experiment 21.
- **21 — table-style precedence must consult the DEFAULT paragraph style: +0.23 AE.** The check for
  "did the paragraph style already declare this?" read the explicit `w:pStyle` only. A paragraph
  with no `w:pStyle` still uses the document's default paragraph style, whose declarations outrank
  the table style. `business-plans/02`'s `Normal` declares `w:line="336"` (1.4) against its
  `TableGrid`'s 240, and Word keeps 1.4 inside those tables; reading only the explicit pStyle let
  the table style win and set that text single-spaced. Symptom was surgical — exactly three gaps on
  page 3 moved, each by 11px, everything else byte-identical. Fixed by using
  `styleId ?? defaultParagraphStyleId`, which the neighbouring `styleDefaults` lookup already did.
  9 scenarios move, 7 better / 2 worse, net **−0.2255 AE**, SSIM +0.7547, no page-count changes;
  `business-plans/02` recovers exactly the +0.1719 it had lost, and five more business-plans /
  letters scenarios recover with it. Test: `DefaultParagraphStyleDeclaration_OutranksTableStyle`.

- **19 — the Auto multiplier must not scale an inline image: landed, +2 scenarios.** Word applies
  the Auto line multiplier to the TEXT line box only; an inline image contributes its height
  unscaled and the line takes the larger of the two. Verified by sweeping brochures/06's docDefault
  `w:line` from 1.15 to 1.50 in Word: its two photo rows grew 1.3% and 3.4% where multiplying the
  image predicts 30%. Fitting the sweep gives `row = 211.6px image + 22.6px (10.9pt) text line ×
  multiplier`, residuals under 1px over five points. `PdfTextEngine` already modelled this (an
  image item keeps its raw height while text runs get `rawHeight × multiplier`), which is why PDF
  held Word's page count on brochures/06 where the raster backends gained a page — the two raster
  backends were the outliers, not Word.
  **The rule lives in two places and only one was wrong at first.** `TextRenderer.CalculateLineHeight`
  is the render path; table cell heights go through `LayoutParagraphForMeasurement` →
  `TableLayout.CalculateCompactLineHeight`, which had the identical bug and took only a float, so
  it could not see the image. Fixing the render path alone moved 44 scenarios and fixed
  business-plans/02 (7→6) but left every image-in-a-table row untouched — brochures/06's photo rows
  came back byte-identical. `CalculateCompactLineHeight` now takes the caller's Auto height so the
  two paths cannot drift again. Combined: **−0.5693 AE**, SSIM +2.62, and **business-plans/02 and
  business-plans/12 both leave the ten-mismatch list** (19→18 for /12). Scoreboard **315/315/316**.

- **18 — a page break at the top of a page is NOT absorbed: probed, no change made.** The
  attractive guard is `CurrentY > ContentTop` on `PageBreakElement`, mirroring
  `AdvanceToBackgroundsTargetPage`. Word says no: minimal fixtures of N consecutive break-only
  paragraphs render **N+1 pages** (1→2, 2→3, 3→4), so interior blank pages are legitimate output.
  Morph already matches; `ConsecutivePageBreakTests` pins it so the guard is not added later. The
  blank page that motivated the question (brochures/06 under the docDefaults `w:line` cascade) is
  a *symptom* of cell content crossing `atLeast` row floors — see experiment 17.
- **17 — default run size when docDefaults omits `w:sz`: landed, count-neutral.** Same rule shape
  as experiment 15, one element over: `docDefaults` present but no `w:sz` → the ECMA-376
  §17.3.2.38 default of 20 half-points (**10pt**), not normal.dotm's 12pt; only a document with no
  styles part or no `docDefaults` keeps the built-in. Word-probe verified on brochures/05
  (declares `docDefaults`, no `w:sz` anywhere): an injected `w:sz="20"` reproduces Word's render
  with zero differing text pixels, `w:sz="24"` repaginates it 4→5. 23 scenarios carry the bug; 13
  moved, 12 better / 1 worse, net **−0.0869 AE**, **zero page-count changes**. This was the real
  content of the long-standing "~9% glyph advances" attribution for those documents — the glyphs
  were not narrow, they were being drawn 20% too large.

- **16 — empty-mark height in cells: reverted wash.** Un-gated experiment 1's mark height inside
  cells (dropped `TableCell` from `inScopedPart`). Now **safe** — wedding/05 and the cards cluster
  no longer regress, so the experiment-1/3b blocker is gone since experiment 15 changed the corpus;
  the rule is reinstatable if a scenario needs it. But not resumes/13's lever: only moved
  resumes/13 PDF 6→4 (the mark box is *shorter* than PDF's old fallback), scoreboard unchanged,
  204 scenarios churned → reverted.
- **15 — default after-spacing when docDefaults omits it: +8 scenarios (biggest win).**
  `ExtractDefaultParagraphProperties` fabricated 8pt after-spacing for any doc lacking explicit
  paragraph defaults. Correct rule keys on `docDefaults` presence: no `styles.xml` / no
  `docDefaults` element → keep Word's 8pt built-in (minimal hand-built docx like multiple_pages,
  table_multipage); `docDefaults` present but no `pPrDefault` → **0** (Word reads the omission as an
  explicit zero). 306/306/306 → **313/313/316**, mismatches 18→10, zero regressions. Fully fixed:
  brochures/07, cover-letters/03/15, letters/09, resumes/02/11/18, wedding/05; PDF-fixed:
  cover-letters/06, newsletters/06, resumes/16. resumes/13 8→4 — page 1 now Word-exact; the
  residual is backend-metric, not spacing.
- **14 — percent-preferred cell widths: menus/03 fixed.** `w:tcW w:type="pct"` was ignored (dxa
  only); menus/03 has no `w:tblGrid`, so the percent widths fell to an equal split, narrowing the
  tip cell until its text spilled to a second PDF page. Added `TableCellProperties.WidthFraction`
  (pct is fiftieths of a percent), resolved against the table width. PDF 305→306, menus/03 PDF 2→1,
  all three backends level at 306, zero regressions; 23 table scenarios' proportions corrected
  toward Word (table_two_column_layout 40/60 → exact 50/50).
- **13 — PDF gains the Exactly/AtLeast line-spacing rules.** `PdfTextEngine` applied only Auto
  (`Auto ? multiplier : 1`); exact-550-twip lines rendered at natural height. Now mirrors the raster
  `CalculateLineHeight`. Count-neutral, zero regressions, 17 scenarios churned PDF pixels toward
  correctness.
- **12 — the fit tolerance is load-bearing: reverted.** Zeroed the raster `HasSpaceFor` slack
  (`ContentHeight × 0.02`, ~13pt; Word's fit is exact, `widorp.cxx:157`). Net −6 → restored. It
  compensates systematic over-measurement; the run mapped the consumers (docs within ~13pt of an
  edge = the height-error shortlist: compatibility_mode_14, resumes/01/14/15, business-plans/12/13).
  One true positive: business-plans/15 PDF 18→**19**, matching Word — its deficit *is* the tolerance.
- **11 — image_wrap_square is a section-type off-by-one: fix built, reverted.** Not the fly rules:
  per ECMA-376 §17.6.22 a `sectPr`'s `w:type` describes how the *owning* section begins, so a
  mid-document break takes the *following* `sectPr`'s type. Morph read the mid one (absent →
  nextPage) while the final section is continuous two-column → the columns page-broke. Fix (type
  lookahead + column band anchored at the break Y) was count-neutral and reverted: the raster
  pipeline can't split a paragraph across columns, Skia and ImageSharp diverged, and PDF bypasses
  `SectionBreakHandler`. Reinstatement needs paragraph-split-across-columns + Skia parity + PDF
  routing.
- **10 — whitespace wrap + line-height rules: verified, reverted (knife-edge loss).**
  XPS-probe-confirmed: Word never wraps on trailing whitespace (40 boundary spaces overhung 50pt
  past the margin) and whitespace never sizes a line (28pt spaces/tab → 11pt control pitch).
  Implemented across three backends; menus/03 raster flipped 1→2 (its degenerate-narrow instruction
  textbox relied on the old space-into-blank-line artifact) → reverted. Reinstate once menus/03's
  textbox geometry is fixed.
- **9 — header growth: verified, deferred.** XPS-verified body top = `max(pgMar.top, pgMar.header +
  headerContentHeight)` to ±0.2pt; anchored header content exempt. `SetHeaderFooterSpace` already
  implements the formula, but all backends feed it zero. Corpus scan: 49 headered scenarios, ~15
  over-band, all currently matching; the only over-band residual (business-plans/12) is already over
  Word → deferred, no winnable scenario.
- **8 — PDF keep gates measure flow-true heights: kept (neutral).** The keep gates measured
  uncollapsed spacing-before while `Draw` applies the `max(0, before − prevAfter)` collapse.
  `MeasureFlowHeight` folds it in. Bit-identical (no keep in the corpus sits within the collapse
  margin of a page bottom); kept as measurement-consistency groundwork for experiment 12.
- **7 — widow/orphan control in the PDF backend: multiple_pages fixed.** `w:widowControl` (default
  on) = two lines minimum each side of a split (`DomainMapper.cxx:2061`). The raster backends
  approximated it; PDF had none. `PdfTextEngine.Draw` now plans line-level splits (two-line minimum
  both sides, whole-paragraph fallback, page-top abandonment, re-planned per segment). PDF 304→305,
  multiple_pages PDF 4→5. complex_spacing is *not* widow-driven (unmoved).
- **6 — Word's advance model measured; PDF quantization a wash: reverted (model kept — see keepers).**
  Implementation churned 315 scenarios with zero count moves and wash metrics. Attribution:
  multiple_pages and complex_spacing PDF deficits are *not* wrap-width driven (no re-wraps under
  true quantised widths).
- **5 — compatibilityMode defaults to 12.** A doc declaring no compatibilityMode is mode 12
  (ECMA-376, `SettingsTable.cxx:633`), not 15; the record default 15 stays for HTML. Count-neutral
  (306/306/304); only complex_spacing shifted. Keys every mode-gated rule off the mode Word used.
- **4 — keep rules in the PDF backend.** PDF had no keep handling; now mirrors the raster
  keepNext/keepLines plus LO abandonment guards (`widorp.cxx:313-395`). Count-neutral. Attribution:
  complex_spacing's PDF deficit is pure wrap difference (kept pairs all fit); resumes/13 is *not*
  keep-driven (same 8 pages with and without keeps).
- **3 — end-of-cell mark collapse.** An empty paragraph directly after a nested table at a cell's
  end collapses to zero height (LO `CollapseEmptyCellPara`, `calcmove.cxx:1088`). PDF 302→304:
  resumes/06 PDF 6→3 and labels/15 PDF 2→1, zero regressions. 3b (un-gating cell mark heights)
  reverted — re-broke wedding/05.
- **2 — space-before dropped at page tops.** A body paragraph at the top of an automatically broken
  page gets no spacing-before (compatibilityMode 15 also after explicit breaks; section breaks and
  the first page keep it). Count-neutral — Morph's cross-page `max(0, before − prevAfter)` collapse
  already absorbed most of it; combined experiment 1+2 pixel fidelity clearly improved (220 pages
  toward Word, top-15 movers all improvements).
- **1 — empty-paragraph mark heights.** An empty paragraph's line takes the paragraph mark's
  style-resolved formatting as a full hhea box (XPS-verified to ±0.07pt — the mark drives the line,
  not an empty run's size). Body only; cells and headers/footers gated (un-gating inflated
  wedding/05's empty Heading1 marks to ~43pt). 307/307/301 → 306/306/302, net −1 (two knife-edge
  losses in compensating-error documents).

## LibreOffice rule index

Reverse-engineered Word rules from the LibreOffice Writer source (`C:\Code\LibreOffice`). The
references are evidence for the behaviour, not code to port (LO is MPL-2.0). Verified in-source.

| Rule | LibreOffice evidence | Status in Morph |
|---|---|---|
| Empty-para line height from the paragraph mark | `SettingsTable.cxx:679`, `porlay.cxx ~594` | exp 1 (body); cells reinstatable per exp 16 |
| Ignore tabs/blanks for line height | `WriterFilter.cxx:318` | exp 10 (verified, reverted) |
| Space-before dropped at page top | `flowfrm.cxx:1415`, `calcmove.cxx:1132`, `flowfrm.cxx:1538` | exp 2 |
| Adjacent spacing = max(after, before) | pass-2 XPS; sum under `doNotUseHTMLParagraphAutoSpacing` `DomainMapper_Impl.cxx:10112` | pass 2 |
| Widow/orphan = 2 lines each side | `DomainMapper.cxx:2061`; `widorp.cxx:453-660` / `:766-837` | exp 7 (PDF); raster prior |
| Keep abandonment (first-on-page, chain can't move) | `widorp.cxx:313-395`, `tabfrm.cxx:2945` | exp 4 (PDF); raster prior |
| Floats never paginate | `fly.cxx:132/161`, `anchoredobjectposition.cxx:457/492-514` | complied pre-pass-4; exp 3 redirected |
| End-of-cell mark collapse | `calcmove.cxx:1088` (CollapseEmptyCellPara) | exp 3 |
| compatibilityMode default 12 | `SettingsTable.cxx:633` | exp 5 |
| mode ≤14 flag set (MinLineHeightByFly, TabOverMargin, AddFrameOffsets, HiddenParaMark) | `SettingsTable.cxx:685-691`, `itrform2.cxx:358`; UseFormerTextWrapping `txtfly.cxx:863` | not modelled (image_wrap_square) |
| Header content pushes body down | `PropertyMap.cxx:1149` | exp 9 (verified, deferred) |
| Exact bottom-of-page fit, no slack | `widorp.cxx:134,157` | exp 12 (load-bearing, restored); exp 22 (complied for plain table rows) |
| Trailing blanks overhang the wrap boundary | `DomainMapper.cxx:142`, `guess.cxx:99-116`, `portxt.cxx:256` | exp 10 (verified, reverted) |
| Break type comes from the following section's `sectPr` | ECMA-376 §17.6.22 | exp 11 (built, reverted) |
| Omitted `docDefaults` `w:sz` is 20 half-points, not the built-in | ECMA-376 §17.3.2.38; Word probe | exp 17 (landed) |
| A page break at a page top still starts a page | Word probe (N breaks → N+1 pages) | exp 18 (no change needed) |

**Dead ends / parked (recorded so they are not re-chased):**

- Fixed-row height returned verbatim (`tabfrm.cxx:5070`) and column balancing (`PropertyMap.cxx:900`)
  are irrelevant — the residual corpus has *zero* `hRule="exact"` rows and *zero* multi-column
  sections.
- LO adds the last cell paragraph's space-after and line spacing to the cell (`flowfrm.cxx:1946`;
  flags `WriterFilter.cxx:313`) — this conflicts with Morph's XPS-validated overlap model
  (`TableHeightCalculator.cs:268`, cell bottom = `max(after, bottomMargin)`); parked pending a cell
  probe.
- Advance quantisation has no LO counterpart (closest: `CommonSalLayout.cxx:826`,
  `DWriteTextRenderer.cxx:101`) — original work; measured in experiment 6.

## The measured models (keepers)

### Height model (pass 2 — foundational)

- **Line pitch = hhea (ascent + descent + line gap) × the Auto multiplier** (exact for Exactly, max
  for AtLeast). Measured: Aptos 12pt single = 14.65pt, Calibri 10.8pt single = 13.18pt. PdfSharp's
  `GetHeight()` and Skia's `ascent + descent + leading` both equal this for every bundled font.
- **An absent `w:spacing/@w:line` is exactly SINGLE (multiplier 1.0) in any styles.xml-bearing
  document** — Word-probed 2026-08-04 with long wrapping paragraphs (line deltas free of
  before/after spacing) in two host packages: inherited pitch == explicit `w:line="240"` == the
  hhea box, for Calibri Light / Segoe UI / Aptos alike, at ±0.35px resolution. Ten fonts probed at
  explicit single all sit on the hhea box (Segoe UI's 1.3301em included). The parser's invented
  1.04 (style path) and 1.08 (style-less-paragraph path) defaults were the corpus-wide
  "~0.6-1pt/line too tall" drift vs Word; fixing them to 1.0 measured +0.0079 aggregate SSIM
  (85 improved / 58 regressed) and moved page counts only TOWARD Word (resumes/13 6→5; nine
  backend-scenarios regained page-count agreement). The `builtInLineSpacingMultiplier` (278/240,
  for documents with no styles.xml at all) is a separately-measured keeper and stays.
- **The line box itself is flag-dependent (Word-probed 2026-08-05 on Baskerville Old Face, the first
  bundled font whose models split):** USE_TYPO_METRICS → the typographic box (sTypoAscender −
  sTypoDescender + sTypoLineGap); otherwise the GDI cell — usWinAscent + usWinDescent + max(0, hheaGap −
  (winTotal − hheaBoxNoGap)). Word measured 14.64 pt at 13 pt single and 17.76 pt at ×1.2 — the GDI
  numbers (hhea would be 13.00/15.60). The hhea-box model survived every earlier probe only because the
  probed fonts' typo/win boxes coincide with their hhea boxes (Aptos, Calibri, Segoe UI, Arial, Times,
  Franklin, Avenir all do); 31 bundled faces diverge (Baskerville +0.14 em, Georgia Pro +0.16, Helvetica
  +0.18, Lato Light +0.22, Work Sans +0.27), and business-plans/05 (Baskerville Normal) ran ~2.2 pt short
  per line under hhea — the largest adjudication-confirmed flip breach (engine 59.5 px mean band error →
  24.0 after the fix, beating production's 20.2 on most bands).
- **Adjacent paragraph spacing collapses to max(after, before)**, not the sum.
- **No 1.20×size floor and no ×1.035 leading boost** — both were fudges compensating the missing
  line gap; they cancel for Calibri-class fonts, which masked the wrong model.

This model took the scoreboard from 304/305/296 to 307/307/301 (pass 2).

### Advance model (experiment 6 — measured, code reverted)

- **Integer-pixel advances at 120 dpi** (0.6pt quantum, the reference machine's 125% display
  scaling). **ppem = round(size × 120/72)** — 11pt and 10.5pt both lay out at em 10.8pt.
- **Letters ≈ round(linearEm × ppem) px** (occasional +1px on TrueType-hinted glyphs).
- **Inter-word spaces are elastic upward**, tracking the nominal-linear ideal: Calibri 0.9998×,
  Arial 1.0000×, Segoe UI 1.0008×, Aptos 1.0125×, Times New Roman 1.0213×.

The PdfSharp implementation was a wash (zero count moves); the model is the keeper for any future
wrap-width work.

**Word probe (2026-08-01) — the ppem@120 grain is an approximation, not Word's model.** A fixture
rendering a fixed string at 10–14pt in 0.5pt steps through Word at 150 dpi, measured by both ink-edge
extent and ink-profile autocorrelation over two renders, puts W(10.5)/W(11) at **0.983 — not 1.000**:
Word does *not* lay 10.5 and 11pt out identically. *The rest of this paragraph's conclusion — that Word
sits between the models and the grain is an unreachable ceiling — was WRONG, and the 0.983 was a
measurement artefact. Superseded by the 2026-08-02 probe below; kept only because it is cited elsewhere.*
Word sits *between* the integer-ppem@120 model
(which buckets both to em 10.8pt → ratio 1.000) and the raw-fractional model (10.5/11 → 0.955), and
wanders per size, matching no integer ppem at any single dpi — DirectWrite fractional-plus-hinting.
At 10.5pt the ppem@120 bucket (1.000) is actually the *closer* of the two to Word (0.983); raw
fractional (0.955) is further, so going fractional over-corrects — too narrow, wraps late. The
residual: ppem@120 over-widths 10 / 10.5pt text by ~2–3% versus Word, which is the visible early wrap
on those documents (resumes/15's 10pt contact block, business/05's 10.5pt body). Because the error is
*size-dependent*, no uniform knob corrects it — the per-font factor and FontWidthScale both washed for
exactly this reason.

### Ppem grain root-caused (2026-08-02) — Word does not quantize the em at all

The probe above measured a doubled *sentence* and recovered its period by autocorrelation, which is
noisy enough at these magnitudes to invent a result. Redone with a **repeated single glyph** — 60 `n`
on one line, so the gap between consecutive ink starts IS the advance, averaged over 59 samples with
no autocorrelation and no wrap — the picture is unambiguous, across Calibri, Aptos and Arial at 8–16pt:

- **Every advance is an integer number of device pixels**, and the *distribution* is mixed
  (11pt Calibri: 20×11px, 22×12px, 17×13px). Word rounds the pen POSITION to a whole pixel; it does
  not round the advance.
- **The mean of those integers tracks the nominal fractional advance**, within ~1% at most sizes
  (Calibri 8pt 8.746 measured vs 8.757 nominal; 11pt 11.949 vs 12.040; 16pt 17.492 vs 17.513). So the
  em is **not** quantized — there is no bucket.
- W(10.5)/W(11) measures **0.952**, against raw-fractional's 0.955 and ppem@120's 1.000. Word *is*
  fractional. The earlier 0.983 "between the models" reading does not reproduce.
- No `hdmx` and no `LTSH` table in Calibri, Aptos or Arial, so there are no cached device metrics to
  explain a per-ppem advance; FreeType's hinted advances do not match Word's either.

**So the grain is the engine's own error, not a property of Word.** `Ppem(size) = round(size × 120/72)`
quantizes the em onto a fixed 120-dpi grid regardless of the output resolution. At 10.5pt that is
17.5 → **18** (+2.9% wide); at 11pt 18.33 → **18** (−1.8% narrow) — two sizes 4.5% apart in nominal
width collapse onto one em. Measured against Word for Aptos over 8–16pt:

| model | RMS error | worst swing between adjacent sizes |
|---|---|---|
| engine, `ppem@120` | 1.61% | **3.97%** (10pt +3.00% → 11pt −0.97%) |
| plain fractional | 1.07% | **0.90%** |

The RMS barely moves, and that is the point: **what breaks wrapping is not the magnitude of the error
but its discontinuity.** A smooth systematic bias shifts every size alike and largely cancels in
relative layout; the engine's error jitters by up to 4% between neighbouring point sizes, so a document
set in 10 or 10.5pt wraps early while the same document at 11pt does not. That is precisely the
observed symptom — resumes/15's 10pt contact block, business/05's 10.5pt body, and image_wrap_square's
11pt density.

**The fix direction is therefore to stop quantizing the em** (keep the nominal fractional advance) and
round only the pen position, at the *actual* output DPI rather than a fixed 120. That is a change to
`CanonicalTextMeasurer.Ppem`/`LinearPixels`/`PixelsToPoints` and it moves every wrap in the corpus, so
it needs its own slice and a full re-measurement — but it is a modelling bug with a known direction,
**not** the fidelity ceiling the earlier probe concluded. A residual smooth trend (−1% at 8pt to +2% at
16pt, consistent across Aptos and Arial) is still unexplained and is the honest remaining unknown.

**Landed, and it bought less than the diagnosis promised.** `Ppem` became `EmPixels` — the em is no
longer rounded; only the accumulated pen position still quantizes, once per line, which is the part
Word does share. Measured over the corpus: **aggregate +0.0006 against Word, 36 snapshots improved,
218 flat, 18 regressed, and page-count agreement unchanged at 321/324** (the same three misses —
business-plans/15, newsletters/06, resumes/13 — so removing the grain moved no page break at all). Best
`agendas-minutes/09` +0.046; worst `business/06` −0.025 and `letters/10` −0.024, though the engine still
beats the production fallback on letters/10 by +0.037 even after that.

Two things it did **not** do, recorded so the next attempt does not assume otherwise:

- **`image_wrap_square` is still a coverage hold-out.** The grain was worth about +0.012 there
  (−0.0365 → −0.0242 against the production fallback), so it was a genuine cause but not the only one.
  What accounts for the remaining 0.024 is now **uncharacterised** — do not re-attribute it to the
  rasterization tail without measuring.
- **It did not move the PDF flip** into range; a +0.0006-scale gain does not close −0.0050.

The change shipped on correctness rather than on that payoff: the old model asserted 10.5pt and 11pt
lay out identically, which the probe shows is false, and the assertion was pinned by two passing tests
(`Ppem_quantizes_at_120_dpi` asserted the bucket; the pen-position test compared against the ideal at
the *quantized* em, so it passed under either model). Both now assert the measured behaviour.

## Root-cause lessons

**The text-metrics attribution is a measured dead end as a fix lever.** Raster once measured words
~8% wide via `FontWidthScale = 1.08` — that is stale: `ModuleInitializer` has since pinned the
harness to 1.0, because a full-corpus measurement found 1.08 gave no ErrorMetric gain and slightly
worse page-count matching. Raster still omits the hhea line gap; PDF applies no width scale and
includes the gap. Three full-suite metric sweeps (raster width 1.08→1.0, PDF width
scaling, PDF gap-free height) moved nothing net — and the bundled fonts' line gaps span 0 (Aptos,
Segoe UI) to 22% of em (Calibri), so no single constant helps. The two raster errors cancel in
ordinary body text, which is why ~97% of scenarios match. Treat these as contributing factors, not
levers.

**Resolved in pass 3 (structural, metrics-independent):** continuous-section-break clamp when the
page already overflowed (resumes/10 — recovered a silently-lost resume copy), keep/widow routing
through `MoveToNextColumn` instead of forcing a page (two_columns), PDF implicit column flow
(two_columns, three_columns), the exact-row pre-advance `CurrentY > ContentTop` guard (blank
leading page), and PDF WordArt reserving the shape's block height (wordart).

**The remaining four are backend-metric knife-edges** (see the corrected scoreboard at the top).
Because Skia/ImageSharp and PdfSharp produce different heights for identical content, a given
document's raster and PDF counts straddle Word and need opposite adjustments; resumes/13 is the
archetype (raster 4, PDF 6, Word 5, page 1 pixel-exact after experiment 15). Closing them
*within the current architecture* requires per-backend metric calibration against Word — a large
change with corpus-wide regression risk against the ~320 currently matching — not another
content-level rule.

**The structural fix is to stop paginating per-backend.** The knife-edges are the symptom of three
independent pagination engines (raster ×2, PDF) measuring and fragmenting the same content
differently. `docs/layout-engine.md` sketched the "if effort is no object" answer: one
backend-independent layout pass over a single canonical metric model, emitting a retained layout
tree that each backend merely paints — so a document paginates once (matching Word once, not three
times), and the knife-edge category dissolves rather than being calibrated away. **That is what
shipped**, and the category did dissolve: all three backends now agree by construction, at Word's
page count on every corpus document.
