# Rendering fidelity todo

Deep comparison of every scenario in `src/Tests/Inputs/` (325 scenarios, 548 Word reference pages): `expected_*.png` (Word, 150 DPI, via RenderHelper) versus `skia_result#page_*.verified.png`, `imagesharp_result#page_*.verified.png`, `pdf_result#page_*.verified.png` (PDFium render), and `html_result.verified.png` (headless-browser screenshot of the HTML export).

Each finding: `severity | backends | pages | description`. `all` = skia+imagesharp+pdf. HTML findings ignore pagination/viewport-width reflow by design and only flag content/styling errors. Not reported: anti-aliasing texture, 1-2px subpixel shifts, ImageSharp's softer glyph rasterization. `[known]` = already documented as accepted in that scenario's notes.md.

Findings at the original 2026-07-12 audit: 394 major, 535 medium, 383 minor across 303 scenarios; 21 scenarios fully faithful on all four outputs. Fixed findings are DELETED from this file as they land (this is a temporary working document; durable knowledge moves to `docs/floating-art-pipeline.md`, `docs/fidelity-audit.md` and `docs/word-features.md`).

**Re-audited in full against the current baselines on 2026-07-20** — every open finding was re-judged page-by-page (numeric ink/alignment measurement for drift claims, side-by-side crops for presence/colour/geometry claims; `all`-backend findings required skia, ImageSharp AND PDF clean before retirement; anything ambiguous was kept). **330 findings were retired as stale and 86 rewritten down to their live residual**, leaving **204 major, 383 medium, 268 minor = 855 open** across 258 scenarios (45 scenarios are now fully clean). Conservative carve-out: 29 findings whose text could not be matched byte-exact were left untouched rather than guessed at. The one added MAJOR was the newsletters/06 baseline regression the audit surfaced — **fixed 2026-07-21**, together with that scenario's solid-navy-icon MAJOR, which shared its root cause (see `docs/floating-art-pipeline.md`).

**2026-07-21:** the `nonstandard_main_part_name` scenario was added (first audit of a package whose main part is not `word/document.xml`) and audited on the same terms, contributing 3 major / 4 medium / 1 minor; its minor (`w:pgSz/@code`) and its banner-clip major were both fixed the same day. The banner fix — fixed-layout tables keeping their declared grid, with `w:tblGrid` authoritative over `w:tcW` — moved 43 scenarios for a net **−0.19 AE** and also retired two of `newsletters/09`'s page-count majors. Header space reservation (the body yielding to a header taller than the top margin) then moved 14 scenarios for a net **−0.55 AE with no regressions at all**, the largest single win of the day. A trailing `<w:br/>` now opening its own line box followed (3 scenarios, net −0.02, no regressions). Running total: **201 major, 388 medium, 268 minor = 857 open** across 259 scenarios. The retirements cluster where the systemic work landed: per-glyph `w:spacing` tracking (the "doubled word gap"/"tracking dropped" family), the XPS-decoded height model (most "line pitch ~10% tighter" and "~9% narrower text" claims now measure under 2%), the floating-art passes, and the 2026-07-20 outline/gradient/wp:align/pct-width fixes. Counts below this line are therefore current, not historical.

## Systemic issues (cross-scenario root causes)

These patterns repeat across many scenarios; fixing one of these clears whole families of the per-scenario findings below.

### All raster + PDF backends

