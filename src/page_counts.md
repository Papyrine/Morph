# Page-count divergence analysis

Word page counts (Word COM via RenderHelper → `expected_*.png`) versus each backend's verified
output (`skia_result#page_*`, `imagesharp_result#page_*`, `pdf_result#page_*`) across 321 scenario
directories in `src/Tests/Inputs/`. The match is *recorded*, not asserted, by the scenario tests —
these mismatches never fail the suite; closing them improves Word fidelity.

**Current state (pass 4, experiment 15 committed): Skia 313, ImageSharp 313, PDF 316.** Ten
scenarios still differ — business-plans/02/12/13/15, complex_spacing, cover-letters/06,
image_wrap_square, newsletters/06, resumes/13, resumes/16. All ten are now in the
**backend-metric-divergence** class: Skia/ImageSharp and PdfSharp produce slightly different
per-line/per-cell heights, so for a given document the raster and PDF counts straddle Word and
need *opposite* per-backend adjustments — not a content-level rule. Pass 2 ended 307/307/301, so
pass 4 is net +6/+6/+15.

**Protocol per experiment:** implement across all three backends, validate in-container with a
no-regen scenario run isolating exactly which scenarios move, accept only net Word-match gains —
washes and regressions revert, but the measured knowledge is kept.

## Pass 4 experiment ledger (newest first)

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
| Exact bottom-of-page fit, no slack | `widorp.cxx:134,157` | exp 12 (load-bearing, restored) |
| Trailing blanks overhang the wrap boundary | `DomainMapper.cxx:142`, `guess.cxx:99-116`, `portxt.cxx:256` | exp 10 (verified, reverted) |
| Break type comes from the following section's `sectPr` | ECMA-376 §17.6.22 | exp 11 (built, reverted) |

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

## Root-cause lessons

**The text-metrics attribution is a measured dead end as a fix lever.** Raster measures words
~8% wide (test-only `FontWidthScale = 1.08`) and omits the hhea line gap; PDF applies no width
scale and includes the gap. Three full-suite metric sweeps (raster width 1.08→1.0, PDF width
scaling, PDF gap-free height) moved nothing net — and the bundled fonts' line gaps span 0 (Aptos,
Segoe UI) to 22% of em (Calibri), so no single constant helps. The two raster errors cancel in
ordinary body text, which is why ~97% of scenarios match. Treat these as contributing factors, not
levers.

**Resolved in pass 3 (structural, metrics-independent):** continuous-section-break clamp when the
page already overflowed (resumes/10 — recovered a silently-lost resume copy), keep/widow routing
through `MoveToNextColumn` instead of forcing a page (two_columns), PDF implicit column flow
(two_columns, three_columns), the exact-row pre-advance `CurrentY > ContentTop` guard (blank
leading page), and PDF WordArt reserving the shape's block height (wordart).

**The remaining ten are backend-metric knife-edges.** Because Skia/ImageSharp and PdfSharp produce
different heights for identical content, a given document's raster and PDF counts straddle Word and
need opposite adjustments; resumes/13 is the archetype (raster 4, PDF 6, Word 5, page 1 pixel-exact
after experiment 15). Closing them requires per-backend metric calibration against Word — a large
change with corpus-wide regression risk against the ~310 currently matching — not another
content-level rule.