1. **Page-number fields (PAGE/NUMPAGES/SECTIONPAGES) evaluated per page** — core fix + section restarts resolved (see `docs/word-features.md`, Page Numbering / Field Codes). **Still open:** (b) business-plans/13 / resumes/13 sequences differ from Word only through the page-count divergence (issue #2); (c) business-plans/12 footer numbers — per-section headers/footers are unmodelled (issue #24). HTML/Markdown keep cached values by design (matrix-documented).
2. **Vertical metrics run ~10% tight and glyph advances ~9-10% narrow vs Word** — resolved via the XPS-decoded height model (see `src/page_counts.md` for the page-count root-cause taxonomy C1-C11 and the metrics history). **Still open:** page-count divergences on long documents remain the dominant residual (business-plans/13/15-class); many per-scenario "drifts up/compresses" findings below predate the regenerated baselines and are unverified — treat as stale until re-checked.
3. **Expanded character spacing (`w:spacing`) mishandled** — ✅ resolved — per-glyph `w:spacing` tracking in all backends (`docs/word-features.md`). The untracked fast path must stay byte-identical; see the tracking commit for the invariant.
4. **Word spaces collapse to zero in some display/heading text (Skia/ImageSharp)** — ✅ resolved; details in `docs/word-features.md` (git history holds the full fix narrative).
5. **Floating/anchored decorative art missing or misplaced** — the largest source of MAJOR findings; ten fix passes landed 2026-07-19 (cell-float hoisting/cell-attached rendering, relativeHeight z-sort, nested-transform affine composition, document-order interleave, a:grpFill resolution, group-frame clipping, pic:spPr geometry crops, walk authority over non-identity nesting). Architecture, authority rules and the attempted-and-reverted decision log now live in `docs/floating-art-pipeline.md` — the history was moved there. **Still open:** (a) STALE, re-rendered 2026-07-19: brochures/07's pictures render at full size in Word's positions on both pages (the interleave/authority passes fixed the size class); (b) RESOLVED 2026-07-19: brochures/03's right circle photo now renders greyscale (−0.015 skia/imagesharp p2) — the a:grayscl WAS declared on the blip (the "not declared" forensics were wrong); the photo is an INLINE pic in a table and `TryParseInlineImageRun` dropped blip effects entirely. Residual: the photo still sits ~20pt high; (c) STALE, measured 2026-07-19: labels/16's sheet top sits within 2.4pt of Word — the walk-authority pass fixed it, the 30px finding predates it; (d) remaining missing freeform/vector shapes: brochures/04/06 (chevrons, balloon art, quote box — both improved 2026-07-19 via the inline-shape passes, residuals remain), business/04/05 (banners, watercolor blobs), cover-letters/06 (top banner's LEFT pink segment missing — the right confetti image renders; the thin segment bars + envelope outline landed 2026-07-20 via the walk noFill-stroke pass, the page-bottom pink band is still missing; re-measured 2026-07-19 — cover-letters/07 measured STALE, it matches Word), letters/03 measured STALE 2026-07-20 — its header gradient banner is a PICTURE and renders identical to Word; letters/11 logo strip landed with the front-solid pass, cards/18/05/06 (fold guides — partially surfaced 2026-07-19 by the dashed-line pass, sub-0.005 deltas; cards/06 p2's teal dividers RESOLVED 2026-07-20 — a zero-WIDTH connector group 0/0-scaled every child position to NaN; degenerate axes now clamp to scale 1, and standalone front line connectors route through the walk — surfacing fold/cut guides across cards/01/02/05/07/11/15 and wedding/02/03 as well), labels/02 RESOLVED 2026-07-20 (walk noFill-stroke pass, #6a; labels/03's tear lines RESOLVED 2026-07-19: dashed + quarter-turn line connectors landed, the sysDot lines render — residual: denser than Word's fine dots, Word likely draws round dot caps at wider spacing), menus/06 (red bars; menus/04's doodle pattern RESOLVED 2026-07-20 via the walk noFill-stroke pass — menus/01's floral art measured STALE 2026-07-19, it renders), resumes/10. Resolved 2026-07-19 via `ParseInlineSingleShapeRun` (standalone inline wsp with solid fill): business-plans/01's accent bar, cover-letters/10's logo, labels/12's 30 flourishes; letters/02's frame resolved via the header z-sort (duotone colour residual → #8); (e) missing/mangled pictures: NONE remaining — brochures/07 (the last of the original list) measured STALE 2026-07-19, its photos render at Word's sizes and positions on both pages. Resolved/reclassified 2026-07-19: newsletters/08/13/14's photos and newsletters/11's hero were front-anchored blip-filled shapes (anchored-blip route + front-of-text image-shape rendering; /11 measured stale); cards/02's blossom measured STALE; business-plans/12's SWOT graphic is a c:chart (documented chart-placeholder limitation). Front-of-text SOLID shapes RENDER as of 2026-07-19 — the corpus-wide experiment ran net −0.46: letters/11's logo + tile strip, brochures/06's olive quote box, newsletters/12's purple dash overlay, menus/02's dark line-burst and resumes/10's accent circles (the last needed the front-shape page-advance + the any-floating-shape empty-paragraph rule) all render now. (newsletters/14's coral DECEMBER banner turned out to be Subtitle-STYLE paragraph shading in a layout table, not a shape — fixed by drawing w:shd in the cell paragraph path, all backends.) FIXED 2026-07-19 from this list: newsletters/03/04 inset photos and cover-letters/09's profile photo were blip-FILLED `wps:wsp` shapes (Word's "fill a shape with a picture") in the INLINE subsystem — both inline paths and the anchored walk now parse `a:blipFill` (see `docs/floating-art-pipeline.md`); brochures/04 p2 improved −0.20 from the same change. menus/01's floral art measured STALE (renders correctly).
6. **Shape geometry defects** — preset polygons, text-box chrome, picture flips, line alpha and connector assembly are resolved (`docs/word-features.md`). (a) outline-only shapes (`a:noFill` + `a:ln`) — ✅ LANDED 2026-07-19 on the third attempt (net −0.02 with the rotation round; letters/05's orange square, menus/07/09 + inline_group_* board frames, labels/09, agendas-minutes/05/06 accents): the guards that made it land are skip-when-txbx (labels/04's chrome double-draw), stroke only faithfully-strokeable geometry (parsed contours or plain rect/ellipse presets — custGeom-without-contours and unbuilt presets like triangle would stroke a bounding box Word doesn't draw), plus the previously-landed document-order interleave (cards/02's z-order), line alpha and walk authority. ✅ FOURTH PASS 2026-07-20 — the WALK's `ParseSolidFillShape` bailed on explicit `<a:noFill/>` before its stroke logic ever ran, so outline-only children of WALK-OWNED groups (non-identity nesting/cell anchors) never rendered; it now falls through to stroke-only emission under the same guards, and the walk plumbs `ExtractLineStyle`'s alpha into `LineAlpha` (labels/04's hexagons are 10%-alpha). Landed: cards/13 white card outlines, labels/02 grey label borders, menus/04 doodle pattern, cards/02 notched ticket/frame outlines (p1+p2), letters/05 p1 orange square, cover-letters/06 segment bars + envelope outline (details per scenario). ✅ FIFTH PASS 2026-07-20 — filled walk shapes now stroke their `a:ln` exactly like ShapeParser's solid branch always has (the asymmetry left walk-owned FILLED shapes borderless): letters/05's teal triangle (a white-filled custGeom whose stroke is the only visible ink) renders on p1, resumes/10's page-edge accent circle now cuts flat at the page edge like Word, cards/02/newsletters/13 ticked closer. Cost: wedding/04's hairline centre divider draws one line-width fatter (same-colour stroke atop a thin filled rect). ✅ SIXTH PASS 2026-07-20 — business/06's LOGO box renders: an INLINE standalone text-carrying wsp claimed by `ParseWordArt` (whose claim has NO warp check), which drew the spPr `a:ln` as a GLYPH stroke; for unwarped (textNoShape) shapes that `a:ln` is now parsed as `BoxLine*` box chrome and stroked in all three backends (the pseudo-bold glyph stroke stops too). Falling through to `ParseTextBox` instead was verified NOT viable — it positions inline drawings at absolute (0,0), not the flow cursor. **Still open in that class:** none known — next candidates come from re-audit; (b) business-plans/02 arrow construction; (c) STALE, re-rendered 2026-07-19: cards/06 draws a single candle group ending at the card edge and business/06's ribbon sits on the right half with one wedge — the duplication class was the dual-parser stray problem, resolved by the authority + clipping passes; (d) ✅ RESOLVED 2026-07-20 — group-child offset (menus/07 / inline_group_crop / inline_group_rotation, −0.10..−0.11 per backend each, wedding/06 −0.0011; ZERO regressions): `<wp:align>` inside `wp:positionH`/`positionV` was entirely unparsed, landing align-positioned anchors at their reference origin. Now folded into the position at parse (`ParsePositioning` gains section page metrics) where the anchor extent is known — group children inherit the group's delta through the ordinary transform math. Gated: cell/txbx-nested anchors skip the fold (cell floats re-base against the CELL box at render); margin-strip references (leftMargin etc.) and paragraph/line vertical alignment don't fold. Corpus scan found only 9 scenarios with foldable aligns; the 5 page-center ones (letters/11, resumes/08, business-plans/12, cards/06, cards/18) measured no change — their extents match the reference box so the fold is zero; (e) stray art Word hides in cards/04-class scenarios is resolved by group-frame clipping, but labels/16-class strays from other subsystems may remain. Rotated preset rects/ellipses also render rotated now (resumes/06's corner strips, business-plans/08's 90°-rotated accent rule — previously drawn axis-aligned).
7. **Text inside dark shapes renders black instead of its white/light run color** — ✅ resolved — colour cascade with contrast-aware automatic colour (`docs/word-features.md`, the colour-cascade note). **Still open:** the HTML export renders the white text but paints neither dark page backgrounds nor dark shape fills behind it (matrix-documented export gap).
8. **Picture effects ignored** — duotone resolved for raster block+floating images (`docs/word-features.md`). **Still open:** the PDF backend applies NO picture effects (no pixel pipeline — PdfSharp only) and the HTML export ships original bytes; group-shape pictures carry no effects; soft-focus/blur (business-plans/02) and warm-tone (newsletters/07) unmodelled; brochures/03's right circle photo renders in colour where Word greyscales it (the greyscale is not declared in the pic XML — mechanism unidentified); letters/02-class duotone pairs resolved 2026-07-19: a:duotone is modelled as a TWO-colour ramp (DuotoneColorHex dark end + DuotoneLightColorHex light end, prstClr black/white handled) and the Skia/ImageSharp block-image overloads no longer drop the duotone colours (newsletters/01's tinted block images were rendering greyscale, −0.026..−0.035/page).
9. **Centered paragraphs inside text boxes/shapes render left-aligned** — docDefaults `w:jc` cascade + math centring + HTML cell alignment resolved (`docs/word-features.md`). **Still open:** (a) centred text can wrap in a narrower measure than Word (6 lines vs 5) — the PCT-FIXED-TABLE subclass RESOLVED 2026-07-20 (cards/01/07/11/15 + wedding/03 placeholders wrap at Word's exact 5-line measure: pct tables of any tblLayout now grow their columns to the pct target, fraction × container — see `TableProperties.PreferredWidthFraction`); remaining narrow measures are OTHER containers (cards/02's ticket-back TEXT BOX ~40px narrow, cards/16's flush-left 6-line variant); (c) cards/02 ticket-back placeholder barely moved.
10. **Table-style conformance gaps** — conditional bold/caps cascade landed, then reverted where it fought autofit. **Still open / re-land prerequisites:** tri-state `w:b` modelling and content-driven autofit must land first; the reverted diff and per-scenario evidence are in git history (search "tri-state").
11. **Numbering defects** — bullet glyph coverage, cross-table restarts and PDF markers resolved (`docs/word-features.md`). **Still open:** none for the raster/PDF backends — SWOT marker colours landed and the labels/16 offset measured stale (see #5c).
12. **TOC rendering broken** — ✅ resolved — tab-stop clamp + Hyperlink-style suppression (`docs/word-features.md`, Tab Stops / TOC rows; the vacuous-fixture lesson is in `docs/floating-art-pipeline.md` decision log #5). **Still open:** page numbers are the cached values (live PAGEREF needs a bookmark→page map); numbers sit ~4pt left of Word (clamp lands at the cell content edge, Word spills into the right cell padding). Investigated 2026-07-19: the clamp input is the layout's maxWidth — letting it spill means plumbing the cell's right padding into paragraph layout, whose pagedLayoutCache is keyed by (paragraph, ContentWidth) and that width key is load-bearing; a padding-aware clamp needs the padding in the cache key too. Deferred as risk-heavy for a 4pt MEDIUM.
13. **Footnotes/endnotes** — reference marks + PDF appendix resolved (`docs/word-features.md`). **Still open:** page-bottom pinning and the separator rule (needs page-level space reservation in layout); footnote text size approximated at 10pt.
14. **Comment markup not rendered** — no balloon, no highlight, no markup-area page shrink (`comments/01`).
15. **Legacy form-field glyphs missing** — ffData parse + per-type Word-print rendering resolved (`docs/word-features.md`, Legacy Form Fields). **Still open:** `content_control_inline` — SDT content controls (w:sdt) still render as block widgets with chrome.
16. **Automatic hyphenation not implemented** — Word's hyphenated breaks don't happen (`hyphenation_auto`, `hyphenation_suppressed` para 3); a word broken mid-word without hyphen in `letters/03` ("Customer S/ervice").
17. **Line-number values** — ✅ resolved; details in `docs/word-features.md` (git history holds the full fix narrative).

### PDF-only

18. **Line numbers** — ✅ resolved together with #17; details in `docs/word-features.md`.
19. **Paragraph borders** — ✅ resolved; details in `docs/word-features.md` (git history holds the full fix narrative).
20. **`a:srcRect` picture crop** — ✅ resolved; details in `docs/word-features.md` (git history holds the full fix narrative).
21. **Picture rotation** — PDF inline/floating image rotation + PDF text-box rotation resolved (`docs/word-features.md`). **Still open:** rotation reserves the un-rotated footprint (documented all-backend limitation); the HTML export applies no picture transforms.
22. **Font substitution** — ✅ resolved; details in `docs/word-features.md` (git history holds the full fix narrative).
23. **Small caps** — ✅ resolved; details in `docs/word-features.md` (git history holds the full fix narrative).

### HTML export

24. **Header/footer content omitted entirely** — ⚠ STALE: re-verified 2026-07-19 — `header`, `footer`, `header_footer` and `header_banner_table` all render their header/footer bands with ink counts comparable to Word (band-level pixel scan; differences are font-weight AA, not missing content). The findings predate the header/footer implementation. `even_odd_headers/*` unverified individually; SDT-wrapped galleries were a real residual fixed under #1(c).
25. **Anchored/floating objects linearized in flow order** — art detaches from its text and stacks at the document top or mid-flow, causing overlaps and empty frames (cards/02/18/19, labels/05/06/08/11, agendas-minutes/16, newsletters/01/02/07/11/12, brochures/01, letters/13, business/03/06, menus/05).
26. **White/light text emitted without its backing shape → invisible white-on-white** (agendas-minutes/02, brochures/03/07/08, cards/19, labels/11/14, menus/03, newsletters/01/08/10, resumes/02, business-plans/08 green-on-green).
27. **Numbering formats lost** — ⚠ STALE (roman/letter part): re-verified 2026-07-18 — agendas-minutes/04/05/06/14 all render I./II./III./IV. + a./b. correctly in the current baselines (`FormatNumber` handles upperRoman/lowerRoman/upperLetter/lowerLetter, style-attached numbering resolves); the "renders as decimal" findings predate a fix. Still open: multi-item restarts at "1." where Word continues, and `business-plans/15`'s TOC formats (see #12).
28. **Inter-paragraph spacing collapsed** — ⚠ STALE: re-verified 2026-07-19 — `letters/01` renders distinct paragraphs with Word-comparable gaps (blank line before the salutation, spacing between body paragraphs; the block sits ~35px lower overall, the #2-family vertical drift), and `empty_paragraphs`' "Text after empty paragraphs." lands within ~13px of Word — blank lines are preserved, not dropped. The findings predate the spacing work (margin collapsing, docDefaults spacing, contextual spacing).
29. **Tab stops collapse to a single space** — ⚠ STALE: re-verified 2026-07-19 — `tab_stops` renders dot leaders, column stops and right-aligned page numbers pixel-close to Word, and `decimal_tabs/01` aligns every decimal point at Word's x (only its line spacing differs — issue #2 family); `bar_tabs` was fixed with #19. The findings predate the tab implementation. Possibly-live remnant: `resumes/14`'s inline dates (unverified); TOC dot-leader issues are tracked under #12.
30. **Section page color scoping wrong** — ⚠ RECLASSIFIED: none of the listed documents carries `w:background` (and most are single-section), so there is no section-colour mechanism to scope — their "page colours" are full-page floating shapes/pictures anchored behind the text, and the stops/wrong-colour symptoms are those shapes being dropped or mispositioned. This is the floating/anchored-art class (systemic #5/#25), not a colour-scoping defect.
31. **CSS styling in AltChunk/HTML-source tables dropped** — row fills, zebra shading, alignment, colours and widths resolved (`docs/word-features.md` has no HTML-import section; the parse lives in `HtmlParser.ParseTable`). **Still open:** cell-level inline formatting is flattened (`cell.TextContent` builds ONE run, so `<b>`/`<span style>` inside a cell lose their formatting); cell padding composes slightly tighter than Word; vertical-align on cells is unmodelled (html_css_alignment's actual demo intent).
### Word-reference (expected_*.png) anomalies worth re-checking rather than "fixing"

32. `newsletters/12` draws an olive stripe Word hides — verify against the DOCX before treating Word as wrong. (The cards/04 half of this anomaly is resolved: the stray tree + bird flock were out-of-frame group children, removed by group-frame clipping — Word was right.)
33. `feature_capture/01`: Skia/ImageSharp render a drop cap where Word's reference shows none (Word appears to ignore the dropCap property here); same scenario's shadow effect renders as a duplicated text copy in ImageSharp.

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
- MEDIUM | html | - | list numbering format lost: roman numerals I.–IV. render as 1.–4. and letter sub-items a./b. render as 1./2.

### agendas-minutes/05

- MINOR | all | p1 | content drifts up ~1.5 line heights by the action-items table
- MEDIUM | html | - | roman-numeral agenda list I.–VI. renders as decimal 1.–6.

### agendas-minutes/06

- MEDIUM | all | p1 | cumulative vertical compression ~3 line heights: ADJOURNMENT section ends ~0.5in higher than Word
- MAJOR | html | - | list numbering broken: I.–VI. renders as 1., 1., 1., 1., 2., 3. (first four top-level items each restart at 1) and a)/b)/c) sub-items render as 1./2./3.

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
- MAJOR | html | - | List numbering wrong: every numbered heading and sub-item renders as "1." (roman I.-VI. and letter a)-c) formats lost, each item restarts at 1)
- MAJOR | html | - | "Attendees:" label text missing — only the name list renders under the title
- MINOR | html | - | Decorative teal stripes at top and bottom of the page are missing

### agendas-minutes/15

- MEDIUM | skia,imagesharp | p1 | Numbered-list continuation lines indented deeper than Word (wrap lines don't return to the hanging-indent column), changing item 4's wrap ("...ribbon, click / an Insert option." vs Word's "...click an / Insert option.")
- MINOR | all | p1 | Body text drifts a few px lower down the page and the action-items table (dark green header bar) sits ~10px lower than Word
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

### align_justified

- CLEAN: html

### align_mixed

- CLEAN: html

### align_right

- CLEAN: html

### bar_tabs

- MINOR | skia,imagesharp,pdf | p1 | text lines drift upward progressively (~5-10px by the last paragraph) versus Word
- MAJOR | html | - | bar-tab vertical separator lines are not rendered at all and the tabbed columns collapse to single spaces ("Column one Column two Column three")

### block_quote

- CLEAN: html

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

- ⚠ PARTIAL | all | p1,p2 | the circle-clipped photos now render as CIRCLES at their positions (pic:spPr ellipse/custGeom crops, systemic #5 ninth pass). Still open: the right photo renders in colour where Word greyscales it (the greyscale isn't declared in the pic XML — #8 effects class) and sits ~20pt high
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
- MAJOR | all | p1 | decorative freeform art still missing on p1: striped bar above the purple panel, and balloon line art in the panel
- MAJOR | pdf | p2 | top-right couple photo rendered ~30% narrower and shifted ~110px right, bleeding to/clipped at the right page edge (left portion of Word's crop lost)
- MEDIUM | all | p2 | right-column reflow: "To replace any of the pictures" paragraph wraps 4 to 5 lines in a narrower block, and the services list ("Passport Expediting"..."Trip Insurance") sits 20-80px lower than Word
- MINOR | all | p1 | "MARGIE'S TRAVEL" title shifted down ~8-10px (in pdf the panel address block is also ~18px low)
- MINOR | skia,imagesharp | p2 | couple photo drawn slightly taller with content shifted ~8px vs Word's crop
- MAJOR | html | - | quote box text "We don't merely book your travel..." and attribution missing from the HTML export
- MAJOR | html | - | balloon line art missing from the HTML export

### brochures/07

- MEDIUM | all | p1,p2 | body text (lorem paragraphs, contact text, right-rail paragraphs) rendered visibly bolder/heavier than Word, with shifted wrap points
- MINOR | all | p1,p2 | text blocks (CONTACT US group, LOREM IPSUM columns, ABOUT US group) uniformly shifted ~10px
- MAJOR | html | - | ABOUT US section's yellow panel background missing, so its white quote block (large " mark + white lorem text) is invisible/absent in the export
- MEDIUM | html | - | body text renders bold where Word shows regular weight

### brochures/08

- MAJOR | all | p1,p2 | navy/blue duotone recolor lost on every photo (skyline, ceiling structure, bottom building band, wavy panel, grid building) — all rendered plain greyscale
- MAJOR | all | p1 | "Contoso Logo" white-framed box and text missing from the orange block at bottom-right
- MEDIUM | all | p1,p2 | thin heading rules missing: under "JOIN OUR TEAM" (p1), under "OUR STORY", under the "MAKE IT YOURS..." title, and the orange rule above the CONTACT US paragraph (p2)
- MINOR | pdf | p2 | numbered client list vertical spacing looser than Word (~50px vs ~38px between items)
- MAJOR | html | - | photos overlap text: bottom building photo covers the address block and the "OUR STORY" / "MAKE IT YOURS" / "CONTACT US" headings
- MAJOR | html | - | "MAKE IT YOURS" body paragraphs invisible (white text without the orange panel background) and numbered list items show bare "1. 2. 3." with no item text
- MAJOR | html | - | "Contoso Logo" framed box missing from the export
- MEDIUM | html | - | photos greyscale (navy duotone lost) in the export

### bullet_list

- CLEAN: html

### business-plans/01

- MAJOR | skia | p1 | missing-glyph tofu boxes rendered after "Contoso, Ltd." and "Casey Jensen" (not present in imagesharp/pdf)
- MEDIUM | all | p1 | vertical spacing collapsed: the contact rail + section columns sit ~1in higher than Word
- MEDIUM | all | p1 | body and contact text rendered bold where Word uses regular weight, shifting wrap points inside paragraphs
- MEDIUM | html | - | body text renders bold where Word shows regular weight

### business-plans/02

- MAJOR | skia,imagesharp | p2,p3,p4,p5,p6,p7 | Page count 7 vs expected 6: extra ~0.5in gap between the header photo and EXECUTIVE SUMMARY on p2 pushes MARKET OPPORTUNITY off the page, every following page shows the prior Word page's sections, and EXIT STRATEGY/MILESTONES & ROADMAP/NEXT STEPS/contact block overflow onto an extra page 7 (PDF paginates correctly at 6).
- MEDIUM | skia,imagesharp | p1 | Cover title word gaps collapsed — "SUNKEN VALLEY FARM BUSINESS PLAN" reads as "SUNKENVALLEYFARM BUSINESSPLAN" (verified in zoom), and the title block sits ~20px lower than Word.
- MINOR | skia,imagesharp | p1 | Footer contact block (rule line, JI-MIN AN, phone/site columns) shifted down ~0.2in as a unit.
- MINOR | pdf | p1,p2,p3,p4,p5,p6 | Uniform small downward drift (~10px) of body content producing ghost doubling of text and table rules.
- MEDIUM | html | - | Hero wheat photo shows the sharp un-blurred original, missing Word's soft-focus treatment.
- MEDIUM | html | - | Financial projection values ($750,000 / $1.2 MILLION / $2.0 MILLION) rendered in italic; Word renders them upright.

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

- MINOR | skia,imagesharp | p1 | title rendered ~15% narrower than Word (condensed glyph widths, x-extent 766 vs 871)
- MINOR | all | p1 | intro paragraph, four section blocks and footer contacts sit 30-50px lower than Word (footer band itself correctly placed)
- MINOR | all | p1 | "PREPARED FOR:/BY:" labels render bold vs Word's regular caps (ink +27-42%)
- MEDIUM | html | - | pale-green footer band starts mid-contact-block (labels and first contact lines sit above/outside it) instead of enclosing the whole PREPARED section, and stops at content width
- MINOR | html | - | title line spacing collapsed so "PROPOSAL" caps touch the "BUSINESS" baseline
- MINOR | html | - | "PREPARED FOR:/BY:" labels bold vs Word regular

### business-plans/08

- MAJOR | pdf | p1 | last contact lines "Seattle, WA 89101" / "Santa Fe, NM 11121" pushed past the page bottom and entirely absent
- MAJOR | skia,imagesharp | p1 | "Seattle, WA 89101" / "Santa Fe, NM 11121" clipped mid-glyph at the page bottom edge (contact-block line spacing inflated ~38% pushes them into the margin)
- MEDIUM | skia,imagesharp | p1 | title "Business proposal" shifted right ~75px and down ~30-55px (Word has it flush at left margin)
- MEDIUM | pdf | p1 | second title line "proposal" indented ~75px right of "Business" (Word has both lines flush left); title also ~55px low
- MEDIUM | all | p2 | body paragraphs render bold vs Word's regular green text, changing intra-paragraph line-break positions
- MINOR | all | p2 | section stack drifts upward, ~80px high by section 5
- MAJOR | html | - | sections 1 (Summary) and 2 (Problem Statement) unreadable: green heading/body text rendered on the green cover background (only the "1."/"2." markers visible)
- MAJOR | html | - | title lines overlap — "proposal" glyphs collide with "Business" and the line is indented right
- MAJOR | html | - | top accent line missing (only a stray dot remains near its start position)
- MEDIUM | html | - | body paragraphs bold vs Word regular
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

- MAJOR | all | p3,p4,p5,p6,p7,p8,p9,p10,p11,p12,p13,p14,p15,p16,p17,p18 | footer page number missing on every content page (Word shows "3".."18" bottom-right; all three backends render nothing there)
- MEDIUM | all | p2 | the thick black rule below the "TABLE OF CONTENTS" heading is missing
- MAJOR | skia,imagesharp | p1 | cover text block pushed ~1in down: colored-arrows logo sits clipped at the bottom page edge and "First Up Consultants" is pushed off-page entirely (missing)
- RECLASSIFIED 2026-07-19: the SWOT "donut-ring graphic" is a c:chart (word/charts/chart1.xml, doughnut) — the documented chart-placeholder limitation (`docs/word-features.md`, Charts), not a #5 art-pipeline gap; no cached preview image ships in the docx to substitute
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

- MAJOR | all | p1 | Cover's full-width light-grey band behind "Small Business Plan" / "SAND + POLISH CONTRACTORS" is missing — title block sits on plain white
- MEDIUM | all | p1 | Cover renders a footer ("SAND + POLISH CONTRACTORS" + page number "2") that Word suppresses on the first page
- MINOR | skia,imagesharp | p1 | Cover title and subtitle sit ~0.3-0.5 in lower than Word although the photo bottom edge matches
- MEDIUM | skia | p5-p23 | Skia drops run-level bold throughout the body: lead-ins "Opportunity:", "Company summary:", "Order fulfillment:", "Pricing:", "SWOT analysis:", "Step 1-4", "The Executive Summary should be written last", "Projected start-up costs" all render regular weight (imagesharp and pdf render them bold)
- MAJOR | all | p16,p17,p18 | Landscape P&L table's JUL column too narrow — values clipped mid-glyph ($22,500 → "$22,5(", $22,850 → "$22,8", $13,850 → "$13,8", $8,000 → "$8,00(", $12,100 → "$12,1(", $1,125 truncated) in skia/imagesharp p17-p18 and pdf p16
- MEDIUM | all | p16,p17 | P&L row label "Marketing/Advertising" does not wrap and collides with the JAN value "$400"
- MEDIUM | html | - | Cover's grey title-band background missing in the HTML export (title/subtitle on plain white); all other content, images, tables, and TOC page numbers are intact

### business-plans/15

- MAJOR | pdf | p4-p10 | Bold side-headings are drawn on the same baseline as adjacent body text, overlapping glyphs — e.g. "Suppliers" over "If you are providing only products or only services…" and "Management" over "How will you transport your products to market?" (p5).
- MAJOR | all | p17 | Body text overruns the bottom margin: the "Taxes Payable…sheet." line prints through the footer text and the final "Payroll Accrual—Salaries and wages…" bullet is clipped at the page bottom edge.
- MEDIUM | all | p11-p18 | All-caps formatting lost in table headers: "START-UP EXPENSES"→"Start-up expenses" (p11), "MONTH 1-12"→"Month 1-12" (p12-p13), "IND. %/JAN.-DEC./ANN. TOT./ANNUAL %"→"Ind. %/Jan.…" (p15), "STARTING MONTH, YEAR—…/BUDGET/AMOUNT OVER BUDGET"→mixed case (p16), "ASSETS/LIABILITIES"→"Assets/Liabilities" (p18).
- MEDIUM | all | p1 | Cover title block indented ~0.9" further right than Word so "Business Plan" wraps onto two lines (3-line title vs Word's 2), pushing the divider rule and "Caneiro Group" down; the Email/Phone/Address block sits ~0.9" lower than Word.
- MEDIUM | all | p2,p3 | Footer bar ("BUSINESS PLAN | APRIL 25, 20XX" + number) is drawn on the TOC pages where Word suppresses the footer entirely.
- MINOR | all | p1 | Title underline rule spans only margin-to-margin instead of extending to the page's left edge as in Word.
- MINOR | all | p3-p19 | Footer line sits ~0.2" lower on the page than Word's footer position on every page.
- MEDIUM | html | - | TOC top-level sections are all numbered "1." ("1. Executive summary", "1. Description of business", "1. Marketing", "1. Appendix") instead of Word's sequential I./II./III./IV.
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

- ✅ 2026-07-20 (+0.0015/backend = new-ink offset penalty; crops match Word): the LOGO box renders its 2pt tx2 border with regular-weight centered text — it is an INLINE standalone text-carrying wsp that `ParseWordArt` claims (no warp check in the claim), and its spPr `a:ln` was being drawn as a GLYPH stroke (faking bold) instead of the box frame; unwarped pseudo-WordArt now carries `BoxLine*` fields, strokes the frame in all three backends, and stops faking the glyph stroke (the ribbon itself matches — right half, single wedge)
- MEDIUM | all | p1 | footer address block ~55-60px higher than Word
- MINOR | skia,imagesharp | p1 | body block (Memo heading + paragraphs) ~25px higher than Word
- MINOR | html | - | LOGO placeholder rendered as bare text without its outlined box

### cards/01

- MEDIUM | all | p1 | small gift icons on the left card halves placed ~35-70px too high vs Word
- MEDIUM | all | p1 | bottom card's green panel and caption sit ~45px higher than Word, shrinking the fold gap between the two card faces
- MINOR | all | p1 | green gift panels offset ~8px up and ~5px right with slightly different size than Word

### cards/02

- MAJOR | all | p1 | scroll-banner picture's three stars render as three orange squares on both tickets; the scroll art itself still diverges
- ✅ PARTIAL 2026-07-20 (+0.0007..+0.0026 = new-ink offset penalty; crops match Word): the orange rounded ticket borders, orange photo-frame inner borders AND the notched outlines now render on p1 (walk explicit-noFill stroke emission — the notch custGeoms flatten). Residual: ticket frame bottom edge sits a few px lower than Word over the code box
- MAJOR | all | p1 | "150220YY" rendered twice per ticket (once at ticket left edge, once below the box) while the white code box is empty and offset right — Word shows a single code centred inside the box
- MEDIUM | pdf | p1,p2 | text rendered bold where Word uses regular weight ("Keep ticket stub" on p1, card-back placeholder paragraph on p2, which also changes its line wraps)
- ✅ 2026-07-20: thin notched border outlines around both ticket backs render matching Word (same walk noFill-stroke pass)
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
- ✅ 2026-07-20: placeholder wraps at Word's 5-line measure (pct fixed-table column growth — see cards/15)
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

- ✅ 2026-07-20 (−0.0078 skia / −0.0008 imagesharp / −0.0071 pdf): all 10 white card outlines render — noFill+white-`a:ln` rects (width from theme `lnRef idx=2`) in translation-remapped nested groups; the walk's explicit-noFill bail was dropping them (see labels/02)
- MEDIUM | all | p1 | Text grid vertically compressed (row pitch ~285px vs Word ~305px) while the white banners/squares stay at Word's positions: text drifts progressively up to ~110px by the bottom row, so titles float above their white banner and the banner instead overlaps the name/address lines. The banners themselves now surface correctly behind the titles (they were mis-ordered under the card art until the document-order group interleave, systemic #5 sixth pass — the old "white placeholder boxes" reading of them was this)
- MEDIUM | html | - | Card outline borders missing, cards blend into the blue background

### cards/15

- MEDIUM | all | p1 | Bottom-half card graphic shifted up ~85px (teal square top at y=739-742 vs Word 825, "Celebrate!" caption follows); top square also up 22px (skia,imagesharp) / 8px (pdf)
- MEDIUM | all | p1 | Small cake icon in the left column displaced ~70px (top) / ~134px (bottom) up
- ✅ 2026-07-20 (+0.0010 thin-ink offset penalty; crops match Word): fold/cut guide lines render — they are STANDALONE front-anchored zero-extent line connectors in the HEADERS, which reached no parse route (the group-branch gate required a blip or solid fill; `IsLineShape` wsps now qualify)
- ✅ 2026-07-20 (+0.0010 = vAlign-centred block baseline shift; crops show Word's exact 5 lines and break words): placeholder wraps at Word's measure — the card table is `tblLayout fixed` + `tblW 5000pct` with tcW 10800 on an 11520 grid, and the pct grow rule was gated on autofit; pct tables of ANY layout now scale their columns to the pct target (fraction × container, `TableProperties.PreferredWidthFraction` — blanket-100% scaling regressed labels/15's 4880pct sheet +0.04 before the fraction landed)
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

- MAJOR | pdf | p2,p4 | corner diagonal-stripe triangle motif missing from all 10 cards on each page (present in Word and both raster backends)
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

- MEDIUM | html | - | education/work entry headings (Jasper University, Bellows College, Lamna Healthcare, Tyler Stein MD, City Hospital) render italic; they are upright in Word
- MINOR | html | - | blank space above each education/work entry heading is collapsed, entries run together noticeably tighter than Word

### complex_document

- CLEAN: html

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
- MEDIUM | html | - | greeting "DEAR JOZI KOS," rendered italic (Word shows upright bold)
- MINOR | html | - | inter-paragraph spacing collapsed — body paragraphs run together with no gap

### cover-letters/02

- MEDIUM | all | p1 | entire content column sits higher than Word — ~0.25" at the "MANASI GOYAL" header, growing to ~0.35" (about 2 line heights) by the signature, from tighter vertical spacing
- MAJOR | html | - | decorative flower graphic at bottom-right missing entirely
- MEDIUM | html | - | cream page background lost — page renders white
- MINOR | html | - | inter-paragraph spacing collapsed — paragraphs run together

### cover-letters/03

- MEDIUM | skia,pdf | p1 | body wraps one word earlier per line — paragraph 1 becomes 7 lines vs Word's 6 (orphan "care."), landing the signature ~2 lines lower
- MINOR | imagesharp | p1 | letter body drifts about half a line lower by the signature
- MAJOR | html | - | right-side lighter sidebar stripe missing entirely (uniform navy background)
- MINOR | html | - | inter-paragraph spacing collapsed — paragraphs run together

### cover-letters/04

- MINOR | imagesharp | p1 | paragraph 2 pulls "at" up to line 1 ("…as a Bookkeeper at") where Word breaks before it, leaving continuation lines starting with a stray leading space
- MINOR | html | - | inter-paragraph spacing collapsed — date, address, greeting and paragraphs run together
- CLEAN: pdf

### cover-letters/05

- MEDIUM | pdf | p1,p2,p3 | body paragraph spacing wider than Word — right-column letter drifts progressively down, "Sincerely,/Yuuri Tanaka" ends ~2 lines lower
- MAJOR | html | - | corner decoration shapes lose their per-page theme colors — page-1 (teal/green/yellow) and page-2 (teal/magenta/orange) clusters all render in page-3's grey/black palette with only faint colored outlines
- MAJOR | html | - | grey diagonal decoration shapes overlap the second section's address text ("123 Elm Avenue / City, State 98052")
- MINOR | html | - | inter-paragraph spacing collapsed — paragraphs run together

### cover-letters/06

- ✅ PARTIAL 2026-07-20 (−0.0030..−0.0042 walk noFill-stroke pass, then the grouped-gradient pass): the thin three-segment bars render at BOTH top and bottom, the envelope icon renders as its outline glyph, AND the page-bottom pink band + the banner's gradient wash panel now render (they are a:gradFill GROUP children — neither grouped parse path read gradients). Still missing: the location-pin/phone glyphs
- MINOR | all | p1 | letter body drifts ~1 line lower than Word by the signature

### cover-letters/07

- MINOR | imagesharp | p1 | First paragraph wraps at different words than Word (line 1 ends "…Manager position", next line starts with a stray leading space)
- MINOR | all | p1 | Letter body drifts upward slightly with tighter paragraph spacing, ending ~0.7 line higher at "Victoria Burke"
- MINOR | html | - | Inter-paragraph spacing collapsed — body paragraphs run together with no blank space between them

### cover-letters/08

- MEDIUM | all | p1 | "Enclosure" renders upright instead of italic
- MEDIUM | skia,imagesharp | p1 | Accumulated tighter line/paragraph spacing makes the signature block ("Angelica Astrom / December 13, 20XX / Enclosure") end ~1.5 lines higher than Word
- MINOR | pdf | p1 | Signature block ends ~0.8 line higher than Word
- MINOR | skia,imagesharp | p1 | closing paragraph's wrapped line starts with a leading space (" your review,")
- MINOR | html | - | Blank-line spacing before/inside the signature block collapsed ("Sincerely," sits tight against the paragraph and the name)

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
- MINOR | html | - | Blank-line spacing around "Sincerely,"/signature collapsed

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
- MINOR | html | - | Paragraph spacing collapsed (greeting, paragraphs and signature run tight)

### cover-letters/15

- MINOR | skia,imagesharp | p1 | Letter body drifts up ~0.5 line by the "Chanchal Sharma / January 13, 20XX" block
- MINOR | html | - | Paragraph spacing collapsed (section headings and paragraphs run tight)

### cover-letters/16

- MEDIUM | all | p1 | body text drifts progressively lower than Word; "Sincerely,/Donna Robbins" block sits ~34px (≈1 line height) lower
- MEDIUM | imagesharp | p1 | paragraph 2 wraps at different points than Word (line 5 ends "QuickBooks, and" and line 6 ends "of a diverse", pulling "and"/"diverse" up a line)
- MINOR | all | p1 | leading space at start of wrapped lines not trimmed — lines "in my ability to provide…" (para 1) and "women and minority owned…" (para 3) are indented one space width off the left margin
- CLEAN: html

### custom_margins

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

### embedded_font

- CLEAN: html

### empty_paragraphs

- MEDIUM | html | - | empty paragraphs dropped entirely — the two sentences render back-to-back with no blank gap (Word shows ~3 blank lines between them)

### even_odd_headers/01

- MINOR | html | - | ODD HEADER / EVEN HEADER text omitted from HTML export (only the two body lines present)
- CLEAN: skia, imagesharp, pdf

### even_odd_headers/02

- MEDIUM | all | p1,p2,p3,p4 | footer text (ODD FOOTER / EVEN FOOTER) placed ~46px (≈0.3in, ~2 line heights) lower than Word on every page; header and body positions match
- MINOR | html | - | header and footer text omitted from HTML export (only the four body-content lines present)

### explicit_break_blank_page

- CLEAN: html

### feature_capture/01

- MAJOR | skia,imagesharp | p1 | giant 3-line drop-cap "D" rendered where Word shows no drop cap ("Drop cap paragraph" reads as one normal line in Word), pushing the paragraph text and table ~0.5in lower
- MAJOR | imagesharp | p1 | "ALL FEATURES" drawn twice — yellow-highlighted bold copy plus an offset gray duplicate below-right (shadow effect rendered as a second text copy); Word shows a single plain small-caps line
- MEDIUM | skia | p1 | "ALL FEATURES" drawn with a yellow highlight/glow and heavier bold-looking glyphs; Word renders plain small caps with no highlight
- MEDIUM | all | p1 | rotated table header cell not wrapped: single vertical "Header" line instead of Word's two stacked vertical lines "Hea/der", making the header row ~65% taller
- MINOR | html | - | "All features" paragraph left-aligned instead of right-aligned
- MINOR | html | - | rotated header cell rendered as horizontal bold centered text (rotation lost)

### field_codes_simple/01

- MEDIUM | html | - | HTML (and Markdown) export still emits the cached "Page 1 of 3" instead of "Page 1 of 1" — the exporters keep the cached value by design (no pagination) [systemic #1 residual (d)]; raster/PDF now render "of 1" correctly

### first_line_indent

- CLEAN: html

### font_families

- CLEAN: html

### font_sizes

- MINOR | imagesharp | p1 | line spacing slightly tight — cumulative upward drift down the size list, "36pt text" baseline ends ~16px higher than Word
- CLEAN: html

### footer

- MEDIUM | all | p1 | footer "Document Footer - Confidential" is rendered ~45px (0.3") lower than Word, nearly a line height closer to the page bottom edge
- MAJOR | html | - | footer text "Document Footer - Confidential" is missing entirely from the HTML export

### form_checkboxes


### form_dropdowns

- CLEAN: html

### form_text_fields

- CLEAN: html

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

### headings

- CLEAN: html

### html_basic_formatting

- ✅ FIXED 2026-07-21 (spacing) | all | p1 | Paragraph spacing compressed vs Word (~44px line pitch vs ~57px at 150dpi) — HTML `<p>` spacing-after raised 8pt→14pt (matches Word's AltChunk import pitch); host pitch now 58px. Net −0.0034 skia AE / +0.0149 SSIM.
- CLEAN: html

### html_complex

- MEDIUM | all | p1 | Table interior cell gridlines missing (only the outer frame is drawn despite border=1 with border-collapse)
- ✅ FIXED 2026-07-21 (block CSS) | all | p1,p2 | All h2 section headings lose CSS color #4472C4 — headings now pass their inline style through `ParseSpanStyle`; render #4472C4. Contributes to html_complex p1 −0.005 AE / +0.010 SSIM.
- ✅ FIXED 2026-07-21 (image px→pt) | all | p1 | Gradient image drawn 312x234px instead of Word's 234x175 — HTML `<img>` width/height are CSS px, now ×0.75 to points (`ParseDimensionAttribute`). Image sizes now match Word; html_images net −0.0281 skia AE / +0.0268 SSIM.
- MEDIUM | all | p1,p2 | "Visit our website for more information." paragraph spills to p2 top — the page break lands one element off Word's. BLOCKED by the intro-wrap root cause below (a narrow-measure issue); not cleanly fixable in isolation.
- PARTIAL 2026-07-21 (block CSS) | all | p2 | Info/Warning/Error styled boxes: background fills (#E7F3FF/#FFF3CD/#F8D7DA) NOW render (div background → paragraph shading). STILL OPEN: colored borders, and the box padding — the fills are thin full-width bands, not padded bordered boxes, and land offset on p2 from the reflow above (p2 AE +0.019).
- MEDIUM | all | p1 | **Intro paragraph wraps 3 lines vs Word's 2 — ATTEMPTED 2026-07-21, REVERTED (net regression).** TWO causes, not the sup/sub: (1) `HtmlParser` did NOT collapse HTML whitespace — literal source newlines in the `<p>` became hard breaks (the intro source has newlines after "and" and "have", exactly where Morph broke). (2) Morph's HTML body text measures ~6-9% NARROWER than Word (same font/size — first line ink height matches — so it's font metrics + sup/sub at 0.7×), so it UNDER-wraps. Fixing (1) alone (`CollapseWhitespace` on text nodes in `ParseInlineNodes`, char-by-char run→single-space, `<pre>` unaffected) is objectively correct HTML behaviour BUT over-corrects the intro to 1 line (Word's 2) because (2) then dominates, and REGRESSES the metric: html_complex p1 +0.068 AE / −0.021 SSIM, p2 +0.011, html_css_margin_padding +0.011 (only 3 scenarios changed; the newline-breaks had been *accidentally compensating* for the narrow measure). It also only SHIFTS the reflow (p2 then loses the "5. Styled Boxes" heading to p1) instead of fixing it. To truly land: fix the whitespace collapse AND match Word's text width (a corpus-wide font-metric issue — same class as the `header_footer`/`resumes` "wraps 3 vs 2 lines" findings), then the page break seats correctly.
- MAJOR | html | - | h2 headings rendered black instead of #4472C4
- MAJOR | html | - | Table styling lost in export: no interior gridlines, auto width instead of 100%
- MAJOR | html | - | Info/Warning/Error box backgrounds and borders missing (text colors kept)

### html_css_alignment

- MEDIUM | all | p1 | Table height:100px ignored (row 33px tall vs Word's 61px)
- MEDIUM | all | p1 | Interior column borders missing despite border=1 (outer frame only; Word shows all three cells ruled)
- MINOR | all | p1 | Justified paragraph breaks after "entire line" instead of Word's "fill the" (same 2-line count, different break word)
- ✅ FIXED 2026-07-21 (spacing) | all | p1 | Paragraph spacing compressed — `<p>` spacing-after 8pt→14pt. Net −0.0027 skia AE / +0.0165 SSIM.
- MEDIUM | html | - | Table interior cell borders and width:100% lost in export (single content-width box)

### html_css_borders

- MAJOR | all | p1 | All seven CSS paragraph borders missing (1px solid black, 2px red, 3px dashed blue, 2px dotted green, 4px double purple, top-red/bottom-blue, 5px orange left bar) — plain unboxed text lines, and with the borders/padding gone the stack compresses ~2in upward
- MEDIUM | all | p1 | Table per-cell border styling lost: one uniform thin box, no 2px thick border on "Cell with thick border" vs 1px gray on "Cell with thin border", and no divider line between the two cells
- MAJOR | html | - | Same seven paragraph borders missing in the HTML export
- MEDIUM | html | - | Table cell border weight/color distinction and inner divider missing in the HTML export

> **ATTEMPT 2026-07-21 — CSS box borders + padding. REVERTED (net regression). Applies to html_css_borders, html_css_margin_padding, html_complex, html_css_colors.**
> The model already has everything needed: `ParagraphProperties.Borders` (CellBorders), the four
> `Border{Top,Bottom,Left,Right}SpacePoints` (which position the border box *outside* the text), and
> `ParseCssBorderShorthand` already parses `1px solid #rrggbb`. Implemented: `ParseInlineStyle` reads
> `border`/`padding`/`margin` (new `FirstCssLengthPoints` converts CSS px→pt at 0.75); `CreateParagraph`
> maps border→`Borders`, padding→all four border-spaces AND Left/RightIndent (so the box edge lands at
> the content margin per the CSS box model rather than pushing outward — border-space alone puts the
> border *outside* the text, which is the DOCX w:pBdr model, not CSS), margin→spacing-after;
> `ParseContainer` propagates the whole box to child paragraphs. Renderers (all three): the shading
> band was drawn tight to the text *before* the top-space reservation, so it had to move after
> `paragraphStartY` and expand by the border-spaces to fill the padded box; the space reservation was
> broadened from "has border" to "has border OR border-space > 0" so padding-without-border still
> reserves vertical room.
>
> **Result: borders and padded boxes DO render** (crops confirmed — the Info/Warning/Error boxes and
> the #CCE5FF padded paragraph match Word's appearance closely), but the metric regressed everywhere:
> html_complex p2 +0.053 AE, html_css_margin_padding +0.034, html_css_borders +0.021 AE / **−0.048
> SSIM**, html_css_colors +0.010 (its div carries `padding:10px`). Three causes:
> 1. **Border styles all draw solid.** `ParseCssBorderShorthand` discards the style token, and — more
>    importantly — the paragraph-border renderers ignore `BorderEdge.Style` entirely (Skia's
>    `CreatePaint` sets only colour/width/Stroke, no dash effect). Word draws dashed/dotted/double.
> 2. **Per-edge borders unparsed** — `border-top`/`-bottom`/`-left` produce nothing, so
>    "Top red, bottom blue borders" and "Left border only" stay blank.
> 3. **Cumulative vertical drift** — the padded box heights don't match Word's, so every border below
>    the first progressively misaligns, which is what dominates the AE.
>
> Also note the blast radius: broadening the reservation gate shifted two DOCX scenarios' PDF output
> (brochures/06, newsletters/14 — paragraphs with border-space but no top border). Any retry must
> keep that gate keyed off a border, or accept and re-judge those.
>
> **To land:** map the CSS style token to `BorderEdge.Style` AND teach the three paragraph-border
> renderers to stroke dashed/dotted/double; parse the per-edge `border-*` longhands; then tune the
> padding so box heights match Word before re-judging.

### html_css_colors

- ✅ FIXED 2026-07-21 (block CSS) | all | p1 | Background fills missing: #FFFFCC and #E0E0E0 (div) bands now render full content-width (block/div background-color → paragraph shading).
- ✅ FIXED 2026-07-21 (named colors) | all | p1 | "Light gray bg, dark blue text" rendered black — `namedColors` expanded 10→147 (full CSS L4), so darkblue/lightgray resolve. html_css_colors net −0.003 AE.
- ✅ FIXED 2026-07-21 (spacing) | all | p1 | Paragraph spacing compressed — `<p>` spacing-after 8pt→14pt; text lines now align with Word. Net −0.0032 skia AE / +0.0104 SSIM.
- MAJOR | html | - | Yellow and div gray backgrounds missing in HTML export
- MAJOR | html | - | darkblue text color rendered black in HTML export

> **✅ LANDED 2026-07-21 (two commits). The whole HTML-AltChunk block-CSS cluster.**
> Root cause was that `HtmlParser` block elements (`<p>`, `<div>`, `<h1..6>`) only honoured a
> *subset* of their inline CSS — `ParseInlineStyle` read alignment/color/indent/line-height while
> the full character path (`ParseSpanStyle`→`ApplyStyleToRunProps`) only fired for `<span>`/`<font>`.
> Fixed in order:
> 1. **Vertical fidelity** (commit "Match Word's HTML paragraph spacing and image sizing"): `<p>`
>    spacing-after 8pt→14pt (Word's AltChunk pitch ~57px at 150 DPI); HTML `<img>` px→pt ×0.75.
> 2. **Block CSS** (this commit): `CreateParagraph` now runs `ParseSpanStyle(element, …)` so a block
>    element's font-size / font-family / weight / style / decoration / color all apply; headings
>    pass their `ParseInlineStyle`; `background-color` → full-width `ParagraphProperties.BackgroundColorHex`
>    (renderers already paint it — Skia TextRenderer.cs:235, ImageSharp:234, PDF PdfTextEngine.cs:329);
>    `ParseContainer` pushes a `<div>`'s own background onto its child paragraphs; `namedColors`
>    expanded 10→147 (full CSS L4 set; darkblue/lightgray/teal/… — `transparent` omitted = no fill);
>    `FirstFontFamily` splits the comma-separated CSS font-family list and strips quotes (the raw
>    `'Times New Roman', serif` had crashed `FontResolver` with a not-found throw).
>
> Net across the four scenarios: html_css_margin_padding −0.027 AE, html_inline_styles −0.021,
> html_css_colors −0.003, html_complex p1 −0.005/+0.010.
> **Residuals (not closed):** (a) full-width shading bands have no padding/border, so they're
> structurally thinner than Word's padded/bordered boxes — drops SSIM slightly and leaves the
> "colored borders" / margin-padding findings open; (b) html_complex p2 AE regressed +0.019 — the
> box backgrounds are correct but land offset because "Visit our website" still spills to p2 (the
> intro-paragraph 3-vs-2-line wrap keeps p1 just over the boundary). Fixing that wrap seats p1 and
> resolves the offset.

### html_css_margin_padding

- MEDIUM | all | p1 | margin-left:50px and 100px indents ignored — both paragraphs sit flush at the left margin (Word shows the staircase)
- PARTIAL 2026-07-21 (block CSS) | all | p1 | Backgrounds now render: #EEE band, #DDD padded-div band, and #CCE5FF fill all present (net −0.027 AE). STILL OPEN: the #0066CC border box, and the box padding (fills are full-width bands, no padding/border) — plus the margin-left staircase is still unindented.
- MEDIUM | all | p1 | 20px div padding and 30px vertical margins collapsed — "Content inside padded div" not inset and "Paragraph with extra vertical margins" sits tight against its neighbors
- MAJOR | html | - | Same three backgrounds and the blue border missing in HTML export
- MEDIUM | html | - | 50px/100px left-margin indents lost in HTML export

### html_font_tag

- ✅ FIXED 2026-07-21 (spacing) | all | p1 | Paragraph spacing compressed — `<p>` spacing-after 8pt→14pt. Net −0.0032 skia AE / +0.0120 SSIM (font sizes 1-7, colors, and Arial/Times/Courier/Georgia faces all faithful).
- CLEAN: html

### html_headings

- ✅ FIXED 2026-07-21 (spacing) | all | p1 | Heading/paragraph spacing compressed — `<p>` spacing-after 8pt→14pt. Net −0.0026 skia AE / +0.0099 SSIM (heading sizes/weights faithful).
- MEDIUM | html | - | Heading 4 and Heading 6 rendered italic in the HTML export (upright bold in Word and all raster/PDF outputs)

### html_images

- MEDIUM | all | p1 | All four embedded images rendered ~33% larger than Word (px dimensions treated as pt instead of 0.75pt), pushing each subsequent caption/image progressively further down the page
- CLEAN: html

### html_inline_styles

- ✅ FIXED 2026-07-21 (block CSS) | all | p1 | Yellow and light-red background bands now render full-width (block background-color → paragraph shading). Part of html_inline_styles net −0.021 AE.
- MAJOR | html | - | Same yellow and light-red backgrounds missing in HTML export
- ✅ FIXED 2026-07-21 (block CSS) | all | p1 | CSS font sizes ignored — block `<p>` now runs through `ParseSpanStyle`; 18pt and 8pt render at their sizes.
- MEDIUM | html | - | Same 18pt/8pt font sizes ignored in HTML export
- ✅ FIXED 2026-07-21 (block CSS) | all | p1 | CSS font families ignored — block `<p>` font-family now applies via `ParseSpanStyle`; `FirstFontFamily` splits the CSS fallback list ('Times New Roman', serif) so Times/Courier resolve (and no longer crash the font loader).
- MEDIUM | html | - | Same Times New Roman/Courier New font families dropped in HTML export
- ✅ FIXED 2026-07-21 (block CSS) | all | p1 | CSS bold (font-weight) and italic (font-style) ignored — both now apply on block `<p>` via `ParseSpanStyle`.
- MEDIUM | html | - | Same bold/italic dropped in HTML export
- ✅ FIXED 2026-07-21 (block CSS) | all | p1 | text-decoration ignored — underline and line-through now apply on block `<p>` via `ParseSpanStyle`.
- MEDIUM | html | - | Same underline/strikethrough dropped in HTML export
- ✅ FIXED 2026-07-21 (spacing) | all | p1 | Paragraph spacing ~20% tighter than Word — `<p>` spacing-after 8pt→14pt. Net −0.0008 skia AE / +0.0069 SSIM (residual from the still-dropped block font-size on the 18pt/8pt lines).

### html_links

- ✅ FIXED 2026-07-21 (spacing) | all | p1 | Paragraph spacing tighter than Word — `<p>` spacing-after 8pt→14pt. Net −0.0018 skia AE / +0.0083 SSIM (link styling already correct).
- CLEAN: html

### html_lists

- MEDIUM | all | p1 | List items render in the serif default font instead of Word's sans-serif (Aptos-style) list font, and at a visibly smaller size
- MEDIUM | all | p1 | Spacing around lists missing: no blank gap after "Unordered list:"/before "Ordered list:" (Word has clear gaps), item leading slightly looser, whole block ends higher
- MINOR | all | p1 | Bullet glyph noticeably smaller/lighter than Word's large Symbol-font solid bullet
- MEDIUM | html | - | List items in serif default font vs Word's sans-serif list font

### html_nested_lists

- MAJOR | all | p1 | Third-level bullets (Item 1.2.1 / 1.2.2) drawn as hollow circles instead of Word's filled squares
- MEDIUM | all | p1 | List text renders in serif default font instead of Word's sans-serif list font
- MEDIUM | all | p1 | Blank gaps before "Nested ordered lists:" and "Mixed nested lists:" headings missing (renders run sections together at uniform item spacing)
- MEDIUM | html | - | List items in serif default font vs Word's sans-serif list font

### html_paragraphs

- MEDIUM | all | p1 | Empty paragraph rendered as a full blank line before "Paragraph after an empty paragraph." — Word collapses it to a normal single paragraph gap
- MINOR | skia,imagesharp | p1 | Leading spaces not trimmed: last paragraph "Paragraph with leading spaces (may be trimmed)." starts indented ~2 spaces, Word (and PDF) render it flush left
- MINOR | all | p1 | Paragraph spacing slightly tighter than Word overall (block ends ~1 line higher)
- CLEAN: html

### html_table

- MEDIUM | all | p1 | Thin outer border drawn around the table although Word renders this table completely borderless
- MEDIUM | all | p1 | Table far more compact than Word: row heights roughly half, near-zero cell padding (adjacent cells' text almost touching, e.g. "Row 2, Cell 1Row 2, Cell 2"), narrower columns
- MEDIUM | all | p1 | Cell text in serif default font instead of Word's sans-serif cell font
- MEDIUM | html | - | Outer table border drawn though Word shows the table borderless
- MEDIUM | html | - | Cell text in serif default font vs Word's sans-serif

### html_table_cell_margin_css

- MAJOR | all | p1 | 2x2 table grid disintegrates: each cell drawn as a separate box offset by its CSS margin with fragmented partial borders (floating "Left margin 20px" box top-right, bare over/underline on "Top/bottom margin", bracket-shaped border around "Default") instead of Word's coherent complete grid
- MEDIUM | skia | p1 | "Margin 10px, Padding 5px" cell text wraps to two lines (single line in Word, ImageSharp and PDF)
- MEDIUM | all | p1 | Cell text in serif default font instead of Word's sans-serif cell font
- MEDIUM | html | - | Inner cell gridlines missing — only a single outer border drawn where Word shows a full light-gray grid around every cell, and cell padding much smaller
- MEDIUM | html | - | Cell text in serif default font vs Word's sans-serif

### html_table_cell_padding_css

- MAJOR | all | p1 | interior cell borders missing — only a single black outer rectangle is drawn, while Word renders a light-gray border around every cell
- MEDIUM | all | p1 | text rendered in serif Times instead of Word's sans-serif
- MEDIUM | all | p1 | CSS cell padding under-applied: table ~20% narrower and rows ~25% shorter than Word, right-column text ends flush against the table border
- MEDIUM | skia | p1 | "20px all sides" wraps to two lines (single line in Word, ImageSharp and PDF)
- MAJOR | html | - | interior cell borders missing (outer box only)
- MEDIUM | html | - | cell padding CSS entirely dropped — all rows collapse to tight single-text-line height
- MEDIUM | html | - | serif Times instead of Word's sans-serif

### html_table_cellpadding

- MAJOR | all | p1 | interior cell borders missing — single black outer rectangle only vs Word's per-cell light-gray borders
- MEDIUM | all | p1 | text rendered in serif Times instead of Word's sans-serif
- MEDIUM | skia | p1 | "Cell with 15px padding" wraps to two lines (single line in Word, ImageSharp and PDF), making row 1 taller
- MAJOR | html | - | interior cell borders missing (outer box only)
- MEDIUM | html | - | cellpadding=15 dropped — rows collapse to tight text height
- MEDIUM | html | - | serif Times instead of Word's sans-serif

### html_table_styled

- MAJOR | all | p1 | cell gridlines/borders missing — only a thin outer rectangle is drawn
- MEDIUM | all | p1 | fixed-width table ignores the 100px/200px column widths — it stretches to full text width (976px vs Word's 622px) with columns roughly 200/420/355px instead of Word's 160/315/147px
- MEDIUM | all | p1 | text rendered in serif Times instead of Word's sans-serif
- MAJOR | html | - | cell borders missing (outer box only)
- MEDIUM | html | - | table width styling ignored — the width:100% styled table renders content-width (206px of the 624px content box) and the 100px/200px fixed columns are exported as width:100pt/200pt (~33% too wide)
- MEDIUM | html | - | serif Times instead of Word's sans-serif

### hyperlinks

- MINOR | all | p1 | whole text block runs ~5-8% narrower than Word (lines end short of Word's line ends; link color/underline and line breaks correct)
- CLEAN: html

### hyphenation_auto

- MEDIUM | all | p1 | automatic hyphenation not applied: paragraph 1 lacks Word's "telecommunica-/tion" end-of-line break and paragraph 2 lacks Word's "hy-/phens" break, so both paragraphs break at different words
- CLEAN: html

### hyphenation_nonbreaking

- CLEAN: html

### hyphenation_soft

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

- MAJOR | imagesharp,pdf | p1 | red and green star icons both rendered blue — all three icons drawn identical to the first (SVG recolor/variant lost); Word and Skia show blue/red/green
- CLEAN: skia, html

### image_cropping/01

- MAJOR | html | - | same crop ignored in HTML export — image shown uncropped (small "Sample", full gradient) instead of the centre-50% crop

### image_rotation/01

- MAJOR | skia,imagesharp | p1 | 45°-rotated image not clipped to its layout box: full diamond drawn (386px tall vs Word's 257px clipped band), top corner overlaps and partially obscures the "Below is a sample image:" text, bottom corner extends ~44px lower than Word; rotation center also ~20px higher
- MAJOR | html | - | rotation missing in HTML export — image shown as plain unrotated rectangle

### image_wrap_square

- MAJOR | all | - | page count 3 vs expected 2 — the two-column "Columns" section body text does not fit at the bottom of page 2 and spills onto an extra page 3 (Word fits it on page 2)
- MAJOR | pdf | p1 | square-wrapped globe ("Web Access Symbol") image missing entirely — paragraph text wraps around an empty reserved space
- MINOR | imagesharp | p1 | Links paragraph wraps differently: "downloadable" pulled up to the first line ("...or even downloadable / documents...") vs Word's break after "even"
- MINOR | all | p1,p2 | cumulative vertical drift of blocks (~10-20px up on p1, down on p2, pie chart slightly offset) with structure intact
- MEDIUM | html | - | Simple Tables data rows styled like headers — all body cells bold and centered (Word shows left-aligned regular text; the complex table below is correct)
- MINOR | html | - | last line of the "Some images, such as charts or graphs..." paragraph rendered centered ("link on the image.") instead of left-aligned

### inline_group_crop

- ✅ 2026-07-20 (−0.112 skia / −0.100 imagesharp / −0.103 pdf): board, wood frame and border now centre at Word's position with both wood strips and all corner notches visible — the wp:align fold landed (H:margin:center at 568pt vs the 468pt margin box)

### inline_group_rotation

- ✅ LARGELY RESOLVED 2026-07-20 (−0.112 skia / −0.100 imagesharp / −0.103 pdf, wp:align fold): the board composition centres at Word's position; residual differences remain in the rotated nested pieces (unvetted crop-level)
- MEDIUM | all | p1 | decorative double border frame renders since 2026-07-19 (outline-only emission); residual: ornamental corner details and the red accent line's exact geometry differ from Word
- MINOR | all | p1 | "Menu" lacks its white+mint outlined glyph style (the text colour itself is correct now)

### inline_image

- CLEAN: html

### inline_shape_arrows

- MEDIUM | skia,imagesharp | p1 | colored-arrow row drawn ~27px left and ~25px above Word's position (arrow sizes themselves correct, gap to "Arrow variants:" label visibly too small)
- MEDIUM | pdf | p1 | all four colored arrows shifted up-left ~20px
- MINOR | all | p1 | "Thinner stroke" arrow and its label paragraph sit ~10-17px higher than Word
- CLEAN: html

### italic_text

- CLEAN: html

### labels/01

- MEDIUM | all | p1 | line spacing inside every label much looser than Word — the 4-line Name/address block spreads to fill the entire cell height, ending ~20px lower and visibly misaligning text with the icons
- CLEAN: html

### labels/02

- ✅ 2026-07-20 (metric +0.0007..+0.0039 = new-ink offset penalty; crops match Word): all 8 label cell borders render — they are `a:noFill` + 0.25pt bg1-lumMod75 `a:ln` RECTS inside non-identity (translation-remapped) nested groups, so the WALK owns them and its explicit-noFill bail dropped them; the walk now falls through to stroke-only emission (same guards as `TryBuildOutlineOnlyShape`)
- MINOR | skia,imagesharp | p1 | TO:/FROM: text block sits ~8px higher than Word

### labels/03

- MEDIUM | all | p1 | ticket text leading ~1.5x looser than Word — "TICKET" sits ~15px lower and the 3-line block spreads down the ticket
- MEDIUM | html | - | dotted tear-line rules missing from all tickets

### labels/04

- ✅ PARTIAL 2026-07-20 (−0.0018 skia / +0.0024 imagesharp / −0.0025 pdf): the hexagon outlines now render at Word's positions AND at their declared 10% line alpha — the shapes are noFill + `a:ln` tx1 with `a:alpha val="10000"`, and the walk was both dropping them (explicit-noFill bail) and discarding line alpha (`ExtractLineStyle`'s alpha is now plumbed into the walk's `LineAlpha`). Still open: the small light-blue hexagon ACCENT per label doesn't render — it is a GRADIENT-filled hexagon preset with no built contours; rendering it faithfully needs a `PresetShapeGeometry` hexagon builder (without one the gradient guard drops it — the unguarded emission painted saturated bounding boxes, worse than absent), plus Word's soft look likely needs gradient-stop alpha, which `GradientFill` doesn't model
- ✅ 2026-07-20 (metric +0.0017 is an SSIM texture artefact — pixel measurement shows ZERO pixels moved away from Word, the bars track Word's ramp within a few units/channel): accent bars render their 90° cyan→deeper-blue gradient — a:gradFill was read only by ShapeParser's STANDALONE branch, which no corpus gradient uses; the grouped branch and the walk now extract it (fillRef was flattening the bars). Guarded to faithful geometry (contours or rect/ellipse presets) — the unguarded first attempt drew the hexagon accents as saturated gradient BOUNDING BOXES (+0.004)
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

### left_indent

- CLEAN: html

### letters/01

- MAJOR | all | p1 | decorative header and footer shape bands rendered in wrong colors (vivid purple-blue/lime-green/white instead of Word's slate-blue/charcoal/lavender palette)
- MEDIUM | all | p1 | Recipient address block starts ~74px lower than Word, pushing the salutation, body and signature down (~1 line by the signature)
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

- MAJOR | all | p1 | footer contact strip pushed down into the navy footer band — "Portland, OR 76543" rendered overlapping the band, phone/email baseline clipped by it
- MEDIUM | all | p1 | body content drifts progressively lower (~2-3 line heights by the signature) though wrapping matches
- MAJOR | html | - | recipient address block and date rendered overlapping the navy header banner (first lines sit on top of the band)

### letters/05

- ✅ 2026-07-19 (−0.0014 per backend p2/p3) + 2026-07-20 (−0.0012..−0.0013 p1): the orange square outline renders on ALL pages — p2/p3 via ShapeParser's outline-only emission, p1 via the walk's (p1's drawing carries a non-identity nested sibling group, so the walk owns every shape in it)
- ✅ 2026-07-20 (−0.0006..−0.0007 per backend p1; p2/p3 had measured stale): the teal triangle renders on ALL pages — it was never an outline-only preset needing a triangle builder; it is a WHITE-FILLED custGeom with a 4.9pt teal `a:ln`, and the walk's `ParseSolidFillShape` deliberately nulled strokes on solid fills while ShapeParser's solid branch always stroked them (p2/p3 SP-owned = rendered; p1's anchor carries a non-identity nested sibling group = walk-owned = dropped). Filled walk shapes now stroke their `a:ln` exactly like SP's
- MAJOR | html | - | logo cluster malformed into a blue blob (overlapping circle + rectangle), hidden teal/purple shapes exposed
- MAJOR | html | - | purple decorative circles overlap the "Taylor Phillips" address text in the second letter section
- MEDIUM | html | - | decorative shapes inconsistent across sections: dashed elements absent everywhere and the third letter section has no shapes at all

### letters/06

- MINOR | skia,imagesharp | p1 | "SCHOOL OF" and "FINE ART" display titles sit 20-28px higher than Word
- CLEAN: html

### letters/07

- MEDIUM | all | p1 | second body paragraph wraps to 4 lines vs Word's 3 (text breaks earlier, column effectively narrower)
- MINOR | html | - | paragraph spacing collapsed so the letter reads as one continuous block (no gaps before "Adrian's...", "Sincerely," or the address)
- MINOR | html | - | the header pattern band stops short of full page width (ends at x=817 of 1024)

### letters/08

- MEDIUM | all | p1 | first body paragraph wraps to 3 lines vs Word's 2 ("...recent visit to New / York.") and second paragraph breaks at different words, shifting the letter body
- MEDIUM | all | p1 | large signature "Joseph Price" rendered in bold/heavy weight instead of Word's light strokes
- MINOR | html | - | signature "Joseph Price" bold vs Word's light weight
- MINOR | html | - | inter-paragraph spacing collapsed — body paragraphs run together

### letters/09

- MEDIUM | all | p1 | first body paragraph wraps to 5 lines vs Word's 4 ("...advanced financial / forecasting."), pushing body text and the footer contact block ~1 line lower
- MINOR | html | - | paragraph spacing collapsed — salutation and paragraphs run together

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
- MINOR | html | - | paragraph spacing collapsed — recipient block, date and salutation run together

### line_breaks

- CLEAN: html

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

### line_spacing

- CLEAN: html

### line_spacing_at_least

- MEDIUM | all | p1 | "At least" line spacing under-applied: gaps between the 12/18/24/36pt paragraphs are ~15–25% smaller than Word's (e.g. 24pt→36pt gap ≈66px vs Word's ≈88px at 150dpi), leaving the 36pt paragraph ~1 line height higher
- CLEAN: html

### line_spacing_exactly

- MEDIUM | all | p1 | "Exactly" line spacing under-computed: lines drift progressively upward vs Word — 24pt-spaced line ~6px high, 36pt-spaced line ~18-21px (nearly a full line) high, so the block ends clearly higher
- CLEAN: html

### long_paragraph

- CLEAN: html

### menus/01

- MEDIUM | all | p1,p2,p3 | entire page content (text and, on p2/p3, the floral art) sits ~30-65px (150dpi) higher than Word with slightly compressed section spacing; offset is largest at the p3 title (~60px)
- MAJOR | html | - | light-grey page background missing: all text renders on white, only the first flower image block carries the grey (Word shows grey behind all 3 pages)

### menus/02

- MEDIUM | all | p1 | header ("New Year's Eve / CELEBRATION / MENU") and all menu items are centred on an axis ~36px left of Word's, shifting the whole text column left

### menus/03

- MAJOR | all | p1 | "EVENT INTRO" / "EVENT DATE" labels and the large gold "EVENT TITLE" heading are missing; their gold rule lines render but misplaced and mis-sized
- MAJOR | skia,pdf | p1 | full-height gold divider line between the two columns is missing (only a short gold tick at top-right remains); ImageSharp draws it
- MEDIUM | all | p1 | both text columns shifted left (instructions column ~25% of page width left of Word) and vertically compressed (menu column ends ~65px high)
- MINOR | skia,imagesharp | p1 | numbered steps render the number at the left indent with the step text centered separately, leaving a large gap (Word centers "2. Press Ctrl+C" as one unit; PDF matches Word)
- MINOR | skia,pdf | p1 | full-page navy background tint fractionally off Word's (em≈1.0 but below visible threshold)
- MAJOR | html | - | all content renders below the navy panel on the white page, leaving the navy block empty and every white-colored text run invisible (only gold headings and step titles visible)
- MAJOR | html | - | "EVENT TITLE" / "EVENT INTRO" / "EVENT DATE" also missing from the HTML export

### menus/04

- ✅ 2026-07-20 (metric +0.0016..+0.0050 = new-ink offset penalty; the pattern composition matches Word in crops): the vegetable-doodle header pattern renders in all backends — the doodles are explicit-noFill stroked custGeoms in a walk-owned group whose contours DO flatten; the walk's noFill bail (not contour parsing) was what dropped them
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

- MAJOR | all | p2 | pale-blue full-page background missing — page renders white (p1/p3 keep it)
- MAJOR | pdf | p3 | sheep logo at bottom right rendered white instead of red — nearly invisible on the pale background (Skia/ImageSharp render it red)
- MINOR | all | p1,p2,p3 | menu items drift progressively downward (~half a line by page bottom)
- MAJOR | html | - | page-3 red bars absent from the page-3 block (no bar at its top or bottom); only two bars render, one at the document top and one cutting through the page-2 "BISTRO MENU" title
- MEDIUM | html | - | pale-blue background covers only page-1 content; page-2/3 content renders on white

### menus/07

- ✅ 2026-07-20 (−0.112 skia / −0.100 imagesharp / −0.103 pdf): chalkboard centres with wood on all sides (wp:align fold; same class as inline_group_crop)
- MINOR | all | p1 | food photos shifted right ~15-25px

### menus/08

- MEDIUM | all | p1 | right instruction block wraps at a different word than Word ("...select the whole / cell.)" vs Word's "...select the / whole cell.)")

### menus/09

- MEDIUM | all | p1 | inner decorative frame renders since 2026-07-19 (outline-only emission; the +0.003 metric tick is the new-ink offset penalty — crops confirm the double frame + corner accents present). Residual: frame geometry slightly off Word's (octagon corner cuts vs rounded corners)
- MINOR | all | p1 | chalkboard ~16px narrower and ~9px shorter than Word (right/bottom edges pulled in)

### mixed_breaks

- MEDIUM | all | p3 | "Content after column break." starts ~1.5 line heights higher than Word, which places the text lower on the page after the column break
- CLEAN: html

### multiple_images

- CLEAN: html

### multiple_pages

- CLEAN: html

### multiple_paragraphs

- CLEAN: html

### nested_list

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
- MINOR | html | - | Inter-paragraph spacing lost — paragraphs run together with no gap

### newsletters/05

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

- ✅ FIXED 2026-07-21 (absolute a:ln/@w) | skia | p1,p2,p4,p5 | **REGRESSION, found by the 2026-07-20 re-audit.** These four skia baselines were 100% SOLID NAVY — 3 unique colours, zero content, 18-30KB PNGs against ImageSharp's 400KB — promoted broken at `8da1f624d` (2026-07-18, "Assemble connector-line arrow glyphs cleanly"). Root cause: that commit scaled group member stroke widths by the group's child→display factor `sx = pixelWidth/ChildExtentX`. But `a:ln/@w` is ABSOLUTE EMU and `a:chExt` is not reliably EMU — these icons are a legacy-VML twip grid (`a:ext=908050` over `a:chExt=1430`, 1 unit = 635 EMU), so the 2pt icon frame drew at 2646px and flooded the page. p3/p6 were ALSO broken (a page-wide navy arc, not "normal"), as were ImageSharp and PDF on every page. Reverted to absolute widths in all four renderers; all 6 pages restored in all 3 backends, and business-plans/12 −0.0006/page × 16 pages, resumes/03 −0.0005. Measurement evidence and the full decision-log entry are in `docs/floating-art-pipeline.md` ("Stroke widths under a group transform" + decision log #7). Allow-list entries removed from `BaselineHealthTests.KnownDegenerate`.
- MAJOR | all | - | page-count mismatch: all three backends produce 6 pages vs Word's 4 (each 2-page edition spills onto a 3rd page; text sets ~10% wider so every column wraps earlier and blocks run longer). **NB: this mismatch means the scenario records NO per-page AE/SSIM at all (`PageDiffs` is null), so nothing here is metric-judgeable — crop-vet by hand.**
- ✅ FIXED 2026-07-21 (absolute a:ln/@w) | all | p1,p2,p3,p4,p5,p6 | every decorative line-art icon (balloons, bell, backpack, stacked books, globe) rendered as a solid navy filled square in a square frame instead of the circled line drawing. Same root cause as the regression above — the icon art was always parsed correctly (custGeom contours and all); the blown-up frame stroke was painting over it. Balloons/bell/globe now match Word's line drawing.
- MAJOR | all | p5 | "HIGHLIGHTS" section heading overlaps the adjacent column's body text (word "viverra" hidden behind the heading) on the yellow-edition second page
- MEDIUM | all | p1,p4 | masthead navy bar text loses its per-letter tracking: skia/imagesharp render bold words with huge word gaps, pdf renders compact bold text with no letter-spacing
- MEDIUM | all | p1,p4 | masthead contact line wraps to two lines ("www.sycamoremiddle.org" drops to its own line) vs one line in Word
- MEDIUM | all | p2,p5 | "NOTES FROM THE COUNSELORS" left column collapses to ~1-2 words per line (column far too narrow) and the text snakes to the bottom of the page
- MEDIUM | all | p3,p6 | overflow spill pages render on a white background instead of the section's page color (light blue / yellow)
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

- ✅ FIXED 2026-07-21 (fixed-layout table widths) | skia,imagesharp | - | page count was 5 vs Word's 4 — all content lagged one page behind from p2 onward. Resolved as a side effect of the fixed-layout table width fix (the over-wide teaser band no longer inflates the layout): both raster backends now render exactly 4 pages. **This also restored the scenario's per-page AE/SSIM**, which a page-count mismatch had suppressed entirely.
- MEDIUM | pdf | - | page count 6 vs Word's 4 — improved to 5 by the 2026-07-21 fixed-layout table width fix (was 6), so the near-blank page containing only the footer rule is gone; still one page over
- MEDIUM | skia,imagesharp | p5 → resolved with the page count above; the stray "Page" footer rule no longer has a page to land on
- MAJOR | all | p1 | masthead "NEWS TODAY" wraps onto two lines (Word: single line), doubling the banner height and triggering the reflow
- MEDIUM | all | p1,p2,p3,p4 | body text wraps 1-2 words earlier per line in every column (wider glyph advances); headlines "New program launches" and "The scoop of the day" also wrap to two lines
- MAJOR | all | p1 | bottom teaser band (School budget / Police prevent crime / Athlete sets record) pushed to the page edge and clipped mid-content; bodies spill to p2 (PDF spills the entire band including headers)
- MEDIUM | all | p3,p4 | photo captions detach from their images and collide with neighbouring content (caption box overlaps the section rule above "Mirjam Nilsson" on p3; caption floats over the "scoop of the day" columns on p4)
- MEDIUM | pdf | p6 | same "Page 4" footer rule drawn through the final column's text
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

- ✅ FIXED 2026-07-21 (fixed-layout table widths) | all | p1 | header banner table was clipped to 962px of the 1240px page width: the header's table is `tblW=12508 dxa` with a NEGATIVE `tblInd=-1593 dxa`, i.e. deliberately wider than the text column and bled to both page edges. `TableLayout` squeezed every over-wide table back to the text column, so the bar stopped ~78% across. A `tblLayout="fixed"` table now keeps its declared grid, and the banner spans x=0..1240 exactly as Word does.
- ✅ FIXED 2026-07-21 (header space reservation) | all | p1 | body content sat 54-59px (~2 line heights) higher than Word and the H1 title overlapped the banner. `MeasureHeaderFooterHeight` was a hardcoded `0` stub in both raster backends (and the PDF passed `0,0`), so the wired-up `SetHeaderFooterSpace` never reserved anything; it also measured only the DEFAULT header, while this document's tall banner is in the `titlePg` FIRST-page header. `RenderHeader` now reserves from the header it actually painted, per page. Every body band is within ±4px of Word (was 54-59px): "PORTFOLIO A" 279 vs Word's 280, "SMITH, John" 339 vs 339, "Bill 1" 394 vs 394.
- MEDIUM | all | p1 | "Notes:" box 249px tall vs Word's 238 (**+4.6%**, was 221px / −7.1% before 2026-07-21). Its single cell contains only seven `<w:br/>` runs, so the height is entirely break-only line boxes. Two separate causes, one fixed: (a) ✅ FIXED — a paragraph ENDING in `<w:br/>` got no line box for the break, so the cell laid out 7 lines where Word lays out 8; (b) OPEN — the remaining error is the line-spacing multiplier, not the line count. **Measured against Word** by rendering probe copies of this fixture with 1/3/7 breaks through RenderHelper: Word's box grows 26.17px per break (= 12.56pt ≈ Arial 11pt's 12.649pt hhea line box) and its intercept (~27.7px = the 6+6pt before/after spacing plus borders) only fits the N+1-line model. Morph grows 27.50px per break = 13.16pt, because `DocumentParser` defaults an unspecified `w:spacing/@w:line` to a **1.04** multiplier (the `ParagraphProperties` model default is 1.08); 12.649 × 1.04 = 13.155. The residual is now a clean, uniform +4.6% at every break count. Fixing it means changing that global default, which is the systemic line-pitch family and needs its own corpus-wide judging pass — do not tune it against this one box.
- MEDIUM | all | p1 | footer marking sits 32px LOWER than Word (y=1716 vs 1684), the opposite direction to the body drift, so footer placement is a separate error from the header one.
- ✅ FIXED 2026-07-21 (w:pgSz/@code) | all | p1 | page rendered 1240x1754 vs Word's 1240x1753. `w:pgSz` declares `w:h=16840` (842.0pt) but also `w:code="9"` — the A4 printer paper code — and Word honours the code's true A4 height (841.89pt) over the rounded twips. `DocumentParser.SnapToPaperCode` now substitutes the code's exact paper when the declared size already matches it to within 0.5pt, in either orientation. Page dimensions now match Word exactly, **which restored the scenario's SSIM** (none → 0.9257 skia / 0.9226 imagesharp; `PageComparison` returns null SSIM when the dimensions differ). The corpus now has ZERO page-size mismatches. cover-letters/09 and resumes/04 (also `code=9`) moved ±0.0001 from sub-pixel AA and their PDFs now emit exact A4 595.276x841.89. The tolerance guard is load-bearing: cards/03 is a 7x5in card carrying a stale `code=23` (5x11.5in envelope) that would otherwise resize it — covered by `PaperCodePageSizeTests`.
- MAJOR | html | - | header and footer dropped entirely from the HTML export — "SENSITIVE//EXAMPLE" appears zero times in `html_result.verified.html` though both raster and PDF render it top and bottom. Other scenarios do export header/footer content.
- MEDIUM | html | - | "Bill 1" and "Notes:" render bold ITALIC. `HtmlExporter`'s boilerplate CSS hardcodes `h4 { font-style: italic }` (Word's default Heading 4 look), which overrides this document's own Heading4 style — based on Heading3, `sz=24`, no italic. Word renders both upright bold. Same latent bug for `h6`.
- MEDIUM | html | - | the "Notes:" box collapses to a ~4px strip: the seven `<w:br/>` runs that give the cell its height in Word produce no height in the export.

### numbered_list

- CLEAN: html

### numbered_list_restart

- CLEAN: html

### numbered_list_tracking

- MINOR | all | p1 | Glyph advance widths ~10% narrower than Word, so each list line ends slightly short of Word's (no wrap changes)
- CLEAN: html

### office_math

- MEDIUM | all | p1 | Built-up OMML fraction 1/2 (numerator stacked over denominator with fraction bar) is flattened to inline text "1/2"
- MEDIUM | all | p1 | Equations rendered in the italic body sans font instead of Cambria Math serif, and math operator spacing is dropped ("a²+b²=c²" instead of "a² + b² = c²")
- MINOR | html | - | Same math linearization in the HTML export: fraction as plain "1/2" and compact "a²+b²=c²" in italic body font

### page_a4

- CLEAN: html

### page_borders/01

- MINOR | all | p1 | Page border box drawn ~3px (~1.5pt) closer to the page edge on top/left (border rectangle slightly larger than Word's); thickness matches
- MAJOR | html | - | Decorative page border is missing entirely from the HTML export (only the sentence is emitted, no border around content)

### page_breaks

- CLEAN: html

### page_landscape

- CLEAN: html

### page_legal

- CLEAN: html

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

### pct_pos_offset

- CLEAN: html

### postcards/01

- MINOR | skia,imagesharp | p1 | bottom two postcard images drawn with a whole-image vertical offset of ~0.1in vs Word (heavy displacement ghost over both bottom images; top row and PDF are aligned)
- MINOR | skia,imagesharp | p2 | postcard-back placeholder text and hand-drawn address rules sit ~0.05-0.1in lower than Word, most visibly in the bottom row of cards
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

### resumes/01

- MINOR | all | p1 | body content drifts upward slightly (~8-15px by the bottom of the page)
- MEDIUM | html | - | employer/school heading lines ("Jasper University", "Bellows College", "Lamna Healthcare | General Practitioner", etc.) rendered italic where Word shows them upright bold

### resumes/02

- MINOR | all | p1 | header text and both body columns sit ~8-13px higher than Word (uniform upward drift; artwork and divider positions otherwise match)
- MAJOR | html | - | header text block (KAI CARTER, GENERAL PRACTITIONER, CONTACT, phone/website/email) not visible — large blank white area below the black band where the white-on-black text should be
- MEDIUM | html | - | X-brush artwork sits at the left edge of the black header band instead of the right side as in Word

### resumes/03

- MEDIUM | all | p1 | Dashed rules rendered solid: header rule, rule below summary, and the dotted vertical column divider all lose their dash pattern
- MEDIUM | skia,imagesharp | p1 | SKILLS entries vertically compressed (bar touches label, no gap between entries) so the block ends ~135px higher than Word; PDF spacing matches Word
- MEDIUM | all | p1 | Education text "Laude" broken mid-word ("...Biology, Cum Lau / de, outstanding...") where Word wraps before "Laude"
- MEDIUM | pdf | p1 | Right-column HOBBIES/CONTACT blocks drift progressively lower (~2.5 line heights by the CONTACT block)
- MEDIUM | html | - | Bold upright runs render italic: job titles (Lamna Healthcare / General Practitioner etc.), university names, skill labels, and Phone/Website/Email sub-headers
- MINOR | html | - | Email address styled as blue underlined hyperlink; Word shows plain black text

### resumes/04

- MAJOR | all | p1 | Last 2-3 lines of OBJECTIVE text overlap the yellow/pink wave shapes at the sidebar bottom (white-on-yellow, barely readable); waves also drawn ~100px higher than Word
- MEDIUM | all | p1 | Sidebar contact entries (address/phone/email/website) spaced ~2x further apart than Word, pushing OBJECTIVE down
- MEDIUM | skia,imagesharp | p1 | COMMUNICATION paragraph wraps to 6 lines vs Word's 7; PDF matches Word

### resumes/05

- MEDIUM | all | p1 | Vertical spacing compressed throughout: right-column sections end ~107px (4+ lines) higher than Word (REFERENCES at y1331 vs 1438) and the sidebar box is ~100px shorter
- MINOR | html | - | Date-range lines (JAN 20XX – AUG 20XX etc.) render italic; Word shows upright

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

- MEDIUM | pdf | p1,p2,p3 | Progressive downward drift through the page — SKILLS/ACTIVITIES sections end ~20-26px (~1 line) lower than Word
- MINOR | html | - | SKILLS bullets black instead of accent color

### resumes/11

- MEDIUM | skia,imagesharp | p1 | Vertical spacing compressed from the EXPERIENCE section down — sections creep progressively higher, SKILLS block ends ~110px (~0.75 in) above Word's position (PDF matches Word within 1px)
- MINOR | all | p1 | Name block starts ~15px lower than Word
- MINOR | html | - | Short section-divider rules above EDUCATION and SKILLS missing (only the contact-row rule is kept)

### resumes/12

- MAJOR | all | p1 | Short coral underline rule below "Manager" is missing in all three backends
- MINOR | pdf | p1 | "VICTORIA BURKE" name block sits ~20px lower than Word
- MAJOR | html | - | Coral underline below "Manager" missing

### resumes/13

- MAJOR | skia,imagesharp | - | Page count 4 vs Word's 5 — compressed vertical layout pulls content up one page from p2 onward (p3 shows Word's p4 content, p4 shows Word's p5 content)
- MAJOR | pdf | - | Page count 6 vs Word's 5 — looser layout pushes Teaching experience off p2, content lags ~1 page behind and overflows onto an extra page 6
- MINOR | all | - | Footer page-number field now recomputes per page (was cached "3" everywhere); the remaining gap vs Word's 1-5 is the page-count divergence [systemic #2], not the field
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

- MAJOR | all | p1,p2,p3 | Skills bulleted list (Creativity, Leadership, Organization, Problem solving, Teamwork) missing on every page — Contact section moves up directly under the "Skills" heading
- MEDIUM | pdf | p1,p2,p3 | 2nd and 3rd Experience entries drift progressively lower (~0.33in by the third entry) from oversized gaps between entries
- MAJOR | html | - | Skills bulleted list missing in all three repeated sections (heading renders with no items)
- MAJOR | html | - | colored content-panel backgrounds wrong: first panel grey instead of light blue, and the yellow (2nd) and grey (3rd) panels missing entirely

### rtl_paragraph

- MEDIUM | html | - | bidi heading and paragraph render left-aligned instead of right-aligned
- CLEAN: skia, imagesharp

### section_break_continuous

- MEDIUM | skia,imagesharp | p1,p2 | Section 2 (heading plus paragraphs 2-7) flows onto page 1 immediately below section 1 instead of starting at the top of page 2 as Word does (section 2's default next-page break rendered as continuous); page 2 therefore begins at paragraph 8 instead of the "Section 2 content" heading.
- CLEAN: html

### section_break_odd_page

- CLEAN: html

### small_caps

- CLEAN: skia, imagesharp, html

### tab_stops

- MEDIUM | html | - | tab formatting collapses to single spaces — TOC dot leaders, tabbed column alignment (Name/Role/Team/Location), left/center/right tab positions, and the signature underline fill are all lost

### table_alignment/01

- MEDIUM | html | - | table alignment lost — the centered and right-aligned tables render left-aligned (all three tables at the left margin with identical widths)

### table_autofit_no_widths

- MEDIUM | all | p1 | autofit column widths distributed differently from Word — "Full Name" column too narrow (header and "Jane Smith" wrap to two lines vs one in Word) while "Hire Date" is too wide (dates fit one line vs Word's two)
- CLEAN: html

### table_borders

- CLEAN: html

### table_cell_margin_per_cell

- MEDIUM | all | p1 | "Left margin emphasis" text sits at the top of cell 2 (~33px too high at 150dpi): Word applies the row's largest top cell margin (cell 1's 15pt) to both cells, the renders honor only cell 2's own 2.5pt top margin
- MEDIUM | html | - | per-cell margins dropped: cell 1's large top margin and cell 2's large left margin are not represented — both texts render with identical compact padding, losing the emphasis the cells demonstrate

### table_cell_padding

- CLEAN: html

### table_cell_padding_varied

- MEDIUM | html | - | varied per-cell padding lost: all six cells render with identical compact padding, so "Large padding (20pt)", "More left/right" and "No padding" cells all look the same

### table_cell_spacing/01

- MEDIUM | all | p1 | cell-spacing gaps collapsed vertically: outer table border sits only ~4px from the cell borders vs Word's ~11px and cell boxes are 38-40px tall vs 47-48, so the detached-border table is 146px tall vs Word's 188 and starts ~17px higher (width and horizontal gaps are close)
- MEDIUM | html | - | detached-border effect lost entirely: renders as an ordinary collapsed-border grid with single shared lines and no gaps (tblCellSpacing ignored)

### table_colors

- CLEAN: html

### table_default_cell_margin

- MEDIUM | html | - | large default cell margins dropped — cells render with small default padding, losing the spacious look that is this document's feature

### table_default_cell_margin_start_end

- CLEAN: html

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
- MINOR | html | - | blank-line/paragraph spacing of the letter collapsed — Date, Dear Recipient, body and closing paragraphs are tightly stacked with none of Word's inter-paragraph gaps.

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

### table_page_break

- CLEAN: html

### table_text_direction

- MINOR | all | p1 | table rows slightly shorter than Word — header/Q1/Q2 row bottoms sit ~5-12px high and the table bottom border ends ~12px early
- MEDIUM | html | - | rotated header-cell text "Quarter" rendered horizontal in HTML export (vertical bottom-to-top text direction lost)

### table_two_column_layout

- MEDIUM | all | p1 | vertical spacing inside cells tighter than Word — right-column "Line 1..10" list drifts upward reaching a full line by Line 10, and the table bottom border sits ~40px higher than Word
- MINOR | html | - | empty-paragraph gaps inside cells collapsed (no blank line after "Left Column"/"Right Column" headings or before "Line 1", which Word renders)

### three_columns

- CLEAN: html

### tracked_changes/01

- MAJOR | all | p1 | tracked deletion "removed." not rendered at all (Word shows it in red strikethrough at end of line)
- MEDIUM | all | p1 | tracked insertion "inserted" rendered as plain black text — red underlined revision styling missing
- MINOR | all | p1 | left-margin change bar (vertical revision line) missing
- MAJOR | html | - | tracked deletion "removed." absent from HTML export
- MEDIUM | html | - | tracked insertion "inserted" shown without any revision styling in HTML

### two_columns

- CLEAN: html

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
- MEDIUM | html | - | red section headings rendered italic (upright in Word)

### wedding/05

- MEDIUM | pdf | p1 | date block also rendered bold where Word uses regular weight
- MEDIUM | all | p1,p2 | lists start too high: wedding-party list ~0.6in above Word's position on p1, menu list ~1 line high on p2 (gap below the wash heading too small)
- MEDIUM | html | - | watercolor washes exported as standalone images stacked above the panels instead of backgrounds behind the headings

### wedding/06

- MEDIUM | all | p1,p2 | card rows too short: fold/borders sit ~1in higher than Word and second-card content ~0.9in high; p2 bottom flower clusters straddle the card borders, p1 card2's poppy dips into the pink banner corner
- MEDIUM | all | p2 | invitation text line spacing compressed ~30%, block ends ~0.5in higher
- MEDIUM | html | - | date block ("Saturday the Twenty-First of June" ... "Reception to Follow") rendered italic; upright in Word
- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/08

- MAJOR | all | p1 | green circled "&" badge between bride's and groom's names missing
- MEDIUM | all | p1,p2 | card panels shorter than Word (p1 borders end ~2in early, p2 ~0.4in) with content blocks 0.4-0.8in higher (Thanks-and-Dedication and time/venue blocks)
- MEDIUM | all | p1 | "dinner and dancing to follow" rendered upright instead of italic
- MAJOR | html | - | green circled "&" badge missing
- MEDIUM | html | - | "00:00 PM" and "VENUE/PLACE" italicized in HTML; upright in Word

### wedding/09

- MEDIUM | all | p1 | invitation banners pinned to top of card rows instead of vertically centered (card1 ~1.5in high, card2 ~2.9in high); card frames end at ~70% page height leaving the bottom unframed
- MAJOR | all | p1 | card2's relocated "on our wedding day"/banner corner collides with the poppy image (text drawn over the flower; pdf card1 poppy also overlaps the yellow "&" box)
- MEDIUM | all | p2 | invitation text line spacing compressed ~30%, block ends ~0.5in higher
- MEDIUM | all | p2 | bottom purple/yellow cluster sits on the card boundary/page bottom instead of inside the cards
- MEDIUM | html | - | date block ("Saturday the Twenty-First of June" ...) rendered italic; upright in Word
- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/10

- MAJOR | html | - | floral header graphic mis-composed the same way (vertical branch-and-rose arrangement instead of Word's horizontal spray above the title)
- MINOR | all | p1 | the two "Candid photos..." placeholder items' checkboxes render light grey instead of dark like every other row in Word
- MINOR | all | p1,p2,p3,p4,p5 | checklist rows drift vertically up to ~10px from Word's positions (cumulative spacing difference down the page)

### wedding/11

- MAJOR | all | p1 | left card's venue/time block "4:30 pm in the afternoon / Cherrywood Chapel / 1234 Cherry Road / Austin, Texas" missing entirely (not moved to p2)
- MAJOR | html | - | same venue/time block ("4:30 pm in the afternoon / Cherrywood Chapel / 1234 Cherry Road / Austin, Texas") missing from the HTML export
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
- MAJOR | pdf | p10,p11 | Highlight backgrounds completely missing — yellow/cyan/green/magenta/red bars behind the five "... Highlight" lines (p10) and the yellow highlight behind "Underline + Highlight" (p11) are absent; text renders on plain white
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

`align_center`, `align_left`, `all_caps`, `bold_text`, `colored_text`, `document_protection/01`, `gutter_margins/01`, `mixed_formatting`, `section_break_even_page`, `section_break_next_page`, `simple_paragraph`, `simple_table`, `strikethrough_text`, `subscript_superscript`, `table_default_style_first_row_run_color`, `table_default_style_first_row_shading`, `table_vmerge_basic`, `table_vmerge_explicit_heights`, `text_wrapping_break`, `underline_text`, `wedding/07`
